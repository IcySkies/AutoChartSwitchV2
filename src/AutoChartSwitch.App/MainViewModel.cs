using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AutoChartSwitch.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoChartSwitch.App;

public sealed class MainViewModel : ObservableObject
{
    internal const string GameMakerJacketStagingPath = "AutoChartSwitchV2/Jackets";
    private const string FallbackJacketFileName = "Memories_Sacrifice_jacket.png";
    private readonly IGameEventSource _gameSource;
    private readonly ILiveChartPublisher _publisher;
    private readonly LiveChartCoordinator _coordinator;
    private readonly AppPersistence _persistence;
    private bool _isBusy;
    private ChartInfo? _currentDisplay;
    private string _statusText = "Starting live monitor...";
    private string _connectionText = "Disconnected";
    private string _bridgeText = "Stopped";
    private string _eventText = "No game event received.";
    private string _lifecycleText = "Idle";
    private bool _hasExitError;

    public AutoChartSettings Settings { get; }
    public ObservableCollection<string> TextInputs { get; } = [];
    public ObservableCollection<string> FreeTypeInputs { get; } = [];
    public ObservableCollection<string> ImageInputs { get; } = [];
    public ObservableCollection<string> Scenes { get; } = [];
    public ChartInfo? CurrentDisplay { get => _currentDisplay; private set => SetProperty(ref _currentDisplay, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string ConnectionText { get => _connectionText; private set => SetProperty(ref _connectionText, value); }
    public string BridgeText { get => _bridgeText; private set => SetProperty(ref _bridgeText, value); }
    public string EventText { get => _eventText; private set => SetProperty(ref _eventText, value); }
    public string LifecycleText { get => _lifecycleText; private set => SetProperty(ref _lifecycleText, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool HasExitError { get => _hasExitError; private set => SetProperty(ref _hasExitError, value); }
    public string CurrentDifficulty => CurrentDisplay is null ? "" : $"{CurrentDisplay.DifficultyName} {ChartFormatter.FormatDifficulty(CurrentDisplay.DifficultyNumber)}".TrimEnd();
    public string CurrentCredits => CurrentDisplay?.CreditsText ?? "";
    public string CurrentTechStats => CurrentDisplay is null ? "" : FormatTechStats(CurrentDisplay.TechStats);
    public bool HasCurrent => CurrentDisplay is not null;
    public event EventHandler? ShowTechStatsRequested;

    public MainViewModel(IGameEventSource gameSource, ILiveChartPublisher publisher,
        LiveChartCoordinator coordinator, AppPersistence persistence, AutoChartSettings settings)
    {
        _gameSource = gameSource;
        _publisher = publisher;
        _coordinator = coordinator;
        _persistence = persistence;
        Settings = settings;
        _coordinator.SetSettings(settings);
        _coordinator.CurrentChartChanged += OnCurrentChartChanged;
        _coordinator.StatusChanged += OnStatusChanged;
        _publisher.ConnectionChanged += OnConnectionChanged;
        _gameSource.StatusChanged += OnBridgeStatusChanged;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await WriteBridgeConfigAsync();
            await _gameSource.StartAsync();
            BridgeText = "Relay subscriber starting";
            StatusText = "Live monitor ready. Connect to OBS when needed to publish chart information.";
        }
        catch (Exception ex) { StatusText = $"Startup failed: {ex.Message}"; }
    }

    public async Task ConnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (_publisher.IsConnected)
            {
                await _publisher.DisconnectAsync();
                return;
            }
            await SaveSettingsAsync();
            StatusText = "Connecting to OBS...";
            await _publisher.ConnectAsync(Settings.ObsUrl.Trim(), Settings.ObsPassword);
            await RefreshSourcesAsync();
        }
        catch (Exception ex) { StatusText = $"OBS connection failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task RefreshSourcesAsync()
    {
        if (!_publisher.IsConnected) { StatusText = "Connect to OBS before refreshing sources."; return; }
        try
        {
            var discovery = await _publisher.DiscoverAsync();
            Replace(TextInputs, discovery.Inputs.Where(x => x.Category is ObsInputCategory.Text or ObsInputCategory.FreeTypeText).Select(x => x.Name));
            Replace(FreeTypeInputs, discovery.Inputs.Where(x => x.Category == ObsInputCategory.FreeTypeText).Select(x => x.Name));
            Replace(ImageInputs, discovery.Inputs.Where(x => x.Category == ObsInputCategory.Image).Select(x => x.Name));
            Replace(Scenes, discovery.Scenes);
            StatusText = $"Loaded {discovery.Inputs.Count} compatible inputs and {discovery.Scenes.Count} scenes.";
        }
        catch (Exception ex) { StatusText = $"Source refresh failed: {ex.Message}"; }
    }

    public void ShowTechStats() => ShowTechStatsRequested?.Invoke(this, EventArgs.Empty);

    public async Task RetryExitAsync()
    {
        var result = await _publisher.SwitchToExitSceneAsync(Settings);
        StatusText = result.Message;
        HasExitError = !result.Succeeded;
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            await _persistence.SaveSettingsAsync(Settings);
            await WriteBridgeConfigAsync();
        }
        catch (Exception ex) { StatusText = $"Settings save failed: {ex.Message}"; }
    }

    private async Task WriteBridgeConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.GamePath)) return;
        var directory = Path.Combine(Settings.GamePath, "AutoChartSwitchV2");
        Directory.CreateDirectory(directory);
        var jacketDirectory = string.IsNullOrWhiteSpace(Settings.JacketCachePath)
            ? Path.Combine(directory, "Jackets")
            : Path.GetFullPath(Settings.JacketCachePath);
        Directory.CreateDirectory(jacketDirectory);
        Settings.JacketCachePath = jacketDirectory;
        SeedFallbackJacket(jacketDirectory);
        var path = Path.Combine(directory, "bridge.ini");
        await File.WriteAllTextAsync(path,
            $"[bridge]{Environment.NewLine}" +
            "port=28745" + Environment.NewLine +
            $"jacket_path={GameMakerJacketStagingPath}{Environment.NewLine}");
    }

    private static void SeedFallbackJacket(string jacketDirectory)
    {
        var source = Path.Combine(AppContext.BaseDirectory, FallbackJacketFileName);
        var destination = Path.Combine(jacketDirectory, FallbackJacketFileName);
        if (!File.Exists(destination) && File.Exists(source)) File.Copy(source, destination);
    }

    private void OnCurrentChartChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            CurrentDisplay = _coordinator.CurrentChart;
            OnPropertyChanged(nameof(CurrentDifficulty));
            OnPropertyChanged(nameof(CurrentCredits));
            OnPropertyChanged(nameof(CurrentTechStats));
            OnPropertyChanged(nameof(HasCurrent));
        });
    }

    private void OnStatusChanged(object? sender, string message) => RunOnUi(() =>
    {
        StatusText = message;
        EventText = $"Sequence {_coordinator.LastSequence}: {_coordinator.LastEventKind?.ToString() ?? "status"}";
        LifecycleText = _coordinator.LastEventKind?.ToString() ?? "Idle";
        if (message.Contains("scene switch failed", StringComparison.OrdinalIgnoreCase)) HasExitError = true;
        else if (message.Contains("Switched to", StringComparison.OrdinalIgnoreCase)) HasExitError = false;
    });

    private void OnBridgeStatusChanged(object? sender, string message) => RunOnUi(() => BridgeText = message);
    private void OnConnectionChanged(object? sender, bool connected) => RunOnUi(() => ConnectionText = connected ? "Connected" : "Disconnected");
    private void RunOnUi(Action action) => Application.Current?.Dispatcher.Invoke(action);

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Distinct(StringComparer.Ordinal)) target.Add(value);
    }

    private static string FormatTechStats(ChartTechStats stats) =>
        $"CHIP {stats.Chip:g}   TECH {stats.Tech:g}   STREAM {stats.Stream:g}\n" +
        $"CHORD {stats.Chord:g}   BURST {stats.Burst:g}" +
        (stats.Gimmick > 0 ? $"   GIMMICK {stats.Gimmick:g}" : "");

    public async Task ShutdownAsync()
    {
        await _persistence.SaveSettingsAsync(Settings);
        await _coordinator.DisposeAsync();
        await _publisher.DisposeAsync();
    }
}
