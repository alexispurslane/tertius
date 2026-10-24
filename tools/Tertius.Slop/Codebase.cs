using System.IO.Enumeration;

namespace Tertius.Slop;

/// <summary>
/// The C# source of a repository that the slop metrics read (docs/plans/code-quality-tooling.md, decision 16): every
/// file under <c>src</c> that a person wrote, ordered by path. The build output in <c>obj</c> and <c>bin</c> and the
/// generated files are not part of it.
/// </summary>
public sealed class Codebase(IReadOnlyList<SourceFile> files)
{
    /// <summary>The files, ordered by path.</summary>
    public IReadOnlyList<SourceFile> Files { get; } = files;

    /// <summary>The source under <c>src</c> in the repository at <paramref name="root"/>, parsed in parallel.</summary>
    public static Codebase Under(string root) =>
        new([
            .. PathsUnder(Path.Combine(root, "src"))
                .AsParallel()
                .Select(path => SourceFile.Parse(Path.GetRelativePath(root, path).Replace('\\', '/'),
                    File.ReadAllText(path)))
                .Where(file => !file.IsGenerated)
                .OrderBy(file => file.Path, StringComparer.Ordinal)
        ]);

    private static FileSystemEnumerable<string> PathsUnder(string folder) =>
        new(folder, (ref entry) => entry.ToFullPath(), new() { RecurseSubdirectories = true })
        {
            ShouldRecursePredicate = (ref entry) => entry.FileName is not ("obj" or "bin"),
            ShouldIncludePredicate = (ref entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        };
}
