using System.Globalization;
using System.Net;
using System.Text;

namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>
/// Server-side SVG charts for PDFs (report exports and dossiers): bar, stacked bar, line, pie / donut. Text is HTML-encoded; no script, no
/// external resources. Mirrored for Arabic (categories run right to left). The screen uses ECharts (ADR 0148) with the same data.
/// </summary>
public static class ChartSvg
{
    private static readonly string[] Palette = ["#1f6feb", "#2da44e", "#bf8700", "#cf222e", "#8250df", "#1b7c83", "#bc4c00", "#57606a"];

    private static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

    private static string N(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Render(string type, IReadOnlyList<string?> categories, IReadOnlyList<(string? Name, IReadOnlyList<decimal> Values)> series, bool rtl, string color = "#1f3a68")
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(series);
        if (categories.Count == 0 || series.Count == 0)
        {
            return string.Empty;
        }

        return type is ChartTypes.Pie or ChartTypes.Donut ? Pie(categories, series[0].Values, type == ChartTypes.Donut, rtl) : Axis(type, categories, series, rtl, color);
    }

    private static string Axis(string type, IReadOnlyList<string?> categories, IReadOnlyList<(string? Name, IReadOnlyList<decimal> Values)> series, bool rtl, string color)
    {
        const int width = 680;
        const int height = 260;
        const int left = 48;
        const int bottom = 54;
        const int top = 16;
        var stacked = type == ChartTypes.StackedBar;
        var max = categories.Select((_, i) => stacked ? series.Sum(s => Math.Max(0, s.Values[i])) : series.Max(s => s.Values[i])).DefaultIfEmpty(0).Max();
        max = max <= 0 ? 1 : max;
        var plotWidth = width - left - 12;
        var plotHeight = height - top - bottom;
        var slot = plotWidth / (double)categories.Count;
        double X(int i) => rtl ? left + plotWidth - ((i + 1) * slot) : left + (i * slot);
        double Y(decimal v) => top + plotHeight - (double)(v / max) * plotHeight;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height + (series.Count > 1 ? 22 : 0)}\" width=\"100%\" role=\"img\">");
        for (var g = 0; g <= 4; g++)
        {
            var v = max * g / 4;
            var y = Y(v);
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{left}\" x2=\"{width - 12}\" y1=\"{y:0.#}\" y2=\"{y:0.#}\" stroke=\"#d0d7de\" stroke-width=\"0.6\"/>")
                .Append(CultureInfo.InvariantCulture, $"<text x=\"{left - 4}\" y=\"{y + 3:0.#}\" font-size=\"8\" text-anchor=\"end\" fill=\"#57606a\">{N(v)}</text>");
        }

        if (type == ChartTypes.Line)
        {
            for (var s = 0; s < series.Count; s++)
            {
                var c = series.Count == 1 ? color : Palette[s % Palette.Length];
                var points = string.Join(' ', categories.Select((_, i) => FormattableString.Invariant($"{X(i) + (slot / 2):0.#},{Y(series[s].Values[i]):0.#}")));
                svg.Append(CultureInfo.InvariantCulture, $"<polyline fill=\"none\" stroke=\"{c}\" stroke-width=\"2\" points=\"{points}\"/>");
                for (var i = 0; i < categories.Count; i++)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{X(i) + (slot / 2):0.#}\" cy=\"{Y(series[s].Values[i]):0.#}\" r=\"2.5\" fill=\"{c}\"/>");
                }
            }
        }
        else
        {
            var barWidth = stacked || series.Count == 1 ? slot * 0.6 : slot * 0.8 / series.Count;
            for (var i = 0; i < categories.Count; i++)
            {
                decimal stack = 0;
                for (var s = 0; s < series.Count; s++)
                {
                    var c = series.Count == 1 ? color : Palette[s % Palette.Length];
                    var v = Math.Max(0, series[s].Values[i]);
                    double x;
                    double y;
                    double h;
                    if (stacked)
                    {
                        x = X(i) + (slot * 0.2);
                        y = Y(stack + v);
                        h = Y(stack) - y;
                        stack += v;
                    }
                    else
                    {
                        var offset = series.Count == 1 ? slot * 0.2 : (slot * 0.1) + ((rtl ? series.Count - 1 - s : s) * barWidth);
                        x = X(i) + offset;
                        y = Y(v);
                        h = top + plotHeight - y;
                    }

                    svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x:0.#}\" y=\"{y:0.#}\" width=\"{barWidth:0.#}\" height=\"{Math.Max(0, h):0.#}\" fill=\"{c}\"/>");
                    if (!stacked && series.Count == 1)
                    {
                        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + (barWidth / 2):0.#}\" y=\"{y - 3:0.#}\" font-size=\"8\" text-anchor=\"middle\" fill=\"#1b1f24\">{N(v)}</text>");
                    }
                }
            }
        }

        for (var i = 0; i < categories.Count; i++)
        {
            var label = categories[i] ?? "—";
            label = label.Length > 18 ? label[..17] + "…" : label;
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{X(i) + (slot / 2):0.#}\" y=\"{top + plotHeight + 14}\" font-size=\"8\" text-anchor=\"middle\" fill=\"#1b1f24\">{E(label)}</text>");
        }

        if (series.Count > 1)
        {
            var x = left;
            for (var s = 0; s < series.Count; s++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{height + 6}\" width=\"10\" height=\"10\" fill=\"{Palette[s % Palette.Length]}\"/>")
                    .Append(CultureInfo.InvariantCulture, $"<text x=\"{x + 14}\" y=\"{height + 15}\" font-size=\"9\" fill=\"#1b1f24\">{E(series[s].Name ?? "—")}</text>");
                x += 24 + (Math.Min(24, (series[s].Name ?? "—").Length) * 6);
            }
        }

        return svg.Append("</svg>").ToString();
    }

    private static string Pie(IReadOnlyList<string?> categories, IReadOnlyList<decimal> values, bool donut, bool rtl)
    {
        var total = values.Where(v => v > 0).Sum();
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 520 220\" width=\"100%\" role=\"img\">");
        if (total <= 0)
        {
            return svg.Append("</svg>").ToString();
        }

        var cx = rtl ? 410.0 : 110.0;
        const double cy = 110;
        const double r = 95;
        var angle = -Math.PI / 2;
        for (var i = 0; i < categories.Count; i++)
        {
            var v = Math.Max(0, values[i]);
            if (v == 0)
            {
                continue;
            }

            var sweep = (double)(v / total) * 2 * Math.PI;
            var end = angle + sweep;
            var large = sweep > Math.PI ? 1 : 0;
            var c = Palette[i % Palette.Length];
            if (sweep >= (2 * Math.PI) - 0.0001)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"{c}\"/>");
            }
            else
            {
                svg.Append(CultureInfo.InvariantCulture,
                    $"<path d=\"M{cx:0.#},{cy:0.#} L{cx + (r * Math.Cos(angle)):0.#},{cy + (r * Math.Sin(angle)):0.#} A{r},{r} 0 {large} 1 {cx + (r * Math.Cos(end)):0.#},{cy + (r * Math.Sin(end)):0.#} Z\" fill=\"{c}\"/>");
            }

            angle = end;
        }

        if (donut)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r * 0.55:0.#}\" fill=\"#ffffff\"/>");
        }

        var lx = rtl ? 20 : 230;
        for (var i = 0; i < Math.Min(categories.Count, 12); i++)
        {
            var y = 20 + (i * 16);
            var pct = values[i] <= 0 ? 0 : values[i] / total * 100;
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{lx}\" y=\"{y - 9}\" width=\"10\" height=\"10\" fill=\"{Palette[i % Palette.Length]}\"/>")
                .Append(CultureInfo.InvariantCulture, $"<text x=\"{lx + 14}\" y=\"{y}\" font-size=\"9\" fill=\"#1b1f24\">{E(categories[i] ?? "—")} — {N(values[i])} ({pct:0.#}%)</text>");
        }

        return svg.Append("</svg>").ToString();
    }
}
