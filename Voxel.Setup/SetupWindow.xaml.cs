using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Voxel.Install;

namespace Voxel.Setup;

public partial class SetupWindow : Window
{
    private readonly string[] _args;
    private readonly bool _update;
    private bool _busy;

    public SetupWindow(string[] args)
    {
        InitializeComponent();
        _args = args;
        SourceInitialized += (_, _) => RoundCorners();
        if (Installation.IsInstalled)
        {
            Heading.Text = "Update Voxel";
            Message.Text = $"Replaces the Voxel on this PC with version {App.Version}. Your voices and settings stay.";
        }
        Footnote.Text = $"Version {App.Version} · installs for you only, no admin needed";

        // `--update`: the app's Update button started this, so no questions: keep the user's shortcut choice, install, reopen.
        _update = args.Contains("--update") && Installation.IsInstalled;
        if (_update)
        {
            DesktopToggle.IsChecked = Installation.HasDesktopShortcut;
            ReadyPanel.Visibility = Visibility.Collapsed;
            Loaded += (_, _) => Install_Click(this, new RoutedEventArgs());
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var desktop = DesktopToggle.IsChecked == true;
        ReadyPanel.Visibility = ErrorPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        Heading.Text = _update ? "Updating Voxel" : "Installing Voxel";
        Message.Text = "This takes a few seconds.";
        _busy = true;

        var progress = new Progress<double>(p =>
        {
            ProgressFill.Width = p * ((FrameworkElement)ProgressFill.Parent).ActualWidth;
            ProgressText.Text = $"Installing… {p:P0}";
        });
        try
        {
            await Task.Run(() =>
            {
                using var payload = Payload.Open(_args) ?? throw new InvalidOperationException("This setup file is damaged. Please download it again.");
                Installation.Install(payload, App.Version, desktop, progress);
            });
            if (_update)
            {
                _busy = false;
                Open_Click(this, e);
                return;
            }
            ProgressPanel.Visibility = Visibility.Collapsed;
            DonePanel.Visibility = Visibility.Visible;
            Heading.Text = "Voxel is ready";
            Message.Text = "Find it in the Start menu anytime.";
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            Heading.Text = "Install didn't finish";
            Message.Text = "Please try again.";
            File.WriteAllText(App.ErrorLog, ex.ToString());
            ErrorText.Text = ex is UnauthorizedAccessException or IOException
                ? $"A file is in use. Close Voxel if it's open, then try again.\n({ex.Message})"
                : ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Installation.Launch();
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // A half-copied install would leave a broken app behind, so closing waits for the copy to finish.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnClosing(e);
    }

    // Windows 11 rounds borderless windows only if asked to.
    private void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
