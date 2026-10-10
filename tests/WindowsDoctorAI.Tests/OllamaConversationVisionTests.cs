using System.Net;
using System.Text;
using System.Text.Json;
using WindowsDoctorAI.AI;

namespace WindowsDoctorAI.Tests;

public sealed class OllamaConversationVisionTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static OllamaDiagnosticAiProvider Create(StubHandler handler)
    {
        return new OllamaDiagnosticAiProvider(
            new HttpClient(handler),
            new OllamaOptions { Enabled = true, Model = "qwen3-vl:8b" });
    }

    [Fact]
    public async Task Chat_sends_ordered_messages_to_local_api()
    {
        var handler = new StubHandler(_ => Json("""{"message":{"role":"assistant","content":"Posso ajudar."},"done":true}"""));
        var provider = Create(handler);

        var result = await provider.ChatAsync([
            new AiChatMessage("system", "Você é um técnico Windows."),
            new AiChatMessage("user", "O que significa este erro?")
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal("Posso ajudar.", result.Text);
        Assert.Contains("qwen3-vl:8b", handler.LastBody!);
        using var document = JsonDocument.Parse(handler.LastBody!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal("Você é um técnico Windows.", messages[0].GetProperty("content").GetString());
        Assert.Equal("O que significa este erro?", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Screenshot_sends_base64_image_and_uses_local_api()
    {
        var handler = new StubHandler(_ => Json("""{"message":{"content":"Código visível: 0x80073712."}}"""));
        var provider = Create(handler);
        var image = Encoding.UTF8.GetBytes("fake-png-for-transport-test");

        var result = await provider.AnalyzeScreenshotAsync(
            "Identifique o código e explique o que deve ser verificado.", image, "image/png");

        Assert.True(result.IsSuccess);
        Assert.Contains("images", handler.LastBody!);
        Assert.Contains(Convert.ToBase64String(image), handler.LastBody!);
        Assert.Contains("0x80073712", result.Text);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("text/plain")]
    public async Task Screenshot_rejects_unsupported_media_without_network_call(string mediaType)
    {
        var handler = new StubHandler(_ => Json("{}"));
        var provider = Create(handler);

        var result = await provider.AnalyzeScreenshotAsync("Leia a tela", new byte[] { 1, 2, 3 }, mediaType);

        Assert.Equal(AiAnalysisStatus.InvalidResponse, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Chat_rejects_excessive_history_without_network_call()
    {
        var handler = new StubHandler(_ => Json("{}"));
        var provider = Create(handler);
        var messages = Enumerable.Range(0, 21)
            .Select(_ => new AiChatMessage("user", "mensagem"))
            .ToArray();

        var result = await provider.ChatAsync(messages);

        Assert.Equal(AiAnalysisStatus.InvalidResponse, result.Status);
        Assert.Empty(handler.Requests);
    }
}
