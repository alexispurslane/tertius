using System.Globalization;
using System.Text;

namespace Tertius.Demo;

/// <summary>
/// The subtitle files of the demo edit, in the ASS format that libass draws in ffmpeg (docs/plans/blue-danube-demo.md,
/// "The readouts on the frame"). Each file is drawn at 1280 by 720, and libass scales it to the video's size. Frame k of the
/// master shows from k / 60 s to (k + 1) / 60 s, each rounded down to the hundredth, the resolution of ASS times. The edit
/// sets the time of frame k to exactly k / 60 s, so every frame shows its own events.
/// </summary>
public static class EditSubtitles
{
    /// <summary>How long a part's title shows, in frames: 4 s.</summary>
    public const int TitleFrames = 4 * Stretch.FramesPerSecond;

    private const string White = "&HF2F2F2&",
        Gray = "&HA0A0A0&",
        Amber = "&H00B0FF&";

    /// <summary>The legend of the frame outlines: the color of each kind of frame, from ARCHITECTURE.md, "The overlay pass", in ASS order (blue, green, red).</summary>
    private static readonly (string Color, string Kind)[] Legend =
    [
        ("&HFAA77A&", "a frame's bubble"),
        ("&HB48FE5&", "falling frame"),
        ("&H58B4C9&", "following frame"),
        ("&HB6C14F&", "resting frame"),
        ("&HF08CB5&", "nesting frame"),
    ];

