using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Tertius.Demo;

/// <summary>
/// Where the parts of an edit lie in its final video, for a score that follows the picture (docs/plans/blue-danube-demo.md,
/// "The cue sheet"). The edit writes it as JSON beside the video. The final video is the opening card, then the kept frames
/// of each film in the script's order, then the closing card, at <see cref="FramesPerSecond"/>. Every time is in seconds
/// from the video's first frame, and it is a frame of the video: frame n of the video starts at n / 60 s, so 60 times
/// each time is a whole number.
/// </summary>
/// <param name="FramesPerSecond">The frames per second of the video, 60.</param>
/// <param name="Length">The length of the video, in seconds: the end of the closing card.</param>
/// <param name="Opening">When the opening card shows.</param>
/// <param name="Films">Each film of the script, in its order.</param>
/// <param name="Parts">When each part's title starts, in the order of the cut list.</param>
/// <param name="Closing">When the closing card shows.</param>
public sealed record CueSheet(int FramesPerSecond, double Length, CardCue Opening, IReadOnlyList<FilmCue> Films, IReadOnlyList<PartCue> Parts, CardCue Closing)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>
    /// The cue sheet of the video that the edit makes of <paramref name="stretches"/> with <paramref name="cuts"/>. Each card
    /// lasts its seconds rounded to whole frames, as its subtitles do. Throws <see cref="ArgumentException"/> for a part that
    /// names a film that <paramref name="stretches"/> does not have.
    /// </summary>
    public static CueSheet Of(IReadOnlyList<Stretch> stretches, CutList cuts)
    {
        ArgumentNullException.ThrowIfNull(stretches);
        ArgumentNullException.ThrowIfNull(cuts);
        int opening = Frames(cuts.Opening);
        var films = new List<FilmCue>(stretches.Count);
        int frame = opening;
        foreach (Stretch stretch in stretches)
        {
            films.Add(new FilmCue(stretch.Film.Name, stretch.From, stretch.From + stretch.Count - 1, Time(frame), Time(frame + stretch.Count)));
            frame += stretch.Count;
        }
        List<PartCue> parts =
        [
            .. cuts.Parts.Select(part => new PartCue(part.Title, part.FirstFilm,
                (films.FirstOrDefault(film => film.Name == part.FirstFilm) ?? throw new ArgumentException($"A part of the cut list names the film '{part.FirstFilm}', which the script does not have.", nameof(cuts))).Start)),
        ];
        int end = frame + Frames(cuts.Closing);
        return new CueSheet(Stretch.FramesPerSecond, Time(end), new CardCue(0, Time(opening)), films, parts, new CardCue(Time(frame), Time(end)));
    }

    /// <summary>The sheet as indented JSON, with its names in camel case.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    /// <summary>The frames of <paramref name="card"/>: its seconds at 60 frames per second, rounded as its subtitles round them.</summary>
    private static int Frames(TitleCard card) => (int)Math.Round(card.Seconds * Stretch.FramesPerSecond);

    /// <summary>The time of frame <paramref name="frame"/> of the video, in seconds.</summary>
    private static double Time(int frame) => frame / (double)Stretch.FramesPerSecond;
}

/// <summary>One film of the script in the final video.</summary>
/// <param name="Name">The film's name.</param>
/// <param name="FirstFrame">The index of the film's first kept frame in the master, after its trims.</param>
/// <param name="LastFrame">The index of the film's last kept frame in the master.</param>
/// <param name="Start">When the film's first kept frame shows, in seconds of the final video.</param>
/// <param name="End">When the film's last kept frame stops showing, in seconds: the start of the next film, or of the closing card.</param>
public sealed record FilmCue(string Name, int FirstFrame, int LastFrame, double Start, double End);

/// <summary>A part's title in the final video.</summary>
/// <param name="Title">The part's title.</param>
/// <param name="Film">The name of the part's first film.</param>
/// <param name="Start">When the title starts to show, in seconds: the start of its first film.</param>
public sealed record PartCue(string Title, string Film, double Start);

/// <summary>When a title card shows, in seconds of the final video.</summary>
/// <param name="Start">The card's first frame.</param>
/// <param name="End">The end of the card's last frame.</param>
public sealed record CardCue(double Start, double End);
