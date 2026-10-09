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
}
