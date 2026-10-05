using System.Globalization;

namespace WindowsDoctorAI.App;

/// <summary>Dados allowlistados para diagnóstico de falha durante a inicialização.</summary>
internal sealed class StartupFailureDetails
{
    private const string AppEntryPointStage = "AppEntryPoint";
    private const string OnLaunchedStage = "OnLaunched";
    private const string XamlParseExceptionTypeName = "XamlParseException";

    private StartupFailureDetails(string stage, string exceptionType, int hresult, string? innerExceptionType)
    {
        Stage = stage;
        ExceptionType = exceptionType;
        HResult = hresult;
        InnerExceptionType = innerExceptionType;
    }

    internal string Stage { get; }

    internal string ExceptionType { get; }

    internal int HResult { get; }

    internal string? InnerExceptionType { get; }

    internal string HResultCode => string.Format(
        CultureInfo.InvariantCulture,
        "0x{0:X8}",
        unchecked((uint)HResult));

    internal static StartupFailureDetails ForAppEntryPoint(Exception exception) =>
        Create(AppEntryPointStage, exception);

    internal static StartupFailureDetails ForOnLaunched(Exception exception) =>
        Create(OnLaunchedStage, exception);

    internal string ToDisplayText()
    {
        var innerExceptionText = InnerExceptionType is null
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, "\r\nTipo da exceção interna: {0}", InnerExceptionType);

        return string.Format(
            CultureInfo.InvariantCulture,
            "O Windows Doctor AI não conseguiu concluir a inicialização.\r\n\r\nEstágio: {0}\r\nTipo de exceção: {1}\r\nCódigo técnico: {2}{3}\r\n\r\nNenhuma varredura de diagnóstico foi iniciada. Consulte README-ALPHA.md e informe apenas os campos técnicos exibidos ao relatar o problema. Não envie dumps ou dados pessoais.",
            Stage,
            ExceptionType,
            HResultCode,
            innerExceptionText);
    }

    private static StartupFailureDetails Create(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var exceptionType = exception.GetType().Name;
        var innerExceptionType = string.Equals(exceptionType, XamlParseExceptionTypeName, StringComparison.Ordinal)
            ? GetSimpleExceptionTypeName(exception.InnerException)
            : null;

        return new StartupFailureDetails(stage, exceptionType, exception.HResult, innerExceptionType);
    }

    private static string? GetSimpleExceptionTypeName(Exception? exception)
    {
        var name = exception?.GetType().Name;
        if (name is null || name.Length is 0 or > 80 || !IsAsciiLetterOrUnderscore(name[0])) return null;

        foreach (var character in name.AsSpan(1))
        {
            if (!IsAsciiLetterOrDigitOrUnderscore(character)) return null;
        }

        return name;
    }

    private static bool IsAsciiLetterOrUnderscore(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private static bool IsAsciiLetterOrDigitOrUnderscore(char value) =>
        IsAsciiLetterOrUnderscore(value) || value is >= '0' and <= '9';
}
