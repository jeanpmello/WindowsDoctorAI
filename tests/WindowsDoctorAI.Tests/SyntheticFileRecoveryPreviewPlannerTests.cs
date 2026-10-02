using WindowsDoctorAI.Application;

namespace WindowsDoctorAI.Tests;

public sealed class SyntheticFileRecoveryPreviewPlannerTests
{
    private static readonly SyntheticFileRecoverySource KnownSource = new(
        "backup-01",
        "version-2026-10-01",
        "volume-C",
        "item-report",
        @"C:\data\report.txt");

    private readonly SyntheticFileRecoveryPreviewPlanner planner = new();

    [Fact]
    public void CompleteKnownSelectionAndAlternateDestinationProduceDescriptiveCreateCopyPreview()
    {
        var result = planner.CreatePreview(ValidRequest(), [KnownSource]);

        Assert.True(result.IsValid);
        var preview = Assert.IsType<SyntheticFileRecoveryPreview>(result.Preview);
        Assert.Equal(KnownSource.BackupSetId, preview.BackupSetId);
        Assert.Equal(KnownSource.VersionId, preview.VersionId);
        Assert.Equal(KnownSource.VolumeId, preview.VolumeId);
        Assert.Equal(KnownSource.ItemId, preview.ItemId);
        Assert.Equal(KnownSource.SourcePath, preview.SourcePath);
        Assert.Equal(@"D:\recovery\report-copy.txt", preview.AlternateDestinationPath);
        Assert.Equal(SyntheticFileRecoveryOverwritePolicy.CreateCopy, preview.OverwritePolicy);
        Assert.Contains("Prévia sintética", preview.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-WBFileRecovery", preview.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wbadmin", preview.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não garante recuperação", preview.Limitations, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exige validação humana", preview.Limitations, StringComparison.Ordinal);
        Assert.Contains("nenhuma ação foi iniciada", preview.Limitations, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSelectionAndDestinationFieldsAreRejected()
    {
        var request = new SyntheticFileRecoveryPreviewRequest(null, " ", null, "", null, null);

        var result = planner.CreatePreview(request, [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        var errors = string.Join('\n', result.ValidationErrors);
        Assert.Contains("conjunto de backup", errors, StringComparison.Ordinal);
        Assert.Contains("versão", errors, StringComparison.Ordinal);
        Assert.Contains("volume", errors, StringComparison.Ordinal);
        Assert.Contains("item", errors, StringComparison.Ordinal);
        Assert.Contains("origem", errors, StringComparison.Ordinal);
        Assert.Contains("destino alternativo", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("backup", "conjunto de backup informado é desconhecido")]
    [InlineData("version", "versão informada é desconhecida")]
    [InlineData("volume", "volume informado é desconhecido")]
    [InlineData("item", "item informado é desconhecido")]
    public void UnknownBackupVersionVolumeOrItemIsBlocked(string field, string expectedError)
    {
        var request = field switch
        {
            "backup" => ValidRequest() with { BackupSetId = "unknown-backup" },
            "version" => ValidRequest() with { VersionId = "unknown-version" },
            "volume" => ValidRequest() with { VolumeId = "unknown-volume" },
            "item" => ValidRequest() with { ItemId = "unknown-item" },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var result = planner.CreatePreview(request, [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        Assert.Contains(result.ValidationErrors, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("relative\\report.txt")]
    [InlineData("C:\\data\\..\\secret.txt")]
    [InlineData("C:\\invalid?.txt")]
    public void InvalidSourcePathIsBlocked(string sourcePath)
    {
        var result = planner.CreatePreview(ValidRequest() with { SourcePath = sourcePath }, [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        Assert.Contains(result.ValidationErrors, error => error.Contains("caminho de origem", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\target")]
    [InlineData("D:\\recovery\\..\\target")]
    [InlineData("D:\\recovery\\bad?.txt")]
    [InlineData("D:\\recovery\\\\target")]
    public void EmptyOrInvalidAlternateDestinationIsBlocked(string destinationPath)
    {
        var result = planner.CreatePreview(ValidRequest() with { AlternateDestinationPath = destinationPath }, [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        Assert.Contains(result.ValidationErrors, error => error.Contains("destino alternativo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SameSourcePathIsBlockedIgnoringWindowsCaseAndTrailingSeparator()
    {
        var result = planner.CreatePreview(
            ValidRequest() with { AlternateDestinationPath = @"c:\DATA\report.txt\" },
            [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        Assert.Contains(result.ValidationErrors, error => error.Contains("mesmo caminho", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MismatchedSourcePathIsBlockedEvenWhenIdentifiersAreKnown()
    {
        var result = planner.CreatePreview(ValidRequest() with { SourcePath = @"C:\data\other.txt" }, [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        Assert.Contains(result.ValidationErrors, error => error.Contains("não corresponde", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnyOverwriteRequestIsRejectedInsteadOfCreatingPreview()
    {
        var result = planner.CreatePreview(ValidRequest() with { OverwriteRequested = true }, [KnownSource]);

        Assert.False(result.IsValid);
        Assert.Null(result.Preview);
        Assert.Contains(result.ValidationErrors, error => error.Contains("Sobrescrita não é permitida", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellationStopsPreviewBeforeValidationOrPlanCreation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            planner.CreatePreview(ValidRequest(), [KnownSource], cancellation.Token));
    }

    private static SyntheticFileRecoveryPreviewRequest ValidRequest() => new(
        KnownSource.BackupSetId,
        KnownSource.VersionId,
        KnownSource.VolumeId,
        KnownSource.ItemId,
        KnownSource.SourcePath,
        @"D:\recovery\report-copy.txt");
}
