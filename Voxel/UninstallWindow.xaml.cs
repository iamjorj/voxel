using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Voxel.Install;

namespace Voxel;

/// <summary>Shown by `Voxel.exe --uninstall` (what Windows' Apps list runs).</summary>
public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RoundCorners();
    }

    /// <summary>Puts the real mic back, then removes Voxel. Also used by `--uninstall --quiet`.</summary>
    public static void Run()
    {
        // Before Installation.Uninstall deletes the file that remembers which mic was the real one.
        try { new VoiceChanger.Core.DefaultMicSwitcher().Restore(); } catch { }
        Installation.Uninstall();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Run();
            Heading.Text = "Voxel was removed";
            Message.Text = "Thanks for trying it.";
        }
        catch (Exception ex)
        {
            Heading.Text = "Uninstall didn't finish";
            Message.Text = ex.Message;
        }
        AskPanel.Visibility = Visibility.Collapsed;
        DoneButton.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
