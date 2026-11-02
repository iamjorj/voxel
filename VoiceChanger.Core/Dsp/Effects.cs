using NAudio.Dsp;
using NAudio.Wave;

namespace VoiceChanger.Core.Dsp;

// Small mono effect stages. Every parameter can be changed while audio is running,
// and each stage passes audio through untouched when its parameters are "off".
// NAudio 3 reads through Span<float>; the array overload just forwards to it.

/// <summary>Multiplies the signal by a sine wave: the classic robot / metallic sound.</summary>
public sealed class RingModulator(ISampleProvider source) : ISampleProvider
{
    private double _phase;
    public float FrequencyHz { get; set; }
    public float Mix { get; set; } = 0.8f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        if (FrequencyHz <= 0 || Mix <= 0) return n;
        var step = 2 * Math.PI * FrequencyHz / WaveFormat.SampleRate;
        for (var i = 0; i < n; i++)
        {
            var s = buffer[i];
            buffer[i] = s * (1 - Mix) + s * (float)Math.Sin(_phase) * Mix;
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
        return n;
    }
}

/// <summary>Soft clipping. Drive 0 = clean, 1 = heavily crunched.</summary>
public sealed class Distortion(ISampleProvider source) : ISampleProvider
{
    public float Drive { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        if (Drive <= 0) return n;
        var gain = 1 + Drive * 12;
        var norm = 1 / MathF.Tanh(gain * 0.5f); // keep loudness roughly constant
        for (var i = 0; i < n; i++)
            buffer[i] = MathF.Tanh(buffer[i] * gain) * 0.5f * norm;
        return n;
    }
}

/// <summary>Volume wobble (LFO on amplitude). Used for shaky / old / ghostly voices.</summary>
public sealed class Tremolo(ISampleProvider source) : ISampleProvider
{
    private double _phase;
    public float RateHz { get; set; }
    public float Depth { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        if (RateHz <= 0 || Depth <= 0) return n;
        var step = 2 * Math.PI * RateHz / WaveFormat.SampleRate;
        for (var i = 0; i < n; i++)
        {
            var lfo = 0.5 + 0.5 * Math.Sin(_phase);
            buffer[i] *= (float)(1 - Depth * lfo);
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
        return n;
    }
}

/// <summary>
/// A delay line whose length moves with an LFO. With Mix = 1 it bends pitch up and down
/// (vibrato / wobble); with Mix ≈ 0.5 and a base delay it doubles the voice (chorus).
/// </summary>
public sealed class ModulatedDelay : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _buffer;
    private int _write;
    private double _phase;

    public ModulatedDelay(ISampleProvider source)
    {
        _source = source;
        _buffer = new float[source.WaveFormat.SampleRate / 10]; // 100 ms max
    }

    public float RateHz { get; set; }
    public float DepthMs { get; set; }
    public float BaseMs { get; set; }
    public float Mix { get; set; } = 1f;
    /// <summary>Feeds the wet signal back into the line: turns the chorus into a flanger.</summary>
    public float Feedback { get; set; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        if (DepthMs <= 0 && BaseMs <= 0) return n;
        var rate = WaveFormat.SampleRate;
        var step = 2 * Math.PI * RateHz / rate;
        var len = _buffer.Length;
        for (var i = 0; i < n; i++)
        {
            var dry = buffer[i];

            var delayMs = BaseMs + DepthMs * (0.5 + 0.5 * Math.Sin(_phase)) + 1; // +1 ms keeps us behind the write head
            var delaySamples = delayMs * rate / 1000.0;
            var readPos = _write - delaySamples;
            if (readPos < 0) readPos += len;
            var i0 = (int)readPos;
            var frac = (float)(readPos - i0);
            var i1 = (i0 + 1) % len;
            var wet = _buffer[i0] * (1 - frac) + _buffer[i1] * frac;

            _buffer[_write] = dry + wet * Feedback;
            buffer[i] = dry * (1 - Mix) + wet * Mix;
            _write = (_write + 1) % len;
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
        return n;
    }
}

/// <summary>Repeating echo.</summary>
public sealed class Echo : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _buffer;
    private int _pos;

    public Echo(ISampleProvider source)
    {
        _source = source;
        _buffer = new float[source.WaveFormat.SampleRate]; // 1 s max
    }

