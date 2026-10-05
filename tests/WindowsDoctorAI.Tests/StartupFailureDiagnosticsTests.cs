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
        Assert.Null(details.InnerExceptionType);
        Assert.Contains($"Estágio: {stage}", displayText, StringComparison.Ordinal);
        Assert.Contains("Tipo de exceção: SensitiveStartupException", displayText, StringComparison.Ordinal);
        Assert.Contains("Código técnico: 0x802B000A", displayText, StringComparison.Ordinal);

        Assert.DoesNotContain("MESSAGE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("STACK_TRACE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\fixture-user\Documents\private.log", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-user", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-host", displayText, StringComparison.Ordinal);
    }

    [Fact]
    public void XamlParseExceptionIncludesOnlySimpleInnerExceptionType()
    {
        var details = StartupFailureDetails.ForOnLaunched(CreateSensitiveXamlParseException());
        var displayText = details.ToDisplayText();

        Assert.Equal(nameof(SensitiveInnerException), details.InnerExceptionType);
        Assert.Contains("Tipo da exceção interna: SensitiveInnerException", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("OUTER_MESSAGE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER_MESSAGE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("STACK_TRACE_SENTINEL", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\fixture-user\Documents\private.log", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-user", displayText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-host", displayText, StringComparison.Ordinal);
    }

    [Fact]
    public void NonXamlExceptionDoesNotExposeInnerExceptionType()
    {
        var details = StartupFailureDetails.ForOnLaunched(CreateSensitiveException(new SensitiveInnerException()));

        Assert.Null(details.InnerExceptionType);
        Assert.DoesNotContain("SensitiveInnerException", details.ToDisplayText(), StringComparison.Ordinal);
    }

    private static SensitiveStartupException CreateSensitiveException()
    {
        var exception = new SensitiveStartupException();
        ExceptionDispatchInfo.SetRemoteStackTrace(
            exception,
            "STACK_TRACE_SENTINEL\r\n   at Synthetic.Startup() in 'C:\\Users\\fixture-user\\source.cs:line 42'");
        return exception;
    }

    private static SensitiveStartupException CreateSensitiveException(Exception innerException)
    {
        var exception = new SensitiveStartupException(innerException);
        ExceptionDispatchInfo.SetRemoteStackTrace(
            exception,
            "STACK_TRACE_SENTINEL\r\n   at Synthetic.Startup() in 'C:\\Users\\fixture-user\\source.cs:line 42'");
        return exception;
    }

    private static XamlParseException CreateSensitiveXamlParseException()
    {
        var innerException = new SensitiveInnerException();
        ExceptionDispatchInfo.SetRemoteStackTrace(
            innerException,
            "STACK_TRACE_SENTINEL\r\n   at Synthetic.Inner() in 'C:\\Users\\fixture-user\\source.cs:line 42'");
        return new XamlParseException(innerException);
    }

    private sealed class SensitiveStartupException : Exception
    {
        internal SensitiveStartupException(Exception? innerException = null)
            : base("MESSAGE_SENTINEL; user=fixture-user; host=fixture-host; path=C:\\Users\\fixture-user\\Documents\\private.log", innerException)
        {
            HResult = unchecked((int)0x802B000A);
        }
    }

    private sealed class XamlParseException : Exception
    {
        internal XamlParseException(Exception innerException)
            : base("OUTER_MESSAGE_SENTINEL; user=fixture-user; host=fixture-host; path=C:\\Users\\fixture-user\\Documents\\private.log", innerException)
        {
            HResult = unchecked((int)0x802B000A);
        }
    }

    private sealed class SensitiveInnerException : Exception
    {
        internal SensitiveInnerException()
            : base("INNER_MESSAGE_SENTINEL; user=fixture-user; host=fixture-host; path=C:\\Users\\fixture-user\\Documents\\private.log")
        {
        }
    }
}
