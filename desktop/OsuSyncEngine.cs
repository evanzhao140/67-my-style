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
///  - the full feed (100 ms) provides play state, combo, misses, kiai/breaks
///    and pause state.
///
/// The feel is a continuous "flow" bed (default 50%) with peaks layered on top:
/// every hit is a longer pulse, every Nth hit is accented, and the bed swells
/// with combo, hits and kiai. Misses duck the bed for a moment; breaks relax it.
/// Nothing is required from the phone app.
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
    private readonly Task _flowTask;
    private readonly object _gate = new();

    private volatile bool _enabled;
    private volatile int _intensity = 55;
    private volatile int _accentEvery = 8;
    private volatile bool _missBuzz = true;
    private volatile bool _startPing = true;
    private volatile int _flow = 50;
    private volatile bool _swell = true;

    private bool _fullConnected;
    private bool _preciseConnected;
    private bool _playing;
    private bool _paused;
    private bool _kiai;
    private bool _break;
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

    // Music bed state.
    private int _flowCurrent;
    private bool _flowActive;
    private long _lastHitAt;
    private long _duckUntil;
    private bool _comboBurstPending;
    private int _comboMilestone;

    public OsuSyncEngine(HapticServer server, int tosuPort = DefaultTosuPort)
    {
        _server = server;
        _tosuPort = tosuPort;
        _fullTask = SocketLoop(FullPath, false, _cts.Token);
        _preciseTask = SocketLoop(PrecisePath, true, _cts.Token);
        _flowTask = FlowLoop(_cts.Token);
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
                    _lastHitAt = 0;
                    _duckUntil = 0;
                    _comboBurstPending = false;
                }
                UpdateFlow();
                _server.Burst("osu_sync_on", 55, 150, 2);
                Log.Line("osu: sync enabled");
            }
            else
            {
                StopFlow(true);
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

    /// <summary>The constant level currently applied to the phone (0 = off).</summary>
    public int FlowLevel
    {
        get { lock (_gate) return _flowCurrent; }
    }

    public bool Kiai
    {
        get { lock (_gate) return _kiai; }
    }

    public void SetOptions(int intensity, int accentEvery, bool missBuzz, bool startPing,
        int flow, bool swell)
    {
        _intensity = Math.Clamp(intensity, 10, 100);
        _accentEvery = Math.Clamp(accentEvery, 1, 64);
        _missBuzz = missBuzz;
        _startPing = startPing;
        _flow = Math.Clamp(flow, 0, 90);
        _swell = swell;
    }

    public void Dispose()
    {
        _enabled = false;
        StopFlow(true);
        _cts.Cancel();
        try
        {
            Task.WaitAll(new[] { _fullTask, _preciseTask, _flowTask }, 1500);
        }
        catch
        {
        }
        _cts.Dispose();
    }

    /// <summary>
    /// Keeps the phone's continuous bed in step with the song: a base level from
    /// the slider, plus hit/combo energy and kiai, with breaks and misses ducking
    /// it. Runs on its own thread so BLE sends never block hit detection.
    /// </summary>
    private async Task FlowLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                UpdateFlow();
            }
            catch
            {
                // Keep the loop alive if the server is shutting down.
            }

            try
            {
                await Task.Delay(110, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void UpdateFlow()
    {
        bool enabled = _enabled;
        bool playing;
        bool paused;
        bool kiai;
        bool isBreak;
        int flow;
        bool swell;
        int combo;
        long lastHit;
        long duckUntil;

        lock (_gate)
        {
            playing = _playing;
            paused = _paused;
            kiai = _kiai;
            isBreak = _break;
            flow = _flow;
            swell = _swell;
            combo = _combo;
            lastHit = _lastHitAt;
            duckUntil = _duckUntil;
        }

        if (!enabled || !playing || paused || flow <= 0)
        {
            StopFlow();
            return;
        }

        long now = Environment.TickCount64;
        int target = flow;

        if (swell)
        {
            int bonus = 0;
            long sinceHit = lastHit == 0 ? long.MaxValue : now - lastHit;
            if (sinceHit < 130) bonus += 12;
            else if (sinceHit < 280) bonus += 6;

            bonus += Math.Min(6, combo / 100);
            if (kiai) bonus += 12;

            // Keep headroom for the hit peaks even in a max-energy section.
            target += Math.Min(22, bonus);
        }

        // Breaks are the quiet part of the map; keep a faint bed, not a wall.
        if (isBreak) target = Math.Min(target, Math.Max(6, flow / 4));

        // A miss immediately opens a hole (the HandleFull path also applies the
        // duck before its burst, so the thud is not floored by the old level).
        if (now < duckUntil) target = Math.Min(target, Math.Max(8, flow / 3));

        target = Math.Clamp(target, 1, 100);

        int current;
        bool active;
        lock (_gate)
        {
            current = _flowCurrent;
            active = _flowActive;
        }

        // Deliberately coarse: the bed should follow the song, not flood the
        // BLE link at the expense of hit timing.
        if (active && Math.Abs(target - current) < 3) return;

        int next = active ? current + (target - current) / 2 : target;
        if (Math.Abs(next - target) <= 1) next = target;
        next = Math.Clamp(next, 1, 100);
        if (active && next == current) return;

        try
        {
            _server.Constant("osu_flow", next);
        }
        catch
        {
            return;
        }

        lock (_gate)
        {
            _flowCurrent = next;
            _flowActive = true;
        }
    }

    private void StopFlow(bool force = false)
    {
        bool active;
        lock (_gate)
        {
            active = _flowActive;
            _flowActive = false;
            _flowCurrent = 0;
        }
        if (!active && !force) return;

        try
        {
            _server.ConstantEnd("osu_flow");
        }
        catch
        {
        }
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
        bool pausedNow = playing && Bool(root, "game", "paused");
        bool kiai = Bool(root, "beatmap", "isKiai");
        bool isBreak = Bool(root, "beatmap", "isBreak");

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
        bool resumePing = false;
        bool missBuzz = false;
        bool stopFlow = false;
        int duck = 0;
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
            _kiai = playing && kiai;
            _break = playing && isBreak;

            bool begin = playing && !_playing;
            bool end = !playing && _playing;
            bool wasPaused = _paused;
            _paused = pausedNow;

            if (begin)
            {
                _playing = true;
                _preciseBase = -1;
                _playHits = 0;
                _preciseSeenHits = 0;
                _fullHitBase = totalHits - misses;
                _missesSeen = misses;
                _lastHitAt = 0;
                _duckUntil = 0;
                _comboBurstPending = false;
                _comboMilestone = 0;
                startPing = _enabled && _startPing && !_paused;
            }
            else if (end)
            {
                _playing = false;
                _preciseBase = -1;
                _playHits = 0;
                _preciseSeenHits = 0;
                _fullHitBase = totalHits - misses;
                _missesSeen = misses;
                _lastHitAt = 0;
                _duckUntil = 0;
                _comboBurstPending = false;
                stopFlow = true;
            }
            else if (pausedNow && !wasPaused)
            {
                _lastHitAt = 0;
                stopFlow = true;
            }
            else if (!pausedNow && wasPaused && _playing)
            {
                _lastHitAt = 0;
                resumePing = _enabled && _startPing;
            }

            if (_playing && !_paused && combo >= 50)
            {
                int milestone = combo / 50;
                if (milestone > _comboMilestone)
                {
                    _comboMilestone = milestone;
                    _comboBurstPending = true;
                }
            }
            else if (combo < 50)
            {
                _comboMilestone = 0;
            }

            if (!_playing || _paused)
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
                    _duckUntil = Environment.TickCount64 + 450;
                    _comboBurstPending = false;
                    duck = _flow > 0 ? Math.Max(8, _flow / 3) : 0;
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

        if (stopFlow) StopFlow();
        if (startPing || resumePing) UpdateFlow();

        if (startPing)
        {
            _server.Burst("osu_start", Math.Clamp(Math.Max(_intensity, 65), 10, 100), 160, 2);
        }
        if (resumePing)
        {
            _server.Burst("osu_resume", 55, 120, 1);
        }
        if (missBuzz)
        {
            // Drop the bed first so the phone does not raise the miss thud to
            // the old flow level. The flow loop restores it after the duck.
            if (duck > 0)
            {
                try
                {
                    _server.Constant("osu_flow", duck);
                }
                catch
                {
                }
                lock (_gate)
                {
                    _flowCurrent = duck;
                    _flowActive = true;
                }
            }

            _server.Burst("osu_miss",
                duck > 0 ? Math.Clamp(duck + 6, 15, 60) : Math.Max(15, _intensity - 25),
                300, 1);
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

            if (!_playing || _paused || !_enabled)
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
        int flow;
        bool kiai;
        bool milestone;
        string id;
        lock (_gate)
        {
            if (_paused) return;

            _lastHitAt = Environment.TickCount64;
            _playHits++;
            count = _playHits;
            flow = _flowCurrent;
            kiai = _kiai;
            milestone = _comboBurstPending;
            if (milestone) _comboBurstPending = false;

            _hitToggle ^= 1;
            id = _hitToggle == 0 ? "osu_hit_a" : "osu_hit_b";
        }

        // A 50-combo crossing feels like a small event rather than another tick.
        if (milestone)
        {
            _server.Burst("osu_combo", 97, 300, 3);
            return;
        }

        int level = Math.Max(_intensity + (kiai ? 6 : 0), flow + 18) + (perfect ? 4 : 0);
        level = Math.Clamp(level, 10, 100);

        if (_accentEvery > 0 && count % _accentEvery == 0)
        {
            int accentLevel = Math.Clamp(Math.Max(level + 10, flow + 32), 10, 100);
            _server.Burst("osu_accent", accentLevel, 170, 1);
        }
        else
        {
            // 90 ms gives two core-haptics taps; 110 ms on a perfect gives a
            // slightly rounder peak without turning into a double-hit.
            _server.Burst(id, level, perfect ? 110 : 90, 1);
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

    private static bool Bool(JsonElement element, params string[] path)
    {
        if (!TryGet(element, out JsonElement value, path)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number))
        {
            return number != 0;
        }
        if (value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out bool parsed))
        {
            return parsed;
        }
        return false;
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
