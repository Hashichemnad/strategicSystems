using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Strategy.Platform.Settings.Application;

/// <summary>
/// Numbering pattern (docs/design/settings.md): literal text <c>A–Z a–z 0–9 - _ / .</c> and tokens <c>{YYYY}</c>, <c>{YY}</c>,
/// <c>{MM}</c>, <c>{UNIT}</c>, <c>{TYPE}</c>, <c>{ORG}</c> (ADR 0082), <c>{SEQ:n}</c> (exactly one, the last token). The counter partition is the rendered prefix.
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

    public static bool UsesType(string pattern) => pattern.Contains("{TYPE}", StringComparison.Ordinal);

    public static bool UsesOrg(string pattern) => pattern.Contains("{ORG}", StringComparison.Ordinal);

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
            "TYPE" => context.TypeCode is { Length: > 0 } type
                ? type.ToUpperInvariant()
                : throw new InvalidOperationException("The numbering pattern uses {TYPE}; pass the type code."),
            "ORG" => context.OrgCode is { Length: > 0 } org
                ? org.ToUpperInvariant()
                : throw new InvalidOperationException("The numbering pattern uses {ORG}; pass the organisation code."),
            _ => SeqMarker,
        });

    public static string Render(string pattern, NumberingContext context, long sequence)
    {
        var digits = Token().Matches(pattern).Single(t => t.Groups["name"].Value == "SEQ").Groups["digits"].Value;
        var width = digits.Length > 0 ? int.Parse(digits, CultureInfo.InvariantCulture) : 4;
        var number = sequence.ToString(new StringBuilder("D").Append(width).ToString(), CultureInfo.InvariantCulture);
        return Partition(pattern, context).Replace(SeqMarker, number, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a number back against the pattern (ADR 0220): its counter partition and sequence, or null when the number does not have the
    /// pattern's shape (e.g. a Salesforce auto-number of another format). Token values are matched as rendered: four / two digits for the
    /// date tokens, letters and digits for unit / type / organisation codes, digits for the sequence.
    /// </summary>
    public static (string Partition, long Sequence)? Parse(string pattern, string number)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var regex = new StringBuilder("^");
        var names = new List<string>();
        var position = 0;
        foreach (Match token in Token().Matches(pattern))
        {
            regex.Append(Regex.Escape(pattern[position..token.Index]));
            var name = token.Groups["name"].Value;
            names.Add(name);
            regex.Append(name switch
            {
                "YYYY" => @"(\d{4})",
                "YY" or "MM" => @"(\d{2})",
                "SEQ" => @"(\d{1,18})",
                _ => @"([A-Za-z0-9]+?)",
            });
            position = token.Index + token.Length;
        }

        regex.Append(Regex.Escape(pattern[position..])).Append('$');
        var match = Regex.Match(number.Trim(), regex.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        if (!match.Success)
        {
            return null;
        }

        var index = 0;
        long sequence = 0;
        var partition = Token().Replace(pattern, token =>
        {
            var value = match.Groups[++index].Value;
            if (token.Groups["name"].Value == "SEQ")
            {
                sequence = long.Parse(value, CultureInfo.InvariantCulture);
                return SeqMarker;
            }

            return value.ToUpperInvariant();
        });
        return (partition, sequence);
    }

    [GeneratedRegex(@"\{(?<name>YYYY|YY|MM|UNIT|TYPE|ORG|SEQ)(:(?<digits>[1-9]))?\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"^[A-Za-z0-9\-_/.]*$")]
    private static partial Regex Literal();
}
