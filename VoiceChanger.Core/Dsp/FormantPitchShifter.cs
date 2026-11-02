using NAudio.Dsp;
using NAudio.Wave;

namespace VoiceChanger.Core.Dsp;

/// <summary>
/// STFT phase-vocoder pitch shifter (Bernsee's method) with the spectral envelope handled
/// separately, so pitch and formants ("timbre / size of the speaker") move independently.
/// Also hosts pitch modulation (vibrato, droop, random flicks) and autotune, because they all
/// just change the per-frame pitch factor.
/// </summary>
public sealed class FormantPitchShifter : ISampleProvider
{
    public enum ModShape { None, Sine, Droop, Random }

    private const int FftSize = 2048;
    private const int Oversample = 4;
    private const int Hop = FftSize / Oversample;
    private const int Latency = FftSize - Hop;
    private static readonly int FftOrder = (int)Math.Log2(FftSize);

    private readonly ISampleProvider _source;
    private readonly int _sampleRate;
    private readonly PitchTracker _tracker;

    private readonly float[] _inFifo = new float[FftSize];
    private readonly float[] _outFifo = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly Complex[] _cep = new Complex[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _lastPhase = new float[FftSize / 2 + 1];
    private readonly float[] _sumPhase = new float[FftSize / 2 + 1];
    private readonly float[] _outAccum = new float[2 * FftSize];
    private readonly float[] _anaFreq = new float[FftSize];
    private readonly float[] _anaMagn = new float[FftSize];
    private readonly float[] _env = new float[FftSize];
    private readonly float[] _synFreq = new float[FftSize];
    private readonly float[] _synMagn = new float[FftSize];
    private readonly int _lifter;
    private int _rover = Latency;

    private double _modPhase;
    private float _randomTarget, _randomCurrent;
    private int _randomCountdown;
    private float _autotuneFactor = 1f;
    private readonly Random _rng = new();

    public FormantPitchShifter(ISampleProvider monoSource)
    {
        _source = monoSource;
        _sampleRate = monoSource.WaveFormat.SampleRate;
        _tracker = new PitchTracker(_sampleRate);
        _lifter = Math.Max(8, _sampleRate / 800); // cepstral cutoff: below the shortest pitch period
        for (var i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>1 = unchanged, 2 = octave up.</summary>
    public float PitchFactor { get; set; } = 1f;

    /// <summary>Formant (envelope) factor. 0 = follow the pitch factor, the classic "chipmunk / giant" sound.</summary>
    public float FormantFactor { get; set; }

    public ModShape Shape { get; set; }
    public float ModRateHz { get; set; }
    public float ModSemitones { get; set; }

    /// <summary>0 = off, 1 = snap fully to the nearest semitone.</summary>
    public float Autotune { get; set; }

    /// <summary>Pitch of the output in Hz (input pitch × factor), 0 when unvoiced. For the vocoder.</summary>
    public float OutputF0 { get; private set; }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        // Always processed, even at factor 1, so the delay never jumps when voices switch.
        for (var i = 0; i < n; i++)
        {
            _inFifo[_rover] = buffer[i];
            buffer[i] = _outFifo[_rover - Latency];
            _rover++;
            if (_rover >= FftSize)
            {
                _rover = Latency;
                ProcessFrame();
            }
        }
        return n;
    }

    private float EffectiveFactor()
    {
        var factor = PitchFactor;

        switch (Shape)
        {
            case ModShape.Sine:
                factor *= MathF.Pow(2, ModSemitones * MathF.Sin((float)_modPhase) / 12);
                break;
            case ModShape.Droop:
                // Slides down over the period, then jumps back up.
                var t = (float)(_modPhase / (2 * Math.PI));
                factor *= MathF.Pow(2, ModSemitones * (0.5f - t) / 12);
                break;
            case ModShape.Random:
                if (--_randomCountdown <= 0)
                {
                    _randomTarget = (float)(_rng.NextDouble() * 2 - 1) * ModSemitones;
                    _randomCountdown = 6 + _rng.Next(10); // frames ≈ 60–160 ms
                }
                _randomCurrent += (_randomTarget - _randomCurrent) * 0.4f;
                factor *= MathF.Pow(2, _randomCurrent / 12);
                break;
        }
        _modPhase += 2 * Math.PI * ModRateHz * Hop / _sampleRate;
        if (_modPhase > 2 * Math.PI) _modPhase -= 2 * Math.PI;

        if (Autotune > 0)
        {
            _tracker.Analyze(_inFifo);
            var f0 = _tracker.F0;
            float target = 1f;
            if (f0 > 0)
            {
                var midi = MathF.Round(69 + 12 * MathF.Log2(f0 * factor / 440f));
                var snapped = 440f * MathF.Pow(2, (midi - 69) / 12);
                target = snapped / (f0 * factor);
            }
            // Strength blends between no correction and full snap; smoothing avoids zipper noise.
            var wanted = 1 + (target - 1) * Autotune;
            _autotuneFactor += (wanted - _autotuneFactor) * (Autotune >= 0.99f ? 0.8f : 0.3f);
            factor *= _autotuneFactor;
        }

        return Math.Clamp(factor, 0.25f, 4f);
    }

    private void ProcessFrame()
    {
        var pitch = EffectiveFactor();
        var formant = FormantFactor <= 0 ? pitch : FormantFactor;
        var half = FftSize / 2;
        var freqPerBin = (float)_sampleRate / FftSize;
        var expct = 2 * MathF.PI * Hop / FftSize;

        // Track the input pitch for the vocoder carrier (cheap enough every frame).
        if (Autotune <= 0) _tracker.Analyze(_inFifo);
        OutputF0 = _tracker.F0 > 0 ? _tracker.F0 * pitch : 0;

        for (var i = 0; i < FftSize; i++)
        {
            _fft[i].X = _inFifo[i] * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftOrder, _fft);

        // Analysis: magnitude and true frequency per bin.
        for (var k = 0; k <= half; k++)
        {
            var re = _fft[k].X;
            var im = _fft[k].Y;
            var magn = 2 * MathF.Sqrt(re * re + im * im);
            var phase = MathF.Atan2(im, re);
            var tmp = phase - _lastPhase[k];
            _lastPhase[k] = phase;
            tmp -= k * expct;
            var qpd = (int)(tmp / MathF.PI);
            if (qpd >= 0) qpd += qpd & 1; else qpd -= qpd & 1;
            tmp -= MathF.PI * qpd;
            tmp = Oversample * tmp / (2 * MathF.PI);
            _anaMagn[k] = magn;
            _anaFreq[k] = k * freqPerBin + tmp * freqPerBin;
        }

        // Spectral envelope by cepstral liftering; then flatten the spectrum.
        for (var k = 0; k <= half; k++)
        {
            var v = MathF.Log(_anaMagn[k] + 1e-6f);
            _cep[k].X = v; _cep[k].Y = 0;
            if (k > 0 && k < half) { _cep[FftSize - k].X = v; _cep[FftSize - k].Y = 0; }
        }
        FastFourierTransform.FFT(false, FftOrder, _cep);
        for (var i = 0; i < FftSize; i++)
        {
            var keep = i < _lifter || i > FftSize - _lifter;
            if (!keep) { _cep[i].X = 0; _cep[i].Y = 0; }
        }
        FastFourierTransform.FFT(true, FftOrder, _cep);
        for (var k = 0; k <= half; k++)
            _env[k] = MathF.Exp(_cep[k].X);

        // Pitch shift the flattened spectrum.
        Array.Clear(_synMagn, 0, FftSize);
        Array.Clear(_synFreq, 0, FftSize);
        for (var k = 0; k <= half; k++)
        {
            var index = (int)(k * pitch);
            if (index > half) break;
            _synMagn[index] += _anaMagn[k] / _env[k];
            _synFreq[index] = _anaFreq[k] * pitch;
        }

        // Re-apply the envelope, moved by the formant factor.
        for (var k = 0; k <= half; k++)
        {
            var pos = k / formant;
            var i0 = (int)pos;
            var e = i0 >= half ? _env[half] : _env[i0] + (_env[i0 + 1] - _env[i0]) * (pos - i0);
            _synMagn[k] *= e;
        }

        // Synthesis.
        for (var k = 0; k <= half; k++)
        {
            var tmp = _synFreq[k] - k * freqPerBin;
            tmp /= freqPerBin;
            tmp = 2 * MathF.PI * tmp / Oversample;
            tmp += k * expct;
            _sumPhase[k] += tmp;
            var phase = _sumPhase[k];
            _fft[k].X = _synMagn[k] * MathF.Cos(phase);
            _fft[k].Y = _synMagn[k] * MathF.Sin(phase);
        }
        for (var k = half + 1; k < FftSize; k++) { _fft[k].X = 0; _fft[k].Y = 0; }
        FastFourierTransform.FFT(false, FftOrder, _fft);

        var scale = 2f / (half * Oversample) * FftSize / 2; // NAudio's inverse FFT already divides by N
        for (var i = 0; i < FftSize; i++)
            _outAccum[i] += _window[i] * _fft[i].X * scale;
        Array.Copy(_outAccum, 0, _outFifo, 0, Hop);
        Array.Copy(_outAccum, Hop, _outAccum, 0, FftSize);
        Array.Copy(_inFifo, Hop, _inFifo, 0, Latency);
    }
}
