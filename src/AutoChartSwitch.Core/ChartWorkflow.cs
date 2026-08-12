namespace AutoChartSwitch.Core;

public sealed class ChartWorkflow
{
    private readonly IChartQueue _queue;
    private readonly IChartValidator _validator;
    private readonly IChartPublisher _publisher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChartInfo? CurrentDisplay { get; private set; }
    public event EventHandler? CurrentDisplayChanged;

    public ChartWorkflow(IChartQueue queue, IChartValidator validator, IChartPublisher publisher)
    {
        _queue = queue;
        _validator = validator;
        _publisher = publisher;
    }

    public async Task<PublishResult> PopFrontAsync(AutoChartSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var candidate = _queue.PeekFront();
            if (candidate is null) return PublishResult.Failure("The queue is empty.");
            var validation = _validator.Validate(candidate, settings.DifficultyCustomPath, true);
            if (!validation.CanDisplay) return PublishResult.Failure(validation.Summary);

            var result = await _publisher.PublishAsync(CreateRequest(candidate, CurrentDisplay, PublishMode.Pop, settings), cancellationToken);
            if (!result.Succeeded) return result;
            if (!_queue.RemoveFront(candidate.Id)) return PublishResult.Failure("The queue changed while Pop was running.");
            CurrentDisplay = candidate;
            CurrentDisplayChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<PublishResult> QuickEditAsync(ChartInfo candidate, AutoChartSettings settings, CancellationToken cancellationToken = default)
    {
        if (CurrentDisplay is null) return PublishResult.Failure("There is no current display to edit.");
        var validation = _validator.Validate(candidate, settings.DifficultyCustomPath, true);
        if (!validation.CanDisplay) return PublishResult.Failure(validation.Summary);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = await _publisher.PublishAsync(CreateRequest(candidate, CurrentDisplay, PublishMode.QuickEdit, settings), cancellationToken);
            if (result.Succeeded)
            {
                CurrentDisplay = candidate with { Id = CurrentDisplay.Id };
                CurrentDisplayChanged?.Invoke(this, EventArgs.Empty);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<PublishResult> RetrySyncAsync(AutoChartSettings settings, CancellationToken cancellationToken = default)
    {
        if (CurrentDisplay is null) return PublishResult.Failure("There is no current display to synchronize.");
        var validation = _validator.Validate(CurrentDisplay, settings.DifficultyCustomPath, true);
        if (!validation.CanDisplay) return PublishResult.Failure(validation.Summary);
        await _gate.WaitAsync(cancellationToken);
        try { return await _publisher.PublishAsync(CreateRequest(CurrentDisplay, CurrentDisplay, PublishMode.Retry, settings), cancellationToken); }
        finally { _gate.Release(); }
    }

    private static PublishRequest CreateRequest(ChartInfo chart, ChartInfo? previous, PublishMode mode, AutoChartSettings settings) =>
        new(chart, previous, mode, CloneSettings(settings), ChartFormatter.GetDifficultyImagePath(settings.DifficultyCustomPath, chart.DifficultyName));

    private static AutoChartSettings CloneSettings(AutoChartSettings settings) => new()
    {
        ObsUrl = settings.ObsUrl,
        ObsPassword = settings.ObsPassword,
        AutoSwitch = settings.AutoSwitch,
        TransitionToSourceDelayMilliseconds = settings.TransitionToSourceDelayMilliseconds,
        DifficultyCustomPath = settings.DifficultyCustomPath,
        EntryScene = settings.EntryScene,
        ExitScene = settings.ExitScene,
        Mappings = new()
        {
            Title = settings.Mappings.Title,
            Artist = settings.Mappings.Artist,
            Credits = settings.Mappings.Credits,
            DifficultyName = settings.Mappings.DifficultyName,
            DifficultyNumber = settings.Mappings.DifficultyNumber,
            Jacket = settings.Mappings.Jacket,
            DifficultyImage = settings.Mappings.DifficultyImage,
            ShowcaseVideo = settings.Mappings.ShowcaseVideo
        }
    };
}
