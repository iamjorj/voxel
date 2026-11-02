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
