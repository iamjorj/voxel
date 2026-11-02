using System.IO;
using System.Windows;
using VoiceChanger.Core;
using Voxel.Pages;

namespace Voxel;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Windows' Apps list runs `Voxel.exe --uninstall` (`--quiet` for no window).
        if (e.Args.Contains("--uninstall"))
        {
            if (e.Args.Contains("--quiet")) { UninstallWindow.Run(); Shutdown(); }
            else new UninstallWindow().Show();
            return;
        }

        VoiceChanger.Core.Dsp.AiVoiceStage.ModelDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "ai");

        var window = new MainWindow();
        // First run (or VB-Cable missing): show the welcome flow. Otherwise go straight to the voices.
        if (Setup.IsComplete && AudioDevices.IsVbCableInstalled())
            window.Navigate(new VoicesPage());
        else
            window.Navigate(new WelcomePage());
        window.Show();
    }
}
