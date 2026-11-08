using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Voxel.Mac.Pages;

namespace Voxel.Mac;

public partial class MainWindow : Window
{
    private UserControl? _previous;

    public MainWindow()
    {
        InitializeComponent();
        SettingsIcon.Data = Icons.Get("settings");
    }

    /// <summary>Shows a page in the window with a short fade-in.</summary>
    public void Navigate(UserControl page)
    {
        page.Opacity = 0;
        page.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }];
        ContentHost.Content = page;
        Dispatcher.UIThread.Post(() => page.Opacity = 1, DispatcherPriority.Render);
        // The gear only makes sense once setup is done and the voices page is the home screen.
        SettingsButton.IsVisible = page is VoicesPage;
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

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (ContentHost.Content is VoicesPage voices) voices.OpenSettings();
    }

    // The whole top strip moves the window, like a normal Mac title bar.
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
}