    /// <summary>The time of frame <paramref name="frame"/> of a video at 60 frames per second, rounded down to the hundredth:
    /// <c>h:mm:ss.cc</c>.</summary>
    public static string Time(int frame)
    {
        long centis = frame * 100L / Stretch.FramesPerSecond;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1:00}:{2:00}.{3:00}",
            centis / 360_000,
            centis / 6_000 % 60,
            centis / 100 % 60,
            centis % 100
        );
    }

    /// <summary>
    /// The subtitles of the master's kept frames: for each frame of <paramref name="stretches"/>, its readouts and its
    /// sim clock with the rate of its film, from its row of <paramref name="rows"/>, the row of the same index. Also the
    /// part titles, the check panels and the legend of <paramref name="cuts"/>. Throws <see cref="ArgumentException"/> for
    /// a part or a panel that names a film that <paramref name="stretches"/> does not have.
    /// </summary>
    public static string Film(IReadOnlyList<Stretch> stretches, IReadOnlyList<ReadoutRow> rows, CutList cuts)
    {
        ArgumentNullException.ThrowIfNull(stretches);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cuts);
        var text = new StringBuilder(Header());
        foreach (Stretch stretch in stretches)
        {
            for (int frame = stretch.From; frame < stretch.From + stretch.Count; frame++)
            {
                ReadoutRow row = rows[frame];
                Event(text: text, from: frame, to: frame + 1, style: "Readout", body: Boxed(ReadoutText.Lines(row)));
                string rate = ReadoutText.Rate(stretch.Film.Every);
                Event(
                    text: text,
                    from: frame,
                    to: frame + 1,
                    style: "Clock",
                    body: Boxed([ReadoutText.Clock(row.Tick)])
                        + "\\N"
                        + Colored(stretch.Film.Every == 1 ? Gray : Amber, Boxed([rate]))
                );
            }

            if (stretch.Film.Outlines)
                Event(
                    text: text,
                    from: stretch.From,
                    to: stretch.From + stretch.Count,
                    style: "Legend",
                    body: LegendText()
                );
        }

        foreach (EditPart part in cuts.Parts)
        {
            Stretch first = Find(stretches, part.FirstFilm, "part");
            Event(
                text: text,
                from: first.From,
                to: first.From + TitleFrames,
                style: "Title",
                body: $"{{\\fad(600,600)}}{Plain(part.Title)}\\N{{\\fs24\\c{Gray}}}{Plain(part.Subtitle)}"
            );
        }

        foreach (PanelSpan span in cuts.Panels)
        {
            int start = stretches.ToList().IndexOf(Find(stretches, span.FirstFilm, "panel")),
                end = stretches.ToList().IndexOf(Find(stretches, span.LastFilm, "panel"));
            ReadoutRow first = rows[stretches[start].Film.FirstFrame];
            for (int index = start; index <= end; index++)
            for (int frame = stretches[index].From; frame < stretches[index].From + stretches[index].Count; frame++)
                Event(
                    text: text,
                    from: frame,
                    to: frame + 1,
                    style: "Panel",
                    body: Boxed(span.Panel(first, rows[frame]))
                );
        }

        return text.ToString();
    }

    /// <summary>
    /// The subtitles of a title card: its lines, centered, from 0 s for its seconds. The script turns wrapping off for
    /// the readouts, but a card line wraps (<c>\q0</c>, smart wrapping) so that it stays inside the card's margins in
    /// any font: the fonts come from macOS, and on Linux libass draws a fallback font whose line can be wider.
    /// </summary>
    public static string Card(TitleCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var text = new StringBuilder(Header());
        string lines =
            "{\\q0}"
            + string.Join(
                "\\N",
                card.Lines.Select(line => $"{{\\fs{line.Size}\\c{(line.Gray ? Gray : White)}}}{Plain(line.Text)}")
            );
        Event(text: text, from: 0, to: card.Frames, style: "Card", body: lines);
        return text.ToString();
    }

    private static Stretch Find(IReadOnlyList<Stretch> stretches, string film, string what) =>
        stretches.FirstOrDefault(stretch => stretch.Film.Name == film)
        ?? throw new ArgumentException(
            $"A {what} of the cut list names the film '{film}', which the script does not have.",
            nameof(film)
        );

    private static void Event(StringBuilder text, int from, int to, string style, string body) =>
        text.Append(CultureInfo.InvariantCulture, $"Dialogue: 0,{Time(from)},{Time(to)},{style},,0,0,0,,{body}\n");

    /// <summary>Lines for a box of monospaced text: braces kept out of the override codes, and every space a hard space, which libass never drops.</summary>
    private static string Boxed(IEnumerable<string> lines) =>
        string.Join("\\N", lines.Select(line => Plain(line).Replace(" ", "\\h", StringComparison.Ordinal)));

    private static string Colored(string color, string body) => $"{{\\c{color}}}{body}";

    /// <summary>Text with its braces turned into parentheses, so that no text starts an override code, and no backslash.</summary>
    private static string Plain(string text) => text.Replace('{', '(').Replace('}', ')').Replace('\\', '/');

    private static string LegendText() =>
        Boxed(["FRAMES"])
        + "\\N"
        + string.Join(
            "\\N",
            Legend.Select(entry => $"{{\\c{entry.Color}}}●{{\\c{White}}}" + Boxed([" " + entry.Kind]))
        );

    /// <summary>The script info and the styles, at 1280 by 720. A box style draws its text on a black box at 60 %.</summary>
    private static string Header() =>
        """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 1280
            PlayResY: 720
            WrapStyle: 2
            ScaledBorderAndShadow: yes

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Readout,Menlo,22,&H00F2F2F2,&H00F2F2F2,&H66000000,&H66000000,0,0,0,0,100,100,0,0,3,5,0,7,24,24,20,1
            Style: Clock,Menlo,22,&H00F2F2F2,&H00F2F2F2,&H66000000,&H66000000,0,0,0,0,100,100,0,0,3,5,0,9,24,24,20,1
            Style: Panel,Menlo,17,&H00F2F2F2,&H00F2F2F2,&H66000000,&H66000000,0,0,0,0,100,100,0,0,3,5,0,3,24,24,20,1
            Style: Legend,Menlo,18,&H00F2F2F2,&H00F2F2F2,&H66000000,&H66000000,0,0,0,0,100,100,0,0,3,5,0,7,24,24,168,1
            Style: Title,Avenir Next,44,&H00F2F2F2,&H00F2F2F2,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,0,2,1,40,40,36,1
            Style: Card,Avenir Next,32,&H00F2F2F2,&H00F2F2F2,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,5,80,80,40,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text

            """.Replace("\r\n", "\n", StringComparison.Ordinal);
}
