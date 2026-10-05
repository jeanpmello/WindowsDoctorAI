using System.Xml.Linq;
using WindowsDoctorAI.App;

namespace WindowsDoctorAI.Tests;

public sealed class MainWindowNavigationWiringTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "App", name));

    [Fact]
    public void NavigationChoicesCoverEveryRouteAndRemainKeyboardAccessible()
    {
        var document = XDocument.Parse(ReadFixture("MainWindow.xaml"));
        var choices = document.Descendants(Presentation + "RadioButton").ToArray();
        var routes = choices
            .Select(choice => (string?)choice.Attribute("Tag"))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "about", "home", "settings" }, routes);
        Assert.All(choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)choice.Attribute("Content")));
            Assert.Equal("MainNavigation", (string?)choice.Attribute("GroupName"));
            Assert.Equal("NavigationButton_Click", (string?)choice.Attribute("Click"));
            Assert.NotEqual("False", (string?)choice.Attribute("IsTabStop"));
        });

        var homeChoice = Assert.Single(choices.Where(choice => (string?)choice.Attribute("Tag") == "home"));
        Assert.Equal("Inicial", (string?)homeChoice.Attribute("Content"));
        Assert.Equal("True", (string?)homeChoice.Attribute("IsChecked"));
        Assert.Single(document.Descendants(Presentation + "Frame")
            .Where(frame => (string?)frame.Attribute(Xaml + "Name") == "PageFrame"));
        Assert.Empty(document.Descendants().Where(element => element.Name.LocalName == "NavigationView"));
    }

    [Fact]
    public void MainWindowLoadsHomeOnceAndMapsEveryDestinationToItsExistingPage()
    {
        var code = ReadFixture("MainWindow.xaml.cs");

        Assert.Equal(1, CountOccurrences(code, "_navigationService.NavigateTo(\"home\")"));
        Assert.True(code.IndexOf("_navigationService.Navigated += NavigationService_Navigated", StringComparison.Ordinal)
            < code.IndexOf("_navigationService.NavigateTo(\"home\")", StringComparison.Ordinal));
        Assert.Contains("element.Tag is string route", code, StringComparison.Ordinal);
        Assert.Contains("_navigationService.NavigateTo(route)", code, StringComparison.Ordinal);
        Assert.Contains("\"home\" => _pageScope.ServiceProvider.GetRequiredService<HomePage>()", code, StringComparison.Ordinal);
        Assert.Contains("\"about\" => _pageScope.ServiceProvider.GetRequiredService<AboutPage>()", code, StringComparison.Ordinal);
        Assert.Contains("\"settings\" => _pageScope.ServiceProvider.GetRequiredService<SettingsPage>()", code, StringComparison.Ordinal);
        Assert.Contains("HomeNavigationButton.IsChecked", code, StringComparison.Ordinal);
        Assert.Contains("AboutNavigationButton.IsChecked", code, StringComparison.Ordinal);
        Assert.Contains("SettingsNavigationButton.IsChecked", code, StringComparison.Ordinal);
        Assert.Contains("PageFrame.Content = page", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationServiceCanReturnToHomeAfterVisitingOtherDestinations()
    {
        var navigation = new NavigationService();
        var navigatedRoutes = new List<string>();
        navigation.Navigated += (_, route) => navigatedRoutes.Add(route);

        navigation.NavigateTo("home");
        navigation.NavigateTo("about");
        navigation.NavigateTo("settings");
        navigation.NavigateTo("home");

        Assert.Equal(new[] { "home", "about", "settings", "home" }, navigatedRoutes);
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.NavigateTo("unknown"));
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