    public float DelayMs { get; set; }
    public float Feedback { get; set; }
    public float Mix { get; set; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        if (DelayMs <= 0 || Mix <= 0) return n;
        var delay = Math.Clamp((int)(DelayMs * WaveFormat.SampleRate / 1000), 1, _buffer.Length - 1);
        for (var i = 0; i < n; i++)
        {
            var readPos = (_pos - delay + _buffer.Length) % _buffer.Length;
            var delayed = _buffer[readPos];
            var dry = buffer[i];
            _buffer[_pos] = dry + delayed * Feedback;
            buffer[i] = dry + delayed * Mix;
            _pos = (_pos + 1) % _buffer.Length;
        }
        return n;
    }
}

/// <summary>Schroeder reverb (4 combs + 2 all-passes): a simple "big room" sound.</summary>
public sealed class Reverb : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Comb[] _combs;
    private readonly AllPass[] _allPasses;

    public Reverb(ISampleProvider source)
    {
        _source = source;
        var scale = source.WaveFormat.SampleRate / 44100f;
        _combs = new[] { 1116, 1188, 1277, 1356 }.Select(l => new Comb((int)(l * scale), 0.84f, 0.2f)).ToArray();
        _allPasses = new[] { 556, 441 }.Select(l => new AllPass((int)(l * scale), 0.5f)).ToArray();
    }

    public float Mix { get; set; }

    /// <summary>Room size 0..1: 0.3 = small tiled room, 0.6 = hall, 1 = cathedral.</summary>
    public float Size
    {
        get => _size;
        set { _size = value; foreach (var c in _combs) c.Feedback = 0.6f + 0.36f * Math.Clamp(value, 0, 1); }
    }
    private float _size = 0.65f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        if (Mix <= 0) return n;
        for (var i = 0; i < n; i++)
        {
            var dry = buffer[i];
            var wet = 0f;
            foreach (var c in _combs) wet += c.Process(dry);
            wet *= 0.25f;
            foreach (var a in _allPasses) wet = a.Process(wet);
            buffer[i] = dry * (1 - Mix * 0.5f) + wet * Mix;
        }
        return n;
    }

    private sealed class Comb(int length, float feedback, float damp)
    {
        private readonly float[] _buf = new float[length];
        private int _pos;
        private float _filtered;

        public float Feedback { get; set; } = feedback;

        public float Process(float input)
        {
            var output = _buf[_pos];
            _filtered = output * (1 - damp) + _filtered * damp;
            _buf[_pos] = input + _filtered * Feedback;
            _pos = (_pos + 1) % _buf.Length;
            return output;
        }
    }

    private sealed class AllPass(int length, float gain)
    {
        private readonly float[] _buf = new float[length];
        private int _pos;

        public float Process(float input)
        {
            var delayed = _buf[_pos];
            var output = -input + delayed;
            _buf[_pos] = input + delayed * gain;
            _pos = (_pos + 1) % _buf.Length;
            return output;
        }
    }
}

/// <summary>Optional low-pass and high-pass (telephone, radio, underwater…).</summary>
public sealed class ToneFilter(ISampleProvider source) : ISampleProvider
{
    private BiQuadFilter? _low, _high;
    private float _lowHz, _highHz;

    public float LowPassHz
    {
        get => _lowHz;
        set { _lowHz = value; _low = value > 0 ? BiQuadFilter.LowPassFilter(WaveFormat.SampleRate, value, 0.9f) : null; }
    }

    public float HighPassHz
    {
        get => _highHz;
        set { _highHz = value; _high = value > 0 ? BiQuadFilter.HighPassFilter(WaveFormat.SampleRate, value, 0.9f) : null; }
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        var low = _low;
        var high = _high;
        if (low == null && high == null) return n;
        for (var i = 0; i < n; i++)
        {
            var s = buffer[i];
            if (high != null) s = high.Transform(s);
            if (low != null) s = low.Transform(s);
            buffer[i] = s;
        }
        return n;
    }
}

/// <summary>Output volume.</summary>
public sealed class Gain(ISampleProvider source) : ISampleProvider
{
    public float Amount { get; set; } = 1f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = source.Read(buffer);
        if (Math.Abs(Amount - 1) < 0.001f) return n;
        for (var i = 0; i < n; i++) buffer[i] *= Amount;
        return n;
    }
}
