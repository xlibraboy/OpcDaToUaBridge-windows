using Avalonia.Controls;
using OpcBridge.Hmi.ViewModels;

namespace OpcBridge.Hmi.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
