using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PlantPulse;

/// <summary>
/// Serves the same newline-delimited JSON haptics protocol as the mod:
/// hello / burst / constant / constant_end / clear / constants over TCP 25666,
/// plus UDP discovery on 25667. The phone app connects either directly over
/// Wi-Fi or through the Plant Bridge (BLE), which is itself a TCP client of
/// this server.
/// </summary>
public sealed class HapticServer : IDisposable
{
    public const int Port = 25666;
    public const int DiscoveryPort = 25667;
    public const string DisplayName = "Plant Pulse";

    private const int StateSyncMs = 2000;
    private const int BurstDedupeMs = 60;
    private const int MaxBurstsPerSecond = 20;

    private readonly List<Client> _clients = new();
    private readonly object _clientsLock = new();
    private readonly ConcurrentDictionary<string, int> _constants = new();
    private readonly Dictionary<string, long> _lastBurstById = new();
    private readonly Queue<long> _burstHistory = new();
    private readonly object _rateLock = new();
    private readonly BlockingCollection<string> _outbox = new(new ConcurrentQueue<string>());
    private readonly Thread _acceptThread;
    private readonly Thread _senderThread;
    private readonly Thread _discoveryThread;
    private volatile bool _running = true;
    private TcpListener _listener;
    private UdpClient _udp;
    private long _lastStateSyncAt;

