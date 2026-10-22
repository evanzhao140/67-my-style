using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PlantPulse;

/// <summary>
/// Direct system-audio sync. Captures the default Windows output with WASAPI
/// loopback, derives loudness and a beat grid locally, then streams:
///
///  - a continuous music_flow bed that is never below 50% while listening;
///  - music_beat peaks at the detected tempo;
///  - an occasional music_accent every four beats.
///
/// There is no websocket or game reader in this path. The capture buffer is
/// 20 ms where Windows allows it, and the beat scheduler sends a little early
/// (the Lead control) to compensate for Bluetooth/phone output latency.
/// </summary>
public sealed class MusicSyncEngine : IDisposable
{
    private const double FreshDataSeconds = 0.5;
    private static readonly Guid IeeeFloatSubtype =
        new("00000003-0000-0010-8000-00aa00389b71");

    private readonly HapticServer _server;
    private readonly object _lifecycle = new();
    private readonly object _stateGate = new();
    private readonly object _clockGate = new();

    private WasapiCapture _capture;
    private MMDevice _device;
    private MusicAnalyzer _analyzer;
    private Thread _bedThread;
    private Thread _beatThread;
    private volatile bool _running;
    private bool _disposed;

    private volatile int _swell = 30;
    private volatile int _punch = 84;
    private volatile int _leadMs = 40;

    private string _deviceName = "";
    private string _error = "";
    private int _bufferMs;
    private int _sampleRate;
    private int _channels;
    private int _bitsPerSample;
    private bool _floatSamples;
    private bool _dataErrorLogged;

    private int _bedCurrent;
    private int _peakCurrent;
    private long _beatCount;
    private long _beatsFired;
    private long _lastBeatTicks;
    private int _lastTempoVersion = -1;

    private float[] _mono = Array.Empty<float>();
    private long _audioSamples;
    private double _audioAnchor;
    private long _wallAnchor;
    private long _lastDataTicks;

    public MusicSyncEngine(HapticServer server)
    {
        _server = server;
    }

    public bool Running => _running;

    public string DeviceName
    {
        get { lock (_stateGate) return _deviceName; }
    }

    public string Error
    {
        get { lock (_stateGate) return _error; }
    }

    public void SetOptions(int swell, int punch, int leadMs)
    {
        _swell = Math.Clamp(swell, 0, 40);
        _punch = Math.Clamp(punch, 50, 100);
        _leadMs = Math.Clamp(leadMs, 0, 200);
    }

    /// <summary>The bed formula: a hard 50% floor, then volume lifts it.</summary>
    public static int BedForVolume(int swell, double volume)
    {
        double lift = Math.Clamp(swell, 0, 40) * Math.Clamp(volume, 0, 1);
        return Math.Clamp(50 + (int)Math.Round(lift), 50, 100);
    }

    public MusicStatus GetStatus()
    {
        MusicAnalyzer analyzer = _analyzer;
        MusicSnapshot snapshot = analyzer != null ? analyzer.Snapshot() : default;
        bool fresh = DataIsFresh(Stopwatch.GetTimestamp());

        lock (_stateGate)
        {
            int volume = (int)Math.Round((fresh ? snapshot.Volume : 0.0) * 100);
            int peak = fresh ? _peakCurrent : _bedCurrent;
            bool tempoLocked = fresh && snapshot.TempoLocked;
            return new MusicStatus(
                _running,
                _deviceName,
                _error,
                tempoLocked ? snapshot.Bpm : 0,
                tempoLocked,
                volume,
                _bedCurrent,
                peak,
                _beatCount,
                _swell,
                _punch,
                _leadMs,
                _bufferMs,
                snapshot.Coverage);
        }
    }

