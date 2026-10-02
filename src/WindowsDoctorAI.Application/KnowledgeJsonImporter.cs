using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Valida e importa apenas dados JSON declarativos; nunca interpreta texto como comando ou caminho.</summary>
public sealed class KnowledgeJsonImporter(IKnowledgeRepository repository)
{
    public const int MaximumPackageBytes = 512 * 1024;
    public const int MaximumRules = 500;
    private const int MaximumConditionItems = 10;
    private static readonly Regex SafeIdentifier = new(@"\A[A-Za-z0-9._-]{1,80}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeErrorCode = new(@"\A0x[0-9a-fA-F]{8}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CbsStoreCorruptionCode = new(@"(?<![A-Za-z0-9_])0x800F0831(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> GenericScannerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "*", "all", "any", "unknown", "none", "system"
    };
    private static readonly HashSet<string> GenericContextTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "*", "all", "any", "error", "error code", "code", "failure", "failed", "update",
        "windows", "setup", "install", "installation", "repair", "service", "servicing", "system", "unknown"
    };
    private static readonly Regex UnsafeText = new(
        @"(?:[A-Za-z]:\\|\\\\|\$\(|`|&&|\|\||[;<>]|\b(?:powershell(?:\.exe)?|cmd(?:\.exe)?|pwsh|bash|sh)\s*(?:-Command|-EncodedCommand|/c|/k)?\b|\b(?:script|commands?|executable)\b|(?<![A-Za-z0-9:/.-])[\w-]+\.(?:exe|bat|cmd|ps1|psm1|vbs|js|sh|com)\b|\b(?:shutdown|restart-computer|stop-computer|format|diskpart|whoami|dism|sfc|reg|sc|net|chkdsk|bcdedit|wmic|taskkill|ipconfig|systeminfo|wevtutil|powercfg|rundll32|regsvr32|python3?|node|npm|perl|ruby|cscript|wscript)\s+(?:/|-[A-Za-z]))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex AbsolutePath = new(
        @"(?:^|[\s""'=(,:])[A-Za-z]:[\\/]|\\\\|(?<![A-Za-z:/])/(?:[A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            MaxDepth = 16,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    /// <summary>Valida o pacote e mostra seus metadados sem gravar regras no banco.</summary>
    public KnowledgePackagePreview Preview(string json)
    {
        var (package, hash) = ParseAndValidate(json);
        return new KnowledgePackagePreview(package.Version, package.Source, package.Rules.Count, hash);
    }

    public async Task<KnowledgeImportResult> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var (package, hash) = ParseAndValidate(json);
        cancellationToken.ThrowIfCancellationRequested();
        await repository.SaveImportAsync(package, hash, cancellationToken).ConfigureAwait(false);
        return new KnowledgeImportResult(package.Version, package.Rules.Count, hash);
    }

