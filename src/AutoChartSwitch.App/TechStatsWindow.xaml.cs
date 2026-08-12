using System.ComponentModel;
using System.Windows;

namespace AutoChartSwitch.App;

public partial class TechStatsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public TechStatsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += (_, _) => Renderer.SetChart(_viewModel.CurrentDisplay);
        Closing += TechStatsWindow_Closing;
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    public void AllowClose() => _allowClose = true;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentDisplay)) Renderer.SetChart(_viewModel.CurrentDisplay);
    }

    private void TechStatsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }
}
