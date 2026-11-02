using System.Windows;
using System.Windows.Controls;

namespace Voxel.Pages;

public partial class ReadyPage : UserControl
{
    public ReadyPage() => InitializeComponent();

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        Setup.MarkComplete();
        ((MainWindow)Window.GetWindow(this)!).Navigate(new VoicesPage());
    }
}
