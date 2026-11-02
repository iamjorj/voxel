using System.IO;
using System.Windows;
using Voxel.Install;

namespace Voxel.Setup;

public partial class App : Application
{
    /// <summary>Version being installed (this setup's own version; the build script keeps it equal to the app's).</summary>
    public static string Version { get; } = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Anything that slips through ends up here instead of the setup just vanishing.</summary>
    public static string ErrorLog { get; } = Path.Combine(Path.GetTempPath(), "VoxelSetup-error.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            File.WriteAllText(ErrorLog, ex.Exception.ToString());
            MessageBox.Show(ex.Exception.Message, "Voxel Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => File.WriteAllText(ErrorLog, ex.ExceptionObject.ToString());

        // `VoxelSetup.exe --silent [--desktop]`: install without a window (exit code 0 = installed).
        if (e.Args.Contains("--silent"))
        {
            try
            {
                using var payload = Payload.Open(e.Args) ?? throw new InvalidOperationException("No app inside this setup.");
                Installation.Install(payload, Version, e.Args.Contains("--desktop"));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Shutdown(1);
            }
            return;
        }

        new SetupWindow(e.Args).Show();
    }
}
