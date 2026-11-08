using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Voxel.Mac.Pages;

public partial class WelcomePage : UserControl
{
    public WelcomePage() => InitializeComponent();

    private void GetStarted_Click(object? sender, RoutedEventArgs e) =>
        ((MainWindow)TopLevel.GetTopLevel(this)!).Navigate(new SetupPage());
}
