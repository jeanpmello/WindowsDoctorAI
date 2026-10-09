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
        var choices = document.Descendants(Presentation + "NavigationViewItem").ToArray();
        var routes = choices
            .Select(choice => (string?)choice.Attribute("Tag"))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "about", "home", "settings" }, routes);
        Assert.All(choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)choice.Attribute("Content")));
            Assert.NotNull(choice.Element(Presentation + "NavigationViewItem.Icon"));
        });

        var homeChoice = Assert.Single(choices.Where(choice => (string?)choice.Attribute("Tag") == "home"));
        Assert.Equal("Inicial", (string?)homeChoice.Attribute("Content"));
        Assert.Single(document.Descendants(Presentation + "NavigationView")
            .Where(view => (string?)view.Attribute(Xaml + "Name") == "MainNavigationView"));
        Assert.Single(document.Descendants(Presentation + "Frame")
            .Where(frame => (string?)frame.Attribute(Xaml + "Name") == "PageFrame"));
    }

    [Fact]
    public void MainWindowLoadsHomeOnceAndMapsEveryDestinationToItsExistingPage()
    {
        var code = ReadFixture("MainWindow.xaml.cs");
        const string subscription = "_navigationService.Navigated += NavigationService_Navigated";
        const string initialNavigation = "_navigationService.NavigateTo(\"home\")";
        var subscriptionIndex = code.IndexOf(subscription, StringComparison.Ordinal);
        var initialNavigationIndex = code.IndexOf(initialNavigation, StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(code, initialNavigation));
        Assert.True(subscriptionIndex >= 0 && initialNavigationIndex > subscriptionIndex,
            "A janela deve assinar o evento de navegação antes de carregar a Home uma única vez.");
        Assert.Contains("args.SelectedItem is NavigationViewItem { Tag: string route }", code, StringComparison.Ordinal);
        Assert.Contains("_navigationService.NavigateTo(route)", code, StringComparison.Ordinal);
        Assert.Contains("\"home\" => _pageScope.ServiceProvider.GetRequiredService<HomePage>()", code, StringComparison.Ordinal);
        Assert.Contains("\"about\" => _pageScope.ServiceProvider.GetRequiredService<AboutPage>()", code, StringComparison.Ordinal);
        Assert.Contains("\"settings\" => _pageScope.ServiceProvider.GetRequiredService<SettingsPage>()", code, StringComparison.Ordinal);
        Assert.Contains("MainNavigationView.SelectedItem", code, StringComparison.Ordinal);
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

    [Fact]
    public void ReselectingActiveRouteRaisesNoEventAndDoesNotRecreateThePage()
    {
        var navigation = new NavigationService();
        var navigatedRoutes = new List<string>();
        var pageCreationCount = 0;

        // MainWindow replaces the page from this event, so each event represents one page creation.
        navigation.Navigated += (_, route) =>
        {
            navigatedRoutes.Add(route);
            pageCreationCount++;
        };

        navigation.NavigateTo("home");
        navigation.NavigateTo("home");
        navigation.NavigateTo("HOME");
        Assert.Equal(new[] { "home" }, navigatedRoutes);
        Assert.Equal(1, pageCreationCount);

        navigation.NavigateTo("about");
        navigation.NavigateTo("about");
        Assert.Equal(new[] { "home", "about" }, navigatedRoutes);
        Assert.Equal(2, pageCreationCount);

        navigation.NavigateTo("home");
        Assert.Equal(new[] { "home", "about", "home" }, navigatedRoutes);
        Assert.Equal(3, pageCreationCount);
        navigation.NavigateTo("home");
        Assert.Equal(new[] { "home", "about", "home" }, navigatedRoutes);
        Assert.Equal(3, pageCreationCount);
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
