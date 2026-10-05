using System.Globalization;

namespace WindowsDoctorAI.App;

/// <summary>Dados allowlistados para diagnóstico de falha durante a inicialização.</summary>
internal sealed class StartupFailureDetails
{
    private const string AppEntryPointStage = "AppEntryPoint";
    private const string OnLaunchedStage = "OnLaunched";

    private StartupFailureDetails(string stage, string exceptionType, int hresult)
    {
        Stage = stage;
        ExceptionType = exceptionType;
        HResult = hresult;
    }

    internal string Stage { get; }

    internal string ExceptionType { get; }

    internal int HResult { get; }

    internal string HResultCode => string.Format(
        CultureInfo.InvariantCulture,
        "0x{0:X8}",
        unchecked((uint)HResult));

    internal static StartupFailureDetails ForAppEntryPoint(Exception exception) =>
        Create(AppEntryPointStage, exception);

    internal static StartupFailureDetails ForOnLaunched(Exception exception) =>
        Create(OnLaunchedStage, exception);

    internal string ToDisplayText() => string.Format(
        CultureInfo.InvariantCulture,
        "O Windows Doctor AI não conseguiu concluir a inicialização.\r\n\r\nEstágio: {0}\r\nTipo de exceção: {1}\r\nCódigo técnico: {2}\r\n\r\nNenhuma varredura de diagnóstico foi iniciada. Consulte README-ALPHA.md e informe apenas o estágio, o tipo de exceção e o código técnico ao relatar o problema. Não envie dumps ou dados pessoais.",
        Stage,
        ExceptionType,
        HResultCode);

    private static StartupFailureDetails Create(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new StartupFailureDetails(stage, exception.GetType().Name, exception.HResult);
    }
}
