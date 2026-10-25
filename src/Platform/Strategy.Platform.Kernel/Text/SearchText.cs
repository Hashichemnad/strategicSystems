using System.Globalization;
using System.Text;

namespace Strategy.Platform.Kernel.Text;

/// <summary>
/// Normalises Arabic and Latin text for search columns (CLAUDE.md §6): removes diacritics (tashkeel) and tatweel,
/// unifies alef forms, alef maqsura → ya, ta marbuta → ha, Arabic-Indic digits → ASCII, lower-cases, collapses spaces.
/// Apply the same function to the stored column and to the search term.
/// </summary>
public static class SearchText
{
    public static string Normalise(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendNormalised(builder, part);
        }

        return CollapseSpaces(builder.ToString());
    }

    private static void AppendNormalised(StringBuilder builder, string value)
    {
        foreach (var ch in value.Normalize(NormalizationForm.FormC))
        {
            switch (ch)
            {
                case 'ـ': // tatweel
                    continue;
                case >= 'ً' and <= 'ٟ': // tashkeel
                case 'ٰ': // superscript alef
                    continue;
                case 'آ' or 'أ' or 'إ' or 'ٱ': // آ أ إ ٱ
                    builder.Append('ا');
                    break;
                case 'ى': // alef maqsura
                    builder.Append('ي');
                    break;
                case 'ة': // ta marbuta
                    builder.Append('ه');
                    break;
                case >= '٠' and <= '٩':
                    builder.Append((char)('0' + (ch - '٠')));
                    break;
                case >= '۰' and <= '۹':
                    builder.Append((char)('0' + (ch - '۰')));
                    break;
                default:
                    if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                    {
                        continue; // Latin accents (é → e after FormD would be stricter; FormC keeps precomposed letters)
                    }

                    builder.Append(char.IsWhiteSpace(ch) ? ' ' : char.ToLowerInvariant(ch));
                    break;
            }
        }
    }

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousSpace = true;
        foreach (var ch in value)
        {
            if (ch == ' ')
            {
                if (!previousSpace)
                {
                    builder.Append(ch);
                }

                previousSpace = true;
            }
            else
            {
                builder.Append(ch);
                previousSpace = false;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
