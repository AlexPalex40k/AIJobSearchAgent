using System.Text;
using System.Text.RegularExpressions;

namespace AIJobSearchAgent.Application.Services;

public static class TechnicalSkillMatcher
{
    private static readonly IReadOnlyDictionary<string, string> AliasToKey =
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
            [".net"] = "dotnet",
            ["dotnet"] = "dotnet"
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> KeyToAliases = AliasToKey
        .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<string>)group.Select(pair => pair.Key).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string skill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);

        var whitespaceNormalized = CollapseWhitespace(skill.Trim());
        return AliasToKey.TryGetValue(whitespaceNormalized, out var alias)
            ? alias
            : whitespaceNormalized;
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
        var normalizedSkill = Normalize(skill);
        var aliases = KeyToAliases.TryGetValue(normalizedSkill, out var knownAliases)
            ? knownAliases
            : [CollapseWhitespace(skill.Trim())];

        return aliases.Select(alias =>
        {
            var tokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var escapedAlias = string.Join(@"\s+", tokens.Select(Regex.Escape));
            return $@"(?<![\p{{L}}\p{{N}}_+#]){escapedAlias}(?![\p{{L}}\p{{N}}_+#])";
        }).ToArray();
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
}
