using System.Windows;

namespace AutoChartSwitch.App;

public partial class MainWindow : Window
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow() => InitializeComponent();

    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ViewModel?.ConnectAsync());
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ViewModel?.RefreshSourcesAsync());
    private void TechStats_Click(object sender, RoutedEventArgs e) => ViewModel?.ShowTechStats();
    private async void RetryExit_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ViewModel?.RetryExitAsync());
    private async void SettingsChanged(object sender, RoutedEventArgs e) => await RunAsync(() => ViewModel?.SaveSettingsAsync());

    private async Task RunAsync(Func<Task?> action)
    {
        var task = action();
        if (task is not null) await task;
    }

    private void ObsPasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is System.Windows.Controls.PasswordBox box) box.Password = ViewModel.Settings.ObsPassword;
    }

    private void ObsPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is System.Windows.Controls.PasswordBox box) ViewModel.Settings.ObsPassword = box.Password;
    }
}
