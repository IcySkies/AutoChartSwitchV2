using AutoChartSwitch.Core;

namespace AutoChartSwitch.Tests;

public sealed class WorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoChartSwitchWorkflowTests", Guid.NewGuid().ToString("N"));

    public WorkflowTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task SuccessfulPopCommitsCurrentAndRemovesFront()
    {
        var chart = CreateDisplayableChart();
        var queue = new ChartQueue([chart]);
        var publisher = new FakePublisher { NextResult = PublishResult.Success() };
        var workflow = new ChartWorkflow(queue, new ChartValidator(), publisher);

        var result = await workflow.PopFrontAsync(Settings(delayMilliseconds: 750));

        Assert.True(result.Succeeded);
        Assert.Empty(queue.Items);
        Assert.Equal(chart, workflow.CurrentDisplay);
        Assert.Equal(PublishMode.Pop, publisher.LastRequest!.Mode);
        Assert.Equal(750, publisher.LastRequest.Settings.TransitionToSourceDelayMilliseconds);
    }

    [Fact]
    public async Task FailedPopLeavesQueueAndCurrentUnchanged()
    {
        var chart = CreateDisplayableChart();
        var queue = new ChartQueue([chart]);
        var publisher = new FakePublisher { NextResult = PublishResult.Failure("partial failure") };
        var workflow = new ChartWorkflow(queue, new ChartValidator(), publisher);

        var result = await workflow.PopFrontAsync(Settings());

        Assert.False(result.Succeeded);
        Assert.Single(queue.Items);
        Assert.Null(workflow.CurrentDisplay);
    }

    [Fact]
    public async Task FailedQuickEditRetainsCommittedDisplay()
    {
        var chart = CreateDisplayableChart();
        var queue = new ChartQueue([chart]);
        var publisher = new FakePublisher { NextResult = PublishResult.Success() };
        var workflow = new ChartWorkflow(queue, new ChartValidator(), publisher);
        await workflow.PopFrontAsync(Settings());
        publisher.NextResult = PublishResult.Failure("edit failed");

        var result = await workflow.QuickEditAsync(chart with { Title = "Changed" }, Settings());

        Assert.False(result.Succeeded);
        Assert.Equal("Song", workflow.CurrentDisplay!.Title);
    }

    private ChartInfo CreateDisplayableChart()
    {
        var jacket = CreateFile("jacket.png");
        var showcase = CreateFile("showcase.mp4");
        CreateFile("Master.png");
        return FormatterAndValidationTests.ValidChart() with
        {
            JacketPath = jacket,
            ShowcaseVideoPath = showcase
        };
    }

    private AutoChartSettings Settings(int delayMilliseconds = 0) => new()
    {
        DifficultyCustomPath = _root,
        TransitionToSourceDelayMilliseconds = delayMilliseconds
    };

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "test");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FakePublisher : IChartPublisher
    {
        public bool IsConnected => true;
        public PublishResult NextResult { get; set; } = PublishResult.Success();
        public PublishRequest? LastRequest { get; private set; }
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<bool>? ConnectionChanged { add { } remove { } }
        public Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ObsDiscovery([], []));
        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
        public Task<PublishResult> RetryExitSceneAsync(CancellationToken cancellationToken = default) => Task.FromResult(NextResult);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
