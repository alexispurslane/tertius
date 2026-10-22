using Tertius.Host;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// A moment of a run that a <see cref="FilmSection"/> waits for: true once <paramref name="world"/>, whose scene ids are
/// <paramref name="scene"/>, has reached it. <see cref="FilmScript.Generate"/> asks it between ticks: at the start of the
/// section and after each of its frames. It only reads the world.
/// </summary>
public delegate bool Cue(World world, SceneInstance scene);

/// <summary>
/// One part of a film script: a film of its own, from one camera, with one number of ticks between its frames. The section
/// starts where the section before it ends. It posts <paramref name="Posts"/>, films until <paramref name="Until"/> holds
/// and then for <paramref name="Hold"/> ticks more, and ends at the first frame at or after that moment.
/// </summary>
/// <param name="Name">The name of the section's film, so its frames are named <c>name-000000</c> and on. A scene id of at most
/// <see cref="FilmStep.MaxNameLength"/> characters.</param>
/// <param name="Camera">The scene id of the camera that films the section.</param>
/// <param name="Every">How many ticks the section runs before each frame, at least 1. Above 1, the video of the section is a time-lapse.</param>
/// <param name="Until">The moment that the section waits for. <see cref="Now"/> holds at once, so the section films only its hold.</param>
/// <param name="Hold">How many ticks the section goes on after its cue holds, 0 or more.</param>
/// <param name="Posts">The events that the section posts at its start, before its first tick, or null for none.</param>
/// <param name="Limit">How many ticks of the section its cue may take before the generator gives up, at least 1.</param>
/// <param name="Outlines">True when the section's frames show the frame outlines (<c>World.ShowFrameOutlines</c>). They start off.</param>
public sealed record FilmSection(
    string Name,
    string Camera,
    int Every,
    Cue Until,
    int Hold = 0,
    IReadOnlyList<PostStep>? Posts = null,
    int Limit = FilmSection.DefaultLimit,
    bool Outlines = false)
{
    /// <summary>The <see cref="Limit"/> of a section that names none: 10 minutes of the engine's 60 ticks per second.</summary>
    public const int DefaultLimit = 36_000;

    /// <summary>A cue that holds at once.</summary>
    public static Cue Now { get; } = (_, _) => true;
}

/// <summary>
/// Writes a script of film steps for one unbroken run of a scene, section by section (docs/plans/blue-danube-demo.md,
/// "The long script"). A script is a fixed list of steps, so the generator first rehearses the run: it builds the scene in
/// a new world, as the command does, and runs each section's ticks until its cue holds. The engine is deterministic, so a
/// run of the script reaches each cue at the same tick.
/// </summary>
public static class FilmScript
{
    /// <summary>
    /// The script of <paramref name="sections"/> in <paramref name="scene"/>, with captures of the size of
    /// <paramref name="window"/>. Each section becomes a camera step, its posts, and one film step whose frames end at
    /// the first frame at or after the moment that the section's cue holds, plus its hold, with at least one frame. A
    /// section whose <see cref="FilmSection.Outlines"/> differs from the section before it, or from off for the first one,
    /// starts with an outlines step.
    /// Throws <see cref="ScriptException"/> for a section that a run cannot take, such as a camera that the scene does not
    /// have, and <see cref="InvalidOperationException"/> for a cue that does not hold within its section's limit.
    /// </summary>
    public static Script Generate(Scene scene, ScriptWindow window, IReadOnlyList<FilmSection> sections)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(sections);
        // As the command makes the world of a headless run, but with no puffs. Gameplay and physics read nothing of the
        // puffs (WorldOptions.PuffCapacity), so no cue sees them, and the rehearsal reaches each cue at the same tick. The
        // puffs of the demo's plumes took about half of the time of a rehearsal.
        using var world = new World(new() { DisposePhysics = true, PuffCapacity = 0 },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced));
        SceneInstance instance = scene.Build(world, RenderResources.None);
        var steps = new List<ScriptStep>();
        // The outlines start off, as World.ShowFrameOutlines does, so a script with no section that shows them has no
        // outlines step.
        bool outlines = false;
        foreach (FilmSection section in sections)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(section.Every, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(section.Hold);
            ArgumentOutOfRangeException.ThrowIfLessThan(section.Limit, 1);
            List<ScriptStep> opening =
                [new CameraStep(section.Camera), .. (section.Posts ?? []).Select(post => (ScriptStep)post)];
            if (section.Outlines != outlines)
                opening.Insert(0, new OutlinesStep(section.Outlines));
            outlines = section.Outlines;
            // The runner checks and takes the camera step and the posts, so the rehearsal posts as a run of the script does.
            ScriptRunner.Run(new(scene.Name, opening, window), world, instance);
            steps.AddRange(opening);
            steps.Add(new FilmStep(section.Name, Rehearse(world, instance, section), section.Every));
        }

        var script = new Script(scene.Name, steps, window);
        // Parse runs the checks of the script format, such as the grammar of the film names.
        return Script.Parse(script.ToJson());
    }

    /// <summary>Runs the frames of <paramref name="section"/> on <paramref name="world"/>, and returns how many it ran.</summary>
    private static int Rehearse(World world, SceneInstance scene, FilmSection section)
    {
        int frames = 0;
        while (!section.Until(world, scene))
        {
            if (frames * section.Every >= section.Limit)
                throw new InvalidOperationException(
                    $"The cue of the section '{section.Name}' did not hold within its limit of {section.Limit} ticks.");
            RunFrame();
        }

        int holdFrames = (section.Hold + section.Every - 1) / section.Every;
        for (int frame = 0; frame < holdFrames; frame++)
            RunFrame();
        if (frames == 0)
            RunFrame();
        return frames;

        void RunFrame()
        {
            for (int tick = 0; tick < section.Every; tick++)
                world.Tick();
            frames++;
        }
    }
}
