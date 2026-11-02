using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceChanger.Core;

namespace Voxel.Pages;

public partial class SetupPage : UserControl
{
    private bool _installed;

    public SetupPage()
    {
        InitializeComponent();
        ShowState(AudioDevices.IsVbCableInstalled());
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_installed)
        {
            ((MainWindow)Window.GetWindow(this)!).Navigate(new ReadyPage());
            return;
        }

        ActionButton.IsEnabled = false;
        ActionButton.Content = "Installing…";
        SetStatus("Waiting for Windows…", (Brush)FindResource("TextMutedBrush"));
        try
        {
            var ok = await VbCableInstaller.InstallAsync(Setup.VbCableSetupPath);
            if (ok) ShowState(true);
            else SetStatus("Didn't finish. Try again.", (Brush)FindResource("DangerBrush"));
        }
        catch (Exception ex)
        {
            // 1223 = the user clicked "No" on the UAC prompt.
            var cancelled = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 };
            SetStatus(cancelled ? "Cancelled. Click Install to try again." : $"Couldn't install: {ex.Message}",
                      (Brush)FindResource("DangerBrush"));
        }
        finally
        {
            if (!_installed) { ActionButton.Content = "Install"; ActionButton.IsEnabled = true; }
        }
    }

    private void ShowState(bool installed)
    {
        _installed = installed;
        if (installed)
        {
            StatusIcon.Text = ""; // check mark
            StatusIcon.Foreground = (Brush)FindResource("SuccessBrush");
            ((Border)StatusIcon.Parent).Background = (Brush)FindResource("SuccessSoftBrush");
            SetStatus("Installed", (Brush)FindResource("SuccessBrush"));
            ActionButton.Content = "Continue";
            ActionButton.IsEnabled = true;
        }
        else
        {
            SetStatus("Not installed", (Brush)FindResource("TextMutedBrush"));
            ActionButton.Content = "Install";
        }
    }

    private void SetStatus(string text, Brush color)
    {
        StatusText.Text = text;
        StatusText.Foreground = color;
    }
}
