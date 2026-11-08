using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Voxel.Mac.Pages;

public partial class ReadyPage : UserControl
{
    public ReadyPage()
    {
        InitializeComponent();
        CheckIcon.Data = Icons.Get("check");
        InfoIcon.Data = Icons.Get("info-circle");
    }

    private void Choose_Click(object? sender, RoutedEventArgs e)
    {
        Setup.MarkComplete();
        ((MainWindow)TopLevel.GetTopLevel(this)!).Navigate(new VoicesPage());
    }
}
