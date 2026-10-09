using WindowsDoctorAI.AI;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class DiagnosticConversationPromptBuilderTests
{
    [Fact]
    public void Build_includes_redacted_diagnostic_and_latest_history()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new DiagnosticResult(
            "WindowsUpdate", "Windows Update", DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
            "Falha 0xC1900107", "descrição", "ação manual", "usuário@empresa.test C:\\Users\\jean", TimeSpan.Zero, now);
        var run = new DiagnosticRun(
            Guid.NewGuid(), now, now, TimeSpan.Zero,
            new ComputerInventory { ComputerName = "PC-PRIVADO", UserName = "jean" },
            new DiagnosticReport([result], now, now, TimeSpan.Zero, new HealthScore(50)));
        var history = Enumerable.Range(0, 20)
            .Select(index => new AiChatMessage("user", $"mensagem {index}"))
            .ToArray();

        var messages = DiagnosticConversationPromptBuilder.Build(run, "Como resolvo isso?", history);
        var joined = string.Join("\n", messages.Select(message => message.Content));

        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[^1].Role);
        Assert.Equal("Como resolvo isso?", messages[^1].Content);
        Assert.Contains("0xC1900107", joined);
        Assert.DoesNotContain("PC-PRIVADO", joined);
        Assert.DoesNotContain("jean", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mensagem 0", joined);
        Assert.Contains("mensagem 19", joined);
    }

    [Fact]
    public void Build_rejects_an_overlong_user_message()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), now, now, TimeSpan.Zero, new ComputerInventory());

        Assert.Throws<ArgumentException>(() => DiagnosticConversationPromptBuilder.Build(
            run, new string('x', DiagnosticConversationPromptBuilder.MaxUserMessageCharacters + 1)));
    }
}
