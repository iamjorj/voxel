using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace Voxel;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RoundCorners();
        Loaded += async (_, _) => await CheckForUpdateAsync();
    }

    private UserControl? _previous;
    private UpdateInfo? _update;
    private bool _updating;

    /// <summary>Shows a page in the window with a short fade-in.</summary>
    public void Navigate(UserControl page)
    {
        page.Opacity = 0;
        ContentHost.Content = page;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        // The gear only makes sense once setup is done and the voices page is the home screen.
        SettingsButton.Visibility = page is Pages.VoicesPage ? Visibility.Visible : Visibility.Collapsed;
        ShowUpdateButton();
    }

    // A moment after opening, so the check never competes with the window coming up
    // and the last update's installer has had time to exit and free its downloaded file.
    private async Task CheckForUpdateAsync()
    {
        if (!Updater.CanUpdate) return;
        await Task.Delay(TimeSpan.FromSeconds(3));
        _update = await Updater.CheckAsync();
        if (_update == null) return;
        UpdateButton.Content = $"Update to {_update.Version}";
        UpdateButton.ToolTip = $"Downloads {_update.Size / (1024 * 1024)} MB. Voxel closes and reopens when it's ready.";
        ShowUpdateButton();
    }

    private void ShowUpdateButton() =>
        UpdateButton.Visibility = _update != null && (_updating || ContentHost.Content is Pages.VoicesPage) ? Visibility.Visible : Visibility.Collapsed;

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null || _updating) return;
        _updating = true;
        try
        {
            await Updater.DownloadAndRunAsync(_update, new Progress<double>(p => UpdateButton.Content = $"Downloading {p:P0}"));
            UpdateButton.Content = "Installing…";
            Close(); // puts the real mic back; the installer takes over from here
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            UpdateButton.Content = "Update failed. Try again";
            UpdateButton.ToolTip = ex.Message;
            _updating = false;
        }
    }

    /// <summary>Opens settings over the current page; the current page stays alive (its audio keeps running).</summary>
    public void ShowSettings(UserControl settings)
    {
        _previous = ContentHost.Content as UserControl;
        Navigate(settings);
    }

    public void Back()
    {
        if (_previous != null) Navigate(_previous);
        _previous = null;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (ContentHost.Content is Pages.VoicesPage voices) voices.OpenSettings();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Windows 11 rounds borderless windows only if asked to.
    private void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = DwmWindowCornerPreferenceRound;
        DwmSetWindowAttribute(hwnd, DwmWindowAttributeCornerPreference, ref preference, sizeof(int));
    }

    private const int DwmWindowAttributeCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
