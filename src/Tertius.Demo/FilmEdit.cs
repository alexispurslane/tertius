using System.Diagnostics;
using Tertius.Host.Scripting;

namespace Tertius.Demo;

/// <summary>
/// One film of a script, and where its frames lie in the script's video (docs/plans/blue-danube-demo.md, "The cut list").
/// The video holds one frame for each capture of the script, so frame k of the video is capture k of the run.
/// </summary>
/// <param name="Name">The film's name.</param>
/// <param name="Camera">The scene id of the camera of the last camera step before the film, or the empty string for none.</param>
/// <param name="Every">The ticks before each frame of the film.</param>
/// <param name="Frames">The film's count of frames.</param>
/// <param name="FirstFrame">The index of the film's first frame in the video: the count of the frames of the films before it.</param>
/// <param name="FirstTick">The world's tick number when the film starts, before its first ticks.</param>
/// <param name="Outlines">True when the last outlines step before the film turned the outlines on. They start off.</param>
public sealed record ScriptFilm(
    string Name,
    string Camera,
    int Every,
    int Frames,
    int FirstFrame,
    long FirstTick,
    bool Outlines
)
{
    /// <summary>
    /// The films of <paramref name="script"/>, in their order. Throws <see cref="ScriptException"/> for a script with a
    /// capture step: the edit takes only film steps for captures, so every frame of the video belongs to a film.
    /// </summary>
    public static IReadOnlyList<ScriptFilm> Of(Script script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var films = new List<ScriptFilm>();
        string camera = "";
        bool outlines = false;
        int frame = 0;
        long tick = 0;
        foreach (ScriptStep step in script.Steps)
            switch (step.Value)
            {
                case RunStep run:
                    tick += run.Ticks;
                    break;
                case CameraStep cameraStep:
                    camera = cameraStep.Entity;
                    break;
                case OutlinesStep outlinesStep:
                    outlines = outlinesStep.Show;
                    break;
                case FilmStep film:
                    films.Add(
                        new(
                            Name: film.Name,
                            Camera: camera,
                            Every: film.Every,
                            Frames: film.Frames,
                            FirstFrame: frame,
                            FirstTick: tick,
                            Outlines: outlines
                        )
                    );
                    frame += film.Frames;
                    tick += (long)film.Frames * film.Every;
                    break;
                case CaptureStep capture:
                    throw new ScriptException(
                        $"The script has the capture '{capture.Name}', and the edit takes only film steps for captures."
                    );
                case PostStep or DumpStep or null:
                    break;
                default:
                    throw new UnreachableException($"The step {step.Value} has a case that the edit does not know.");
            }

        return films;
    }

    /// <summary>
    /// Throws <see cref="ScriptException"/> unless <paramref name="rows"/> holds one row for each frame of
    /// <paramref name="films"/>, in order, each named as the runner names the frame: the film's name, a dash and the
    /// frame's index in the film in six digits. So a log of another script cannot reach the edit.
    /// </summary>
    public static void CheckRows(IReadOnlyList<ScriptFilm> films, IReadOnlyList<ReadoutRow> rows)
    {
        ArgumentNullException.ThrowIfNull(films);
        ArgumentNullException.ThrowIfNull(rows);
        int frames = films.Sum(film => film.Frames);
        if (rows.Count != frames)
            throw new ScriptException($"The log has {rows.Count} rows, and the script has {frames} frames.");
        foreach (ScriptFilm film in films)
        {
            var step = new FilmStep(film.Name, film.Frames, film.Every);
            for (int k = 0; k < film.Frames; k++)
            {
                string name = step.FrameName(k);
                if (rows[film.FirstFrame + k].Frame != name)
                    throw new ScriptException(
                        $"Row {film.FirstFrame + k} of the log is named '{rows[film.FirstFrame + k].Frame}', and the frame of the script is '{name}'."
                    );
            }
        }
    }
}

/// <summary>Frames that the edit drops from the head and the tail of one film.</summary>
/// <param name="Film">The film's name.</param>
/// <param name="Head">How many of the film's first frames the edit drops, 0 or more.</param>
/// <param name="Tail">How many of the film's last frames the edit drops, 0 or more.</param>
public sealed record EditTrim(string Film, int Head, int Tail);

