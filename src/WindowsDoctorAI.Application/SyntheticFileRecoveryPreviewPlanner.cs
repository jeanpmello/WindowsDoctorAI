namespace WindowsDoctorAI.Application;

/// <summary>Política fixa para a prévia sintética; nenhuma alternativa que permita sobrescrita é aceita.</summary>
public enum SyntheticFileRecoveryOverwritePolicy
{
    CreateCopy
}

/// <summary>Item sintético fornecido pelo chamador para validação determinística; não é descoberto em backups reais.</summary>
public sealed record SyntheticFileRecoverySource(
    string BackupSetId,
    string VersionId,
    string VolumeId,
    string ItemId,
    string SourcePath);

/// <summary>Seleção explícita para uma prévia apenas; não autoriza nem inicia recuperação.</summary>
public sealed record SyntheticFileRecoveryPreviewRequest(
    string? BackupSetId,
    string? VersionId,
    string? VolumeId,
    string? ItemId,
    string? SourcePath,
    string? AlternateDestinationPath,
    bool OverwriteRequested = false);

/// <summary>Dados descritivos de um plano sintético. Não contém comando executável ou copiável.</summary>
public sealed record SyntheticFileRecoveryPreview(
    string BackupSetId,
    string VersionId,
    string VolumeId,
    string ItemId,
    string SourcePath,
    string AlternateDestinationPath,
    SyntheticFileRecoveryOverwritePolicy OverwritePolicy,
    string Summary,
    string Limitations);

public sealed record SyntheticFileRecoveryPreviewResult(
    SyntheticFileRecoveryPreview? Preview,
    IReadOnlyList<string> ValidationErrors)
{
    public bool IsValid => Preview is not null && ValidationErrors.Count == 0;
}

