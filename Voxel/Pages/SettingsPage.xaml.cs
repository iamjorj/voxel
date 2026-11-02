using System.IO;
using System.Windows;
using System.Windows.Controls;
using VoiceChanger.Core;

namespace Voxel.Pages;

public partial class SettingsPage : UserControl
{
    private readonly Action _beforeSetup;

    /// <param name="beforeSetup">Called before the welcome flow is reopened (turns the current voice off).</param>
    public SettingsPage(Action beforeSetup)
    {
        InitializeComponent();
        _beforeSetup = beforeSetup;
        var installed = AudioDevices.IsVbCableInstalled();
        CableStatus.Text = installed ? "Installed (VB-CABLE by VB-Audio, www.vb-cable.com)" : "Not installed: run setup to add it.";
    }

    private void Setup_Click(object sender, RoutedEventArgs e)
    {
        _beforeSetup();
        var flag = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voxel", "setup-complete");
        if (File.Exists(flag)) File.Delete(flag);
        ((MainWindow)Window.GetWindow(this)!).Navigate(new WelcomePage());
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ((MainWindow)Window.GetWindow(this)!).Back();
}
