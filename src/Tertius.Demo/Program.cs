using Tertius.Host;

namespace Tertius.Demo;

public static class Program
{
    /// <summary>
    /// Runs one of the demo's scenes. See <see cref="ScriptCommand"/> and docs/plans/host-and-scripted-runs.md.
    /// <list type="bullet">
    /// <item><c>--script scripts/drop-box.json --headless --out captures/drop-box</c> runs a script with no window.</item>
    /// <item><c>--script scripts/drop-box.json --out captures/drop-box --speed 0.5</c> plays it in a window.</item>
    /// <item><c>--scene drop-box</c> runs the scene live in a window until you close it.</item>
    /// </list>
    /// </summary>
    public static int Main(string[] args) => ScriptCommand.Run(args, DemoScenes.All, Console.Out, Console.Error);
}