    private static (KnowledgePackage Package, string Sha256) ParseAndValidate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) is 0 or > MaximumPackageBytes)
            throw new InvalidDataException($"O pacote deve ter entre 1 byte e {MaximumPackageBytes} bytes.");

        KnowledgePackage package;
        try
        {
            package = JsonSerializer.Deserialize<KnowledgePackage>(json, JsonOptions)
                ?? throw new InvalidDataException("O pacote JSON está vazio.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("JSON inválido ou fora do schema de conhecimento 1.0/1.1/1.2.", exception);
        }

        Validate(package);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return (package, hash);
    }

    private static void Validate(KnowledgePackage package)
    {
        var strictSchema = package.SchemaVersion is "1.1" or "1.2" or "1.3";
        var structuredSourceSchema = package.SchemaVersion is "1.2" or "1.3";
        var structuredEvidenceSchema = package.SchemaVersion == "1.3";
        if (!strictSchema && package.SchemaVersion != "1.0")
            throw new InvalidDataException("schemaVersion deve ser exatamente '1.0', '1.1', '1.2' ou '1.3'.");
        ValidateText(package.Version, "version", 40);
        if (!Regex.IsMatch(package.Version, @"\A[A-Za-z0-9._-]{1,40}\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException("version deve conter apenas letras, números, ponto, hífen ou sublinhado.");
        ValidateText(package.Source, "source", 160);
        if (package.Rules is null || package.Rules.Count is 0 or > MaximumRules)
            throw new InvalidDataException($"rules deve conter entre 1 e {MaximumRules} entradas.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in package.Rules)
        {
            if (rule is null || rule.Id is null || !SafeIdentifier.IsMatch(rule.Id) || rule.Version is < 1 or > 1_000_000)
                throw new InvalidDataException("Cada regra precisa de id seguro e version inteira entre 1 e 1000000.");
            if (!seen.Add($"{rule.Id}:{rule.Version}"))
                throw new InvalidDataException($"Regra duplicada: {rule.Id} v{rule.Version}.");
            ValidateText(rule.Domain, "domain", 80);
            ValidateText(rule.Title, "title", 200);
            if (!Enum.IsDefined(rule.Impact)) throw new InvalidDataException($"Impacto inválido na regra {rule.Id}.");
            ValidateList(rule.ErrorCodes, "errorCodes", rule.Id, 50, 100);
            ValidateList(rule.Symptoms, "symptoms", rule.Id, 50, 300);
            ValidateList(rule.Causes, "causes", rule.Id, 50, 1000);
            ValidateList(rule.Solutions, "solutions", rule.Id, 50, 1000);
            if (rule.Match is null && rule.ErrorCodes.Concat(rule.Symptoms).Any(value => CbsStoreCorruptionCode.IsMatch(value)))
                throw new InvalidDataException("0x800F0831 não pode usar correspondência legada por código/sintoma; exige evidência CBS tipada no schema 1.3.");
            if (rule.References is null || rule.References.Count is 0 or > 50)
                throw new InvalidDataException($"A regra {rule.Id} deve ter entre 1 e 50 referências declaradas.");
            foreach (var reference in rule.References)
            {
                if (reference is null) throw new InvalidDataException($"Referência vazia na regra {rule.Id}.");
                ValidateText(reference.Title, "reference.title", 200);
                ValidateText(reference.Url, "reference.url", 2048);
                if (!Uri.TryCreate(reference.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host))
                    throw new InvalidDataException($"A referência da regra {rule.Id} deve ser uma URL HTTPS absoluta.");
            }

            if (strictSchema)
                ValidateStrictRule(rule, structuredSourceSchema, structuredEvidenceSchema);
            else if (rule.Applicability is not null || rule.Match is not null || rule.RequiredEvidence is not null || rule.Procedure is not null)
                throw new InvalidDataException($"A regra {rule.Id} usa campos do schema 1.1, mas o pacote declara 1.0.");
        }
    }

    private static void ValidateStrictRule(KnowledgeRule rule, bool structuredSourceSchema, bool structuredEvidenceSchema)
    {
        ValidateText(rule.Applicability, $"applicability ({rule.Id})", 600);
        if (rule.Match is null)
            throw new InvalidDataException($"A regra {rule.Id} precisa de condição de match estrita no schema 1.1.");
        ValidateText(rule.Match.ExactErrorCode, $"match.exactErrorCode ({rule.Id})", 10);
        if (!SafeErrorCode.IsMatch(rule.Match.ExactErrorCode))
            throw new InvalidDataException($"A regra {rule.Id} precisa de um código exato no formato 0x seguido por oito dígitos hexadecimais.");
        ValidateRequiredConditions(rule.Match.ScannerNames, "match.scannerNames", rule.Id, 80);
        var contextTerms = rule.Match.RequiredContextTerms ?? Array.Empty<string>();
        var sourceProviders = rule.Match.RequiredSourceProviders ?? Array.Empty<string>();
        var evidenceTypes = rule.Match.RequiredEvidenceTypes ?? Array.Empty<string>();
        if (contextTerms.Count > 0)
            ValidateRequiredConditions(contextTerms, "match.requiredContextTerms", rule.Id, 120);
        if (sourceProviders.Count > 0)
        {
            if (!structuredSourceSchema)
                throw new InvalidDataException($"A regra {rule.Id} usa providers estruturados, disponíveis somente no schema 1.2.");
            ValidateRequiredConditions(sourceProviders, "match.requiredSourceProviders", rule.Id, 120);
            if (sourceProviders.Any(provider => !string.Equals(
                    DiagnosticSourceMetadata.NormalizeProvider(provider), provider, StringComparison.Ordinal)))
                throw new InvalidDataException($"A regra {rule.Id} declara provider de origem desconhecido ou não canônico.");
        }
        if (evidenceTypes.Count > 0)
        {
            if (!structuredEvidenceSchema)
                throw new InvalidDataException($"A regra {rule.Id} usa tipos de evidência estruturada, disponíveis somente no schema 1.3.");
            ValidateRequiredConditions(evidenceTypes, "match.requiredEvidenceTypes", rule.Id, 80);
            var knownEvidenceTypes = Enum.GetNames<CbsEvidenceType>();
            if (evidenceTypes.Any(value => !knownEvidenceTypes.Contains(value, StringComparer.Ordinal)))
                throw new InvalidDataException($"A regra {rule.Id} declara tipo de evidência estruturada desconhecido.");
        }
        if (contextTerms.Count == 0 && sourceProviders.Count == 0 && evidenceTypes.Count == 0)
            throw new InvalidDataException($"A regra {rule.Id} precisa declarar contexto textual, provider estruturado ou tipo de evidência específico.");
        if (rule.Match.ScannerNames.Any(value => GenericScannerNames.Contains(value))
            || contextTerms.Any(value => GenericContextTerms.Contains(value)))
            throw new InvalidDataException($"A regra {rule.Id} usa uma condição genérica; indique scanner e contexto específicos.");
        if (rule.ErrorCodes.Count != 0 || rule.Symptoms.Count != 0)
            throw new InvalidDataException($"A regra {rule.Id} não pode combinar match estrito com listas de códigos/sintomas legadas.");

        if (rule.RequiredEvidence is null || rule.RequiredEvidence.Count is 0 or > MaximumConditionItems)
            throw new InvalidDataException($"A regra {rule.Id} precisa declarar entre 1 e {MaximumConditionItems} evidências necessárias.");
        ValidateList(rule.RequiredEvidence, "requiredEvidence", rule.Id, MaximumConditionItems, 250);

        if (rule.Procedure is null)
            throw new InvalidDataException($"A regra {rule.Id} precisa declarar procedimento no schema 1.1.");
        ValidateText(rule.Procedure.DiagnosticAction, $"procedure.diagnosticAction ({rule.Id})", 1200);
        ValidateText(rule.Procedure.CorrectiveAction, $"procedure.correctiveAction ({rule.Id})", 1200);
        ValidateText(rule.Procedure.RequiredPrivilege, $"procedure.requiredPrivilege ({rule.Id})", 800);
        ValidateText(rule.Procedure.Risk, $"procedure.risk ({rule.Id})", 800);
        ValidateText(rule.Procedure.Backup, $"procedure.backup ({rule.Id})", 800);
        ValidateText(rule.Procedure.Rollback, $"procedure.rollback ({rule.Id})", 800);
        ValidateText(rule.Procedure.SourceLimitation, $"procedure.sourceLimitation ({rule.Id})", 1000);
        if (!rule.Procedure.ManualOnly || !rule.Procedure.RequiresUserConfirmation)
            throw new InvalidDataException($"A regra {rule.Id} só pode oferecer ação manual que exige confirmação explícita.");
        if (rule.Procedure.RequiresElevation && !rule.Procedure.IsModifying)
            throw new InvalidDataException($"A regra {rule.Id} não pode exigir elevação sem declarar ação modificadora.");
        if (string.Equals(rule.Match.ExactErrorCode, "0x80073712", StringComparison.OrdinalIgnoreCase)
            && (!rule.Procedure.IsModifying || !rule.Procedure.RequiresElevation
            || !rule.Procedure.Rollback.Contains("indisponível", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A regra 0x80073712 precisa permanecer privilegiada, modificadora e sem rollback disponível.");

        if (string.Equals(rule.Match.ExactErrorCode, "0x800F0831", StringComparison.OrdinalIgnoreCase)
            && (!structuredEvidenceSchema
                || evidenceTypes.Count == 0
                || !sourceProviders.Contains(DiagnosticSourceMetadata.WindowsUpdateClientProvider, StringComparer.Ordinal)
                || !rule.Match.ScannerNames.Contains("Windows Update", StringComparer.Ordinal)
                || !contextTerms.Contains("CBS marker=", StringComparer.Ordinal)
                || rule.Procedure.IsModifying
                || rule.Procedure.RequiresElevation))
            throw new InvalidDataException("A regra 0x800F0831 exige evidência CBS tipada no schema 1.3, provider WindowsUpdateClient e orientação somente diagnóstica.");
    }

    private static void ValidateRequiredConditions(IReadOnlyList<string>? values, string field, string ruleId, int maximumLength)
    {
        if (values is null || values.Count is 0 or > MaximumConditionItems)
            throw new InvalidDataException($"{field} da regra {ruleId} deve conter entre 1 e {MaximumConditionItems} itens.");
        ValidateList(values, field, ruleId, MaximumConditionItems, maximumLength);
        if (values.Any(value => !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new InvalidDataException($"{field} da regra {ruleId} não pode conter espaços externos ou itens duplicados.");
    }

    private static void ValidateList(IReadOnlyList<string>? values, string field, string ruleId, int maximumCount, int maximumLength)
    {
        if (values is null || values.Count > maximumCount)
            throw new InvalidDataException($"{field} da regra {ruleId} excede o limite de {maximumCount} itens.");
        foreach (var value in values) ValidateText(value, $"{field} ({ruleId})", maximumLength);
    }

    private static void ValidateText(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl)
            || UnsafeText.IsMatch(value) || AbsolutePath.IsMatch(value))
            throw new InvalidDataException($"Campo {field} vazio, excessivo ou contendo caminho/comando/caractere de injeção não permitido.");
    }
}

public sealed record KnowledgeImportResult(string Version, int ImportedRules, string Sha256);
public sealed record KnowledgePackagePreview(string Version, string Source, int RuleCount, string Sha256);
