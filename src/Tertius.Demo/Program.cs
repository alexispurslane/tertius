using Tertius.Host;

namespace Tertius.Demo;

public static class Program
{
    /// <summary>
    /// Runs a script on one of the demo's scenes:
    /// <c>dotnet run --project src/Tertius.Demo -- --script scripts/drop-box.json --headless --out captures/drop-box</c>.
    /// </summary>
    public static int Main(string[] args) => ScriptCommand.Run(args, DemoScenes.All, Console.Out, Console.Error);
}
