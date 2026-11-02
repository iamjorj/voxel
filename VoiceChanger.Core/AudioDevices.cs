using NAudio.CoreAudioApi;

namespace VoiceChanger.Core;

/// <summary>Finds the audio devices the engine needs: the real mic and the VB-Cable ends.</summary>
public static class AudioDevices
{
    // VB-Cable names its two ends like this. "CABLE Input" is where we write audio;
    // "CABLE Output" is the fake microphone other apps read from.
    public const string CableInputName = "CABLE Input";
    public const string CableOutputName = "CABLE Output";

    public static MMDevice DefaultMicrophone()
    {
        using var e = new MMDeviceEnumerator();
        return e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
    }

    /// <summary>
    /// The user's physical microphone: the default mic, unless that is a VB-Cable device
    /// (the VB-Cable installer makes CABLE Output the default), in which case the first
    /// real capture device. Null if the PC has no real microphone at all.
    /// </summary>
    public static MMDevice? RealMicrophone()
    {
        var def = DefaultMicrophone();
        if (!IsVbCableDevice(def)) return def;
        def.Dispose();

        using var e = new MMDeviceEnumerator();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            if (!IsVbCableDevice(d)) return d;
            d.Dispose();
        }
        return null;
    }

    public static bool IsVbCableDevice(MMDevice d) =>
        d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase);

    public static MMDevice DefaultSpeakers()
    {
        using var e = new MMDeviceEnumerator();
        return e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
    }

    /// <summary>First active playback device that is not part of VB-Cable, or null.</summary>
    public static MMDevice? RealSpeakers()
    {
        using var e = new MMDeviceEnumerator();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (!IsVbCableDevice(d)) return d;
            d.Dispose();
        }
        return null;
    }

    /// <summary>The render side of VB-Cable, or null if VB-Cable is not installed.</summary>
    public static MMDevice? CableInput() => Find(DataFlow.Render, CableInputName);

    /// <summary>The capture side of VB-Cable, or null if VB-Cable is not installed.</summary>
    public static MMDevice? CableOutput() => Find(DataFlow.Capture, CableOutputName);

    public static bool IsVbCableInstalled() => CableInput() != null && CableOutput() != null;

    private static MMDevice? Find(DataFlow flow, string nameContains)
    {
        using var e = new MMDeviceEnumerator();
        foreach (var d in e.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            if (d.FriendlyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                return d;
            d.Dispose();
        }
        return null;
    }
}
