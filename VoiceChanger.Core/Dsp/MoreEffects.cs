using NAudio.Dsp;
using NAudio.Wave;

namespace VoiceChanger.Core.Dsp;

/// <summary>Hands samples that were pushed in back out again. Used to split a signal into two paths.</summary>
public sealed class SplitSource(WaveFormat format) : ISampleProvider
{
    private float[] _queue = new float[8192];
    private int _count;

    public WaveFormat WaveFormat => format;

    public void Push(ReadOnlySpan<float> samples)
    {
        if (_count + samples.Length > _queue.Length) Array.Resize(ref _queue, (_count + samples.Length) * 2);
        samples.CopyTo(_queue.AsSpan(_count));
        _count += samples.Length;
    }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = Math.Min(buffer.Length, _count);
        _queue.AsSpan(0, n).CopyTo(buffer);
        Array.Copy(_queue, n, _queue, 0, _count - n);
        _count -= n;
        buffer[n..].Clear();
        return buffer.Length;
    }
}

/// <summary>A second copy of the voice at another pitch/formant, mixed in. Choirs, demons behind you, twins.</summary>
public sealed class Doubler : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly SplitSource _split;
    private readonly FormantPitchShifter _shift;
    private float[] _tmp = new float[8192];

    public Doubler(ISampleProvider source)
    {
        _source = source;
        _split = new SplitSource(source.WaveFormat);
        _shift = new FormantPitchShifter(_split);
    }

    /// <summary>Pitch of the copy relative to the main voice. 0 = off.</summary>
    public float Pitch { get; set; }
    public float Formant { get; set; }
    public float Mix { get; set; } = 0.5f;
    public float Drive { get; set; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        if (Pitch <= 0 || Mix <= 0) return n;
        if (_tmp.Length < n) _tmp = new float[n * 2];

        _shift.PitchFactor = Pitch;
        _shift.FormantFactor = Formant;
        _split.Push(buffer[..n]);
        _shift.Read(_tmp.AsSpan(0, n));

        var gain = 1 + Drive * 12;
        for (var i = 0; i < n; i++)
        {
            var copy = _tmp[i];
            if (Drive > 0) copy = MathF.Tanh(copy * gain) * 0.6f;
            buffer[i] = buffer[i] * (1 - Mix * 0.4f) + copy * Mix;
        }
        return n;
    }
}

/// <summary>16-band channel vocoder: the voice shapes a synthetic carrier (saw wave or noise).</summary>
public sealed class Vocoder : ISampleProvider
{
    private const int Bands = 16;
    private readonly ISampleProvider _source;
    private readonly Func<float> _pitchHz;
    private readonly BiQuadFilter[] _modBands = new BiQuadFilter[Bands];
    private readonly BiQuadFilter[] _carBands = new BiQuadFilter[Bands];
    private readonly float[] _env = new float[Bands];
    private readonly float _attack, _release;
    private readonly Random _rng = new();
    private double _phase;
    private float _hz = 110;

    public Vocoder(ISampleProvider source, Func<float> pitchHz)
    {
        _source = source;
        _pitchHz = pitchHz;
        var sr = source.WaveFormat.SampleRate;
        for (var b = 0; b < Bands; b++)
        {
            var f = 120 * MathF.Pow(7000f / 120, b / (Bands - 1f));
            _modBands[b] = BiQuadFilter.BandPassFilterConstantPeakGain(sr, f, 4f);
            _carBands[b] = BiQuadFilter.BandPassFilterConstantPeakGain(sr, f, 4f);
        }
        _attack = MathF.Exp(-1f / (0.004f * sr));
        _release = MathF.Exp(-1f / (0.040f * sr));
    }

    public float Mix { get; set; }
    public bool NoiseCarrier { get; set; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        if (Mix <= 0) return n;
        var sr = WaveFormat.SampleRate;
        var target = _pitchHz();
        if (target > 0) _hz += (target - _hz) * 0.01f;

        for (var i = 0; i < n; i++)
        {
            var x = buffer[i];
            float carrier;
            if (NoiseCarrier)
            {
                carrier = (float)(_rng.NextDouble() * 2 - 1);
            }
            else
            {
                _phase += _hz / sr;
                if (_phase >= 1) _phase -= 1;
                carrier = (float)(_phase * 2 - 1) * 0.5f;
            }

            var wet = 0f;
            for (var b = 0; b < Bands; b++)
            {
                var m = MathF.Abs(_modBands[b].Transform(x));
                _env[b] = m > _env[b] ? _attack * _env[b] + (1 - _attack) * m : _release * _env[b] + (1 - _release) * m;
                wet += _carBands[b].Transform(carrier) * _env[b];
            }
            buffer[i] = x * (1 - Mix) + wet * Mix * 3f;
        }
        return n;
    }
}

