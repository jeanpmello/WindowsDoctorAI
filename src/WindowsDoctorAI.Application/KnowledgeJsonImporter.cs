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
    private static readonly Regex SafeIdentifier = new(@"\A[A-Za-z0-9._-]{1,80}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeText = new(
        @"(?:[A-Za-z]:\\|\\\\|\$\(|`|&&|\|\||[;<>]|\b(?:powershell(?:\.exe)?|cmd(?:\.exe)?|pwsh|bash|sh)\s*(?:-Command|-EncodedCommand|/c|/k)?\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex AbsolutePath = new(
        @"(?:^|[\s""'=(,:])[A-Za-z]:[\\/]|\\\\|(?<![A-Za-z0-9:/])/(?:[A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+",
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
            throw new InvalidDataException("JSON inválido ou fora do schema de conhecimento 1.0.", exception);
        }

        Validate(package);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return (package, hash);
    }

    private static void Validate(KnowledgePackage package)
    {
        if (!string.Equals(package.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException("schemaVersion deve ser exatamente '1.0'.");
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
        }
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
