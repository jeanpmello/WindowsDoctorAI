using System.Globalization;

namespace WindowsDoctorAI.Domain;

/// <summary>Parsing invariant de BuildNumber WMI sem sinais, zeros à esquerda ou formatos alternativos.</summary>
public static class OperatingSystemBuildNumber
{
    /// <summary>Retorna true somente para inteiro decimal positivo em representação canônica.</summary>
    public static bool TryParseCanonicalPositive(string? value, out long build)
    {
        build = 0;
        return value is not null
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out build)
            && build > 0
            && string.Equals(build.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
    }

    /// <summary>Preserva o texto original somente quando é um BuildNumber decimal positivo canônico.</summary>
    public static string? Normalize(string? value) => TryParseCanonicalPositive(value, out _) ? value : null;
}
