using System.Runtime.InteropServices;

namespace VoiceChanger.Mac;

public enum MicrophoneAccess { NotAskedYet = 0, Restricted = 1, Denied = 2, Allowed = 3 }

/// <summary>
/// Whether macOS lets Voxel use the microphone. macOS asks the user the first time the mic is
/// opened; a denied app just gets silence, so the UI checks this to explain what to do.
/// </summary>
public static class MacPermissions
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    public static MicrophoneAccess Microphone()
    {
        NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
        var audio = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "soun"); // AVMediaTypeAudio
        return (MicrophoneAccess)(long)Send(objc_getClass("AVCaptureDevice"), sel_registerName("authorizationStatusForMediaType:"), audio);
    }

    /// <summary>Opens System Settings at Privacy &amp; Security → Microphone.</summary>
    public static void OpenMicrophoneSettings() =>
        System.Diagnostics.Process.Start("/usr/bin/open", "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone");

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
}
