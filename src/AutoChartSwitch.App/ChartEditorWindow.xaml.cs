using System.Globalization;
using System.IO;
using System.Windows;
using AutoChartSwitch.Core;
using Microsoft.Win32;

namespace AutoChartSwitch.App;

public partial class ChartEditorWindow : Window
{
    private readonly Guid _id;
    private readonly string _difficultyCustomPath;
    private readonly IChartValidator _validator;
    public ChartInfo? Result { get; private set; }

    private ChartEditorWindow(ChartInfo? chart, string title, string difficultyCustomPath, IChartValidator validator)
    {
        InitializeComponent();
        Title = title;
        Owner = Application.Current.MainWindow;
        _id = chart?.Id ?? Guid.NewGuid();
        _difficultyCustomPath = difficultyCustomPath;
        _validator = validator;
        if (chart is null) return;
        TitleBox.Text = chart.Title;
        ArtistBox.Text = chart.Artist;
        IllustratorBox.Text = chart.Illustrator;
        CharterBox.Text = chart.Charter;
        DifficultyNameBox.Text = chart.DifficultyName;
        DifficultyNumberBox.Text = chart.DifficultyNumber.ToString("0.0", CultureInfo.InvariantCulture);
        JacketBox.Text = chart.JacketPath;
        ChipBox.Text = FormatStat(chart.TechStats?.Chip ?? 0m);
        TechBox.Text = FormatStat(chart.TechStats?.Tech ?? 0m);
        StreamBox.Text = FormatStat(chart.TechStats?.Stream ?? 0m);
        ChordBox.Text = FormatStat(chart.TechStats?.Chord ?? 0m);
        BurstBox.Text = FormatStat(chart.TechStats?.Burst ?? 0m);
        GimmickBox.Text = FormatStat(chart.TechStats?.Gimmick ?? 0m);
        ShowcaseBox.Text = chart.ShowcaseVideoPath;
    }

    public static ChartInfo? Edit(ChartInfo? chart, string title, string difficultyCustomPath, IChartValidator validator)
    {
        var dialog = new ChartEditorWindow(chart, title, difficultyCustomPath, validator);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(DifficultyNumberBox.Text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var difficulty))
        {
            MessageText.Text = "Difficulty Number must use a decimal point and contain a number from 0.0 to 99.9.";
            return;
        }

        if (!TryGetTechStats(out var techStats)) return;

        var chart = new ChartInfo
        {
            Id = _id,
            Title = TitleBox.Text.Trim(),
            Artist = ArtistBox.Text.Trim(),
            Illustrator = IllustratorBox.Text.Trim(),
            Charter = CharterBox.Text.Trim(),
            DifficultyName = DifficultyNameBox.Text.Trim(),
            DifficultyNumber = difficulty,
            JacketPath = Normalize(JacketBox.Text),
            TechStats = techStats,
            ShowcaseVideoPath = Normalize(ShowcaseBox.Text)
        };
        var validation = _validator.Validate(chart, _difficultyCustomPath, false);
        if (!validation.CanSave)
        {
            MessageText.Text = validation.Summary;
            return;
        }
        Result = chart;
        DialogResult = true;
    }

    private static string Normalize(string value)
    {
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value);
    }

    private bool TryGetTechStats(out ChartTechStats stats)
    {
        stats = new();
        if (!TryParseStat(ChipBox, "CHIP", out var chip) || !TryParseStat(TechBox, "TECH", out var tech) ||
            !TryParseStat(StreamBox, "STREAM", out var stream) || !TryParseStat(ChordBox, "CHORD", out var chord) ||
            !TryParseStat(BurstBox, "BURST", out var burst) || !TryParseStat(GimmickBox, "GIMMICK", out var gimmick)) return false;
        stats = new() { Chip = chip, Tech = tech, Stream = stream, Chord = chord, Burst = burst, Gimmick = gimmick };
        return true;
    }

    private bool TryParseStat(System.Windows.Controls.TextBox textBox, string label, out decimal value)
    {
        var text = textBox.Text.Trim();
        if (decimal.TryParse(string.IsNullOrEmpty(text) ? "0" : text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && value >= 0m) return true;
        MessageText.Text = $"{label} must be a non-negative number using a decimal point.";
        return false;
    }

    private static string FormatStat(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void BrowseJacket_Click(object sender, RoutedEventArgs e) => Browse(JacketBox, "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*");
    private void BrowseShowcase_Click(object sender, RoutedEventArgs e) => Browse(ShowcaseBox, "Video files|*.mp4;*.mov;*.webm;*.mkv;*.avi|All files|*.*");

    private static void Browse(System.Windows.Controls.TextBox target, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog() == true) target.Text = dialog.FileName;
    }
}
