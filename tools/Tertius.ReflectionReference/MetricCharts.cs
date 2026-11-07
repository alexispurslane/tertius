using System.Globalization;
using System.Text;

namespace Tertius.ReflectionReference;

internal sealed record MetricChartCase(string Material, MetricReference Basis, double[] Off, double[] On);

internal static class MetricCharts
{
    private static readonly int[] Rows = [260, 320, 380];
    private static readonly string[] Regions = ["full", "outer", "tails"];
    private static readonly double[] Fractions = [0.1, 0.5, 0.9];
    private static readonly string[] Colors = ["var(--chart-1,#5599dd)", "var(--chart-2,#dd9955)", "var(--chart-3,#66bb99)"];

    public static string Render(List<MetricChartCase> cases)
    {
        var html = new StringBuilder("""
            <!doctype html><html><head><meta charset="utf-8"><style>
            :root{font-family:var(--font-sans,system-ui);color:var(--foreground,#ddd)}
            body{margin:0}h2{font-size:18px;margin:20px 0 8px}h3{font-size:14px;margin:0 0 8px}
            p{font-size:12px;color:var(--muted-foreground,#aaa);line-height:1.45}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
            .panel{background:var(--card,#202126);padding:16px;border-radius:var(--radius,10px)}svg{width:100%;height:170px;overflow:visible}
            svg text{font:10px var(--font-sans,system-ui);fill:var(--muted-foreground,#aaa)}.legend{font-size:12px;display:flex;gap:16px;flex-wrap:wrap}
            table{font-size:12px;width:100%;border-collapse:collapse}td,th{text-align:right;padding:5px 3px}td:first-child,th:first-child{text-align:left}
            @media(max-width:520px){.grid{grid-template-columns:1fr}}
            </style></head><body><div class="legend"><span style="color:var(--chart-1,#5599dd)">Reference2048</span><span style="color:var(--chart-2,#dd9955)">OFF</span><span style="color:var(--chart-3,#66bb99)">ON</span></div>
            <p>Retained native1280×720 data; original80×160crop; radiance before display/FSR. Same axes and zero background for every curve in each plot. No fitted amplitude. Dashed lines are the fixed reference10/50/90%levels. Profiles are aggregates or the stated original scanlines, not new samples.</p>
            """);
        foreach (MetricChartCase item in cases)
        {
            MetricComparison off = item.Basis.Compare(item.Basis.Reference, item.Off);
            MetricComparison on = item.Basis.Compare(item.Basis.Reference, item.On);
            html.Append($"<h2>{item.Material} · outgoing visible-plate specular</h2><div class=panel><table><tr><th>Observable</th><th>OFF/reference</th><th>ON/reference</th></tr>");
            foreach (string region in Regions)
            {
                MetricRegionResult before = off.Regions.Single(value => value.Name == region);
                MetricRegionResult after = on.Regions.Single(value => value.Name == region);
                html.Append($"<tr><td>{region} missing light</td><td>{Percent(before.Totals.MissingFraction)}</td><td>{Percent(after.Totals.MissingFraction)}</td></tr>");
                html.Append($"<tr><td>{region} extra light</td><td>{Percent(before.Totals.ExcessFraction)}</td><td>{Percent(after.Totals.ExcessFraction)}</td></tr>");
                html.Append($"<tr><td>{region} eligible rows /160</td><td>{before.ValidRows}</td><td>{after.ValidRows}</td></tr>");
                html.Append($"<tr><td>{region} band / whole-reference light</td><td>{Percent(before.ReferenceCoverageFraction)}</td><td>{Percent(after.ReferenceCoverageFraction)}</td></tr>");
            }
            html.Append("</table></div><div class=grid>");
            html.Append(Profile(
                title: "Column-mean profile · aggregate", origin: item.Basis.Crop.Left,
                values: [item.Basis.ColumnMean(item.Basis.Reference), item.Basis.ColumnMean(item.Off), item.Basis.ColumnMean(item.On)]
            ));
            foreach (int pixelY in Rows)
            {
                int row = pixelY - item.Basis.Crop.Top;
                html.Append(Profile(title: $"Horizontal scanline y={pixelY}", origin: item.Basis.Crop.Left, values: [item.Basis.Row(item.Basis.Reference, row), item.Basis.Row(item.Off, row), item.Basis.Row(item.On, row)]));
            }
            html.Append("</div>");
        }
        html.Append("<p>Band denominators are fixed reference light, not watts. Band validity and radiance coverage are retained per row in region-rows.csv.gz. Every pixel remains in full-crop totals. Missing and extra light cannot cancel. One-bounce reference versus live source-history feedback measures whole-pipeline deviation, not isolated limiter error. Reference spread and32correlated frames are not accuracy bounds or quality acceptance.</p></body></html>");
        return html.ToString();
    }

    private static string Profile(string title, int origin, double[][] values)
    {
        const double left = 45;
        const double top = 12;
        const double width = 245;
        const double height = 115;
        double peak = values.SelectMany(value => value).Max();
        if (peak == 0)
            peak = 1;
        double ProjectX(double coordinate) => left + coordinate / (values[0].Length - 1) * width;
        double ProjectY(double value) => top + height - value / peak * height;
        var svg = new StringBuilder($"<div class=panel><h3>{title}</h3><svg viewBox='0 0 310 165' role=img aria-label='{title}'>");
        svg.Append($"<path d='M45 12V127H290' fill=none stroke='var(--border,#555)'/>");
        double amplitude = values[0].Max();
        foreach (double fraction in Fractions)
        {
            string coordinate = Number(ProjectY(amplitude * fraction));
            svg.Append($"<path d='M45 {coordinate}H290' stroke='var(--border,#555)' stroke-dasharray='3 3' fill=none/>");
        }
        for (int candidate = 0; candidate < values.Length; candidate++)
        {
            string points = string.Join(' ', values[candidate].Select((value, index) => Number(ProjectX(index)) + "," + Number(ProjectY(value))));
            svg.Append($"<polyline points='{points}' fill=none stroke='{Colors[candidate]}' stroke-width='1.6'/>");
        }
        svg.Append($"<text x=41 y=130 text-anchor=end>0</text><text x=41 y=16 text-anchor=end>{Number(peak)}</text><text x=45 y=144>{Number(origin + 0.5)}</text><text x=290 y=144 text-anchor=end>{Number(origin + values[0].Length - 0.5)}</text><text x=165 y=162 text-anchor=middle>original x · pixels; vertical radiance W/(m²sr)</text></svg></div>");
        return svg.ToString();
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Percent(double? fraction) => fraction.HasValue ? (fraction.Value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%" : "undefined";
}