    public HapticServer()
    {
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "pulse-tcp" };
        _senderThread = new Thread(SenderLoop) { IsBackground = true, Name = "pulse-sender" };
        _discoveryThread = new Thread(DiscoveryLoop) { IsBackground = true, Name = "pulse-discovery" };
    }

    /// <summary>True once the TCP listener owns port 25666.</summary>
    public volatile bool TcpReady;

    /// <summary>True once the UDP discovery socket owns port 25667.</summary>
    public volatile bool DiscoveryReady;

    /// <summary>Most recent event id, for the status line.</summary>
    public string LastEvent = "";

    public int ClientCount
    {
        get
        {
            lock (_clientsLock) return _clients.Count;
        }
    }

    public void Start()
    {
        _acceptThread.Start();
        _senderThread.Start();
        _discoveryThread.Start();
    }

    /// <summary>Sends a burst (intensity 0-100). Rate limited per id and overall.</summary>
    public void Burst(string id, int intensity, int durationMs, int pulses)
    {
        if (string.IsNullOrEmpty(id)) return;

        int effective = Math.Clamp(intensity, 1, 100);
        long now = Environment.TickCount64;
        lock (_rateLock)
        {
            if (_lastBurstById.TryGetValue(id, out long last) && now - last < BurstDedupeMs) return;
            _lastBurstById[id] = now;

            while (_burstHistory.Count > 0 && now - _burstHistory.Peek() > 1000) _burstHistory.Dequeue();
            if (_burstHistory.Count >= MaxBurstsPerSecond) return;
            _burstHistory.Enqueue(now);
        }

        LastEvent = id;
        _outbox.Add(JsonSerializer.Serialize(new
        {
            type = "burst",
            id,
            intensity = effective,
            durationMs = Math.Clamp(durationMs, 40, 2000),
            pulses = Math.Clamp(pulses, 1, 5),
        }, Json.Wire));

        // Keep the log readable while high-rate textures stream.
        if (id is not ("heart_tick" or "heart_dub" or "rain_drop" or "warp_tick"
            or "groove_hat" or "groove_tick" or "osu_hit_a" or "osu_hit_b"
            or "osu_accent" or "osu_miss" or "over_kick" or "over_bass"
            or "over_hat" or "over_snare" or "sketch_tap" or "sketch_accent"
            or "music_beat" or "music_accent"))
        {
            Log.Line($"burst {id} {effective}%");
        }
    }

    /// <summary>Starts or updates a continuous vibration source.</summary>
    public void Constant(string id, int intensity)
    {
        int effective = Math.Clamp(intensity, 1, 100);
        if (_constants.TryGetValue(id, out int previous) && previous == effective) return;
        _constants[id] = effective;
        _outbox.Add(JsonSerializer.Serialize(new
        {
            type = "constant",
            id,
            intensity = effective,
        }, Json.Wire));
    }

    /// <summary>Stops one continuous vibration source.</summary>
    public void ConstantEnd(string id)
    {
        if (_constants.TryRemove(id, out _))
        {
            _outbox.Add(JsonSerializer.Serialize(new { type = "constant_end", id }, Json.Wire));
        }
    }

    /// <summary>Stops all continuous vibrations on the phone.</summary>
    public void Clear()
    {
        if (_constants.IsEmpty) return;
        _constants.Clear();
        _outbox.Add(JsonSerializer.Serialize(new { type = "clear" }, Json.Wire));
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                TcpReady = true;
                Log.Line($"tcp: listening on port {Port}");

                while (_running)
                {
                    TcpClient tcp = _listener.AcceptTcpClient();
                    tcp.NoDelay = true;

                    Client client = new(tcp, new StreamWriter(tcp.GetStream(), new UTF8Encoding(false))
                    {
                        AutoFlush = true,
                    });
                    lock (_clientsLock) _clients.Add(client);

                    Log.Line($"tcp: client connected ({tcp.Client.RemoteEndPoint})");
                    client.Write(HelloJson());
                    foreach (KeyValuePair<string, int> pair in _constants) client.Write(ConstantJson(pair.Key, pair.Value));
                    client.Write(SyncJson());

                    Thread reader = new(() => ReadLoop(client)) { IsBackground = true, Name = "pulse-client" };
                    reader.Start();
                }
            }
            catch (Exception ex)
            {
                TcpReady = false;
                if (!_running) return;
                Log.Line($"tcp: port {Port} unavailable ({ex.Message}); retrying in 5 s");
                CloseListener();
                Thread.Sleep(5000);
            }
        }
    }

    private void ReadLoop(Client client)
    {
        try
        {
            using StreamReader reader = new(client.Tcp.GetStream(), new UTF8Encoding(false));
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains("\"hello\"")) continue;
                client.Write(HelloJson());
                foreach (KeyValuePair<string, int> pair in _constants) client.Write(ConstantJson(pair.Key, pair.Value));
                client.Write(SyncJson());
            }
        }
        catch
        {
            // A dropped connection is expected; the read loop is the detector.
        }
        finally
        {
            RemoveClient(client);
        }
    }

    private void SenderLoop()
    {
        while (_running)
        {
            try
            {
                if (_outbox.TryTake(out string line, 250)) SendToAll(line);
                MaybeStateSync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Line("sender: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Re-sends all active constants every two seconds so a phone or bridge that
    /// missed a one-shot message converges quickly.
    /// </summary>
    private void MaybeStateSync()
    {
        long now = Environment.TickCount64;
        if (_constants.IsEmpty || ClientCount == 0 || now - _lastStateSyncAt < StateSyncMs) return;
        _lastStateSyncAt = now;
        SendToAll(SyncJson());
    }

    private void SendToAll(string line)
    {
        Client[] snapshot;
        lock (_clientsLock) snapshot = _clients.ToArray();
        foreach (Client client in snapshot)
        {
            if (!client.Write(line)) RemoveClient(client);
        }
    }

    private void RemoveClient(Client client)
    {
        bool removed;
        lock (_clientsLock) removed = _clients.Remove(client);
        if (!removed) return;
        try
        {
            client.Tcp.Close();
        }
        catch
        {
        }
        Log.Line("tcp: client disconnected");
    }

    private void CloseListener()
    {
        try
        {
            _listener?.Stop();
        }
        catch
        {
        }
        _listener = null;
    }

    private static string HelloJson() => JsonSerializer.Serialize(new
    {
        type = "hello",
        name = DisplayName,
        proto = 1,
    }, Json.Wire);

    private static string ConstantJson(string id, int intensity) => JsonSerializer.Serialize(new
    {
        type = "constant",
        id,
        intensity,
    }, Json.Wire);

    private string SyncJson()
    {
        Dictionary<string, int> active = new(_constants);
        return JsonSerializer.Serialize(new { type = "constants", active }, Json.Wire);
    }

    private void DiscoveryLoop()
    {
        while (_running)
        {
            try
            {
                UdpClient udp = new();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _udp = udp;
                DiscoveryReady = true;
                Log.Line($"udp: discovery on port {DiscoveryPort}");

                while (_running)
                {
                    IPEndPoint from = new(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    string text = Encoding.UTF8.GetString(data);
                    if (!text.StartsWith("PLANTAPI_DISCOVER", StringComparison.Ordinal)) continue;

                    byte[] reply = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        magic = "PLANTAPI",
                        name = DisplayName,
                        port = Port,
                        proto = 1,
                    }, Json.Wire));
                    udp.Send(reply, reply.Length, from);
                }
            }
            catch (Exception ex)
            {
                DiscoveryReady = false;
                if (!_running) return;
                Log.Line($"udp: port {DiscoveryPort} unavailable ({ex.Message}); retrying in 5 s");
                try
                {
                    _udp?.Dispose();
                }
                catch
                {
                }
                Thread.Sleep(5000);
            }
        }
    }

    public void Dispose()
    {
        if (!_running) return;
        _running = false;
        TcpReady = false;
        DiscoveryReady = false;
        CloseListener();
        try
        {
            _udp?.Dispose();
        }
        catch
        {
        }

        Client[] snapshot;
        lock (_clientsLock)
        {
            snapshot = _clients.ToArray();
            _clients.Clear();
        }
        foreach (Client client in snapshot)
        {
            try
            {
                client.Tcp.Close();
            }
            catch
            {
            }
        }

        try
        {
            _outbox.CompleteAdding();
        }
        catch
        {
        }

        _acceptThread.Join(500);
        _senderThread.Join(500);
        _discoveryThread.Join(500);
        _outbox.Dispose();
    }

    private sealed class Client
    {
        private readonly StreamWriter _writer;
        private readonly object _gate = new();

        public Client(TcpClient tcp, StreamWriter writer)
        {
            Tcp = tcp;
            _writer = writer;
        }

        public TcpClient Tcp { get; }

        public bool Write(string line)
        {
            try
            {
                lock (_gate)
                {
                    _writer.Write(line);
                    _writer.Write('\n');
                    _writer.Flush();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

public static class Json
{
    /// <summary>Compact single-line JSON for the newline-delimited protocol.</summary>
    public static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
