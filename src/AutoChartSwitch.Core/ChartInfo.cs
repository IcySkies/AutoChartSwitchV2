using System.Text.Json.Serialization;

namespace AutoChartSwitch.Core;

public sealed record ChartInfo
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string GameChartId { get; init; } = "";
    public string RawDifficultyName { get; init; } = "";
    public string Title { get; init; } = "";
    public string FormattedTitle { get; init; } = "";
    public string Artist { get; init; } = "";
    public string FormattedArtist { get; init; } = "";
    public string Illustrator { get; init; } = "";
    public string FormattedIllustrator { get; init; } = "";
    public string Charter { get; init; } = "";
    public string FormattedCharter { get; init; } = "";
    public string DifficultyName { get; init; } = "";
    public decimal DifficultyNumber { get; init; }
    public string JacketPath { get; init; } = "";
    public ChartTechStats TechStats { get; init; } = new();
    public string ShowcaseVideoPath { get; init; } = "";

    [JsonIgnore]
    public string CreditsText => ChartFormatter.FormatCredits(Illustrator, Charter);
}

public sealed record ChartTechStats
{
    public decimal Chip { get; init; }
    public decimal Tech { get; init; }
    public decimal Stream { get; init; }
    public decimal Chord { get; init; }
    public decimal Burst { get; init; }
    public decimal Gimmick { get; init; }

    [JsonIgnore]
    public IReadOnlyList<decimal> Values => [Chip, Tech, Stream, Chord, Burst, Gimmick];
}

public static class ChartFormatter
{
    public static string NormalizeDifficulty(string? value) => DifficultyCodes.Normalize(value);

    public static string FormatDifficulty(decimal value)
    {
        if (value == 0m) return "";
        if (value < 10m && decimal.Truncate(value) == value)
            return value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string FormatCredits(string illustrator, string charter) =>
        $"Illust: {illustrator}\nChart: {charter}";

    public static string GetDifficultyImagePath(string customPath, string difficultyName) =>
        Path.GetFullPath(Path.Combine(customPath, $"{difficultyName}.png"));
}

public static class DifficultyCodes
{
    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "OPENING" => "OPN",
        "OPN" => "OPN",
        "MIDDLE" => "MID",
        "MID" => "MID",
        "FINALE" => "FIN",
        "FIN" => "FIN",
        "ENCORE" => "ENC",
        "ENC" => "ENC",
        "BACKSTAGE" => "BKS",
        "BKS" => "BKS",
        "SHATTER" => "SHATTER",
        _ => "SHATTER"
    };
}
