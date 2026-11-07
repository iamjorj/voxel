namespace VoiceChanger.Mac;

/// <summary>
/// Makes VB-Cable the Mac's default microphone while a voice is on, and puts the user's real mic
/// back afterwards. The original mic is saved to disk so it can still be restored if the app
/// crashed or was killed while a voice was on.
/// </summary>
public sealed class MacDefaultMicSwitcher
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxel", "original-mic.txt");

    /// <summary>The mic that was default before we switched, or null if we haven't switched.</summary>
    public string? OriginalMicUid { get; private set; } = File.Exists(StatePath) ? File.ReadAllText(StatePath).Trim() : null;

    /// <summary>Sets <paramref name="cable"/> as default and remembers <paramref name="realMic"/> for <see cref="Restore"/>.</summary>
    public void SwitchTo(MacAudioDevice cable, MacAudioDevice realMic)
    {
        if (OriginalMicUid == null)
        {
            OriginalMicUid = realMic.Uid;
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, OriginalMicUid);
        }
        MacAudioDevices.SetDefaultMicrophone(cable);
    }

    /// <summary>Puts the user's real mic back as default. Safe to call when nothing was switched.</summary>
    public void Restore()
    {
        var uid = OriginalMicUid;
        if (uid == null) return;
        try
        {
            // A mic that was unplugged meanwhile can't be restored; macOS has already picked another.
            var original = MacAudioDevices.ByUid(uid);
            if (original != null) MacAudioDevices.SetDefaultMicrophone(original);
        }
        finally
        {
            OriginalMicUid = null;
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
    }

    /// <summary>If VB-Cable ended up as the default speakers the Mac goes silent: point them back at real ones. Returns true if repaired.</summary>
    public static bool RepairSpeakersIfHijacked()
    {
        var current = MacAudioDevices.DefaultSpeakers();
        if (current == null || !MacAudioDevices.IsCable(current)) return false;
        var real = MacAudioDevices.RealSpeakers();
        if (real == null) return false;
        MacAudioDevices.SetDefaultSpeakers(real);
        return true;
    }
}