/// <summary>The frames of one film that the edit keeps: <paramref name="Count"/> frames of the video from
/// <paramref name="From"/> on.</summary>
public sealed record Stretch(ScriptFilm Film, int From, int Count)
{
    /// <summary>The frames per second of the video, and so the frames of one second.</summary>
    public const int FramesPerSecond = 60;

    /// <summary>
    /// One stretch for each of <paramref name="films"/>, in their order, less the frames of <paramref name="trims"/>.
    /// Throws <see cref="ArgumentException"/> for a trim of a film that <paramref name="films"/> does not have, a trim of
    /// fewer than 0 frames, and a trim that leaves less than one second of its film.
    /// </summary>
    public static IReadOnlyList<Stretch> Of(IReadOnlyList<ScriptFilm> films, IReadOnlyList<EditTrim> trims)
    {
        ArgumentNullException.ThrowIfNull(films);
        ArgumentNullException.ThrowIfNull(trims);
        foreach (EditTrim trim in trims)
        {
            if (films.All(film => film.Name != trim.Film))
                throw new ArgumentException(
                    $"The trim names the film '{trim.Film}', which the script does not have.",
                    nameof(trims)
                );
            if (trim.Head < 0 || trim.Tail < 0)
                throw new ArgumentException(
                    $"The trim of the film '{trim.Film}' drops fewer than 0 frames.",
                    nameof(trims)
                );
        }

        var stretches = new List<Stretch>(films.Count);
        foreach (ScriptFilm film in films)
        {
            EditTrim? trim = trims.SingleOrDefault(t => t.Film == film.Name);
            if (trim == null)
            {
                stretches.Add(new(film, film.FirstFrame, film.Frames));
                continue;
            }

            int count = film.Frames - trim.Head - trim.Tail;
            if (count < FramesPerSecond)
                throw new ArgumentException(
                    $"The trim of the film '{film.Name}' leaves {count} frames, less than one second.",
                    nameof(trims)
                );
            stretches.Add(new(film, film.FirstFrame + trim.Head, count));
        }

        return stretches;
    }
}

/// <summary>A part of the sequence: its title shows over the first 4 s of the stretch of its first film.</summary>
/// <param name="FirstFilm">The name of the part's first film.</param>
/// <param name="Title">The part's title.</param>
/// <param name="Subtitle">One sentence under the title.</param>
public sealed record EditPart(string FirstFilm, string Title, string Subtitle);

/// <summary>A check panel that shows over the stretches from the film <paramref name="FirstFilm"/> through the film
/// <paramref name="LastFilm"/>.</summary>
/// <param name="FirstFilm">The name of the span's first film. The panel measures from the first frame of this film.</param>
/// <param name="LastFilm">The name of the span's last film.</param>
/// <param name="Panel">The lines of the panel at each frame.</param>
public sealed record PanelSpan(string FirstFilm, string LastFilm, CheckPanel Panel);

/// <summary>One line of a title card, in Avenir Next.</summary>
/// <param name="Text">The line's text, or the empty string for a gap.</param>
/// <param name="Size">The size of the font, in pixels of a picture 720 pixels high.</param>
/// <param name="Gray">True for gray text, false for white.</param>
public sealed record CardLine(string Text, int Size, bool Gray = false);

/// <summary>A title card: black, with its lines centered, for <paramref name="Seconds"/> seconds.</summary>
public sealed record TitleCard(double Seconds, IReadOnlyList<CardLine> Lines)
{
    /// <summary>
    /// The frames that the card lasts in the video: its seconds at 60 frames per second, rounded to the nearest frame. The
    /// card's subtitles and its black frames both take this count.
    /// </summary>
    public int Frames => (int)Math.Round(Seconds * Stretch.FramesPerSecond);
}

/// <summary>
/// The cut list of an edit (docs/plans/blue-danube-demo.md, "The cut list"): every film of the script becomes one stretch
/// in the script's order, less its trims, with the parts' titles, the check panels, and a card before and after.
/// </summary>
public sealed record CutList(
    IReadOnlyList<EditPart> Parts,
    IReadOnlyList<EditTrim> Trims,
    IReadOnlyList<PanelSpan> Panels,
    TitleCard Opening,
    TitleCard Closing
);
