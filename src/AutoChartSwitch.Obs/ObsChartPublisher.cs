using AutoChartSwitch.Core;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;

namespace AutoChartSwitch.Obs;

public sealed class ObsChartPublisher : IChartPublisher, ILiveChartPublisher
{
    public const string DefaultTextFont = "HYPixel 9px MERGED";
    public const string FallbackTextFont = "Unifont";
    private const string RestartAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART";
    private readonly OBSWebsocket _client = new();
    private readonly Func<string, bool> _defaultFontSupports;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _cycleLock = new();
    private bool _disposed;
    private long _generation;
    private PlaybackCycle? _cycle;
    private string? _pendingExitScene;

    public bool IsConnected => _client.IsConnected;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<bool>? ConnectionChanged;

    public ObsChartPublisher(Func<string, bool>? defaultFontSupports = null)
    {
        _defaultFontSupports = defaultFontSupports ?? (_ => true);
        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
        _client.MediaInputPlaybackStarted += OnPlaybackStarted;
        _client.MediaInputPlaybackEnded += OnPlaybackEnded;
    }

    public async Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "ws")
            throw new ArgumentException("OBS URL must be an absolute ws:// URL.", nameof(url));

        if (_client.IsConnected) _client.Disconnect();

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => connected.TrySetResult();
        _client.Connected += handler;
        try
        {
            _client.ConnectAsync(url, password);
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        }
        finally { _client.Connected -= handler; }
    }

    public Task DisconnectAsync()
    {
        if (_client.IsConnected) _client.Disconnect();
        ConnectionChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public async Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return await WithClientAsync(() =>
        {
            var inputs = _client.GetInputList()
                .Select(ToInputInfo)
                .Where(x => x is not null)
                .Cast<ObsInputInfo>()
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var scenes = _client.ListScenes().Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            return new ObsDiscovery(inputs, scenes);
        }, cancellationToken);
    }

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected) return PublishResult.Failure("OBS is not connected.");
        await _requestGate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => PublishCoreAsync(request, cancellationToken), cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return PublishResult.Failure($"OBS publish failed: {Friendly(ex)}"); }
        finally { _requestGate.Release(); }
    }

    public async Task<PublishResult> RetryExitSceneAsync(CancellationToken cancellationToken = default)
    {
        string? scene;
        lock (_cycleLock) scene = _pendingExitScene;
        if (string.IsNullOrWhiteSpace(scene)) return PublishResult.Failure("There is no failed exit-scene switch to retry.");
        if (!_client.IsConnected) return PublishResult.Failure("OBS is not connected.");
        try
        {
            await WithClientAsync(() => { _client.SetCurrentProgramScene(scene); return true; }, cancellationToken);
            lock (_cycleLock) _pendingExitScene = null;
            RaiseStatus($"Switched to exit scene '{scene}'.");
            return PublishResult.Success("Exit scene switch completed.");
        }
        catch (Exception ex)
        {
            var message = $"Exit scene switch failed: {Friendly(ex)}";
            RaiseStatus(message);
            return PublishResult.Failure(message);
        }
    }

    public async Task<PublishResult> PublishSelectionAsync(ChartInfo chart, AutoChartSettings settings, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected) return PublishResult.Failure("OBS is not connected.");
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => PublishLiveCoreAsync(chart, settings, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return PublishResult.Failure($"OBS publish failed: {Friendly(ex)}"); }
        finally { _requestGate.Release(); }
    }

    public async Task<PublishResult> SwitchToEntrySceneAsync(AutoChartSettings settings, CancellationToken cancellationToken = default) =>
        await SwitchToSceneAsync(settings.EntryScene, "entry", cancellationToken);

    public async Task<PublishResult> SwitchToExitSceneAsync(AutoChartSettings settings, CancellationToken cancellationToken = default) =>
        await SwitchToSceneAsync(settings.ExitScene, "exit", cancellationToken);

    private async Task<PublishResult> PublishLiveCoreAsync(ChartInfo chart, AutoChartSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovery = DiscoverCore();
        var mappings = settings.Mappings.AsLiveDictionary();
        var mappingError = ValidateLiveMappings(mappings, discovery.Inputs);
        if (mappingError is not null) return PublishResult.Failure(mappingError);

        var resolvedChart = ResolveGamePaths(chart, settings.GamePath);
        var jacket = ChartAssetResolver.ResolveJacket(resolvedChart, settings.JacketCachePath);
        var difficultyImage = ChartAssetResolver.ResolveDifficultyImage(settings.DifficultyCustomPath, chart.DifficultyName);
        var presentation = ResolveTextPresentation(resolvedChart, _defaultFontSupports);
        var values = ProjectLive(resolvedChart, jacket, difficultyImage)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        ApplyTextPresentation(values, presentation);
        var outputs = values.Keys.Where(output => output is not (ObsOutput.Jacket or ObsOutput.DifficultyImage) || !string.IsNullOrWhiteSpace(values[output])).ToList();
        var snapshots = outputs.ToDictionary(output => output, output => (JObject)_client.GetInputSettings(mappings[output]).Settings.DeepClone());
        var modified = new List<ObsOutput>();
        try
        {
            foreach (var output in outputs)
            {
                SetValue(mappings[output], output, values[output], presentation.FontFaces.GetValueOrDefault(output), snapshots[output]);
                modified.Add(output);
            }

            var warning = new List<string>();
            if (string.IsNullOrWhiteSpace(jacket)) warning.Add("jacket asset unavailable");
            if (string.IsNullOrWhiteSpace(difficultyImage)) warning.Add("difficulty image unavailable");
            var message = warning.Count == 0 ? "Chart information synchronized with OBS." :
                $"Chart information synchronized with OBS ({string.Join(", ", warning)}).";
            RaiseStatus(message);
            return PublishResult.Success(message);
        }
        catch (Exception ex)
        {
            foreach (var output in modified.AsEnumerable().Reverse())
            {
                try { _client.SetInputSettings(mappings[output], snapshots[output], false); }
                catch { }
            }
            return PublishResult.Failure($"OBS publish failed: {Friendly(ex)}");
        }
    }

    internal static ChartInfo ResolveGamePaths(ChartInfo chart, string gamePath, string? gameMakerSandboxRoot = null)
    {
        if (string.IsNullOrWhiteSpace(chart.JacketPath) || Path.IsPathRooted(chart.JacketPath))
            return chart;

        gameMakerSandboxRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIVIDSTASIS");
        var sandboxPath = string.IsNullOrWhiteSpace(gameMakerSandboxRoot)
            ? ""
            : Path.GetFullPath(Path.Combine(gameMakerSandboxRoot, chart.JacketPath));
        if (!string.IsNullOrWhiteSpace(sandboxPath) && File.Exists(sandboxPath))
            return chart with { JacketPath = sandboxPath };

        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            var legacyPath = Path.GetFullPath(Path.Combine(gamePath, chart.JacketPath));
            if (File.Exists(legacyPath)) return chart with { JacketPath = legacyPath };
        }

        return string.IsNullOrWhiteSpace(sandboxPath) ? chart : chart with { JacketPath = sandboxPath };
    }

    private async Task<PublishResult> SwitchToSceneAsync(string scene, string label, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected) return PublishResult.Failure("OBS is not connected.");
        if (string.IsNullOrWhiteSpace(scene)) return PublishResult.Failure($"The {label} scene is required.");
        try
        {
            await WithClientAsync(() =>
            {
                if (!_client.ListScenes().Any(item => StringComparer.Ordinal.Equals(item.Name, scene)))
                    throw new InvalidOperationException($"The configured {label} scene '{scene}' does not exist in OBS.");
                _client.SetCurrentProgramScene(scene);
                return true;
            }, cancellationToken);
            var message = $"Switched to {label} scene '{scene}'.";
            RaiseStatus(message);
            return PublishResult.Success(message);
        }
        catch (Exception ex)
        {
            var message = $"{label} scene switch failed: {Friendly(ex)}";
            RaiseStatus(message);
            return PublishResult.Failure(message);
        }
    }

    private async Task<PublishResult> PublishCoreAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Settings.TransitionToSourceDelayMilliseconds is < 0 or > 60000)
            return PublishResult.Failure("Transition-to-source delay must be from 0 to 60000 milliseconds.");
        var discovery = DiscoverCore();
        var mapping = request.Settings.Mappings.AsDictionary();
        var mappingError = ValidateMappings(mapping, discovery.Inputs);
        if (mappingError is not null) return PublishResult.Failure(mappingError);

        if (request.Mode == PublishMode.Pop && request.Settings.AutoSwitch)
        {
            if (string.IsNullOrWhiteSpace(request.Settings.EntryScene) || string.IsNullOrWhiteSpace(request.Settings.ExitScene))
                return PublishResult.Failure("Entry and exit scenes are required when Auto-switch is enabled.");
            if (!discovery.Scenes.Contains(request.Settings.EntryScene, StringComparer.Ordinal) ||
                !discovery.Scenes.Contains(request.Settings.ExitScene, StringComparer.Ordinal))
                return PublishResult.Failure("The configured entry or exit scene no longer exists in OBS.");
            var showcaseSettings = _client.GetInputSettings(mapping[ObsOutput.ShowcaseVideo]).Settings;
            if (showcaseSettings.Value<bool?>("looping") == true || showcaseSettings.Value<bool?>("loop") == true)
                return PublishResult.Failure("Showcase Video looping must be disabled for Auto-switch.");
        }

        var currentPresentation = ResolveTextPresentation(request.Chart, _defaultFontSupports);
        var currentValues = Project(request.Chart, request.DifficultyImagePath)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        ApplyTextPresentation(currentValues, currentPresentation);
        var previousPresentation = request.PreviousChart is null
            ? null
            : ResolveTextPresentation(request.PreviousChart, _defaultFontSupports);
        Dictionary<ObsOutput, string>? previousValues = null;
        if (request.PreviousChart is not null)
        {
            previousValues = Project(request.PreviousChart,
                    ChartFormatter.GetDifficultyImagePath(request.Settings.DifficultyCustomPath, request.PreviousChart.DifficultyName))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            ApplyTextPresentation(previousValues, previousPresentation!);
        }
        var outputs = SelectOutputs(request.Mode, currentValues, previousValues);
        if (previousPresentation is not null)
        {
            foreach (var pair in currentPresentation.FontFaces)
            {
                if (!StringComparer.Ordinal.Equals(pair.Value, previousPresentation.FontFaces.GetValueOrDefault(pair.Key)))
                    outputs.Add(pair.Key);
            }
        }
        var snapshots = outputs.ToDictionary(output => output, output => (JObject)_client.GetInputSettings(mapping[output]).Settings.DeepClone());
        var originalScene = request.Mode == PublishMode.Pop && request.Settings.AutoSwitch ? _client.GetCurrentProgramScene() : null;
        var modified = new List<ObsOutput>();
        var sceneChanged = false;
        PlaybackCycle? priorCycle;
        string? priorPendingExit;
        lock (_cycleLock)
        {
            priorCycle = _cycle;
            priorPendingExit = _pendingExitScene;
        }

        try
        {
            if (request.Mode == PublishMode.Pop && request.Settings.AutoSwitch)
            {
                _client.SetCurrentProgramScene(request.Settings.EntryScene);
                sceneChanged = true;

                if (request.Settings.TransitionToSourceDelayMilliseconds > 0)
                {
                    RaiseStatus($"Entry transition started. Waiting {request.Settings.TransitionToSourceDelayMilliseconds} ms before updating chart sources.");
                    await Task.Delay(request.Settings.TransitionToSourceDelayMilliseconds, cancellationToken);
                }
            }

            foreach (var output in outputs.Where(x => x is not ObsOutput.ShowcaseVideo))
            {
                SetValue(mapping[output], output, currentValues[output], currentPresentation.FontFaces.GetValueOrDefault(output), snapshots[output]);
                modified.Add(output);
            }

            if (outputs.Contains(ObsOutput.ShowcaseVideo))
            {
                SetValue(mapping[ObsOutput.ShowcaseVideo], ObsOutput.ShowcaseVideo, currentValues[ObsOutput.ShowcaseVideo]);
                modified.Add(ObsOutput.ShowcaseVideo);
            }

            var restartShowcase = request.Mode is PublishMode.Pop or PublishMode.Retry || outputs.Contains(ObsOutput.ShowcaseVideo);
            PlaybackCycle? existing;
            lock (_cycleLock) existing = _cycle;
            if (restartShowcase)
            {
                if (request.Mode == PublishMode.Pop)
                {
                    if (request.Settings.AutoSwitch)
                        ArmCycle(mapping[ObsOutput.ShowcaseVideo], request.Settings.ExitScene);
                    else
                        DisarmCycle();
                }
                else if (existing is not null)
                    ArmCycle(mapping[ObsOutput.ShowcaseVideo], existing.ExitScene);

                _client.TriggerMediaInputAction(mapping[ObsOutput.ShowcaseVideo], RestartAction);
                StartPlaybackPoll();
            }

            return PublishResult.Success(request.Mode switch
            {
                PublishMode.Pop => "Chart displayed and removed from the queue.",
                PublishMode.QuickEdit => "Current display updated.",
                _ => "Current display synchronized again."
            });
        }
        catch (Exception ex)
        {
            lock (_cycleLock)
            {
                _cycle = priorCycle;
                _pendingExitScene = priorPendingExit;
            }
            var rollbackErrors = new List<string>();
            foreach (var output in modified.AsEnumerable().Reverse())
            {
                try { _client.SetInputSettings(mapping[output], snapshots[output], false); }
                catch (Exception rollbackEx) { rollbackErrors.Add($"{output}: {Friendly(rollbackEx)}"); }
            }
            if (sceneChanged && originalScene is not null)
            {
                try { _client.SetCurrentProgramScene(originalScene); }
                catch (Exception rollbackEx) { rollbackErrors.Add($"scene: {Friendly(rollbackEx)}"); }
            }
            var message = $"OBS publish failed: {Friendly(ex)}";
            if (rollbackErrors.Count > 0) message += $" Rollback also failed ({string.Join("; ", rollbackErrors)}).";
            return PublishResult.Failure(message, rollbackErrors.Count > 0);
        }
    }

    private ObsDiscovery DiscoverCore()
    {
        var inputs = _client.GetInputList().Select(ToInputInfo).Where(x => x is not null).Cast<ObsInputInfo>().ToList();
        var scenes = _client.ListScenes().Select(x => x.Name).ToList();
        return new(inputs, scenes);
    }

    internal static string? ValidateMappings(IReadOnlyDictionary<ObsOutput, string> mappings, IReadOnlyList<ObsInputInfo> inputs)
    {
        if (mappings.Values.Any(string.IsNullOrWhiteSpace)) return "All eight OBS source mappings are required.";
        var duplicate = mappings.Values.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) return $"OBS source '{duplicate.Key}' is assigned more than once.";
        var lookup = inputs.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var pair in mappings)
        {
            if (!lookup.TryGetValue(pair.Value, out var input)) return $"Mapped OBS source '{pair.Value}' does not exist.";
            var valid = pair.Key switch
            {
                ObsOutput.Credits => input.Category == ObsInputCategory.FreeTypeText,
                ObsOutput.Title or ObsOutput.Artist or ObsOutput.DifficultyName or ObsOutput.DifficultyNumber => input.Category is ObsInputCategory.Text or ObsInputCategory.FreeTypeText,
                ObsOutput.Jacket or ObsOutput.DifficultyImage => input.Category == ObsInputCategory.Image,
                ObsOutput.ShowcaseVideo => input.Category == ObsInputCategory.Media,
                _ => false
            };
            if (!valid) return $"OBS source '{input.Name}' has incompatible kind '{input.Kind}' for {pair.Key}.";
        }
        return null;
    }

    internal static IReadOnlyDictionary<ObsOutput, string> Project(ChartInfo chart, string difficultyImagePath) =>
        new Dictionary<ObsOutput, string>
        {
            [ObsOutput.Title] = chart.Title,
            [ObsOutput.Artist] = chart.Artist,
            [ObsOutput.Credits] = chart.CreditsText,
            [ObsOutput.DifficultyName] = chart.DifficultyName,
            [ObsOutput.DifficultyNumber] = ChartFormatter.FormatDifficulty(chart.DifficultyNumber),
            [ObsOutput.Jacket] = Path.GetFullPath(chart.JacketPath),
            [ObsOutput.DifficultyImage] = difficultyImagePath,
            [ObsOutput.ShowcaseVideo] = Path.GetFullPath(chart.ShowcaseVideoPath)
        };

    internal static IReadOnlyDictionary<ObsOutput, string> ProjectLive(ChartInfo chart, string jacketPath, string difficultyImagePath) =>
        new Dictionary<ObsOutput, string>
        {
            [ObsOutput.Title] = chart.Title,
            [ObsOutput.Artist] = chart.Artist,
            [ObsOutput.Credits] = chart.CreditsText,
            [ObsOutput.DifficultyName] = chart.DifficultyName,
            [ObsOutput.DifficultyNumber] = ChartFormatter.FormatDifficulty(chart.DifficultyNumber),
            [ObsOutput.Jacket] = jacketPath,
            [ObsOutput.DifficultyImage] = difficultyImagePath
        };

    internal static ObsTextPresentation ResolveTextPresentation(ChartInfo chart, Func<string, bool> defaultFontSupports)
    {
        var title = ResolveText(chart.Title, chart.FormattedTitle, defaultFontSupports);
        var artist = ResolveText(chart.Artist, chart.FormattedArtist, defaultFontSupports);
        var illustrator = ResolveText(chart.Illustrator, chart.FormattedIllustrator, defaultFontSupports);
        var charter = ResolveText(chart.Charter, chart.FormattedCharter, defaultFontSupports);
        return new ObsTextPresentation(
            new Dictionary<ObsOutput, string>
            {
                [ObsOutput.Title] = title.Value,
                [ObsOutput.Artist] = artist.Value,
                [ObsOutput.Credits] = ChartFormatter.FormatCredits(illustrator.Value, charter.Value)
            },
            new Dictionary<ObsOutput, string>
            {
                [ObsOutput.Title] = title.FontFace,
                [ObsOutput.Artist] = artist.FontFace,
                [ObsOutput.Credits] = illustrator.FontFace == FallbackTextFont || charter.FontFace == FallbackTextFont
                    ? FallbackTextFont
                    : DefaultTextFont
            });
    }

    private static ResolvedText ResolveText(string value, string formattedValue, Func<string, bool> defaultFontSupports)
    {
        if (defaultFontSupports(value)) return new(value, DefaultTextFont);
        if (!string.IsNullOrWhiteSpace(formattedValue))
            return new(formattedValue, defaultFontSupports(formattedValue) ? DefaultTextFont : FallbackTextFont);
        return new(value, FallbackTextFont);
    }

    private static void ApplyTextPresentation(IDictionary<ObsOutput, string> values, ObsTextPresentation presentation)
    {
        foreach (var pair in presentation.Values) values[pair.Key] = pair.Value;
    }

    internal static string? ValidateLiveMappings(IReadOnlyDictionary<ObsOutput, string> mappings, IReadOnlyList<ObsInputInfo> inputs)
    {
        if (mappings.Values.Any(string.IsNullOrWhiteSpace)) return "All seven live OBS source mappings are required.";
        var duplicate = mappings.Values.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) return $"OBS source '{duplicate.Key}' is assigned more than once.";
        var lookup = inputs.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var pair in mappings)
        {
            if (!lookup.TryGetValue(pair.Value, out var input)) return $"Mapped OBS source '{pair.Value}' does not exist.";
            var valid = pair.Key switch
            {
                ObsOutput.Credits => input.Category == ObsInputCategory.FreeTypeText,
                ObsOutput.Title or ObsOutput.Artist or ObsOutput.DifficultyName or ObsOutput.DifficultyNumber => input.Category is ObsInputCategory.Text or ObsInputCategory.FreeTypeText,
                ObsOutput.Jacket or ObsOutput.DifficultyImage => input.Category == ObsInputCategory.Image,
                _ => false
            };
            if (!valid) return $"OBS source '{input.Name}' has incompatible kind '{input.Kind}' for {pair.Key}.";
        }
        return null;
    }

    private static HashSet<ObsOutput> SelectOutputs(PublishMode mode, IReadOnlyDictionary<ObsOutput, string> current, IReadOnlyDictionary<ObsOutput, string>? previous)
    {
        if (mode is PublishMode.Pop or PublishMode.Retry || previous is null) return Enum.GetValues<ObsOutput>().ToHashSet();
        return current.Where(pair => !StringComparer.Ordinal.Equals(pair.Value, previous[pair.Key])).Select(pair => pair.Key).ToHashSet();
    }

    private void SetValue(string inputName, ObsOutput output, string value, string? fontFace = null, JObject? existingSettings = null)
    {
        var key = GetSettingKey(output);
        var settings = new JObject { [key] = value };
        if (!string.IsNullOrWhiteSpace(fontFace))
        {
            var font = existingSettings?["font"] is JObject currentFont
                ? (JObject)currentFont.DeepClone()
                : new JObject();
            font["face"] = fontFace;
            settings["font"] = font;
        }
        _client.SetInputSettings(inputName, settings, true);
    }

    internal static string GetSettingKey(ObsOutput output) => output switch
    {
        ObsOutput.Jacket or ObsOutput.DifficultyImage => "file",
        ObsOutput.ShowcaseVideo => "local_file",
        _ => "text"
    };

    private static ObsInputInfo? ToInputInfo(InputBasicInfo input)
    {
        var kind = string.IsNullOrWhiteSpace(input.UnversionedKind) ? input.InputKind : input.UnversionedKind;
        var category = kind switch
        {
            "text_ft2_source" => ObsInputCategory.FreeTypeText,
            "text_gdiplus" => ObsInputCategory.Text,
            "image_source" => ObsInputCategory.Image,
            "ffmpeg_source" => ObsInputCategory.Media,
            _ => (ObsInputCategory?)null
        };
        return category is null ? null : new(input.InputName, input.InputKind, kind, category.Value);
    }

    private void ArmCycle(string inputName, string exitScene)
    {
        lock (_cycleLock)
        {
            _pendingExitScene = null;
            _cycle = new(++_generation, inputName, exitScene, false);
        }
    }

    private void DisarmCycle()
    {
        lock (_cycleLock) _cycle = null;
    }

    private void StartPlaybackPoll()
    {
        PlaybackCycle? cycle;
        lock (_cycleLock) cycle = _cycle;
        if (cycle is null) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(100);
                PlaybackCycle? current;
                lock (_cycleLock) current = _cycle;
                if (current?.Generation != cycle.Generation || !_client.IsConnected) return;
                try
                {
                    var status = await WithClientAsync(() => _client.GetMediaInputStatus(cycle.InputName), CancellationToken.None);
                    if (status.State == MediaState.OBS_MEDIA_STATE_PLAYING)
                    {
                        MarkStarted(cycle.InputName);
                        return;
                    }
                }
                catch { return; }
            }
            RaiseStatus("Showcase playback did not enter the playing state; automatic exit is still armed.");
        });
    }

    private void OnPlaybackStarted(object? sender, MediaInputPlaybackStartedEventArgs e) => MarkStarted(e.InputName);

    private void MarkStarted(string? inputName)
    {
        if (string.IsNullOrWhiteSpace(inputName)) return;
        lock (_cycleLock)
        {
            if (_cycle?.InputName == inputName) _cycle = _cycle with { ObservedStarted = true };
        }
    }

    private void OnPlaybackEnded(object? sender, MediaInputPlaybackEndedEventArgs e)
    {
        PlaybackCycle? cycle;
        lock (_cycleLock) cycle = _cycle;
        if (cycle is null || !cycle.ObservedStarted || cycle.InputName != e.InputName) return;
        _ = CompleteCycleAsync(cycle);
    }

    private async Task CompleteCycleAsync(PlaybackCycle cycle)
    {
        lock (_cycleLock)
        {
            if (_cycle?.Generation != cycle.Generation) return;
            _cycle = null;
        }
        try
        {
            await WithClientAsync(() => { _client.SetCurrentProgramScene(cycle.ExitScene); return true; }, CancellationToken.None);
            RaiseStatus($"Showcase completed; switched to '{cycle.ExitScene}'.");
        }
        catch (Exception ex)
        {
            lock (_cycleLock) _pendingExitScene = cycle.ExitScene;
            RaiseStatus($"Exit scene switch failed: {Friendly(ex)} Use Retry Exit Scene.");
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        ConnectionChanged?.Invoke(this, true);
        RaiseStatus("Connected to OBS.");
        _ = RecoverCycleAsync();
    }

    private void OnDisconnected(object? sender, ObsDisconnectionInfo e)
    {
        ConnectionChanged?.Invoke(this, false);
        RaiseStatus($"OBS disconnected: {e.DisconnectReason ?? e.ObsCloseCode.ToString()}.");
    }

    private async Task RecoverCycleAsync()
    {
        PlaybackCycle? cycle;
        lock (_cycleLock) cycle = _cycle;
        if (cycle is null) return;
        try
        {
            var status = await WithClientAsync(() => _client.GetMediaInputStatus(cycle.InputName), CancellationToken.None);
            if (status.State == MediaState.OBS_MEDIA_STATE_ENDED)
            {
                lock (_cycleLock) _cycle = cycle with { ObservedStarted = true };
                await CompleteCycleAsync(cycle with { ObservedStarted = true });
            }
            else if (status.State == MediaState.OBS_MEDIA_STATE_PLAYING) MarkStarted(cycle.InputName);
        }
        catch (Exception ex) { RaiseStatus($"Could not recover showcase status: {Friendly(ex)}"); }
    }

    private async Task<T> WithClientAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected) throw new InvalidOperationException("OBS is not connected.");
        await _requestGate.WaitAsync(cancellationToken);
        try { return await Task.Run(action, cancellationToken); }
        finally { _requestGate.Release(); }
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);
    private static string Friendly(Exception ex) => ex is AggregateException aggregate ? aggregate.GetBaseException().Message : ex.Message;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync();
        _client.Connected -= OnConnected;
        _client.Disconnected -= OnDisconnected;
        _client.MediaInputPlaybackStarted -= OnPlaybackStarted;
        _client.MediaInputPlaybackEnded -= OnPlaybackEnded;
        _requestGate.Dispose();
    }

    private sealed record PlaybackCycle(long Generation, string InputName, string ExitScene, bool ObservedStarted);
    private sealed record ResolvedText(string Value, string FontFace);
}

internal sealed record ObsTextPresentation(
    IReadOnlyDictionary<ObsOutput, string> Values,
    IReadOnlyDictionary<ObsOutput, string> FontFaces);
