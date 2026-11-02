using VoiceChanger.Core.Dsp;

namespace VoiceChanger.Core;

public enum VoiceCategory { AI, People, Creatures, Spooky, Machines, Places, Devices, Fun, Animals }

/// <summary>
/// A voice preset: a name, a category, an icon key for the UI, and the settings for each
/// effect stage. Every setting defaults to "off", so a preset only lists what it uses.
/// </summary>
public sealed record Voice(string Name, VoiceCategory Category, string Icon)
{
    /// <summary>AI conversion model name (file in the app's ai folder), or null for none.</summary>
    public string? AiModel { get; init; }

    // Pitch & timbre
    /// <summary>1.0 = unchanged, 2.0 = one octave up, 0.5 = one octave down.</summary>
    public float Pitch { get; init; } = 1f;
    /// <summary>Formant factor: 1.25 = smaller/brighter speaker, 0.8 = bigger. 0 = follow Pitch (classic chipmunk/giant).</summary>
    public float Formant { get; init; }
    public FormantPitchShifter.ModShape PitchModShape { get; init; }
    public float PitchModHz { get; init; }
    public float PitchModSemitones { get; init; }
    /// <summary>0 = off, 1 = hard snap to the nearest semitone.</summary>
    public float Autotune { get; init; }

    // Second voice copy
    public float DoublePitch { get; init; }
    public float DoubleFormant { get; init; }
    public float DoubleMix { get; init; } = 0.5f;
    public float DoubleDrive { get; init; }

    // Vocoder
    public float VocoderMix { get; init; }
    public bool VocoderNoise { get; init; }

    // Wobble / chorus / flanger
    public float WobbleHz { get; init; }
    public float WobbleDepthMs { get; init; }
    public float WobbleBaseMs { get; init; }
    public float WobbleMix { get; init; } = 1f;
    public float WobbleFeedback { get; init; }

    // Ring modulation: the robot sound
    public float RingHz { get; init; }
    public float RingMix { get; init; } = 0.8f;

    // Dirt
    /// <summary>Soft distortion, 0..1.</summary>
    public float Drive { get; init; }
    public int Bits { get; init; }
    public int RateDivider { get; init; } = 1;

    // Tone
    public float LowPassHz { get; init; }
    public float HighPassHz { get; init; }
    public float EqLowDb { get; init; }
    public float EqMidHz { get; init; } = 1000f;
    public float EqMidDb { get; init; }
    public float EqHighDb { get; init; }

    // Dynamics & rhythm
    public float TremoloHz { get; init; }
    public float TremoloDepth { get; init; }
    public float StutterHz { get; init; }
    public float StutterDuty { get; init; } = 0.5f;
    public bool StutterRandom { get; init; }
    public float Compress { get; init; }

    // Space
    public float EchoMs { get; init; }
    public float EchoFeedback { get; init; }
    public float EchoMix { get; init; }
    public float ReverbMix { get; init; }
    public float ReverbSize { get; init; } = 0.65f;

    // Texture
    public NoiseLayer.Kind Noise { get; init; }
    public float NoiseLevel { get; init; }

    public float Gain { get; init; } = 1f;

    public static Voice Clown => VoiceCatalog.All.First(v => v.Name == "Clown");

    /// <summary>No change at all: used to test the microphone.</summary>
    public static readonly Voice Normal = new("Normal", VoiceCategory.People, "microphone");
}
