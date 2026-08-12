using System.Text.Json;
using AutoChartSwitch.App;
using AutoChartSwitch.Core;

namespace AutoChartSwitch.Tests;

public sealed class QueueAndInterchangeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoChartSwitchTests", Guid.NewGuid().ToString("N"));

    public QueueAndInterchangeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void QueueOperationsPreserveExpectedOrderAndIds()
    {
        var first = FormatterAndValidationTests.ValidChart();
        var second = FormatterAndValidationTests.ValidChart();
        var queue = new ChartQueue();

        queue.InsertBack(first);
        queue.InsertFront(second);
        Assert.Equal([second.Id, first.Id], queue.Items.Select(x => x.Id));

        Assert.True(queue.Move(first.Id, 0));
        Assert.Equal(first.Id, queue.PeekFront()!.Id);
        Assert.True(queue.Replace(first.Id, first with { Title = "Edited", Id = Guid.NewGuid() }));
        Assert.Equal(first.Id, queue.Items[0].Id);
        Assert.Equal("Edited", queue.Items[0].Title);
        Assert.True(queue.Delete(second.Id));
        Assert.Single(queue.Items);
    }

    [Fact]
    public async Task JsonRoundTripPreservesOrderAndCanonicalPaths()
    {
        var interchange = new ChartInterchange(new ChartValidator());
        var first = FormatterAndValidationTests.ValidChart();
        var second = FormatterAndValidationTests.ValidChart() with { Title = "Second" };
        var path = Path.Combine(_root, "charts.json");

        await interchange.ExportAsync(path, [first, second]);
        var preview = await interchange.PreviewImportAsync(path);

        Assert.Equal(["Song", "Second"], preview.Charts.Select(x => x.Title));
        Assert.All(preview.Charts, x => Assert.True(Path.IsPathFullyQualified(x.JacketPath)));
        Assert.Equal(30m, preview.Charts[0].TechStats.Chip);
        Assert.DoesNotContain("\"values\"", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RelativeImportedPathsResolveAgainstJsonDirectory()
    {
        var path = Path.Combine(_root, "relative.json");
        var chart = FormatterAndValidationTests.ValidChart() with
        {
            JacketPath = "assets/jacket.png",
            ShowcaseVideoPath = "assets/showcase.mp4"
        };
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, charts = new[] { chart } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(path, json);

        var preview = await new ChartInterchange(new ChartValidator()).PreviewImportAsync(path);

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "assets/jacket.png")), preview.Charts[0].JacketPath);
    }

    [Fact]
    public async Task UnsupportedSchemaIsRejectedAtomically()
    {
        var path = Path.Combine(_root, "future.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":2,\"charts\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ChartInterchange(new ChartValidator()).PreviewImportAsync(path));
    }

    [Fact]
    public void AppDataRootUsesSuiteFolder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SVC-AS", "AutoChartSwitchV2");
        Assert.Equal(expected, AppPersistence.RootPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
