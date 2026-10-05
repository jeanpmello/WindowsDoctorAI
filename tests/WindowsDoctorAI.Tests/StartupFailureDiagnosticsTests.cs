using System.Runtime.ExceptionServices;
using WindowsDoctorAI.App;

namespace WindowsDoctorAI.Tests;

public sealed class StartupFailureDiagnosticsTests
{
    [Theory]
    [InlineData("AppEntryPoint")]
    [InlineData("OnLaunched")]
    public void StartupFailureDetailsContainOnlyAllowlistedFields(string stage)
    {
        var exception = CreateSensitiveException();
        var details = stage switch
        {
            "AppEntryPoint" => StartupFailureDetails.ForAppEntryPoint(exception),
            "OnLaunched" => StartupFailureDetails.ForOnLaunched(exception),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        var displayText = details.ToDisplayText();

        Assert.Equal(stage, details.Stage);
        Assert.Equal(nameof(SensitiveStartupException), details.ExceptionType);
        Assert.Equal(unchecked((int)0x802B000A), details.HResult);
        Assert.Equal("0x802B000A", details.HResultCode);
        Assert.Contains($"Estágio: {stage}", displayText, StringComparison.Ordinal);
        Assert.Contains("Tipo de exceção: SensitiveStartupException", displayText, StringComparison.Ordinal);
        Assert.Contains("Código técnico: 0x802B000A", displayText, StringComparison.Ordinal);

        Assert.DoesNotContain("MESSAGE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("STACK_TRACE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\fixture-user\Documents\private.log", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-user", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-host", displayText, StringComparison.Ordinal);
    }

    private static SensitiveStartupException CreateSensitiveException()
    {
        var exception = new SensitiveStartupException();
        ExceptionDispatchInfo.SetRemoteStackTrace(
            exception,
            "STACK_TRACE_SENTINEL\r\n   at Synthetic.Startup() in 'C:\\Users\\fixture-user\\source.cs:line 42'");
        return exception;
    }

    private sealed class SensitiveStartupException : Exception
    {
        internal SensitiveStartupException()
            : base("MESSAGE_SENTINEL; user=fixture-user; host=fixture-host; path=C:\\Users\\fixture-user\\Documents\\private.log")
        {
            HResult = unchecked((int)0x802B000A);
        }
    }
}
