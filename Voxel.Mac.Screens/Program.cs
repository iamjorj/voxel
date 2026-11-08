using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Voxel.Mac;
using Voxel.Mac.Pages;

namespace Voxel.Mac.Screens;

static class Program
{
    static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "screens");
        Directory.CreateDirectory(output);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var window = new MainWindow();
        window.Show();

        Shoot(window, new WelcomePage(), output, "1-welcome");
        Shoot(window, new SetupPage(), output, "2-setup");
        var installed = new SetupPage();
        installed.ShowState(true);
        Shoot(window, installed, output, "3-setup-installed");
        Shoot(window, new ReadyPage(), output, "4-ready");
        Shoot(window, new VoicesPage(), output, "5-voices");
        var ai = new VoicesPage();
        Shoot(window, ai, output, "6-voices-ai-on", () => ai.Preview("AI", "E-girl", 0.7));
        var fun = new VoicesPage();
        Shoot(window, fun, output, "7-voices-fun-on", () => fun.Preview("Fun", "Clown", 0.45));
        Shoot(window, new SettingsPage(() => { }), output, "8-settings");

        Console.WriteLine($"Saved to {output}");
        return 0;
    }

    private static void Shoot(MainWindow window, UserControl page, string folder, string name, Action? arrange = null)
    {
        window.Navigate(page);
        arrange?.Invoke();
        // Settle layout and skip the fade-in.
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        page.Transitions = null;
        page.Opacity = 1;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
        using (var file = File.Create(Path.Combine(folder, name + ".png"))) frame.Save(file);
        Console.WriteLine($"  {name}: {frame.PixelSize.Width}x{frame.PixelSize.Height}");
    }
}
