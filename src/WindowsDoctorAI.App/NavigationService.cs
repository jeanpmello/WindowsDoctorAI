using WindowsDoctorAI.Core;

namespace WindowsDoctorAI.App;

/// <summary>Publica navegação por identificadores; a janela resolve a página correspondente via DI.</summary>
public sealed class NavigationService : INavigationService
{
    private static readonly HashSet<string> Routes = new(StringComparer.OrdinalIgnoreCase) { "home", "about", "settings" };
    public event EventHandler<string>? Navigated;

    public void NavigateTo(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (!Routes.Contains(destination)) throw new ArgumentOutOfRangeException(nameof(destination), destination, "Rota de navegação desconhecida.");
        Navigated?.Invoke(this, destination);
    }
}
