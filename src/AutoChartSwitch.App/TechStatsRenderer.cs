using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoChartSwitch.Core;

namespace AutoChartSwitch.App;

public sealed class TechStatsRenderer : FrameworkElement
{
    private const double AnimationStepSeconds = 1d / 60d;
    private static readonly BitmapImage SixRowLabels = LoadImage("sp_techstats2025_0_padded.png");
    private static readonly BitmapImage FiveRowLabels = LoadImage("sp_techstats2025_1_padded.png");
    private static readonly BitmapImage[] Digits = Enumerable.Range(0, 10).Select(index => LoadImage($"sp_font_techstat_{index}.png")).ToArray();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _targetValues = new double[6];
    private readonly double[] _animatedValues = new double[6];
    private readonly decimal[] _displayValues = new decimal[6];
    private double _previousSeconds;
    private double _stepAccumulator;
    private bool _hasChart;

    public TechStatsRenderer()
    {
        Width = TechStatsLayout.Width;
        Height = TechStatsLayout.Height;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void SetChart(ChartInfo? chart)
    {
        _hasChart = chart is not null;
        if (chart is null)
        {
            Array.Clear(_targetValues, 0, _targetValues.Length);
            Array.Clear(_animatedValues, 0, _animatedValues.Length);
            Array.Clear(_displayValues, 0, _displayValues.Length);
            _stepAccumulator = 0;
            _previousSeconds = _clock.Elapsed.TotalSeconds;
            InvalidateVisual();
            return;
        }

        Array.Clear(_targetValues, 0, _targetValues.Length);
        Array.Clear(_animatedValues, 0, _animatedValues.Length);
        Array.Clear(_displayValues, 0, _displayValues.Length);
        var values = (chart.TechStats ?? new ChartTechStats()).Values;
        TechStatsLayout.CopyValues(values, _targetValues, _displayValues);
        _stepAccumulator = 0;
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!_hasChart || ActualWidth <= 0 || ActualHeight <= 0) return;

        var scale = Math.Min(ActualWidth / TechStatsLayout.Width, ActualHeight / TechStatsLayout.Height);
        var offsetX = (ActualWidth - (TechStatsLayout.Width * scale)) / 2;
        var offsetY = (ActualHeight - (TechStatsLayout.Height * scale)) / 2;
        drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        var hasGimmick = _targetValues[5] > 0;
        var rowCount = hasGimmick ? 6 : 5;
        drawingContext.DrawImage(hasGimmick ? SixRowLabels : FiveRowLabels,
            new Rect(0, 0, TechStatsLayout.LabelWidth, TechStatsLayout.LabelHeight));
        var rainbowBrush = CreateFrozenBrush(TechStatsPalette.GetRainbowColor(_clock.Elapsed.TotalMilliseconds));
        for (var i = 0; i < rowCount; i++)
        {
            var rowTop = TechStatsLayout.GetRowTop(i, hasGimmick);
            DrawBar(drawingContext, _animatedValues[i], rowTop, rainbowBrush);
            DrawNumber(drawingContext, _displayValues[i], rowTop, rainbowBrush);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => CompositionTarget.Rendering -= OnRendering;

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_hasChart) return;
        var now = _clock.Elapsed.TotalSeconds;
        _stepAccumulator = Math.Min(_stepAccumulator + now - _previousSeconds, 0.1d);
        _previousSeconds = now;
        while (_stepAccumulator >= AnimationStepSeconds)
        {
            for (var i = 0; i < _animatedValues.Length; i++)
                _animatedValues[i] += (_targetValues[i] - _animatedValues[i]) * 0.2d;
            _stepAccumulator -= AnimationStepSeconds;
        }
        InvalidateVisual();
    }

    private static void DrawBar(DrawingContext drawingContext, double value, double top, Brush rainbowBrush)
    {
        value = Math.Max(0, value);
        if (value > 200)
        {
            drawingContext.DrawRectangle(rainbowBrush, null,
                new Rect(TechStatsLayout.BarLeft, top, TechStatsLayout.FullBarWidth, TechStatsLayout.BarHeight));
            drawingContext.DrawRectangle(Brushes.White, null,
                new Rect(TechStatsLayout.BarLeft, top, TechStatsLayout.GetBarWidth(value - 200), TechStatsLayout.BarHeight));
            return;
        }

        drawingContext.DrawRectangle(CreateFrozenBrush(TechStatsPalette.GetBarColor(value)), null,
            new Rect(TechStatsLayout.BarLeft, top, TechStatsLayout.GetBarWidth(value), TechStatsLayout.BarHeight));
    }

