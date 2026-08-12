using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoChartSwitch.Core;

public enum ImportMode { Append, Replace }

public sealed record ChartImportPreview(IReadOnlyList<ChartInfo> Charts, IReadOnlyList<string> Warnings);

public interface IChartInterchange
{
    Task<ChartImportPreview> PreviewImportAsync(string path, CancellationToken cancellationToken = default);
    Task ExportAsync(string path, IEnumerable<ChartInfo> charts, CancellationToken cancellationToken = default);
}

public sealed class ChartInterchange : IChartInterchange
{
    private readonly IChartValidator _validator;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict
    };

    public ChartInterchange(IChartValidator validator) => _validator = validator;

    public async Task<ChartImportPreview> PreviewImportAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ChartListDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The JSON document is empty.");
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported schemaVersion {document.SchemaVersion}.");
        if (document.Charts is null) throw new InvalidDataException("The charts array is required.");

        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var charts = document.Charts.Select(x => Canonicalize(x, root)).ToList();
        var structuralErrors = charts.SelectMany((chart, index) =>
            _validator.Validate(chart, root, false).Issues
                .Where(issue => issue.BlocksDisplay)
                .Select(issue => $"Chart {index + 1}, {issue.Field}: {issue.Message}"))
            .ToList();
        if (structuralErrors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, structuralErrors));

        var warnings = charts.SelectMany((chart, index) =>
            new[] { chart.JacketPath, chart.ShowcaseVideoPath }
                .Where(file => !File.Exists(file))
                .Select(file => $"Chart {index + 1}: file not found: {file}"))
            .ToList();
        return new(charts, warnings);
    }

    public async Task ExportAsync(string path, IEnumerable<ChartInfo> charts, CancellationToken cancellationToken = default)
    {
        var document = new ChartListDocument { SchemaVersion = 1, Charts = charts.Select(x => Canonicalize(x, Environment.CurrentDirectory)).ToList() };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }

    private static ChartInfo Canonicalize(ChartInfo chart, string basePath) => chart with
    {
        Id = chart.Id == Guid.Empty ? Guid.NewGuid() : chart.Id,
        Title = chart.Title.Trim(),
        Artist = chart.Artist.Trim(),
        Illustrator = chart.Illustrator.Trim(),
        Charter = chart.Charter.Trim(),
        DifficultyName = chart.DifficultyName.Trim(),
        JacketPath = Resolve(chart.JacketPath, basePath),
        TechStats = chart.TechStats ?? new(),
        ShowcaseVideoPath = Resolve(chart.ShowcaseVideoPath, basePath)
    };

    private static string Resolve(string path, string root) =>
        string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

    private sealed class ChartListDocument
    {
        public int SchemaVersion { get; set; }
        public List<ChartInfo>? Charts { get; set; }
    }
}
