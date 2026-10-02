namespace WindowsDoctorAI.Core;

/// <summary>Porta de navegação por rotas, sem referência a controles de apresentação.</summary>
public interface INavigationService
{
    event EventHandler<string>? Navigated;
    void NavigateTo(string destination);
}
