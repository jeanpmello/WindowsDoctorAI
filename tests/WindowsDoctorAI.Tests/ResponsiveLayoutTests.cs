using System.Xml.Linq;

namespace WindowsDoctorAI.Tests;

public sealed class ResponsiveLayoutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "App", name));

    [Fact]
    public void HomePageDeclaresResponsiveTargetsAndSizeChangedHandler()
    {
        var document = XDocument.Parse(ReadFixture("HomePage.xaml"));
        var page = document.Root;

        Assert.Equal("Page_SizeChanged", (string?)page?.Attribute("SizeChanged"));
        foreach (var name in new[]
        {
            "ContentStack", "MetricSummaryGrid", "CategorySummaryGrid",
            "KnowledgeReportGrid", "InventoryGrid", "DiagnosticActionsStack",
            "CbsActionsStack", "BackupActionsStack", "AiActionsStack"
        })
        {
            Assert.Single(document.Descendants().Where(element =>
                (string?)element.Attribute(Xaml + "Name") == name));
        }
    }

    [Fact]
    public void HomePageKeepsCardsInNamedGridsForCompactReflow()
    {
        var document = XDocument.Parse(ReadFixture("HomePage.xaml"));

        Assert.Equal(3, document.Descendants(Presentation + "Grid")
            .Single(grid => (string?)grid.Attribute(Xaml + "Name") == "MetricSummaryGrid")
            .Elements(Presentation + "Border").Count());
        Assert.Equal(6, document.Descendants(Presentation + "Grid")
            .Single(grid => (string?)grid.Attribute(Xaml + "Name") == "InventoryGrid")
            .Elements(Presentation + "Border").Count());
    }

    [Fact]
    public void HomePageCodeBehindContainsCompactAndWideLayoutRules()
    {
        var code = ReadFixture("HomePage.xaml.cs");

        Assert.Contains("width < 900", code, StringComparison.Ordinal);
        Assert.Contains("width < 620", code, StringComparison.Ordinal);
        Assert.Contains("Orientation.Vertical", code, StringComparison.Ordinal);
        Assert.Contains("Orientation.Horizontal", code, StringComparison.Ordinal);
        Assert.Contains("SetGridColumns", code, StringComparison.Ordinal);
        Assert.Contains("SetChildPositions", code, StringComparison.Ordinal);
    }
}
