namespace AutoChartSwitch.Core;

public static class ChartAssetResolver
{
    public static string ResolveJacket(ChartInfo chart, string cachePath)
    {
        if (!string.IsNullOrWhiteSpace(chart.JacketPath) && File.Exists(chart.JacketPath))
        {
            var source = Path.GetFullPath(chart.JacketPath);
            if (string.IsNullOrWhiteSpace(cachePath) || string.IsNullOrWhiteSpace(chart.GameChartId)) return source;

            try
            {
                Directory.CreateDirectory(cachePath);
                var cached = Path.GetFullPath(Path.Combine(cachePath,
                    $"{SafeFileName(chart.GameChartId)}-{SafeFileName(chart.DifficultyName)}.png"));
                if (!StringComparer.OrdinalIgnoreCase.Equals(source, cached)) File.Copy(source, cached, true);
                return cached;
            }
            catch (IOException) { return source; }
            catch (UnauthorizedAccessException) { return source; }
            catch (ArgumentException) { return source; }
        }
        if (string.IsNullOrWhiteSpace(cachePath)) return "";

        if (!string.IsNullOrWhiteSpace(chart.GameChartId))
        {
            var chartId = SafeFileName(chart.GameChartId);
            var difficulty = SafeFileName(chart.DifficultyName);
            var candidates = new[]
            {
                Path.Combine(cachePath, $"{chartId}-{difficulty}.png"),
                Path.Combine(cachePath, $"song_{chartId}-{difficulty}.png"),
                Path.Combine(cachePath, $"{chartId}.png"),
                Path.Combine(cachePath, $"song_{chartId}.png")
            };
            if (candidates.FirstOrDefault(File.Exists) is { } match) return Path.GetFullPath(match);
        }

        var fallback = Path.Combine(cachePath, "Memories_Sacrifice_jacket.png");
        return File.Exists(fallback) ? Path.GetFullPath(fallback) : "";
    }

    public static string ResolveDifficultyImage(string directory, string difficultyCode)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "";
        var path = Path.Combine(directory, $"{SafeFileName(difficultyCode)}.png");
        return File.Exists(path) ? Path.GetFullPath(path) : "";
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Trim().Select(character => invalid.Contains(character) ? '_' : character));
    }
}
