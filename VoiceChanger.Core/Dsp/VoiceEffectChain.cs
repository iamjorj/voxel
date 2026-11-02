using NAudio.Wave;

namespace VoiceChanger.Core.Dsp;

/// <summary>
/// The full mono effect chain. Built once per session; switching voices only changes
/// parameters, so it's instant and never interrupts the audio.
/// </summary>
public sealed class VoiceEffectChain : ISampleProvider, IDisposable
{
    private readonly AiVoiceStage _ai;
    private readonly FormantPitchShifter _pitch;
    private readonly Doubler _doubler;
    private readonly Vocoder _vocoder;
    private readonly ModulatedDelay _wobble;
    private readonly RingModulator _ring;
    private readonly Distortion _drive;
    private readonly BitCrusher _crush;
    private readonly ToneFilter _tone;
    private readonly Equalizer _eq;
    private readonly Tremolo _tremolo;
    private readonly StutterGate _stutter;
    private readonly Compressor _comp;
    private readonly Echo _echo;
    private readonly Reverb _reverb;
    private readonly Gain _gain;

    public VoiceEffectChain(ISampleProvider monoSource)
    {
        // AI conversion first (a different person), then every effect on top of that.
        _ai = new AiVoiceStage(monoSource);
        _pitch = new FormantPitchShifter(_ai);
        _doubler = new Doubler(_pitch);
        _vocoder = new Vocoder(_doubler, () => _pitch.OutputF0);
        _wobble = new ModulatedDelay(_vocoder);
        _ring = new RingModulator(_wobble);
        _drive = new Distortion(_ring);
        _crush = new BitCrusher(_drive);
        _tone = new ToneFilter(_crush);
        _eq = new Equalizer(_tone);
        _tremolo = new Tremolo(_eq);
        _stutter = new StutterGate(_tremolo);
        _comp = new Compressor(_stutter);
        _echo = new Echo(_comp);
        _reverb = new Reverb(_echo);
        _gain = new Gain(_reverb);
    }

    public WaveFormat WaveFormat => _gain.WaveFormat;

    public int Read(float[] buffer, int offset, int count) => _gain.Read(buffer, offset, count);
    public int Read(Span<float> buffer) => _gain.Read(buffer);

    public void Dispose() => _ai.Dispose();

    public void Apply(Voice v)
    {
        _ai.SetModel(v.AiModel);
        _pitch.PitchFactor = v.Pitch;
        _pitch.FormantFactor = v.Formant;
        _pitch.Shape = v.PitchModShape;
        _pitch.ModRateHz = v.PitchModHz;
        _pitch.ModSemitones = v.PitchModSemitones;
        _pitch.Autotune = v.Autotune;

        _doubler.Pitch = v.DoublePitch; _doubler.Formant = v.DoubleFormant; _doubler.Mix = v.DoubleMix; _doubler.Drive = v.DoubleDrive;
        _vocoder.Mix = v.VocoderMix; _vocoder.NoiseCarrier = v.VocoderNoise;
        _wobble.RateHz = v.WobbleHz; _wobble.DepthMs = v.WobbleDepthMs; _wobble.BaseMs = v.WobbleBaseMs; _wobble.Mix = v.WobbleMix; _wobble.Feedback = v.WobbleFeedback;
        _ring.FrequencyHz = v.RingHz; _ring.Mix = v.RingMix;
        _drive.Drive = v.Drive;
        _crush.Bits = v.Bits; _crush.RateDivider = Math.Max(1, v.RateDivider);
        _tone.LowPassHz = v.LowPassHz; _tone.HighPassHz = v.HighPassHz;
        _eq.LowDb = v.EqLowDb; _eq.MidHz = v.EqMidHz; _eq.MidDb = v.EqMidDb; _eq.HighDb = v.EqHighDb;
        _tremolo.RateHz = v.TremoloHz; _tremolo.Depth = v.TremoloDepth;
        _stutter.RateHz = v.StutterHz; _stutter.Duty = v.StutterDuty; _stutter.RandomMode = v.StutterRandom;
        _comp.Amount = v.Compress;
        _echo.DelayMs = v.EchoMs; _echo.Feedback = v.EchoFeedback; _echo.Mix = v.EchoMix;
        _reverb.Mix = v.ReverbMix; _reverb.Size = v.ReverbSize;
        _gain.Amount = v.Gain;
    }
}
