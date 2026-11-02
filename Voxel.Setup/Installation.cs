using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace Voxel.Install;

/// <summary>
/// Where Voxel lives on the user's PC and how it gets there and away again. Per-user install:
/// no admin rights, files in %LocalAppData%\Programs\Voxel, a Start-menu shortcut and an entry
/// in Windows' installed-apps list. Shared by VoxelSetup (install) and Voxel.exe --uninstall.
/// </summary>
public static class Installation
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string InstallDir { get; } = Path.Combine(LocalAppData, "Programs", "Voxel");
    public static string ExePath { get; } = Path.Combine(InstallDir, "Voxel.exe");

    /// <summary>Settings and state the app writes: setup flag, remembered real mic.</summary>
    private static readonly string[] DataDirs = [Path.Combine(LocalAppData, "Voxel"), Path.Combine(LocalAppData, "VoiceChanger")];

    private static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Voxel.lnk");
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Voxel.lnk");
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Voxel";

    public static bool IsInstalled => File.Exists(ExePath);

    /// <summary>Unpacks the app (a zip of the published folder), replacing any older version.</summary>
    public static void Install(Stream zip, string version, bool desktopShortcut, IProgress<double>? progress = null)
    {
        CloseRunningApp();
        if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, recursive: true);
        Directory.CreateDirectory(InstallDir);

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        long total = archive.Entries.Sum(e => e.Length), done = 0;
        var root = InstallDir + Path.DirectorySeparatorChar;
        var buffer = new byte[1 << 20];
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(InstallDir, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Bad path in package: {entry.FullName}");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var file = File.Create(target);
            int n;
            while ((n = source.Read(buffer)) > 0)
            {
                file.Write(buffer, 0, n);
                done += n;
                progress?.Report(total == 0 ? 1 : (double)done / total);
            }
        }

        CreateShortcut(StartMenuShortcut);
        if (desktopShortcut) CreateShortcut(DesktopShortcut);
        else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);
        Register(version, total / 1024);
    }

    /// <summary>
    /// Removes shortcuts, the installed-apps entry and Voxel's settings, then the program folder.
    /// Called from the installed Voxel.exe, which can't delete its own folder while it runs:
    /// a hidden cmd waits a moment for it to exit and removes the folder then.
    /// </summary>
    public static void Uninstall()
    {
        CloseRunningApp();
        foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut })
            if (File.Exists(shortcut)) File.Delete(shortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        foreach (var dir in DataDirs)
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }

        // Only ever delete the real install folder, and only from the copy that lives in it.
        var running = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(running, InstallDir, StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    /// <summary>Starts the installed app as the current user.</summary>
    public static void Launch() =>
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = InstallDir });

    /// <summary>Asks any other running Voxel to close (it puts the real mic back as it exits), then makes sure it's gone.</summary>
    private static void CloseRunningApp()
    {
        var self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("Voxel").Where(p => p.Id != self))
        {
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(5000)) { p.Kill(); p.WaitForExit(3000); }
            }
            catch (InvalidOperationException) { } // already gone
        }
    }

    /// <summary>
    /// Writes a .lnk with the shell's own IShellLinkW. (WScript.Shell can't save to folders with
    /// non-English names, e.g. a OneDrive desktop called "Рабочий стол".)
    /// </summary>
    private static void CreateShortcut(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(ExePath);
            link.SetWorkingDirectory(InstallDir);
            link.SetIconLocation(ExePath, 0);
            link.SetDescription("Free one-click voice changer");
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        // Vtable order matters; only the setters are used.
        void GetPath(IntPtr file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription(IntPtr name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(IntPtr iconPath, int size, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    private static void Register(string version, long sizeKb)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Voxel");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Javohir Doniyorov");
        key.SetValue("DisplayIcon", ExePath);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{ExePath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{ExePath}\" --uninstall --quiet");
        key.SetValue("URLInfoAbout", "https://github.com/iamjorj/voxel");
        key.SetValue("HelpLink", "https://github.com/iamjorj/voxel/issues");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("EstimatedSize", (int)Math.Min(sizeKb, int.MaxValue), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