    public bool Start(out string error)
    {
        lock (_lifecycle)
        {
            if (_running)
            {
                error = "";
                return true;
            }

            if (_disposed)
            {
                error = "Music sync has been closed.";
                return false;
            }

            // A capture can stop on its own (device change, sleep/resume). If
            // the panel is restarted before the old workers have noticed, make
            // sure they are gone before assigning a new _running state.
            Thread oldBed = _bedThread;
            Thread oldBeat = _beatThread;
            _bedThread = null;
            _beatThread = null;
            _running = false;
            if (oldBed != null && oldBed.IsAlive && oldBed != Thread.CurrentThread) oldBed.Join(1000);
            if (oldBeat != null && oldBeat.IsAlive && oldBeat != Thread.CurrentThread) oldBeat.Join(1000);

            CleanupCapture();
            lock (_stateGate)
            {
                _error = "";
                _deviceName = "";
                _bedCurrent = 0;
                _peakCurrent = 0;
                _beatCount = 0;
                _beatsFired = 0;
                _lastBeatTicks = 0;
                _lastTempoVersion = -1;
                _dataErrorLogged = false;
            }
            lock (_clockGate)
            {
                _audioSamples = 0;
                _audioAnchor = 0;
                _wallAnchor = Stopwatch.GetTimestamp();
                _lastDataTicks = _wallAnchor;
            }

            if (!OpenCapture(out error))
            {
                lock (_stateGate) _error = error;
                CleanupCapture();
                return false;
            }

            _running = true;
            _server.Constant("music_flow", 50);
            lock (_stateGate)
            {
                _bedCurrent = 50;
                _peakCurrent = 50;
            }

            _bedThread = new Thread(BedLoop) { IsBackground = true, Name = "music-bed" };
            _beatThread = new Thread(BeatLoop) { IsBackground = true, Name = "music-beat" };
            _bedThread.Start();
            _beatThread.Start();

            Log.Line($"music: listening to {DeviceName} " +
                     $"({_bufferMs} ms buffer, {_sampleRate} Hz, {_channels} ch)");
            error = "";
            return true;
        }
    }

