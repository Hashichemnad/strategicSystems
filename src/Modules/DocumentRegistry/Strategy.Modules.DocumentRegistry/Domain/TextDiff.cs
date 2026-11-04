namespace Strategy.Modules.DocumentRegistry.Domain;

/// <summary>A run of the comparison: <c>same</c>, <c>added</c> or <c>removed</c> text.</summary>
public sealed record DiffPart(string Kind, string Text);

/// <summary>
/// Word-level comparison of two versions' extracted text (longest common subsequence, in-house, deterministic). Large texts are compared line by
/// line first so the table stays small.
/// </summary>
public static class TextDiff
{
    public const int MaxTokens = 4000;

    public static IReadOnlyList<DiffPart> Compare(string? from, string? to)
    {
        var a = Tokens(from ?? string.Empty);
        var b = Tokens(to ?? string.Empty);
        if (a.Count > MaxTokens || b.Count > MaxTokens)
        {
            a = Lines(from ?? string.Empty);
            b = Lines(to ?? string.Empty);
            if (a.Count > MaxTokens || b.Count > MaxTokens)
            {
                a = [.. a.Take(MaxTokens)];
                b = [.. b.Take(MaxTokens)];
            }
        }

        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var parts = new List<DiffPart>();
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (a[x] == b[y])
            {
                Add(parts, "same", a[x]);
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                Add(parts, "removed", a[x++]);
            }
            else
            {
                Add(parts, "added", b[y++]);
            }
        }

        while (x < a.Count)
        {
            Add(parts, "removed", a[x++]);
        }

        while (y < b.Count)
        {
            Add(parts, "added", b[y++]);
        }

        return parts;
    }

    public static (int Added, int Removed) Count(IReadOnlyList<DiffPart> parts) =>
        (parts.Where(p => p.Kind == "added").Sum(p => p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
            parts.Where(p => p.Kind == "removed").Sum(p => p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));

    private static List<string> Tokens(string text) =>
        [.. text.Replace("\r", string.Empty, StringComparison.Ordinal).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)];

    private static List<string> Lines(string text) => [.. text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')];

    private static void Add(List<DiffPart> parts, string kind, string token)
    {
        if (parts.Count > 0 && parts[^1].Kind == kind)
        {
            parts[^1] = parts[^1] with { Text = parts[^1].Text + " " + token };
        }
        else
        {
            parts.Add(new DiffPart(kind, token));
        }
    }
}
