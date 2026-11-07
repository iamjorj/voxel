using VoiceChanger.Core;

namespace VoiceChanger.Mac;

/// <summary>
/// The one-click logic, the Mac twin of VoiceChangerService: TurnOn(voice) makes the changed voice
/// appear in every app, TurnOff() puts everything back. The UI only ever talks to this class.
/// </summary>
public sealed class MacVoiceChangerService : IDisposable
{
    private readonly MacAudioEngine _engine = new();
    private readonly MacDefaultMicSwitcher _switcher = new();

    public bool IsOn { get; private set; }
    public Voice? CurrentVoice { get; private set; }

    /// <summary>Live microphone level 0..1 while audio runs (0 otherwise), for meters.</summary>
    public float Level => _engine.IsRunning ? _engine.InputLevel : 0f;

    /// <summary>See <see cref="MacAudioEngine.TakeDiagnostics"/>.</summary>
    public string TakeDiagnostics() => _engine.TakeDiagnostics();

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
                var mic = MacAudioDevices.RealMicrophone() ?? throw new InvalidOperationException("No microphone found on this Mac.");
                var speakers = MacAudioDevices.DefaultSpeakers() ?? throw new InvalidOperationException("No speakers found on this Mac.");
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

    public MacVoiceChangerService()
    {
        _engine.Failed += ex => Failed?.Invoke(ex);
        // If a previous run died while a voice was on, fix the default mic now.
        _switcher.Restore();
        MacDefaultMicSwitcher.RepairSpeakersIfHijacked();
    }

    public void TurnOn(Voice voice)
    {
        if (IsOn) { _engine.SetVoice(voice); CurrentVoice = voice; return; }

        var mic = MacAudioDevices.RealMicrophone() ?? throw new InvalidOperationException("No microphone found on this Mac.");
        var cable = MacAudioDevices.Cable();

        if (cable != null)
        {
            // Normal path: we play into VB-Cable, other apps record from it.
            IsPreviewOnly = false;
            _engine.Start(voice, mic, cable);
            _engine.SetMonitor(_hearMyself);
            _switcher.SwitchTo(cable, mic);
        }
        else
        {
            // VB-Cable not installed: play through the speakers so the effect can at least be heard.
            IsPreviewOnly = true;
            var speakers = MacAudioDevices.DefaultSpeakers() ?? throw new InvalidOperationException("No speakers found on this Mac.");
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