    public void Stop()
    {
        WasapiCapture capture;
        Thread bed;
        Thread beat;

        lock (_lifecycle)
        {
            if (!_running && _capture == null && _bedThread == null && _beatThread == null) return;
            _running = false;
            capture = _capture;
            _capture = null;
            bed = _bedThread;
            beat = _beatThread;
            _bedThread = null;
            _beatThread = null;
        }

        try
        {
            capture?.StopRecording();
        }
        catch
        {
        }

        if (bed != null && bed.IsAlive && bed != Thread.CurrentThread) bed.Join(1000);
        if (beat != null && beat.IsAlive && beat != Thread.CurrentThread) beat.Join(1000);

        MusicAnalyzer analyzer = _analyzer;
        _analyzer = null;
        if (analyzer != null) analyzer.Onset -= OnOnset;

        try
        {
            capture?.Dispose();
        }
        catch
        {
        }

        try
        {
            _device?.Dispose();
        }
        catch
        {
        }
        _device = null;

        lock (_stateGate)
        {
            _server.ConstantEnd("music_flow");
            _bedCurrent = 0;
            _peakCurrent = 0;
        }

        Log.Line("music: stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private bool OpenCapture(out string error)
    {
        error = "";
        try
        {
            using MMDeviceEnumerator enumerator = new();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_device == null)
            {
                error = "No default audio output device was found.";
                return false;
            }
            lock (_stateGate) _deviceName = _device.FriendlyName;
        }
        catch (Exception ex)
        {
            error = "No audio output device is available (" + ex.Message + "). " +
                    "Play something through the default speakers or headphones and try again.";
            return false;
        }

        // Prefer a tiny event-driven loopback buffer. Some drivers reject the
        // low-latency request, so fall back to NAudio's default loopback path
        // rather than failing the whole feature.
        try
        {
            LowLatencyLoopbackCapture lowLatency = new(_device);
            if (TryStartCapture(lowLatency, 20, out error)) return true;
            Log.Line("music: low-latency capture unavailable (" + error + "); retrying default");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Line("music: low-latency capture could not be created (" + ex.Message + "); retrying default");
        }

        try
        {
            WasapiLoopbackCapture fallback = new(_device);
            if (TryStartCapture(fallback, 100, out error)) return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (string.IsNullOrEmpty(error))
        {
            error = "Could not start system audio capture.";
        }
        error = "Could not start system audio capture: " + error;
        return false;
    }

    private bool TryStartCapture(WasapiCapture capture, int bufferMs, out string error)
    {
        error = "";
        MusicAnalyzer analyzer = null;

        try
        {
            WaveFormat format = capture.WaveFormat;
            if (!SupportedFormat(format))
            {
                error = $"unsupported audio format ({format.Encoding}, {format.BitsPerSample}-bit)";
                capture.Dispose();
                return false;
            }

            analyzer = new MusicAnalyzer(format.SampleRate);
            analyzer.Onset += OnOnset;

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();

            _capture = capture;
            _analyzer = analyzer;
            _sampleRate = format.SampleRate;
            _channels = format.Channels;
            _bitsPerSample = format.BitsPerSample;
            _floatSamples = format.Encoding == WaveFormatEncoding.IeeeFloat
                || (format is WaveFormatExtensible extensible
                    && extensible.SubFormat == IeeeFloatSubtype);
            _bufferMs = bufferMs;

            lock (_clockGate)
            {
                _audioSamples = 0;
                _audioAnchor = 0;
                _wallAnchor = Stopwatch.GetTimestamp();
                _lastDataTicks = _wallAnchor;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (analyzer != null) analyzer.Onset -= OnOnset;
            try
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.StopRecording();
            }
            catch
            {
            }
            try
            {
                capture.Dispose();
            }
            catch
            {
            }
            return false;
        }
    }

    private static bool SupportedFormat(WaveFormat format)
    {
        if (format == null || format.Channels <= 0) return false;
        return format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat => format.BitsPerSample == 32,
            WaveFormatEncoding.Pcm => format.BitsPerSample is 16 or 24 or 32,
            WaveFormatEncoding.Extensible => format.BitsPerSample is 16 or 24 or 32,
            _ => false,
        };
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        if (!_running) return;
        try
        {
            int bytesPerSample = _bitsPerSample / 8;
            int bytesPerFrame = bytesPerSample * _channels;
            int frames = e.BytesRecorded / bytesPerFrame;
            if (frames <= 0) return;

            if (_mono.Length < frames) _mono = new float[frames];
            Decode(e.Buffer, frames);

            MusicAnalyzer analyzer = _analyzer;
            if (analyzer == null) return;
            analyzer.Process(_mono, frames);

            long now = Stopwatch.GetTimestamp();
            lock (_clockGate)
            {
                _audioSamples += frames;
                _audioAnchor = _audioSamples / (double)_sampleRate;
                _wallAnchor = now;
                _lastDataTicks = now;
            }
        }
        catch (Exception ex)
        {
            if (!_dataErrorLogged)
            {
                _dataErrorLogged = true;
                lock (_stateGate) _error = "Audio read error: " + ex.Message;
                Log.Line("music: audio read error (" + ex.Message + ")");
                _running = false;
                lock (_stateGate)
                {
                    _server.ConstantEnd("music_flow");
                    _bedCurrent = 0;
                    _peakCurrent = 0;
                }
            }
        }
    }

    private void Decode(byte[] buffer, int frames)
    {
        int bytesPerSample = _bitsPerSample / 8;
        int offset = 0;

        for (int frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            for (int channel = 0; channel < _channels; channel++)
            {
                float sample;
                if (_floatSamples)
                {
                    sample = BitConverter.ToSingle(buffer, offset);
                }
                else if (_bitsPerSample == 16)
                {
                    sample = BitConverter.ToInt16(buffer, offset) / 32768f;
                }
                else if (_bitsPerSample == 24)
                {
                    int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                    if ((value & 0x800000) != 0) value -= 0x1000000;
                    sample = value / 8388608f;
                }
                else if (_bitsPerSample == 32)
                {
                    sample = BitConverter.ToInt32(buffer, offset) / 2147483648f;
                }
                else
                {
                    sample = 0;
                }

                sum += sample;
                offset += bytesPerSample;
            }

            float mono = sum / _channels;
            _mono[frame] = Math.Clamp(mono, -1f, 1f);
        }
    }

    private void OnOnset(OnsetInfo info)
    {
        if (!_running || info.TempoLocked) return;

        // Before the tempo grid has locked, transients still produce an
        // immediate peak. Once locked, the scheduler owns the pulse timing and
        // can send early by the Lead amount.
        FireBeat(info.Volume, info.Strength, false, true);
    }

    private void BedLoop()
    {
        while (_running)
        {
            try
            {
                MusicAnalyzer analyzer = _analyzer;
                MusicSnapshot snapshot = analyzer != null ? analyzer.Snapshot() : default;
                bool fresh = DataIsFresh(Stopwatch.GetTimestamp());
                int target = BedForVolume(_swell, fresh ? snapshot.Volume : 0.0);

                lock (_stateGate)
                {
                    if (!_running) break;
                    if (Math.Abs(target - _bedCurrent) >= 2 || _bedCurrent == 0)
                    {
                        _server.Constant("music_flow", target);
                        _bedCurrent = target;
                    }
                }

                if (snapshot.TempoVersion != _lastTempoVersion)
                {
                    _lastTempoVersion = snapshot.TempoVersion;
                    if (snapshot.TempoLocked)
                    {
                        Log.Line($"music: tempo {snapshot.Bpm} BPM " +
                                 $"(coverage {snapshot.Coverage:0.00})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("music: bed loop " + ex.Message);
            }

            Thread.Sleep(110);
        }
    }

    private void BeatLoop()
    {
        while (_running)
        {
            int sleepMs = 4;
            try
            {
                MusicAnalyzer analyzer = _analyzer;
                if (analyzer == null)
                {
                    Thread.Sleep(20);
                    continue;
                }

                MusicSnapshot snapshot = analyzer.Snapshot();
                long nowTicks = Stopwatch.GetTimestamp();
                bool fresh;
                lock (_clockGate) fresh = nowTicks - _lastDataTicks < FreshDataSeconds * Stopwatch.Frequency;

                if (snapshot.TempoLocked && fresh && snapshot.NextBeatTime > 0)
                {
                    double nowAudio = CurrentAudioTime(nowTicks);
                    bool recentMusic = snapshot.LastOnsetTime > 0
                        && nowAudio - snapshot.LastOnsetTime
                            < Math.Max(2.5, snapshot.Period * 3.0);
                    if (!recentMusic)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    double due = snapshot.NextBeatTime - _leadMs / 1000.0;
                    double remaining = due - nowAudio;

                    if (remaining <= 0)
                    {
                        // Do not fire a beat that is very old after a stall;
                        // advance the grid instead.
                        if (nowAudio - snapshot.NextBeatTime < 0.35)
                        {
                            bool accent;
                            lock (_stateGate) accent = _beatsFired % 4 == 0;
                            FireBeat(snapshot.Volume, snapshot.LastOnsetStrength, accent, false);
                        }
                        analyzer.AdvanceBeat(snapshot.NextBeatTime);
                    }
                    else if (remaining < 0.012)
                    {
                        sleepMs = 0;
                    }
                    else if (remaining < 0.03)
                    {
                        sleepMs = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("music: beat loop " + ex.Message);
                sleepMs = 20;
            }

            if (!_running) break;
            if (sleepMs <= 0)
            {
                Thread.SpinWait(200);
            }
            else
            {
                Thread.Sleep(sleepMs);
            }
        }
    }

    private double CurrentAudioTime(long nowTicks)
    {
        lock (_clockGate)
        {
            double elapsed = (nowTicks - _wallAnchor) / (double)Stopwatch.Frequency;
            return _audioAnchor + Math.Clamp(elapsed, 0, 0.25);
        }
    }

    private bool DataIsFresh(long nowTicks)
    {
        lock (_clockGate)
        {
            return nowTicks - _lastDataTicks < FreshDataSeconds * Stopwatch.Frequency;
        }
    }

    private void FireBeat(double volume, double strength, bool accent, bool immediate)
    {
        if (!_running) return;

        long now = Stopwatch.GetTimestamp();
        long minimumGap = immediate ? 80 : 100;
        int bed = BedForVolume(_swell, volume);
        int peak = _punch + (int)Math.Round(14.0 * (volume - 0.5));
        if (accent) peak += 6;
        peak = Math.Clamp(peak, 50, 100);
        peak = Math.Max(peak, bed + 8);
        peak = Math.Min(peak, 100);

        int onsetLift = (int)Math.Round(Math.Clamp(strength - 1.0, 0, 3));
        peak = Math.Min(100, peak + onsetLift);

        int duration = accent
            ? 160 + (int)Math.Round(35 * volume)
            : 100 + (int)Math.Round(40 * volume);
        int pulses = accent ? 3 : 2;
        string id = accent ? "music_accent" : "music_beat";

        lock (_stateGate)
        {
            if (!_running) return;
            if (now - _lastBeatTicks < minimumGap * Stopwatch.Frequency / 1000) return;
            _lastBeatTicks = now;
            _beatCount++;
            _beatsFired++;
            _peakCurrent = peak;
        }

        _server.Burst(id, peak, duration, pulses);
    }

    private void OnRecordingStopped(object sender, StoppedEventArgs e)
    {
        if (!_running) return;

        string message = e.Exception != null ? e.Exception.Message : "the audio device stopped";
        lock (_stateGate) _error = "Audio capture stopped: " + message;
        _running = false;

        lock (_stateGate)
        {
            _server.ConstantEnd("music_flow");
            _bedCurrent = 0;
            _peakCurrent = 0;
        }

        Log.Line("music: capture stopped (" + message + ")");
    }

    private void CleanupCapture()
    {
        MusicAnalyzer analyzer = _analyzer;
        _analyzer = null;
        if (analyzer != null) analyzer.Onset -= OnOnset;

        try
        {
            _capture?.StopRecording();
        }
        catch
        {
        }
        try
        {
            _capture?.Dispose();
        }
        catch
        {
        }
        _capture = null;

        try
        {
            _device?.Dispose();
        }
        catch
        {
        }
        _device = null;
    }

    private sealed class LowLatencyLoopbackCapture : WasapiCapture
    {
        public LowLatencyLoopbackCapture(MMDevice device) : base(device, true, 20)
        {
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        {
            return base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
        }
    }
}

public readonly record struct MusicStatus(
    bool Running,
    string Device,
    string Error,
    int Bpm,
    bool TempoLocked,
    int Volume,
    int Bed,
    int Peak,
    long Beats,
    int Swell,
    int Punch,
    int Lead,
    int BufferMs,
    double Coverage);
