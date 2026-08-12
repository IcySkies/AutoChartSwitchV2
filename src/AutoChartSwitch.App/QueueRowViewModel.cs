using AutoChartSwitch.Core;

namespace AutoChartSwitch.App;

public sealed record QueueRowViewModel(ChartInfo Chart, string Difficulty, string Validation)
{
    public string Title => Chart.Title;
    public string Artist => Chart.Artist;
    public string DifficultyName => Chart.DifficultyName;
    public bool HasWarning => !string.IsNullOrWhiteSpace(Validation);
}
