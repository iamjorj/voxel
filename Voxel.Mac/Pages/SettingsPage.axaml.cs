using Avalonia.Controls;
using Avalonia.Interactivity;
using VoiceChanger.Mac;

namespace Voxel.Mac.Pages;

public partial class SettingsPage : UserControl
{
    private readonly Action _beforeSetup;

    /// <param name="beforeSetup">Called before the welcome flow is reopened (turns the current voice off).</param>
    public SettingsPage(Action beforeSetup)
    {
        InitializeComponent();
        _beforeSetup = beforeSetup;
        BackIcon.Data = Icons.Get("arrow-left");
        CableStatus.Text = Setup.IsCableInstalled ? "Installed (VB-CABLE by VB-Audio, www.vb-cable.com)" : "Not installed: run setup to add it.";
        MicStatus.Text = (OperatingSystem.IsMacOS() ? MacPermissions.Microphone() : MicrophoneAccess.NotAskedYet) switch
        {
            MicrophoneAccess.Allowed => "Allowed.",
            MicrophoneAccess.Denied or MicrophoneAccess.Restricted => "Not allowed, so Voxel hears nothing. Turn Voxel on under Privacy & Security → Microphone.",
            _ => "Your Mac asks the first time you turn on a voice.",
        };
    }

    private void Setup_Click(object? sender, RoutedEventArgs e)
    {
        _beforeSetup();
        Setup.Reset();
        ((MainWindow)TopLevel.GetTopLevel(this)!).Navigate(new WelcomePage());
    }

    private void Privacy_Click(object? sender, RoutedEventArgs e) => MacPermissions.OpenMicrophoneSettings();

    private void Back_Click(object? sender, RoutedEventArgs e) => ((MainWindow)TopLevel.GetTopLevel(this)!).Back();
}
