using WindowsDoctorAI.AI;

namespace WindowsDoctorAI.Tests;

public sealed class EmbeddedOnnxGenAiProviderTests
{
    [Fact]
    public async Task Disabled_provider_does_not_touch_model_directory()
    {
        var provider = new EmbeddedOnnxGenAiProvider(new EmbeddedAiOptions
        {
            Enabled = false,
            ModelDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });

        var availability = await provider.CheckAvailabilityAsync();
        var result = await provider.AnalyzeAsync(new AiAnalysisRequest("sistema", "dados"));

        Assert.False(availability.IsReady);
        Assert.Equal(AiAnalysisStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task Enabled_provider_reports_missing_model_without_loading_runtime()
    {
        var provider = new EmbeddedOnnxGenAiProvider(new EmbeddedAiOptions
        {
            Enabled = true,
            ModelDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });

        var availability = await provider.CheckAvailabilityAsync();
        var result = await provider.AnalyzeAsync(new AiAnalysisRequest("sistema", "dados"));

        Assert.False(availability.IsReady);
        Assert.Equal(AiAnalysisStatus.ModelNotInstalled, result.Status);
    }

    [Fact]
    public async Task Enabled_provider_rejects_directory_without_onnx_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WindowsDoctorAI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var provider = new EmbeddedOnnxGenAiProvider(new EmbeddedAiOptions
            {
                Enabled = true,
                ModelDirectory = directory
            });

            var availability = await provider.CheckAvailabilityAsync();

            Assert.False(availability.IsReady);
            Assert.Contains("ONNX", availability.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Screenshot_analysis_rejects_unsupported_media_without_model()
    {
        var provider = new EmbeddedOnnxGenAiProvider(new EmbeddedAiOptions { Enabled = true });

        var result = await provider.AnalyzeScreenshotAsync("ler o erro", new byte[] { 1, 2, 3 }, "image/gif");

        Assert.Equal(AiAnalysisStatus.InvalidResponse, result.Status);
        Assert.Contains("formato", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Screenshot_analysis_rejects_oversized_image_before_model_load()
    {
        var provider = new EmbeddedOnnxGenAiProvider(new EmbeddedAiOptions { Enabled = true });
        var image = new byte[(8 * 1024 * 1024) + 1];

        var result = await provider.AnalyzeScreenshotAsync("ler o erro", image, "image/png");

        Assert.Equal(AiAnalysisStatus.InvalidResponse, result.Status);
        Assert.Contains("8 MiB", result.Message, StringComparison.Ordinal);
    }
}
