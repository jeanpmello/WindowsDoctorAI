namespace WindowsDoctorAI.Application;

/// <summary>Mensagens de UI que deliberadamente descartam detalhes potencialmente sensíveis de exceções.</summary>
public static class DiagnosticPrivacyMessages
{
    public static string DiagnosticFailure(Exception? _) =>
        "Não foi possível concluir o diagnóstico. Nenhum reparo foi aplicado; tente novamente ou revise as configurações locais.";

    public static string HistoryLoadFailure(Exception? _) =>
        "Não foi possível carregar o histórico local. Você ainda pode iniciar um novo diagnóstico.";
}
