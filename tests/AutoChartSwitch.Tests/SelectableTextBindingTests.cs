using System.Xml.Linq;

namespace AutoChartSwitch.Tests;

public sealed class SelectableTextBindingTests
{
    [Fact]
    public void SelectableBoundTextUsesOneWayBindings()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "AutoChartSwitch.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var bindings = document.Descendants(presentation + "TextBox")
            .Where(element => element.Attribute("Style")?.Value.Contains("SelectableText", StringComparison.Ordinal) == true)
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value?.StartsWith("{Binding", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(bindings);
        Assert.All(bindings, binding => Assert.Contains("Mode=OneWay", binding, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AutoChartSwitchV2.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the AutoChartSwitchV2 repository root.");
    }
}
