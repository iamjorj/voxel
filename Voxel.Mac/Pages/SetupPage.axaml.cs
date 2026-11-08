using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using VoiceChanger.Mac;

namespace Voxel.Mac.Pages;

public partial class SetupPage : UserControl
{
    private bool _installed;

    public SetupPage()
    {
        InitializeComponent();
        ShowState(Setup.IsCableInstalled);
    }

    private async void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (_installed)
        {
            ((MainWindow)TopLevel.GetTopLevel(this)!).Navigate(new ReadyPage());
            return;
        }

        ActionButton.IsEnabled = false;
        ActionButton.Content = "Installing…";
        SetStatus("Waiting for your password…", "TextMutedBrush");
        try
        {
            if (await MacVbCableInstaller.InstallAsync(Setup.VbCableFolder)) ShowState(true);
            else SetStatus("Didn't finish. Try again.", "DangerBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled. Click Install to try again.", "DangerBrush");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't install: {ex.Message}", "DangerBrush");
        }
        finally
        {
            if (!_installed) { ActionButton.Content = "Install"; ActionButton.IsEnabled = true; }
        }
    }

    internal void ShowState(bool installed)
    {
        _installed = installed;
        if (installed)
        {
            StatusIcon.Data = Icons.Get("check");
            StatusIcon.Stroke = Brush("SuccessBrush");
            StatusCircle.Background = Brush("SuccessSoftBrush");
            SetStatus("Installed", "SuccessBrush");
            ActionButton.Content = "Continue";
            ActionButton.IsEnabled = true;
        }
        else
        {
            StatusIcon.Data = Icons.Get("microphone");
            SetStatus("Not installed", "TextMutedBrush");
            ActionButton.Content = "Install";
        }
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = Brush(brush);
    }

    // From the app, not this page: the page isn't in the window yet when the constructor sets the first state.
    private static IBrush Brush(string key) => (IBrush)Avalonia.Application.Current!.FindResource(key)!;
}
