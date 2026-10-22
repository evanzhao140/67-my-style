namespace PlantPulse;

/// <summary>
/// Turns a stream of mono audio samples into the two things music sync needs:
/// a smoothed loudness value and a beat grid inferred from transients.
///
/// The detector deliberately favours short, mobile bursts over a full FFT:
/// it low-passes the signal, watches for sharp energy rises, then scores
/// candidate periods against the last few seconds of onsets. That is plenty
/// for haptics and keeps the audio callback cheap.
/// </summary>
public sealed class MusicAnalyzer
{
    public const double MinPeriod = 0.3333; // 180 BPM
    public const double MaxPeriod = 1.0;    // 60 BPM

    private const int HistoryFrames = 150;   // 1.5 s at 100 frames/s
    private const int MaxOnsets = 32;
    private const double FrameSeconds = 0.01;
    private const double RefractorySeconds = 0.13;
    private const double OnsetTolerance = 0.055;

    private readonly int _sampleRate;
    private readonly int _frameSize;
    private readonly float[] _frame;
    private int _frameFill;
    private long _processedSamples;

    private readonly double[] _lowHistory = new double[HistoryFrames];
    private readonly double[] _fullHistory = new double[HistoryFrames];
    private int _historyCount;
    private int _historyIndex;

    private readonly Biquad _lowPass;
    private readonly List<OnsetPoint> _onsets = new();
    private readonly object _gate = new();

    private double _prevLow;
    private double _prevFull;
    private double _volume;
    private double _audioTime;
    private double _lastOnsetTime = -10;
    private double _lastOnsetStrength;
    private long _onsetCount;
    private double _refractoryUntil;

    private int _bpm;
    private bool _tempoLocked;
    private double _period;
    private double _phase;
    private double _nextBeatTime;
    private double _coverage;
    private double _lastTempoAnalyze;
    private double _lastGoodTempoAt;
    private int _tempoVersion;

    public MusicAnalyzer(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _frameSize = Math.Max(32, _sampleRate / 100);
        _frame = new float[_frameSize];
        _lowPass = Biquad.LowPass(150, _sampleRate, 0.707);
    }

    public event Action<OnsetInfo> Onset;

