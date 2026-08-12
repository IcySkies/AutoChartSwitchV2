using System.Text.Json.Serialization;

namespace AutoChartSwitch.Core;

public enum GameEventKind
{
    Selection,
    ChartLoadingStarted,
    ChartStarted,
    ChartExitTransitionStarted,
    GameplayEnded,
    LobbySelection
}

public sealed record GameChartSnapshot
{
    public string ChartId { get; init; } = "";
    public string RawDifficultyName { get; init; } = "";
    public string DifficultyCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string FormattedTitle { get; init; } = "";
    public string Artist { get; init; } = "";
    public string FormattedArtist { get; init; } = "";
    public string Illustrator { get; init; } = "";
    public string FormattedIllustrator { get; init; } = "";
    public string Charter { get; init; } = "";
    public string FormattedCharter { get; init; } = "";
    public decimal DifficultyNumber { get; init; }
    public string JacketPath { get; init; } = "";
    public ChartTechStats TechStats { get; init; } = new();

    [JsonIgnore]
    public string CanonicalDifficultyCode => DifficultyCodes.Normalize(
        string.IsNullOrWhiteSpace(DifficultyCode) ? RawDifficultyName : DifficultyCode);

    public ChartInfo ToChartInfo() => new()
    {
        GameChartId = ChartId.Trim(),
        RawDifficultyName = RawDifficultyName.Trim(),
        Title = Title.Trim(),
        FormattedTitle = FormattedTitle.Trim(),
        Artist = Artist.Trim(),
        FormattedArtist = FormattedArtist.Trim(),
        Illustrator = Illustrator.Trim(),
        FormattedIllustrator = FormattedIllustrator.Trim(),
        Charter = Charter.Trim(),
        FormattedCharter = FormattedCharter.Trim(),
        DifficultyName = CanonicalDifficultyCode,
        DifficultyNumber = DifficultyNumber,
        JacketPath = JacketPath.Trim(),
        TechStats = TechStats ?? new()
    };
}

public sealed record GameEventEnvelope
{
    public int ProtocolVersion { get; init; } = 1;
    public long Sequence { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public GameEventKind Kind { get; init; }
    public GameChartSnapshot? Chart { get; init; }
}

public interface IGameEventSource : IAsyncDisposable
{
    bool IsListening { get; }
    int Port { get; }
    event EventHandler<GameEventEnvelope>? EventReceived;
    event EventHandler<string>? StatusChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface ILiveChartPublisher : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<string>? StatusChanged;
    event EventHandler<bool>? ConnectionChanged;
    Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<PublishResult> PublishSelectionAsync(ChartInfo chart, AutoChartSettings settings, CancellationToken cancellationToken = default);
    Task<PublishResult> SwitchToEntrySceneAsync(AutoChartSettings settings, CancellationToken cancellationToken = default);
    Task<PublishResult> SwitchToExitSceneAsync(AutoChartSettings settings, CancellationToken cancellationToken = default);
}

public sealed class LiveChartCoordinator : IAsyncDisposable
{
    private readonly IGameEventSource _source;
    private readonly ILiveChartPublisher _publisher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _eventQueueLock = new();
    private Task _eventTail = Task.CompletedTask;
    private bool _autoSwitchSessionActive;
    private bool _disposed;

    public ChartInfo? CurrentChart { get; private set; }
    public GameEventKind? LastEventKind { get; private set; }
    public long LastSequence { get; private set; }
    public event EventHandler? CurrentChartChanged;
    public event EventHandler<string>? StatusChanged;

    public LiveChartCoordinator(IGameEventSource source, ILiveChartPublisher publisher)
    {
        _source = source;
        _publisher = publisher;
        _source.EventReceived += OnEventReceived;
        _source.StatusChanged += OnSourceStatusChanged;
        _publisher.StatusChanged += OnPublisherStatusChanged;
    }

    public async Task HandleAsync(GameEventEnvelope envelope, AutoChartSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LastSequence = envelope.Sequence;
            LastEventKind = envelope.Kind;
            if (envelope.Chart is not null)
            {
                CurrentChart = envelope.Chart.ToChartInfo();
                CurrentChartChanged?.Invoke(this, EventArgs.Empty);
            }

            switch (envelope.Kind)
            {
                case GameEventKind.Selection when CurrentChart is not null:
                case GameEventKind.LobbySelection when CurrentChart is not null:
                    Raise(await _publisher.PublishSelectionAsync(CurrentChart, settings, cancellationToken));
                    break;
                case GameEventKind.ChartLoadingStarted:
                    if (CurrentChart is not null)
                        Raise(await _publisher.PublishSelectionAsync(CurrentChart, settings, cancellationToken));
                    if (settings.AutoSwitch && !_autoSwitchSessionActive)
                    {
                        _autoSwitchSessionActive = true;
                        Raise(await _publisher.SwitchToEntrySceneAsync(settings, cancellationToken));
                    }
                    break;
                case GameEventKind.ChartStarted:
                    if (CurrentChart is not null)
                        Raise(await _publisher.PublishSelectionAsync(CurrentChart, settings, cancellationToken));
                    break;
                case GameEventKind.ChartExitTransitionStarted:
                    if (_autoSwitchSessionActive)
                    {
                        _autoSwitchSessionActive = false;
                        if (settings.AutoSwitch) Raise(await _publisher.SwitchToExitSceneAsync(settings, cancellationToken));
                    }
                    break;
                case GameEventKind.GameplayEnded:
                    _autoSwitchSessionActive = false;
                    break;
            }
        }
        finally { _gate.Release(); }
    }

    private void OnEventReceived(object? sender, GameEventEnvelope envelope)
    {
        lock (_eventQueueLock)
        {
            _eventTail = _eventTail.ContinueWith(
                _ => HandleAsync(envelope, CurrentSettings ?? new()),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }
    }

    private AutoChartSettings? CurrentSettings { get; set; }
    public void SetSettings(AutoChartSettings settings) => CurrentSettings = settings;

    private void OnSourceStatusChanged(object? sender, string message) => StatusChanged?.Invoke(this, message);
    private void OnPublisherStatusChanged(object? sender, string message) => StatusChanged?.Invoke(this, message);
    private void Raise(PublishResult result) => StatusChanged?.Invoke(this, result.Message);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _source.EventReceived -= OnEventReceived;
        await _source.StopAsync();
        await _source.DisposeAsync();
        Task pending;
        lock (_eventQueueLock) pending = _eventTail;
        try { await pending; }
        catch (Exception ex) { StatusChanged?.Invoke(this, $"Pending game event failed: {ex.Message}"); }
        _gate.Dispose();
    }
}
