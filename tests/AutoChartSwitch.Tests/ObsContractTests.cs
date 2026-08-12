using AutoChartSwitch.Core;
using AutoChartSwitch.Obs;

namespace AutoChartSwitch.Tests;

public sealed class ObsContractTests
{
    [Fact]
    public void AllEightUniqueCompatibleMappingsValidate()
    {
        var mappings = Mappings().AsDictionary();
        var inputs = new List<ObsInputInfo>
        {
            Text("title"), Text("artist"), FreeType("credits"), Text("difficulty-name"), Text("difficulty-number"),
            Image("jacket"), Image("difficulty-image"), Media("showcase")
        };

        Assert.Null(ObsChartPublisher.ValidateMappings(mappings, inputs));
    }

    [Fact]
    public void CreditsMustMapToFreeTypeAndMappingsMustBeUnique()
    {
        var mappings = Mappings();
        var inputs = new List<ObsInputInfo>
        {
            Text("title"), Text("artist"), Text("credits"), Text("difficulty-name"), Text("difficulty-number"),
            Image("jacket"), Image("difficulty-image"), Media("showcase")
        };
        Assert.Contains("incompatible", ObsChartPublisher.ValidateMappings(mappings.AsDictionary(), inputs), StringComparison.OrdinalIgnoreCase);

        mappings.Artist = "title";
        Assert.Contains("assigned more than once", ObsChartPublisher.ValidateMappings(mappings.AsDictionary(), inputs), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectionAndSettingKeysMatchObsContract()
    {
        var chart = FormatterAndValidationTests.ValidChart() with { Illustrator = "Ivy", Charter = "Casey", DifficultyNumber = 5m };
        var projection = ObsChartPublisher.Project(chart, "C:\\difficulty\\Master.png");

        Assert.Equal("Illust: Ivy\nChart: Casey", projection[ObsOutput.Credits]);
        Assert.Equal("5", projection[ObsOutput.DifficultyNumber]);
        Assert.Equal("text", ObsChartPublisher.GetSettingKey(ObsOutput.Title));
        Assert.Equal("file", ObsChartPublisher.GetSettingKey(ObsOutput.Jacket));
        Assert.Equal("local_file", ObsChartPublisher.GetSettingKey(ObsOutput.ShowcaseVideo));
    }

    [Fact]
    public void UnsupportedTextUsesFormattedValueBeforeChangingFont()
    {
        var chart = FormatterAndValidationTests.ValidChart() with
        {
            Title = "\u66f2\u540d",
            FormattedTitle = "TITLE",
            Artist = "ARTIST"
        };

        var presentation = ObsChartPublisher.ResolveTextPresentation(chart, IsAscii);

        Assert.Equal("TITLE", presentation.Values[ObsOutput.Title]);
        Assert.Equal("ARTIST", presentation.Values[ObsOutput.Artist]);
        Assert.Equal(ObsChartPublisher.DefaultTextFont, presentation.FontFaces[ObsOutput.Title]);
        Assert.Equal(ObsChartPublisher.DefaultTextFont, presentation.FontFaces[ObsOutput.Artist]);
    }

    [Fact]
    public void CreditsSourceUsesUnifontWhenEitherCreditHasNoUsableFormattedValue()
    {
        var chart = FormatterAndValidationTests.ValidChart() with
        {
            Illustrator = "\u63d2\u753b",
            Charter = "\u8c31\u5e08",
            FormattedCharter = "CHARTER"
        };

        var presentation = ObsChartPublisher.ResolveTextPresentation(chart, IsAscii);

        Assert.Equal("Illust: \u63d2\u753b\nChart: CHARTER", presentation.Values[ObsOutput.Credits]);
        Assert.Equal(ObsChartPublisher.FallbackTextFont, presentation.FontFaces[ObsOutput.Credits]);
    }

    [Fact]
    public void UnsupportedFormattedValueStillUsesUnifont()
    {
        var chart = FormatterAndValidationTests.ValidChart() with
        {
            Title = "\u66f2\u540d",
            FormattedTitle = "\u683c\u5f0f\u5316\u66f2\u540d"
        };

        var presentation = ObsChartPublisher.ResolveTextPresentation(chart, IsAscii);

        Assert.Equal("\u683c\u5f0f\u5316\u66f2\u540d", presentation.Values[ObsOutput.Title]);
        Assert.Equal(ObsChartPublisher.FallbackTextFont, presentation.FontFaces[ObsOutput.Title]);
    }

    [Fact]
    public void RelativeLiveJacketResolvesFromGameMakerSandboxBeforeGameDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-v2-sandbox-jacket", Guid.NewGuid().ToString("N"));
        var sandbox = Path.Combine(root, "sandbox");
        var game = Path.Combine(root, "game");
        var relative = Path.Combine("AutoChartSwitchV2", "Jackets", "chart-OPENING.png");
        var sandboxJacket = Path.Combine(sandbox, relative);
        var gameJacket = Path.Combine(game, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(sandboxJacket)!);
        Directory.CreateDirectory(Path.GetDirectoryName(gameJacket)!);
        try
        {
            File.WriteAllText(sandboxJacket, "sandbox");
            File.WriteAllText(gameJacket, "game");
            var chart = new ChartInfo { JacketPath = relative };

            var resolved = ObsChartPublisher.ResolveGamePaths(chart, game, sandbox);

            Assert.Equal(Path.GetFullPath(sandboxJacket), resolved.JacketPath);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ObsSourceMappings Mappings() => new()
    {
        Title = "title", Artist = "artist", Credits = "credits", DifficultyName = "difficulty-name",
        DifficultyNumber = "difficulty-number", Jacket = "jacket", DifficultyImage = "difficulty-image",
        ShowcaseVideo = "showcase"
    };

    private static ObsInputInfo Text(string name) => new(name, "text_gdiplus_v3", "text_gdiplus", ObsInputCategory.Text);
    private static ObsInputInfo FreeType(string name) => new(name, "text_ft2_source_v2", "text_ft2_source", ObsInputCategory.FreeTypeText);
    private static ObsInputInfo Image(string name) => new(name, "image_source", "image_source", ObsInputCategory.Image);
    private static ObsInputInfo Media(string name) => new(name, "ffmpeg_source", "ffmpeg_source", ObsInputCategory.Media);
    private static bool IsAscii(string value) => value.All(character => character <= 0x7f);
}
