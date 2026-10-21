using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PlantPulse;

/// <summary>
/// Reads osu!lazer's live play state from tosu (the standard memory reader) and
/// converts it into haptics on the shared <see cref="HapticServer"/>:
///
///  - tosu's precise feed (10 ms by default) reports the hit-error array; each
///    new entry is one judged hit, so taps stay tight even at high BPM.
///  - the full feed (150 ms) provides play state, combo, misses and beatmap.
///
/// Every hit is a short tick, every Nth hit is accented, combo adds a little
/// energy, and misses get a low thud. Nothing is required from the phone app.
/// </summary>
public sealed class OsuSyncEngine : IDisposable
{
    public const int DefaultTosuPort = 24050;
    private const string FullPath = "/websocket/v2";
    private const string PrecisePath = "/websocket/v2/precise";
    private static readonly string[] HitKeys = { "0", "50", "100", "300", "geki", "katu" };

    private readonly HapticServer _server;
    private readonly int _tosuPort;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _fullTask;
    private readonly Task _preciseTask;
    private readonly object _gate = new();

    private volatile bool _enabled;
    private volatile int _intensity = 55;
    private volatile int _accentEvery = 8;
    private volatile bool _missBuzz = true;
    private volatile bool _startPing = true;

    private bool _fullConnected;
    private bool _preciseConnected;
    private bool _playing;
    private string _state = "";
    private string _beatmap = "";
    private int _combo;
    private int _maxCombo;
    private int _totalHits;
    private int _misses;
    private int _playHits;
    private int _mode;
    private int _preciseSeenHits;
    private int _preciseBase = -1;
    private int _fullHitBase = -1;
    private int _missesSeen = -1;
    private int _hitToggle;
    private long _lastPreciseAt;
    private string _lastEvent = "";

    public OsuSyncEngine(HapticServer server, int tosuPort = DefaultTosuPort)
    {
        _server = server;
        _tosuPort = tosuPort;
        _fullTask = SocketLoop(FullPath, false, _cts.Token);
        _preciseTask = SocketLoop(PrecisePath, true, _cts.Token);
    }

    /// <summary>While false the sockets stay connected but no haptics are sent.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;
            _enabled = value;