/// <summary>
/// Produz somente uma prévia descritiva contra uma lista de origem sintética fornecida pelo chamador.
/// Não lê o sistema de backup, filesystem, destino ou estado do Windows.
/// </summary>
public sealed class SyntheticFileRecoveryPreviewPlanner
{
    public SyntheticFileRecoveryPreviewResult CreatePreview(
        SyntheticFileRecoveryPreviewRequest? request,
        IReadOnlyCollection<SyntheticFileRecoverySource>? knownSyntheticSources,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        if (request is null)
        {
            errors.Add("A solicitação de prévia está ausente.");
            return Invalid(errors);
        }

        RequireIdentifier(request.BackupSetId, "conjunto de backup", errors);
        RequireIdentifier(request.VersionId, "versão", errors);
        RequireIdentifier(request.VolumeId, "volume", errors);
        RequireIdentifier(request.ItemId, "item", errors);

        var sourcePathIsValid = TryNormalizeWindowsPath(request.SourcePath, out var normalizedSourcePath) &&
                                !IsWindowsRoot(normalizedSourcePath);
        if (!sourcePathIsValid)
            errors.Add("O caminho de origem do arquivo é obrigatório e deve ser absoluto e válido no Windows.");

        var destinationPathIsValid = TryNormalizeWindowsPath(request.AlternateDestinationPath, out var normalizedDestinationPath);
        if (!destinationPathIsValid)
            errors.Add("O destino alternativo é obrigatório e deve ser absoluto e válido no Windows.");

        if (sourcePathIsValid && destinationPathIsValid &&
            string.Equals(normalizedSourcePath, normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("O destino não pode ser o mesmo caminho do item de origem.");
        }

        if (request.OverwriteRequested)
            errors.Add("Sobrescrita não é permitida; a única política disponível é CreateCopy.");

        var sources = knownSyntheticSources ?? Array.Empty<SyntheticFileRecoverySource>();
        IReadOnlyCollection<SyntheticFileRecoverySource>? itemCandidates = null;

        if (HasIdentifier(request.BackupSetId))
        {
            var backupCandidates = sources
                .Where(source => source is not null && string.Equals(source.BackupSetId, request.BackupSetId, StringComparison.Ordinal))
                .ToArray();
            if (backupCandidates.Length == 0)
            {
                errors.Add("O conjunto de backup informado é desconhecido na lista sintética fornecida.");
            }
            else if (HasIdentifier(request.VersionId))
            {
                var versionCandidates = backupCandidates
                    .Where(source => string.Equals(source.VersionId, request.VersionId, StringComparison.Ordinal))
                    .ToArray();
                if (versionCandidates.Length == 0)
                {
                    errors.Add("A versão informada é desconhecida para o conjunto selecionado.");
                }
                else if (HasIdentifier(request.VolumeId))
                {
                    var volumeCandidates = versionCandidates
                        .Where(source => string.Equals(source.VolumeId, request.VolumeId, StringComparison.Ordinal))
                        .ToArray();
                    if (volumeCandidates.Length == 0)
                    {
                        errors.Add("O volume informado é desconhecido para a versão selecionada.");
                    }
                    else if (HasIdentifier(request.ItemId))
                    {
                        var itemMatches = volumeCandidates
                            .Where(source => string.Equals(source.ItemId, request.ItemId, StringComparison.Ordinal))
                            .ToArray();
                        if (itemMatches.Length == 0)
                        {
                            errors.Add("O item informado é desconhecido para o volume selecionado.");
                        }
                        else
                        {
                            itemCandidates = itemMatches;
                            if (sourcePathIsValid && !itemMatches.Any(source =>
                                    TryNormalizeWindowsPath(source.SourcePath, out var knownSourcePath) &&
                                    string.Equals(knownSourcePath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                errors.Add("O caminho de origem não corresponde ao item selecionado.");
                            }
                        }
                    }
                }
            }
        }

        if (errors.Count > 0 || itemCandidates is null)
            return Invalid(errors);

        var preview = new SyntheticFileRecoveryPreview(
            request.BackupSetId!,
            request.VersionId!,
            request.VolumeId!,
            request.ItemId!,
            normalizedSourcePath,
            normalizedDestinationPath,
            SyntheticFileRecoveryOverwritePolicy.CreateCopy,
            "Prévia sintética validada para o item selecionado e destino alternativo, com política CreateCopy.",
            "Esta prévia não consulta backups reais, não verifica integridade do backup, existência ou permissões do destino e não garante recuperação. Exige validação humana; nenhuma ação foi iniciada.");

        return new SyntheticFileRecoveryPreviewResult(preview, Array.Empty<string>());
    }

    private static void RequireIdentifier(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"O campo {field} é obrigatório.");
        else if (!string.Equals(value, value!.Trim(), StringComparison.Ordinal))
            errors.Add($"O campo {field} não pode conter espaços externos.");
    }

    private static bool HasIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static SyntheticFileRecoveryPreviewResult Invalid(IReadOnlyCollection<string> errors) =>
        new(null, errors.ToArray());

    private static bool IsWindowsRoot(string normalizedPath) =>
        normalizedPath.Length == 3 && char.IsAsciiLetter(normalizedPath[0]) && normalizedPath[1] == ':' && normalizedPath[2] == '\\' ||
        normalizedPath.StartsWith("\\\\", StringComparison.Ordinal) && normalizedPath.Count(character => character == '\\') == 3;

    private static bool TryNormalizeWindowsPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) ||
            !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
            path.Contains('/') ||
            path.Any(char.IsControl))
        {
            return false;
        }

        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
                return false;

            var trimmedUncPath = path.EndsWith('\\') ? path[..^1] : path;
            if (trimmedUncPath.EndsWith('\\'))
                return false;
            var components = trimmedUncPath[2..].Split('\\');
            if (components.Length < 2 || components.Any(component => !IsValidPathComponent(component)))
                return false;

            normalizedPath = "\\\\" + string.Join('\\', components);
            return true;
        }

        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;

        if (path.Length == 3)
        {
            normalizedPath = char.ToUpperInvariant(path[0]) + @":\";
            return true;
        }

        var pathWithoutTrailingSeparator = path.EndsWith('\\') ? path[..^1] : path;
        if (pathWithoutTrailingSeparator.EndsWith('\\'))
            return false;

        var remainder = pathWithoutTrailingSeparator[3..];
        var pathComponents = remainder.Split('\\');
        if (pathComponents.Any(component => !IsValidPathComponent(component)))
            return false;

        normalizedPath = char.ToUpperInvariant(path[0]) + @":\" + string.Join('\\', pathComponents);
        return true;
    }

    private static bool IsValidPathComponent(string component) =>
        component.Length > 0 &&
        component is not "." and not ".." &&
        component[^1] is not '.' and not ' ' &&
        !component.Any(character => character < 32 || "<>:\"|?*".Contains(character));
}