/// <summary>Lo-fi: fewer bits and a lower sample rate.</summary>
public sealed class BitCrusher(ISampleProvider source) : ISampleProvider
{
    private int _hold;
    private float _held;

    /// <summary>0 = off; 4–8 sounds like old game consoles.</summary>
    public int Bits { get; set; }
    /// <summary>1 = off; 6 at 48 kHz ≈ 8 kHz sample rate.</summary>
    public int RateDivider { get; set; } = 1;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        if (Bits <= 0 && RateDivider <= 1) return n;
        var step = Bits > 0 ? 2f / (1 << Bits) : 0;
        for (var i = 0; i < n; i++)
        {
            if (RateDivider > 1)
            {
                if (_hold++ % RateDivider == 0) _held = buffer[i];
                buffer[i] = _held;
            }
            if (step > 0) buffer[i] = MathF.Round(buffer[i] / step) * step;
        }
        return n;
    }
}

/// <summary>Three-band tone control: low shelf at 200 Hz, movable mid peak, high shelf at 4 kHz.</summary>
public sealed class Equalizer(ISampleProvider source) : ISampleProvider
{
    private BiQuadFilter? _low, _mid, _high;
    private float _lowDb, _midHz = 1000, _midDb, _highDb;

    public float LowDb { get => _lowDb; set { _lowDb = value; Rebuild(); } }
    public float MidHz { get => _midHz; set { _midHz = value; Rebuild(); } }
    public float MidDb { get => _midDb; set { _midDb = value; Rebuild(); } }
    public float HighDb { get => _highDb; set { _highDb = value; Rebuild(); } }
    public WaveFormat WaveFormat => source.WaveFormat;

    private void Rebuild()
    {
        var sr = WaveFormat.SampleRate;
        _low = _lowDb != 0 ? BiQuadFilter.LowShelf(sr, 200, 1f, _lowDb) : null;
        _mid = _midDb != 0 && _midHz > 0 ? BiQuadFilter.PeakingEQ(sr, _midHz, 1.2f, _midDb) : null;
        _high = _highDb != 0 ? BiQuadFilter.HighShelf(sr, 4000, 1f, _highDb) : null;
    }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        var (low, mid, high) = (_low, _mid, _high);
        if (low == null && mid == null && high == null) return n;
        for (var i = 0; i < n; i++)
        {
            var s = buffer[i];
            if (low != null) s = low.Transform(s);
            if (mid != null) s = mid.Transform(s);
            if (high != null) s = high.Transform(s);
            buffer[i] = s;
        }
        return n;
    }
}

/// <summary>Rhythmic or random on/off gating.</summary>
public sealed class StutterGate(ISampleProvider source) : ISampleProvider
{
    private readonly Random _rng = new();
    private double _phase;
    private float _gate = 1;
    private bool _randomOpen = true;

    public float RateHz { get; set; }
    public float Duty { get; set; } = 0.5f;
    public bool RandomMode { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        if (RateHz <= 0) return n;
        var sr = WaveFormat.SampleRate;
        var ramp = 1f / (0.003f * sr);
        for (var i = 0; i < n; i++)
        {
            var before = _phase;
            _phase += RateHz / sr;
            if (_phase >= 1)
            {
                _phase -= 1;
                _randomOpen = _rng.NextDouble() < Duty;
            }
            var open = RandomMode ? _randomOpen : _phase < Duty;
            _gate = Math.Clamp(_gate + (open ? ramp : -ramp), 0, 1);
            buffer[i] *= _gate;
            _ = before;
        }
        return n;
    }
}

/// <summary>Simple compressor. Amount 0..1 goes from gentle levelling to shouting-radio squash.</summary>
public sealed class Compressor : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _attack, _release;
    private float _env;

    public Compressor(ISampleProvider source)
    {
        _source = source;
        var sr = source.WaveFormat.SampleRate;
        _attack = MathF.Exp(-1f / (0.005f * sr));
        _release = MathF.Exp(-1f / (0.080f * sr));
    }

    public float Amount { get; set; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        if (Amount <= 0) return n;
        var thresholdDb = -8 - 24 * Amount;          // −8 … −32 dB
        var threshold = MathF.Pow(10, thresholdDb / 20);
        var ratio = 1 + 7 * Amount;                  // 1:1 … 8:1
        var makeup = MathF.Pow(1 / threshold, 1 - 1 / ratio) * 0.8f;
        for (var i = 0; i < n; i++)
        {
            var a = MathF.Abs(buffer[i]);
            _env = a > _env ? _attack * _env + (1 - _attack) * a : _release * _env + (1 - _release) * a;
            var gain = _env > threshold ? (threshold + (_env - threshold) / ratio) / _env : 1f;
            buffer[i] = Math.Clamp(buffer[i] * gain * makeup, -1f, 1f);
        }
        return n;
    }
}