            if (value)
            {
                lock (_gate)
                {
                    _preciseBase = -1;
                    _fullHitBase = -1;
                    _playHits = 0;
                    _missesSeen = _misses;
                }
                _server.Burst("osu_sync_on", 45, 110, 2);
                Log.Line("osu: sync enabled");
            }
            else
            {
                Log.Line("osu: sync paused");
            }
        }
    }

    public bool Connected
    {
        get { lock (_gate) return _fullConnected; }
    }

    public bool PreciseConnected
    {
        get { lock (_gate) return _preciseConnected; }
    }

    public bool Playing
    {
        get { lock (_gate) return _playing; }
    }

    public string StateName
    {
        get { lock (_gate) return _state; }
    }

    public string BeatmapLabel
    {
        get { lock (_gate) return _beatmap; }
    }

    public int Combo
    {
        get { lock (_gate) return _combo; }
    }

    public int MaxCombo
    {
        get { lock (_gate) return _maxCombo; }
    }

    public int TotalHits
    {
        get { lock (_gate) return _totalHits; }
    }

    public int Misses
    {
        get { lock (_gate) return _misses; }
    }

    public string LastEvent
    {
        get { lock (_gate) return _lastEvent; }
    }

    public void SetOptions(int intensity, int accentEvery, bool missBuzz, bool startPing)
    {
        _intensity = Math.Clamp(intensity, 10, 100);
        _accentEvery = Math.Clamp(accentEvery, 1, 64);
        _missBuzz = missBuzz;
        _startPing = startPing;
    }

    public void Dispose()
    {
        _enabled = false;
        _cts.Cancel();
        try
        {
            Task.WaitAll(new[] { _fullTask, _preciseTask }, 1500);
        }
        catch
        {
        }
        _cts.Dispose();
    }

    private async Task SocketLoop(string path, bool precise, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using ClientWebSocket socket = new();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                await socket.ConnectAsync(
                    new Uri($"ws://127.0.0.1:{_tosuPort}{path}"), token).ConfigureAwait(false);

                SetConnected(precise, true);
                await ReadLoop(socket, precise, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // tosu is not running yet (or was closed); retry quietly.
            }
            finally
            {
                SetConnected(precise, false);
            }

            try
            {
                await Task.Delay(2000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void SetConnected(bool precise, bool connected)
    {
        lock (_gate)
        {
            if (precise) _preciseConnected = connected;
            else _fullConnected = connected;

            if (connected)
            {
                Log.Line(precise ? "osu: tosu precise feed connected" : "osu: tosu feed connected");
            }
            else if (precise ? _fullConnected : _preciseConnected)
            {
                Log.Line(precise ? "osu: tosu precise feed lost" : "osu: tosu feed lost");
            }
        }
    }

    private async Task ReadLoop(ClientWebSocket socket, bool precise, CancellationToken token)
    {
        byte[] buffer = new byte[32 * 1024];
        using MemoryStream message = new();

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure, "", token).ConfigureAwait(false);
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > 4_000_000)
                {
                    message.SetLength(0);
                    break;
                }
            }
            while (!result.EndOfMessage);

            if (message.Length == 0) continue;

            try
            {
                using JsonDocument document = JsonDocument.Parse(message.ToArray());
                if (precise) HandlePrecise(document.RootElement);
                else HandleFull(document.RootElement);
            }
            catch (JsonException)
            {
                // Ignore a malformed frame and keep the feed alive.
            }
        }
    }

    private void HandleFull(JsonElement root)
    {
        string state = Str(root, "state", "name");
        int stateNumber = Int(root, "state", "number");
        bool playing = stateNumber == 2
            || state.Equals("play", StringComparison.OrdinalIgnoreCase)
            || state.Equals("playing", StringComparison.OrdinalIgnoreCase)
            || state.StartsWith("gameplay", StringComparison.OrdinalIgnoreCase);

        string artist = Str(root, "beatmap", "artist");
        string title = Str(root, "beatmap", "title");
        string version = Str(root, "beatmap", "version");
        string mods = Str(root, "play", "mods", "name");

        string label = "—";
        if (!string.IsNullOrWhiteSpace(title))
        {
            label = string.IsNullOrWhiteSpace(artist)
                ? title
                : artist + " — " + title;
            if (!string.IsNullOrWhiteSpace(version)) label += "  [" + version + "]";
            if (!string.IsNullOrWhiteSpace(mods)) label += "  ·  " + mods;
        }

        int combo = Int(root, "play", "combo", "current");
        int maxCombo = Int(root, "play", "combo", "max");
        int misses = Int(root, "play", "hits", "0");
        int totalHits = SumHits(root);
        int mode = Int(root, "play", "mode", "number");

        bool startPing = false;
        bool missBuzz = false;
        int fallbackHits = 0;

        lock (_gate)
        {
            _state = state;
            _beatmap = label;
            _combo = combo;
            _maxCombo = maxCombo;
            _totalHits = totalHits;
            _misses = misses;
            _mode = mode;

            bool begin = playing && !_playing;
            bool end = !playing && _playing;
            if (begin || end)
            {
                _playing = playing;
                _preciseBase = -1;
                _playHits = 0;
                _preciseSeenHits = 0;
                _fullHitBase = totalHits - misses;
                _missesSeen = misses;
                startPing = begin && _enabled && _startPing;
            }

            if (!_playing)
            {
                _missesSeen = misses;
                _fullHitBase = totalHits - misses;
            }
            else
            {
                if (_missesSeen < 0) _missesSeen = misses;
                if (misses > _missesSeen && _enabled && _missBuzz)
                {
                    missBuzz = true;
                    _lastEvent = "miss";
                }
                _missesSeen = misses;

                bool preciseFresh = _preciseConnected
                    && Environment.TickCount64 - _lastPreciseAt < 1200
                    && _mode != 2;
                int successful = totalHits - misses;

                if (_fullHitBase < 0 || successful < _fullHitBase)
                {
                    _fullHitBase = successful;
                }
                else if (!preciseFresh || (_preciseSeenHits == 0 && successful >= 3))
                {
                    int delta = successful - _fullHitBase;
                    _fullHitBase = successful;
                    if (_enabled && delta > 0) fallbackHits = Math.Min(delta, 4);
                }
                else
                {
                    _fullHitBase = successful;
                }
            }
        }

        if (startPing)
        {
            _server.Burst("osu_start", Math.Clamp(_intensity - 10, 10, 100), 90, 2);
        }
        if (missBuzz)
        {
            _server.Burst("osu_miss", Math.Max(15, _intensity - 25), 220, 1);
        }
        for (int i = 0; i < fallbackHits; i++) FireHit(false);
    }

    private void HandlePrecise(JsonElement root)
    {
        int length = 0;
        int lastError = 0;
        bool hasError = false;

        if (root.TryGetProperty("hitErrors", out JsonElement errors)
            && errors.ValueKind == JsonValueKind.Array)
        {
            length = errors.GetArrayLength();
            foreach (JsonElement item in errors.EnumerateArray())
            {
                if (item.TryGetInt32(out int immediate))
                {
                    lastError = immediate;
                    hasError = true;
                }
                else if (item.TryGetDouble(out double offset))
                {
                    lastError = (int)Math.Round(offset);
                    hasError = true;
                }
            }
        }

        int fireCount = 0;
        bool perfect = false;

        lock (_gate)
        {
            _lastPreciseAt = Environment.TickCount64;

            if (!_playing || !_enabled)
            {
                _preciseBase = length;
                return;
            }

            if (_preciseBase < 0 || length < _preciseBase)
            {
                _preciseBase = length;
                return;
            }

            int delta = length - _preciseBase;
            _preciseBase = length;
            if (delta <= 0) return;

            fireCount = Math.Min(delta, 4);
            perfect = hasError && Math.Abs(lastError) <= 15;
            _preciseSeenHits += fireCount;
            _lastEvent = perfect ? "perfect" : "hit";
        }

        for (int i = 0; i < fireCount; i++) FireHit(perfect);
    }

    private void FireHit(bool perfect)
    {
        if (!_enabled) return;

        int count;
        int combo;
        string id;
        lock (_gate)
        {
            _playHits++;
            count = _playHits;
            combo = _combo;
            _hitToggle ^= 1;
            id = _hitToggle == 0 ? "osu_hit_a" : "osu_hit_b";
        }

        int level = Math.Clamp(
            _intensity + (perfect ? 3 : 0) + Math.Min(8, combo / 50), 10, 100);

        if (_accentEvery > 0 && count % _accentEvery == 0)
        {
            _server.Burst("osu_accent", Math.Clamp(level + 18, 1, 100), 75, 2);
        }
        else
        {
            _server.Burst(id, level, 45, 1);
        }
    }

    // MARK: - JSON helpers

    private static bool TryGet(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;
        foreach (string key in path)
        {
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty(key, out JsonElement next))
            {
                return false;
            }
            value = next;
        }
        return true;
    }

    private static string Str(JsonElement element, params string[] path)
    {
        return TryGet(element, out JsonElement value, path)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int Int(JsonElement element, params string[] path)
    {
        if (!TryGet(element, out JsonElement value, path)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out int parsed))
        {
            return parsed;
        }
        return 0;
    }

    private static int SumHits(JsonElement root)
    {
        if (!TryGet(root, out JsonElement hits, "play", "hits")
            || hits.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        int total = 0;
        foreach (string key in HitKeys)
        {
            if (hits.TryGetProperty(key, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out int number))
            {
                total += number;
            }
        }
        return total;
    }
}
