using System.Runtime.InteropServices;
using System.Text;

namespace VoiceChanger.Mac;

/// <summary>An audio device. <see cref="Uid"/> stays the same across restarts; <see cref="Id"/> may not.</summary>
public sealed record MacAudioDevice(uint Id, string Uid, string Name, int InputChannels, int OutputChannels);

/// <summary>Finds the audio devices the engine needs: the real mic and VB-Cable.</summary>
public static class MacAudioDevices
{
    // On a Mac VB-Cable is one device with both ends: we play into it, other apps record from it.
    public const string CableName = "VB-Cable";

    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint SystemObject = 1;
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    private static readonly uint Devices = FourCC("dev#");
    private static readonly uint DefaultInput = FourCC("dIn ");
    private static readonly uint DefaultOutput = FourCC("dOut");
    private static readonly uint Name = FourCC("lnam");
    private static readonly uint DeviceUid = FourCC("uid ");
    private static readonly uint StreamConfiguration = FourCC("slay");
    private static readonly uint ScopeGlobal = FourCC("glob");
    private static readonly uint ScopeInput = FourCC("inpt");
    private static readonly uint ScopeOutput = FourCC("outp");

    internal static uint FourCC(string code) => (uint)(code[0] << 24 | code[1] << 16 | code[2] << 8 | code[3]);

    public static IReadOnlyList<MacAudioDevice> All()
    {
        var bytes = GetBytes(SystemObject, Devices, ScopeGlobal);
        var devices = new List<MacAudioDevice>();
        for (var i = 0; i + 4 <= bytes.Length; i += 4)
        {
            var id = BitConverter.ToUInt32(bytes, i);
            devices.Add(new MacAudioDevice(id, GetString(id, DeviceUid), GetString(id, Name),
                Channels(id, ScopeInput), Channels(id, ScopeOutput)));
        }
        return devices;
    }

    public static MacAudioDevice? ByUid(string uid) => All().FirstOrDefault(d => d.Uid == uid);

    public static MacAudioDevice? DefaultMicrophone() => ById(GetUInt(SystemObject, DefaultInput));
    public static MacAudioDevice? DefaultSpeakers() => ById(GetUInt(SystemObject, DefaultOutput));

    public static void SetDefaultMicrophone(MacAudioDevice device) => SetUInt(SystemObject, DefaultInput, device.Id);
    public static void SetDefaultSpeakers(MacAudioDevice device) => SetUInt(SystemObject, DefaultOutput, device.Id);

    public static bool IsCable(MacAudioDevice device) => device.Name.Contains(CableName, StringComparison.OrdinalIgnoreCase);

    /// <summary>VB-Cable, or null if it is not installed.</summary>
    public static MacAudioDevice? Cable() =>
        All().FirstOrDefault(d => IsCable(d) && d.InputChannels > 0 && d.OutputChannels > 0);

    /// <summary>
    /// The user's physical microphone: the default mic, unless that is VB-Cable,
    /// in which case the first real input device. Null if the Mac has no real microphone.
    /// </summary>
    public static MacAudioDevice? RealMicrophone()
    {
        var current = DefaultMicrophone();
        if (current != null && !IsCable(current)) return current;
        return All().FirstOrDefault(d => d.InputChannels > 0 && !IsCable(d));
    }

    /// <summary>First playback device that is not VB-Cable, or null.</summary>
    public static MacAudioDevice? RealSpeakers() => All().FirstOrDefault(d => d.OutputChannels > 0 && !IsCable(d));

    private static MacAudioDevice? ById(uint id) => All().FirstOrDefault(d => d.Id == id);

    private static int Channels(uint id, uint scope)
    {
        // AudioBufferList: a count, then (on 64-bit, after padding) 16-byte AudioBuffers starting with their channel count.
        var bytes = GetBytes(id, StreamConfiguration, scope);
        if (bytes.Length < 8) return 0;
        var channels = 0;
        var buffers = BitConverter.ToUInt32(bytes, 0);
        for (var i = 0; i < buffers && 8 + i * 16 + 4 <= bytes.Length; i++)
            channels += (int)BitConverter.ToUInt32(bytes, 8 + i * 16);
        return channels;
    }

    private static byte[] GetBytes(uint target, uint selector, uint scope)
    {
        var address = new PropertyAddress { Selector = selector, Scope = scope };
        if (AudioObjectGetPropertyDataSize(target, ref address, 0, IntPtr.Zero, out var size) != 0 || size == 0) return [];
        var bytes = new byte[size];
        return AudioObjectGetPropertyData(target, ref address, 0, IntPtr.Zero, ref size, bytes) == 0 ? bytes[..(int)size] : [];
    }

    private static uint GetUInt(uint target, uint selector)
    {
        var bytes = GetBytes(target, selector, ScopeGlobal);
        return bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0;
    }

    private static void SetUInt(uint target, uint selector, uint value)
    {
        var address = new PropertyAddress { Selector = selector, Scope = ScopeGlobal };
        var status = AudioObjectSetPropertyData(target, ref address, 0, IntPtr.Zero, sizeof(uint), BitConverter.GetBytes(value));
        if (status != 0) throw new InvalidOperationException($"Couldn't change the default audio device (Core Audio error {status}).");
    }

    private static string GetString(uint target, uint selector)
    {
        var bytes = GetBytes(target, selector, ScopeGlobal);
        if (bytes.Length < IntPtr.Size) return "";
        var text = (IntPtr)BitConverter.ToInt64(bytes, 0);
        if (text == IntPtr.Zero) return "";
        try
        {
            var buffer = new byte[CFStringGetLength(text) * 4 + 1];
            return CFStringGetCString(text, buffer, buffer.Length, Utf8) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : "";
        }
        finally
        {
            CFRelease(text);
        }
    }

    /// <summary>Makes a CFString; the caller releases it with <see cref="Release"/>.</summary>
    internal static IntPtr CreateCFString(string text) =>
        CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(text + '\0'), Utf8);

    internal static void Release(IntPtr cf) => CFRelease(cf);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress { public uint Selector, Scope, Element; }

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyDataSize(uint target, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, out uint size);
    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyData(uint target, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size, [Out] byte[] data);
    [DllImport(CoreAudio)]
    private static extern int AudioObjectSetPropertyData(uint target, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint size, byte[] data);
    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr text);
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr text, [Out] byte[] buffer, nint size, uint encoding);
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] text, uint encoding);
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
