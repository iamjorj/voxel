using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VoiceChanger.Core.Dsp;
using Voxel.Mac.Pages;

namespace Voxel.Mac;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AiVoiceStage.ModelDirectory = Path.Combine(AppPaths.Assets, "ai");
            var window = new MainWindow();
            // First run (or VB-Cable missing): show the welcome flow. Otherwise go straight to the voices.
            window.Navigate(Setup.IsComplete && Setup.IsCableInstalled ? new VoicesPage() : new WelcomePage());
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void About_Click(object? sender, EventArgs e) =>
        System.Diagnostics.Process.Start("/usr/bin/open", "https://github.com/iamjorj/voxel");
}
