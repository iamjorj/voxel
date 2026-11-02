using System.Windows;
using System.Windows.Controls;

namespace Voxel.Pages;

public partial class WelcomePage : UserControl
{
    public WelcomePage() => InitializeComponent();

    private void GetStarted_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow(this)!).Navigate(new SetupPage());
}
