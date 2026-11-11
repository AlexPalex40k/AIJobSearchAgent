using System.Text;
using System.Text.RegularExpressions;

namespace AIJobSearchAgent.Application.Services;

public static class TechnicalSkillMatcher
{
    private static readonly IReadOnlyDictionary<string, string> AliasToFamily =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "csharp",
            ["c sharp"] = "csharp",
            ["csharp"] = "csharp",
            ["ef core"] = "entityframeworkcore",
            ["entity framework core"] = "entityframeworkcore",
            ["entity-framework core"] = "entityframeworkcore",
            ["asp.net core"] = "aspnetcore",
            ["asp net core"] = "aspnetcore",
            ["aspnet core"] = "aspnetcore",
            ["asp.net-core"] = "aspnetcore",
            ["microsoft sql server"] = "sqlserver",
            ["ms sql server"] = "sqlserver",
            ["ms sql"] = "sqlserver",
            ["mssql"] = "sqlserver",
            ["sql server"] = "sqlserver",
            ["microsoft .net framework"] = "dotnetframework",
            ["microsoft dotnet framework"] = "dotnetframework",
            [".net framework"] = "dotnetframework",
            ["dotnet framework"] = "dotnetframework",
            ["microsoft .net core"] = "dotnetcore",
            ["microsoft dotnet core"] = "dotnetcore",
            [".net core"] = "dotnetcore",
            ["dotnet core"] = "dotnetcore",
            [".net"] = "dotnet",
            ["dotnet"] = "dotnet"
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FamilyToAliases = AliasToFamily
        .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<string>)group
                .Select(pair => pair.Key)
                .OrderByDescending(alias => alias.Length)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<KeyValuePair<string, string>> AliasesByLength = AliasToFamily
        .OrderByDescending(pair => pair.Key.Length)
        .ToArray();

    private static readonly HashSet<string> VersionAwareFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "aspnetcore",
        "dotnet",
        "dotnetcore",
        "dotnetframework",
        "entityframeworkcore",
        "sqlserver"
    };

    public static string Normalize(string skill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);

        var identity = Parse(skill);
        return identity.Versions.Count == 0
            ? identity.Family
            : $"{identity.Family}@{string.Join('|', identity.Versions)}";
    }

    public static bool IsMatch(string candidateSkill, string requiredSkill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateSkill);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredSkill);

        var candidate = Parse(candidateSkill);
        var requirement = Parse(requiredSkill);
        if (!string.Equals(candidate.Family, requirement.Family, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requirement.Versions.Count == 0)
        {
            return true;
        }

        return candidate.Versions.Count > 0 &&
            candidate.Versions.Intersect(requirement.Versions, StringComparer.OrdinalIgnoreCase).Any();
    }

    public static string? FindMatchingCandidate(
        string requiredSkill,
        IEnumerable<string> candidateSkills)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredSkill);
        ArgumentNullException.ThrowIfNull(candidateSkills);

        return candidateSkills.FirstOrDefault(candidateSkill =>
            !string.IsNullOrWhiteSpace(candidateSkill) && IsMatch(candidateSkill, requiredSkill));
    }

    public static bool IsMentioned(string skill, string vacancyDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);
        ArgumentNullException.ThrowIfNull(vacancyDescription);

        return GetMentionPatterns(skill).Any(pattern => Regex.IsMatch(
            vacancyDescription,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)));
    }

    internal static IReadOnlyList<string> GetMentionPatterns(string skill)
    {
        var identity = Parse(skill);
        var aliases = FamilyToAliases.TryGetValue(identity.Family, out var knownAliases)
            ? knownAliases
            : [CollapseWhitespace(skill.Trim())];

        return aliases.Select(alias =>
        {
            var tokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var escapedAlias = string.Join(@"\s+", tokens.Select(Regex.Escape));
            var boundedAlias = $@"(?<![\p{{L}}\p{{N}}_+#]){escapedAlias}(?![\p{{L}}\p{{N}}_+#])";
            if (identity.Versions.Count == 0)
            {
                return boundedAlias;
            }

            var versionAlternatives = string.Join(
                '|',
                identity.Versions.Select(version =>
                    $@"(?<![\d.]){Regex.Escape(version)}(?![\d.])"));
            return $@"{boundedAlias}(?=[^;\r\n]{{0,30}}(?:{versionAlternatives}))";
        }).ToArray();
    }

    private static SkillIdentity Parse(string skill)
    {
        var normalized = CollapseWhitespace(skill.Trim());
        foreach (var (alias, family) in AliasesByLength)
        {
            if (string.Equals(normalized, alias, StringComparison.OrdinalIgnoreCase))
            {
                return new SkillIdentity(family, []);
            }

            if (!VersionAwareFamilies.Contains(family) ||
                !normalized.StartsWith($"{alias} ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = normalized[(alias.Length + 1)..];
            if (TryParseVersions(suffix, out var versions))
            {
                return new SkillIdentity(family, versions);
            }
        }

        return new SkillIdentity(normalized, []);
    }

    private static bool TryParseVersions(
        string suffix,
        out IReadOnlyList<string> versions)
    {
        var parts = Regex.Split(
            suffix,
            @"\s*(?:/|\bor\b)\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var parsed = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var value = Regex.Replace(
                part.Trim(),
                @"^(?:version\s+|v\s*)",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!Regex.IsMatch(value, @"^\d+(?:\.\d+){0,2}$", RegexOptions.CultureInvariant))
            {
                versions = [];
                return false;
            }

            if (!parsed.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                parsed.Add(value);
            }
        }

        versions = parsed;
        return parsed.Count > 0;
    }

    private static string CollapseWhitespace(string value)
    {
        var result = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    result.Append(' ');
                }

                previousWasWhitespace = true;
            }
            else
            {
                result.Append(char.ToLowerInvariant(character));
                previousWasWhitespace = false;
            }
        }

        return result.ToString();
    }

    private sealed record SkillIdentity(string Family, IReadOnlyList<string> Versions);
}
