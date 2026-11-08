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
            AiVoiceStage.ModelDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "ai");
            var window = new MainWindow();
            // First run (or VB-Cable missing): show the welcome flow. Otherwise go straight to the voices.
            window.Navigate(Setup.IsComplete && Setup.IsCableInstalled ? new VoicesPage() : new WelcomePage());
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
