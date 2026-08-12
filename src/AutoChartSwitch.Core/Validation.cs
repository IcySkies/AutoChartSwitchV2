namespace AutoChartSwitch.Core;

public sealed record ValidationIssue(string Field, string Message, bool BlocksDisplay);

public sealed record ChartValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool CanSave => Issues.All(x =>
        !x.BlocksDisplay || x.Message is "File does not exist." or "Difficulty image folder is required.");
    public bool CanDisplay => Issues.All(x => !x.BlocksDisplay);
    public string Summary => string.Join(Environment.NewLine, Issues.Select(x => $"{x.Field}: {x.Message}"));
}

public interface IChartValidator
{
    ChartValidationResult Validate(ChartInfo chart, string difficultyCustomPath, bool checkFiles);
}

public sealed class ChartValidator : IChartValidator
{
    public ChartValidationResult Validate(ChartInfo chart, string difficultyCustomPath, bool checkFiles)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var issues = new List<ValidationIssue>();
        Require(chart.Title, nameof(chart.Title), issues);
        Require(chart.Artist, nameof(chart.Artist), issues);
        Require(chart.Illustrator, nameof(chart.Illustrator), issues);
        Require(chart.Charter, nameof(chart.Charter), issues);
        Require(chart.DifficultyName, nameof(chart.DifficultyName), issues);
        Require(chart.JacketPath, nameof(chart.JacketPath), issues);
        Require(chart.ShowcaseVideoPath, nameof(chart.ShowcaseVideoPath), issues);

        if (chart.DifficultyNumber is < 0m or > 99.9m || decimal.Round(chart.DifficultyNumber, 1) != chart.DifficultyNumber)
            issues.Add(new(nameof(chart.DifficultyNumber), "Enter a value from 0.0 to 99.9 with at most one decimal place.", true));

        if (chart.TechStats is null)
            issues.Add(new(nameof(chart.TechStats), "Tech stats are required.", true));
        else
        {
            var statNames = new[] { nameof(ChartTechStats.Chip), nameof(ChartTechStats.Tech), nameof(ChartTechStats.Stream), nameof(ChartTechStats.Chord), nameof(ChartTechStats.Burst), nameof(ChartTechStats.Gimmick) };
            for (var i = 0; i < chart.TechStats.Values.Count; i++)
                if (chart.TechStats.Values[i] < 0m)
                    issues.Add(new($"{nameof(chart.TechStats)}.{statNames[i]}", "Enter a non-negative value.", true));
        }

        if (string.IsNullOrWhiteSpace(difficultyCustomPath))
            issues.Add(new(nameof(difficultyCustomPath), "Difficulty image folder is required.", true));
        else if (chart.DifficultyName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            issues.Add(new(nameof(chart.DifficultyName), "Contains characters that cannot be used in an image filename.", true));

        if (checkFiles)
        {
            CheckFile(chart.JacketPath, nameof(chart.JacketPath), issues);
            CheckFile(chart.ShowcaseVideoPath, nameof(chart.ShowcaseVideoPath), issues);
            if (!string.IsNullOrWhiteSpace(difficultyCustomPath) &&
                chart.DifficultyName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                CheckFile(ChartFormatter.GetDifficultyImagePath(difficultyCustomPath, chart.DifficultyName), "DifficultyImagePath", issues);
        }

        return new(issues);
    }

    private static void Require(string value, string field, ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) issues.Add(new(field, "Required.", true));
    }

    private static void CheckFile(string path, string field, ICollection<ValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
            issues.Add(new(field, "File does not exist.", true));
    }
}
