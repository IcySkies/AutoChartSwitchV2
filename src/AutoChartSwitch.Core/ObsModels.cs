namespace AutoChartSwitch.Core;

public enum ObsOutput
{
    Title,
    Artist,
    Credits,
    DifficultyName,
    DifficultyNumber,
    Jacket,
    DifficultyImage,
    ShowcaseVideo
}

public enum ObsInputCategory { Text, FreeTypeText, Image, Media }

public sealed record ObsInputInfo(string Name, string Kind, string UnversionedKind, ObsInputCategory Category);

public sealed class ObsSourceMappings
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Credits { get; set; } = "";
    public string DifficultyName { get; set; } = "";
    public string DifficultyNumber { get; set; } = "";
    public string Jacket { get; set; } = "";
    public string DifficultyImage { get; set; } = "";
    public string ShowcaseVideo { get; set; } = "";

    public IReadOnlyDictionary<ObsOutput, string> AsLiveDictionary() => new Dictionary<ObsOutput, string>
    {
        [ObsOutput.Title] = Title,
        [ObsOutput.Artist] = Artist,
        [ObsOutput.Credits] = Credits,
        [ObsOutput.DifficultyName] = DifficultyName,
        [ObsOutput.DifficultyNumber] = DifficultyNumber,
        [ObsOutput.Jacket] = Jacket,
        [ObsOutput.DifficultyImage] = DifficultyImage
    };

    public IReadOnlyDictionary<ObsOutput, string> AsDictionary() => new Dictionary<ObsOutput, string>
    {
        [ObsOutput.Title] = Title,
        [ObsOutput.Artist] = Artist,
        [ObsOutput.Credits] = Credits,
        [ObsOutput.DifficultyName] = DifficultyName,
        [ObsOutput.DifficultyNumber] = DifficultyNumber,
        [ObsOutput.Jacket] = Jacket,
        [ObsOutput.DifficultyImage] = DifficultyImage,
        [ObsOutput.ShowcaseVideo] = ShowcaseVideo
    };
}

public sealed class AutoChartSettings
{
    public string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
    public string ObsPassword { get; set; } = "";
    public bool AutoSwitch { get; set; }
    public int BridgePort { get; set; } = 28745;
    public string GamePath { get; set; } = "";
    public string JacketCachePath { get; set; } = "";
    public int TransitionToSourceDelayMilliseconds { get; set; }
    public string DifficultyCustomPath { get; set; } = "";
    public string EntryScene { get; set; } = "";
    public string ExitScene { get; set; } = "";
    public ObsSourceMappings Mappings { get; set; } = new();
}

public enum PublishMode { Pop, QuickEdit, Retry }

public sealed record PublishRequest(
    ChartInfo Chart,
    ChartInfo? PreviousChart,
    PublishMode Mode,
    AutoChartSettings Settings,
    string DifficultyImagePath);

public sealed record PublishResult(bool Succeeded, string Message, bool RollbackFailed = false)
{
    public static PublishResult Success(string message = "Synchronized with OBS.") => new(true, message);
    public static PublishResult Failure(string message, bool rollbackFailed = false) => new(false, message, rollbackFailed);
}

public sealed record ObsDiscovery(IReadOnlyList<ObsInputInfo> Inputs, IReadOnlyList<string> Scenes);

public interface IChartPublisher : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<string>? StatusChanged;
    event EventHandler<bool>? ConnectionChanged;
    Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default);
    Task<PublishResult> RetryExitSceneAsync(CancellationToken cancellationToken = default);
}