    private static void DrawNumber(DrawingContext drawingContext, decimal value, double top, Brush rainbowBrush)
    {
        var roundedValue = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
        var text = TechStatsLayout.FormatNumber(roundedValue);
        var x = TechStatsLayout.GetNumberLeft(text.Length);
        foreach (var character in text)
        {
            var bounds = new Rect(x, top, TechStatsLayout.DigitWidth, TechStatsLayout.DigitHeight);
            if (character == '-')
            {
                drawingContext.DrawRectangle(TechStatsPalette.UsesRainbowNumber(roundedValue) ? rainbowBrush : Brushes.White,
                    null, TechStatsLayout.GetMinusBounds(x, top));
            }
            else if (TechStatsPalette.UsesRainbowNumber(roundedValue))
            {
                drawingContext.PushOpacityMask(new ImageBrush(Digits[character - '0']) { Stretch = Stretch.Fill });
                drawingContext.DrawRectangle(rainbowBrush, null, bounds);
                drawingContext.Pop();
            }
            else drawingContext.DrawImage(Digits[character - '0'], bounds);
            x += TechStatsLayout.DigitAdvance;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static BitmapImage LoadImage(string fileName)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/TechStats/Original/{fileName}", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

}

internal static class TechStatsLayout
{
    public const double Width = 508;
    public const double Height = 200;
    public const double LabelWidth = 180;
    public const double LabelHeight = 200;
    public const double BarLeft = 189;
    public const double BarHeight = 25;
    public const double FullBarWidth = 240;
    public const double NumberRight = 508;
    public const double DigitWidth = 20;
    public const double DigitHeight = 25;
    public const double DigitAdvance = 25;

    public static double GetRowTop(int row, bool hasGimmick) => row * (hasGimmick ? 35 : 40);

    public static double GetBarWidth(double value)
    {
        var fraction = Math.Clamp(value, 0, 200) / 200d;
        return Math.Ceiling((FullBarWidth / 5d) * fraction) * 5d;
    }

    public static double GetNumberLeft(int digitCount) =>
        NumberRight - ((digitCount * DigitWidth) + (Math.Max(0, digitCount - 1) * (DigitAdvance - DigitWidth)));

    public static string FormatNumber(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);

    public static Rect GetMinusBounds(double left, double top) => new(left + 8, top + 11, 5, 3);

    public static void CopyValues(IReadOnlyList<decimal> values, double[] targets, decimal[] display)
    {
        Array.Clear(targets, 0, targets.Length);
        Array.Clear(display, 0, display.Length);
        for (var i = 0; i < Math.Min(values.Count, Math.Min(targets.Length, display.Length)); i++)
        {
            targets[i] = (double)values[i];
            display[i] = values[i];
        }
    }
}

internal static class TechStatsPalette
{
    public static Color GetBarColor(double value) => FromGameMakerHsv(55 + Math.Clamp(value, 0, 200), 200, 255);

    public static Color GetRainbowColor(double elapsedMilliseconds) =>
        FromGameMakerHsv((elapsedMilliseconds / 2d) % 255d, 200, 255);

    public static bool UsesRainbowNumber(decimal roundedValue) => Math.Abs(roundedValue) >= 400m;

    private static Color FromGameMakerHsv(double hue, double saturation, double brightness)
    {
        var h = hue / 255d * 360d;
        var s = saturation / 255d;
        var v = brightness / 255d;
        var chroma = v * s;
        var secondary = chroma * (1d - Math.Abs((h / 60d % 2d) - 1d));
        var match = v - chroma;
        var (red, green, blue) = h switch
        {
            < 60 => (chroma, secondary, 0d), < 120 => (secondary, chroma, 0d), < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma), < 300 => (secondary, 0d, chroma), _ => (chroma, 0d, secondary)
        };
        return Color.FromRgb(ToByte(red + match), ToByte(green + match), ToByte(blue + match));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Floor(value * 255d + 0.5d), 0d, 255d);
}
