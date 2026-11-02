using NAudio.CoreAudioApi;

namespace VoiceChanger.Core;

/// <summary>
/// The one-click logic: TurnOn(voice) makes the changed voice appear in every app,
/// TurnOff() puts everything back. The UI only ever talks to this class.
/// </summary>
public sealed class VoiceChangerService : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly DefaultMicSwitcher _switcher = new();

    public bool IsOn { get; private set; }
    public Voice? CurrentVoice { get; private set; }

    /// <summary>Live microphone level 0..1 while audio runs (0 otherwise), for meters.</summary>
    public float Level => _engine.IsRunning ? _engine.InputLevel : 0f;

    private bool _hearMyself;

    /// <summary>
    /// Play what other apps hear on your own speakers/headphones. With a voice on, that's the
    /// changed voice; with nothing on, it's the plain microphone, so it doubles as a mic test.
    /// </summary>
    public bool HearMyself
    {
        get => _hearMyself;
        set
        {
            if (_hearMyself == value) return;
            _hearMyself = value;
            if (IsOn)
            {
                _engine.SetMonitor(value);
            }
            else if (value)
            {
                // Mic test: plain voice straight to the speakers, no device switching.
                using var mic = AudioDevices.RealMicrophone() ?? throw new InvalidOperationException("No microphone found on this PC.");
                using var speakers = AudioDevices.DefaultSpeakers();
                _engine.Start(Voice.Normal, mic, speakers);
            }
            else
            {
                _engine.Stop();
            }
        }
    }

    /// <summary>True when audio is going to the speakers instead of VB-Cable (VB-Cable missing).</summary>
    public bool IsPreviewOnly { get; private set; }


    public event Action<Exception>? Failed;

    public VoiceChangerService()
    {
        _engine.Failed += ex => Failed?.Invoke(ex);
        // If a previous run died while a voice was on, fix the default mic now.
        _switcher.Restore();
        // If the VB-Cable installer hijacked the speakers, give the user their sound back.
        DefaultMicSwitcher.RepairSpeakersIfHijacked();
    }

    public void TurnOn(Voice voice)
    {
        if (IsOn) { _engine.SetVoice(voice); CurrentVoice = voice; return; }

        using var mic = AudioDevices.RealMicrophone()
            ?? throw new InvalidOperationException("No microphone found on this PC.");
        var cableIn = AudioDevices.CableInput();
        var cableOut = AudioDevices.CableOutput();

        if (cableIn != null && cableOut != null)
        {
            // Normal path: other apps read from CABLE Output, we write into CABLE Input.
            IsPreviewOnly = false;
            _engine.Start(voice, mic, cableIn);
            _engine.SetMonitor(_hearMyself);
            _switcher.SwitchTo(cableOut, mic);
        }
        else
        {
            // VB-Cable not installed: play through the speakers so the effect can at least be heard.
            IsPreviewOnly = true;
            using var speakers = AudioDevices.DefaultSpeakers();
            _engine.Start(voice, mic, speakers);
        }

        CurrentVoice = voice;
        IsOn = true;
    }

    public void TurnOff()
    {
        if (!IsOn) return;
        _engine.Stop();
        _switcher.Restore();
        IsOn = false;
        CurrentVoice = null;
        // Keep the mic test running if the user still wants to hear themselves.
        if (_hearMyself)
        {
            _hearMyself = false;
            HearMyself = true;
        }
    }

    public void Dispose()
    {
        TurnOff();
        _engine.Dispose();
    }
}
