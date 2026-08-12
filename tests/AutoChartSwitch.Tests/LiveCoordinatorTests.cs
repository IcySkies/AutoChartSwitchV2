using AutoChartSwitch.Core;

namespace AutoChartSwitch.Tests;

public sealed class LiveCoordinatorTests
{
    [Fact]
    public async Task LoadingAndGameplayLifecyclePublishAndSwitchScenes()
    {
        var source = new FakeSource();
        var publisher = new FakeLivePublisher();
        await using var coordinator = new LiveChartCoordinator(source, publisher);
        var settings = new AutoChartSettings { AutoSwitch = true };
        coordinator.SetSettings(settings);

        await coordinator.HandleAsync(new GameEventEnvelope
        {
            Sequence = 1,
            Kind = GameEventKind.Selection,
            Chart = new GameChartSnapshot { ChartId = "song", RawDifficultyName = "OPENING", Title = "Song" }
        }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 2, Kind = GameEventKind.ChartLoadingStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 3, Kind = GameEventKind.ChartStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 4, Kind = GameEventKind.ChartExitTransitionStarted }, settings);

        Assert.Equal(3, publisher.PublishedCount);
        Assert.Equal(["entry", "exit"], publisher.SceneSwitches);
        Assert.Equal("OPN", coordinator.CurrentChart!.DifficultyName);
    }

    [Fact]
    public async Task ChartStartAloneDoesNotSwitchEntryScene()
    {
        var source = new FakeSource();
        var publisher = new FakeLivePublisher();
        await using var coordinator = new LiveChartCoordinator(source, publisher);
        var settings = new AutoChartSettings { AutoSwitch = true };

        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 1, Kind = GameEventKind.ChartStarted }, settings);

        Assert.Empty(publisher.SceneSwitches);
    }

    [Fact]
    public async Task LobbySelectionPublishesChosenChartImmediately()
    {
        var source = new FakeSource();
        var publisher = new FakeLivePublisher();
        await using var coordinator = new LiveChartCoordinator(source, publisher);
        var settings = new AutoChartSettings();

        await coordinator.HandleAsync(new GameEventEnvelope
        {
            Sequence = 1,
            Kind = GameEventKind.LobbySelection,
            Chart = new GameChartSnapshot { ChartId = "lobby-song", RawDifficultyName = "FINALE", Title = "Lobby Song" }
        }, settings);

        Assert.Equal(1, publisher.PublishedCount);
        Assert.Equal("lobby-song", coordinator.CurrentChart!.GameChartId);
        Assert.Equal(GameEventKind.LobbySelection, coordinator.LastEventKind);
    }

    [Fact]
    public async Task RepeatedLifecycleEventsDoNotSwitchAgain()
    {
        var source = new FakeSource();
        var publisher = new FakeLivePublisher();
        await using var coordinator = new LiveChartCoordinator(source, publisher);
        var settings = new AutoChartSettings { AutoSwitch = true };

        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 1, Kind = GameEventKind.ChartLoadingStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 2, Kind = GameEventKind.ChartLoadingStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 3, Kind = GameEventKind.ChartStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 4, Kind = GameEventKind.ChartStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 5, Kind = GameEventKind.ChartExitTransitionStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 6, Kind = GameEventKind.ChartExitTransitionStarted }, settings);

        Assert.Equal(["entry", "exit"], publisher.SceneSwitches);
    }

    [Fact]
    public async Task GameplayEndWithoutTransitionResetsSessionWithoutSwitchingExit()
    {
        var source = new FakeSource();
        var publisher = new FakeLivePublisher();
        await using var coordinator = new LiveChartCoordinator(source, publisher);
        var settings = new AutoChartSettings { AutoSwitch = true };

        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 1, Kind = GameEventKind.ChartLoadingStarted }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 2, Kind = GameEventKind.GameplayEnded }, settings);
        await coordinator.HandleAsync(new GameEventEnvelope { Sequence = 3, Kind = GameEventKind.ChartLoadingStarted }, settings);

        Assert.Equal(["entry", "entry"], publisher.SceneSwitches);
    }

    private sealed class FakeSource : IGameEventSource
    {
        public bool IsListening => true;
        public int Port => 0;
        public event EventHandler<GameEventEnvelope>? EventReceived { add { } remove { } }
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLivePublisher : ILiveChartPublisher
    {
        public bool IsConnected => true;
        public int PublishedCount { get; private set; }
        public List<string> SceneSwitches { get; } = [];
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<bool>? ConnectionChanged { add { } remove { } }
        public Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ObsDiscovery([], []));
        public Task<PublishResult> PublishSelectionAsync(ChartInfo chart, AutoChartSettings settings, CancellationToken cancellationToken = default)
        {
            PublishedCount++;
            return Task.FromResult(PublishResult.Success());
        }
        public Task<PublishResult> SwitchToEntrySceneAsync(AutoChartSettings settings, CancellationToken cancellationToken = default)
        {
            SceneSwitches.Add("entry");
            return Task.FromResult(PublishResult.Success());
        }
        public Task<PublishResult> SwitchToExitSceneAsync(AutoChartSettings settings, CancellationToken cancellationToken = default)
        {
            SceneSwitches.Add("exit");
            return Task.FromResult(PublishResult.Success());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
