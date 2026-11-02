using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace VoiceChanger.Core;

/// <summary>
/// Makes "CABLE Output" the Windows default microphone while a voice is on, and puts the
/// user's real mic back afterwards. The original mic id is saved to disk so it can still be
/// restored if the app crashed or was killed while a voice was on.
/// </summary>
public sealed class DefaultMicSwitcher
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceChanger", "original-mic.txt");

    // A second marker file records that the real mic was hidden, so a crashed run unhides it next start.
    private static readonly string HiddenPath = Path.Combine(Path.GetDirectoryName(StatePath)!, "hidden-mic.txt");

    /// <summary>Id of the mic that was default before we switched, or null if we haven't switched.</summary>
    public string? OriginalMicId { get; private set; } = File.Exists(StatePath) ? File.ReadAllText(StatePath).Trim() : null;

    private string? HiddenMicId { get; set; } = File.Exists(HiddenPath) ? File.ReadAllText(HiddenPath).Trim() : null;

    /// <summary>
    /// Sets <paramref name="newDefaultMic"/> as default for every role and remembers
    /// <paramref name="realMic"/> as the device to go back to in <see cref="Restore"/>.
    /// (Hiding the real mic from other apps was tried and dropped: a hidden endpoint can't be
    /// captured from, so it would silence our own input.)
    /// </summary>
    public void SwitchTo(MMDevice newDefaultMic, MMDevice realMic)
    {
        if (OriginalMicId == null)
        {
            OriginalMicId = realMic.ID;
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, OriginalMicId);
        }
        SetDefault(newDefaultMic.ID);
    }

    /// <summary>Puts the user's real mic back as default. Safe to call when nothing was switched.</summary>
    public void Restore()
    {
        // Leftover from an older build that hid the mic: make sure it is visible again.
        var hidden = HiddenMicId;
        if (hidden != null)
        {
            try { ((IPolicyConfig)new PolicyConfigClient()).SetEndpointVisibility(hidden, 1); } catch { }
            HiddenMicId = null;
            if (File.Exists(HiddenPath)) File.Delete(HiddenPath);
        }
        var id = OriginalMicId;
        if (id == null) return;
        try { SetDefault(id); }
        finally
        {
            OriginalMicId = null;
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
    }

    /// <summary>
    /// The VB-Cable installer makes its own devices the default speakers AND mic, which
    /// leaves the PC silent. If that happened, point the default speakers back at real ones.
    /// Returns true if a repair was made.
    /// </summary>
    public static bool RepairSpeakersIfHijacked()
    {
        using var current = AudioDevices.DefaultSpeakers();
        if (!AudioDevices.IsVbCableDevice(current)) return false;
        using var real = AudioDevices.RealSpeakers();
        if (real == null) return false;
        SetDefault(real.ID);
        return true;
    }

    /// <summary>
    /// Same repair for the microphone: if a VB-Cable device is the default mic while no voice
    /// is on, point it back at the real mic. Returns true if a repair was made.
    /// </summary>
    public static bool RepairMicIfHijacked()
    {
        using var current = AudioDevices.DefaultMicrophone();
        if (!AudioDevices.IsVbCableDevice(current)) return false;
        using var real = AudioDevices.RealMicrophone();
        if (real == null) return false;
        SetDefault(real.ID);
        return true;
    }

    private static void SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
    }

    // Windows has no public API for changing the default device; every switcher app uses this
    // undocumented but stable COM interface (same one the Sound control panel uses).
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Only SetDefaultEndpoint is used; the others must be declared to keep the vtable order.
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
