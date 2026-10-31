using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Strategy.Platform.Settings.Application;

/// <summary>
/// Numbering pattern (docs/design/settings.md): literal text <c>A–Z a–z 0–9 - _ / .</c> and tokens <c>{YYYY}</c>, <c>{YY}</c>,
/// <c>{MM}</c>, <c>{UNIT}</c>, <c>{SEQ:n}</c> (exactly one, the last token). The counter partition is the rendered prefix.
/// </summary>
public static partial class NumberingPattern
{
    public const int MaxLength = 100;
    private const string SeqMarker = "{SEQ}";

    /// <summary>Null when valid, else a message key.</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaxLength)
        {
            return "settings.errors.patternInvalid";
        }

        var tokens = Token().Matches(pattern);
        var literal = Token().Replace(pattern, string.Empty);
        if (!Literal().IsMatch(literal))
        {
            return "settings.errors.patternInvalid";
        }

        var seq = tokens.Where(t => t.Groups["name"].Value == "SEQ").ToList();
        if (seq.Count != 1 || tokens[^1].Groups["name"].Value != "SEQ")
        {
            return "settings.errors.patternSequence";
        }

        // Any brace left outside a known token is an unknown token.
        return literal.Contains('{', StringComparison.Ordinal) || literal.Contains('}', StringComparison.Ordinal)
            ? "settings.errors.patternInvalid"
            : null;
    }

    public static bool UsesUnit(string pattern) => pattern.Contains("{UNIT}", StringComparison.Ordinal);

    /// <summary>The pattern with every token but the sequence filled in: the counter partition.</summary>
    public static string Partition(string pattern, NumberingContext context) =>
        Token().Replace(pattern, match => match.Groups["name"].Value switch
        {
            "YYYY" => context.Date.Year.ToString("D4", CultureInfo.InvariantCulture),
            "YY" => (context.Date.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
            "MM" => context.Date.Month.ToString("D2", CultureInfo.InvariantCulture),
            "UNIT" => context.UnitCode is { Length: > 0 } unit
                ? unit.ToUpperInvariant()
                : throw new InvalidOperationException("The numbering pattern uses {UNIT}; pass the unit code."),
            _ => SeqMarker,
        });

    public static string Render(string pattern, NumberingContext context, long sequence)
    {
        var digits = Token().Matches(pattern).Single(t => t.Groups["name"].Value == "SEQ").Groups["digits"].Value;
        var width = digits.Length > 0 ? int.Parse(digits, CultureInfo.InvariantCulture) : 4;
        var number = sequence.ToString(new StringBuilder("D").Append(width).ToString(), CultureInfo.InvariantCulture);
        return Partition(pattern, context).Replace(SeqMarker, number, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\{(?<name>YYYY|YY|MM|UNIT|SEQ)(:(?<digits>[1-9]))?\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"^[A-Za-z0-9\-_/.]*$")]
    private static partial Regex Literal();
}