    public int SampleRate => _sampleRate;

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_frame, 0, _frame.Length);
            _frameFill = 0;
            _processedSamples = 0;
            Array.Clear(_lowHistory, 0, _lowHistory.Length);
            Array.Clear(_fullHistory, 0, _fullHistory.Length);
            _historyCount = 0;
            _historyIndex = 0;
            _lowPass.Reset();
            _onsets.Clear();
            _prevLow = 0;
            _prevFull = 0;
            _volume = 0;
            _audioTime = 0;
            _lastOnsetTime = -10;
            _lastOnsetStrength = 0;
            _onsetCount = 0;
            _refractoryUntil = 0;
            _bpm = 0;
            _tempoLocked = false;
            _period = 0;
            _phase = 0;
            _nextBeatTime = 0;
            _coverage = 0;
            _lastTempoAnalyze = 0;
            _lastGoodTempoAt = 0;
            _tempoVersion = 0;
        }
    }

    /// <summary>Accepts any block size; frames are assembled internally at 10 ms.</summary>
    public void Process(float[] samples, int count)
    {
        if (samples == null || count <= 0) return;

        OnsetInfo? pending = null;
        lock (_gate)
        {
            for (int i = 0; i < count; i++)
            {
                _frame[_frameFill++] = samples[i];
                if (_frameFill < _frameSize) continue;
                OnsetInfo? detected = ProcessFrame();
                if (detected.HasValue) pending = detected;
            }
        }

        if (pending.HasValue) Onset?.Invoke(pending.Value);
    }

    public MusicSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new MusicSnapshot(
                _audioTime,
                _volume,
                _bpm,
                _tempoLocked,
                _period,
                _nextBeatTime,
                _lastOnsetTime,
                _lastOnsetStrength,
                _onsetCount,
                _tempoVersion,
                _coverage);
        }
    }

    /// <summary>
    /// The scheduler has fired the beat at <paramref name="scheduledTime"/>.
    /// Advance only if the grid has not already moved on in the meantime.
    /// </summary>
    public void AdvanceBeat(double scheduledTime)
    {
        lock (_gate)
        {
            if (!_tempoLocked || _period <= 0) return;
            if (Math.Abs(_nextBeatTime - scheduledTime) > _period * 0.2) return;

            _nextBeatTime += _period;
            if (_nextBeatTime < _audioTime - _period)
            {
                double missed = Math.Floor((_audioTime - _nextBeatTime) / _period) + 1;
                _nextBeatTime += missed * _period;
            }
        }
    }

    private OnsetInfo? ProcessFrame()
    {
        _frameFill = 0;

        double fullSum = 0;
        double lowSum = 0;
        for (int i = 0; i < _frame.Length; i++)
        {
            float x = _frame[i];
            double low = _lowPass.Process(x);
            fullSum += x * (double)x;
            lowSum += low * low;
        }

        _processedSamples += _frame.Length;
        _audioTime = _processedSamples / (double)_sampleRate;

        double fullRms = Math.Sqrt(fullSum / _frame.Length);
        double lowRms = Math.Sqrt(lowSum / _frame.Length);

        UpdateVolume(fullRms);

        bool hasBaseline = _historyCount >= 30;
        bool canTrigger = hasBaseline && _audioTime >= 0.3 && _audioTime >= _refractoryUntil;
        double lowZ = 0;
        double fullZ = 0;

        if (hasBaseline)
        {
            Stats(_lowHistory, _historyCount, out double lowMean, out double lowStd);
            Stats(_fullHistory, _historyCount, out double fullMean, out double fullStd);
            lowZ = (lowRms - lowMean) / (lowStd + 0.0006);
            fullZ = (fullRms - fullMean) / (fullStd + 0.0015);

            bool lowRise = lowRms > _prevLow * 1.10 + 0.0008;
            bool fullRise = fullRms > _prevFull * 1.08 + 0.0020;
            bool lowHit = canTrigger && lowZ > 1.30 && lowRise && lowRms > 0.0015;
            bool fullHit = canTrigger && fullZ > 1.50 && fullRise && fullRms > 0.0030;

            if (lowHit || fullHit)
            {
                double strength = Math.Clamp(Math.Max(lowZ, fullZ), 0.5, 8);
                _lastOnsetTime = _audioTime;
                _lastOnsetStrength = strength;
                _onsetCount++;
                _refractoryUntil = _audioTime + RefractorySeconds;
                _onsets.Add(new OnsetPoint(_audioTime, strength));
                TrimOnsets();

                MaybeUpdateTempo(_audioTime);
                NudgePhase(_audioTime);

                OnsetInfo info = new(
                    _audioTime,
                    strength,
                    _volume,
                    _tempoLocked,
                    _onsetCount);
                AddHistory(_lowHistory, lowRms);
                AddHistory(_fullHistory, fullRms);
                _prevLow = lowRms;
                _prevFull = fullRms;
                return info;
            }
        }

        AddHistory(_lowHistory, lowRms);
        AddHistory(_fullHistory, fullRms);
        _prevLow = lowRms;
        _prevFull = fullRms;

        if (_tempoLocked && _audioTime - _lastOnsetTime > 4.0)
        {
            UnlockTempo("no onsets");
        }

        return null;
    }

    private void UpdateVolume(double fullRms)
    {
        // dBFS range chosen for ordinary music: -48 dB is barely audible,
        // -8 dB is a loud master. The bed never relies on this reaching zero.
        double db = 20.0 * Math.Log10(fullRms + 1e-9);
        double target = Math.Clamp((db + 48.0) / 40.0, 0.0, 1.0);
        double alpha = target > _volume ? 0.28 : 0.045;
        _volume += (target - _volume) * alpha;
    }

    private void TrimOnsets()
    {
        double cutoff = _audioTime - 8.0;
        while (_onsets.Count > 0 && _onsets[0].Time < cutoff) _onsets.RemoveAt(0);
        while (_onsets.Count > MaxOnsets) _onsets.RemoveAt(0);
    }

    private void MaybeUpdateTempo(double now)
    {
        if (_onsets.Count < 6) return;
        if (now - _lastTempoAnalyze < 0.35) return;
        _lastTempoAnalyze = now;

        // Candidate periods come from every pair in the recent onset train.
        // Pairwise deltas catch both steady beats and syncopated patterns, and
        // are then folded into the haptic-friendly 60-180 BPM octave.
        Dictionary<int, double> bins = new();
        double oldest = _onsets[0].Time;
        double span = Math.Max(0.001, now - oldest);

        for (int i = 0; i < _onsets.Count; i++)
        {
            OnsetPoint a = _onsets[i];
            double recencyA = 0.45 + 0.55 * Math.Clamp((a.Time - oldest) / span, 0, 1);
            for (int j = i + 1; j < _onsets.Count; j++)
            {
                OnsetPoint b = _onsets[j];
                double delta = b.Time - a.Time;
                if (delta < 0.24 || delta > 2.0) continue;

                double period = FoldPeriod(delta);
                if (period < MinPeriod - 0.001 || period > MaxPeriod + 0.001) continue;

                double recencyB = 0.45 + 0.55 * Math.Clamp((b.Time - oldest) / span, 0, 1);
                double weight = Math.Sqrt(a.Strength * b.Strength)
                    * Math.Sqrt(recencyA * recencyB);
                if (weight <= 0) weight = 0.01;

                int key = (int)Math.Round(period * 200); // 5 ms buckets
                bins[key] = bins.TryGetValue(key, out double existing)
                    ? existing + weight
                    : weight;
            }
        }

        if (bins.Count == 0) return;

        List<KeyValuePair<int, double>> candidates = bins
            .OrderByDescending(pair => pair.Value)
            .Take(10)
            .ToList();

        Candidate best = default;
        bool haveBest = false;
        double totalWeight = 0;
        foreach (OnsetPoint point in _onsets)
        {
            double recency = 0.45 + 0.55 * Math.Clamp((point.Time - oldest) / span, 0, 1);
            totalWeight += point.Strength * recency;
        }
        if (totalWeight <= 0) return;

        foreach (KeyValuePair<int, double> pair in candidates)
        {
            double period = pair.Key / 200.0;
            int bpm = (int)Math.Round(60.0 / period);

            // Try a phase anchored on every recent onset. Scoring against all
            // onsets handles a missing kick or a syncopated note gracefully.
            double bestCoverage = 0;
            double bestPhase = 0;
            int bestMatches = 0;
            foreach (OnsetPoint anchor in _onsets)
            {
                double phase = Wrap(anchor.Time, period);
                double matched = 0;
                int matches = 0;
                foreach (OnsetPoint point in _onsets)
                {
                    double distance = DistanceToGrid(point.Time, phase, period);
                    if (distance > OnsetTolerance) continue;
                    double recency = 0.45 + 0.55 * Math.Clamp((point.Time - oldest) / span, 0, 1);
                    matched += point.Strength * recency * (1.0 - distance / OnsetTolerance);
                    matches++;
                }

                double coverage = matched / totalWeight;
                if (coverage > bestCoverage)
                {
                    bestCoverage = coverage;
                    bestPhase = phase;
                    bestMatches = matches;
                }
            }

            if (bestMatches < 3) continue;

            double prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120.0) / 0.55, 2));
            double score = bestCoverage + prior * 0.08;
            if (!haveBest || score > best.Score)
            {
                haveBest = true;
                best = new Candidate(period, bestPhase, bestCoverage, bpm, score);
            }
        }

        if (!haveBest) return;

        bool accept;
        if (!_tempoLocked)
        {
            accept = best.Coverage >= 0.48;
        }
        else
        {
            bool different = best.Bpm != _bpm
                && Math.Abs(best.Period - _period) > 0.015;
            accept = best.Coverage >= 0.45
                && (different || best.Coverage > _coverage + 0.10);
        }

        if (!accept) return;

        bool wasLocked = _tempoLocked;
        int oldBpm = _bpm;
        bool tempoChanged = !wasLocked || Math.Abs(best.Bpm - oldBpm) >= 2;

        _period = best.Period;
        _phase = best.Phase;
        _bpm = best.Bpm;
        _coverage = best.Coverage;
        _tempoLocked = true;
        _lastGoodTempoAt = now;
        _nextBeatTime = NextGrid(now, _phase, _period);

        if (tempoChanged)
        {
            _tempoVersion++;
        }
    }

    private void NudgePhase(double now)
    {
        if (!_tempoLocked || _period <= 0) return;

        double distance = DistanceToGrid(now, _phase, _period);
        if (distance <= 0.004 || distance > 0.065) return;

        double nearest = NearestGrid(now, _phase, _period);
        double error = now - nearest;
        _phase = Wrap(_phase + error * 0.15, _period);
        _nextBeatTime = NextGrid(now, _phase, _period);
    }

    private void UnlockTempo(string reason)
    {
        if (!_tempoLocked) return;
        _tempoLocked = false;
        _bpm = 0;
        _period = 0;
        _phase = 0;
        _nextBeatTime = 0;
        _coverage = 0;
        _tempoVersion++;
        Log.Line("music: tempo released (" + reason + ")");
    }

    private static double FoldPeriod(double period)
    {
        while (period < MinPeriod) period *= 2;
        while (period > MaxPeriod) period /= 2;
        return period;
    }

    private static double Wrap(double value, double period)
    {
        if (period <= 0) return 0;
        double wrapped = value % period;
        return wrapped < 0 ? wrapped + period : wrapped;
    }

    private static double NearestGrid(double time, double phase, double period)
    {
        double steps = Math.Round((time - phase) / period);
        return phase + steps * period;
    }

    private static double DistanceToGrid(double time, double phase, double period)
    {
        return Math.Abs(time - NearestGrid(time, phase, period));
    }

    private static double NextGrid(double after, double phase, double period)
    {
        double steps = Math.Ceiling((after - phase) / period);
        double next = phase + steps * period;
        if (next <= after + 0.001) next += period;
        return next;
    }

    private void AddHistory(double[] history, double value)
    {
        history[_historyIndex] = value;
        _historyIndex = (_historyIndex + 1) % HistoryFrames;
        if (_historyCount < HistoryFrames) _historyCount++;
    }

    private static void Stats(double[] history, int count, out double mean, out double std)
    {
        double sum = 0;
        for (int i = 0; i < count; i++) sum += history[i];
        mean = sum / count;
        double squares = 0;
        for (int i = 0; i < count; i++)
        {
            double difference = history[i] - mean;
            squares += difference * difference;
        }
        std = Math.Sqrt(squares / count);
    }

    private readonly record struct OnsetPoint(double Time, double Strength);

    private readonly record struct Candidate(
        double Period,
        double Phase,
        double Coverage,
        int Bpm,
        double Score);

    private struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public static Biquad LowPass(double cutoff, int sampleRate, double q)
        {
            double w0 = 2.0 * Math.PI * Math.Clamp(cutoff, 20, sampleRate * 0.45) / sampleRate;
            double cos = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * q);
            double a0 = 1.0 + alpha;
            return new Biquad
            {
                _b0 = (1.0 - cos) * 0.5 / a0,
                _b1 = (1.0 - cos) / a0,
                _b2 = (1.0 - cos) * 0.5 / a0,
                _a1 = -2.0 * cos / a0,
                _a2 = (1.0 - alpha) / a0,
            };
        }

        public double Process(double input)
        {
            double output = _b0 * input + _z1;
            _z1 = _b1 * input - _a1 * output + _z2;
            _z2 = _b2 * input - _a2 * output;
            return output;
        }

        public void Reset()
        {
            _z1 = 0;
            _z2 = 0;
        }
    }
}

public readonly record struct OnsetInfo(
    double Time,
    double Strength,
    double Volume,
    bool TempoLocked,
    long Count);

public readonly record struct MusicSnapshot(
    double AudioTime,
    double Volume,
    int Bpm,
    bool TempoLocked,
    double Period,
    double NextBeatTime,
    double LastOnsetTime,
    double LastOnsetStrength,
    long OnsetCount,
    int TempoVersion,
    double Coverage);
