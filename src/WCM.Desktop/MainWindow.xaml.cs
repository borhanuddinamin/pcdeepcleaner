using WCM.Desktop.Services;
using WCM.Desktop.ViewModels;

namespace WCM.Desktop;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(MainWindowViewModel viewModel, ThemeService theme)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => ThemeService.SetTitleBar(this, theme.IsDark);
        Loaded += async (_, _) => await viewModel.StartAsync();
    }
}
