using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace Tertius.Slop;

/// <summary>
/// The C# source of a repository that the slop metrics read (docs/plans/code-quality-tooling.md, decision 16): every
/// file under <c>src</c> that a person wrote, ordered by path. The build output in <c>obj</c> and <c>bin</c> and the
/// generated files are not part of it. It also holds the names that the files outside <c>src</c> use, so that a rule
/// can tell code that nothing uses from code that only a test or a script uses (decision 17).
/// </summary>
/// <param name="files">The files under <c>src</c>, ordered by path.</param>
/// <param name="namesOutside">The names in the tests, the benchmarks and the scripts, matched without case.</param>
public sealed partial class Codebase(IReadOnlyList<SourceFile> files, IReadOnlySet<string> namesOutside)
{
    // The folders outside src whose files use the code: the tests and benchmarks call it, and the scripts name its
    // events and steps, which the code finds by name through reflection.
    private static readonly string[] OutsideFolders = ["tests", "benchmarks", "scripts"];

    /// <summary>The top folders of a repository that the metrics read: <c>src</c>, and the folders whose files use it.</summary>
    public static readonly IReadOnlyList<string> Folders = ["src", .. OutsideFolders];

    /// <summary>The files, ordered by path.</summary>
    public IReadOnlyList<SourceFile> Files { get; } = files;

    /// <summary>
    /// Every name in the C# and JSON files of the tests, the benchmarks and the scripts, as text, without case. A
    /// script names a property in camel case, so the case does not count.
    /// </summary>
    public IReadOnlySet<string> NamesOutside { get; } = namesOutside;

    /// <summary>The source under <c>src</c> in the repository at <paramref name="root"/>, parsed in parallel.</summary>
    public static Codebase Under(string root) =>
        new(files:
            [
                .. PathsUnder(folder: Path.Combine(root, "src"), extensions: [".cs"])
                    .AsParallel()
                    .Select(path => SourceFile.Parse(Path.GetRelativePath(root, path).Replace('\\', '/'),
                        File.ReadAllText(path)))
                    .Where(file => !file.IsGenerated)
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
            ],
            namesOutside: NamesIn(string.Join('\n',
                OutsideFolders.Select(folder => Path.Combine(root, folder)).Where(Directory.Exists)
                    .SelectMany(folder => PathsUnder(folder: folder, extensions: [".cs", ".json"]))
                    .AsParallel().Select(File.ReadAllText))));

    /// <summary>Every name in <paramref name="text"/>: each word of letters, digits and <c>_</c>, without case.</summary>
    public static IReadOnlySet<string> NamesIn(string text) =>
        Name().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static FileSystemEnumerable<string> PathsUnder(string folder, string[] extensions) =>
        new(folder, (ref entry) => entry.ToFullPath(), new() { RecurseSubdirectories = true })
        {
            ShouldRecursePredicate = (ref entry) => entry.FileName is not ("obj" or "bin"),
            ShouldIncludePredicate = (ref entry) =>
            {
                string name = entry.FileName.ToString();
                return !entry.IsDirectory &&
                       extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
            }
        };

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Name();
}
