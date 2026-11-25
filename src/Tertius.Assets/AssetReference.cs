using System.Text.RegularExpressions;

namespace Tertius.Assets;

/// <summary>
/// A reference to a model asset: <c>package:path</c> for every root piece of the file, or <c>package:path#piece</c> for
/// one piece (docs/plans/asset-pipeline.md, "Ids and fragments"). The path is the source file's path in the package's
/// <c>assets/</c> folder, with its extension, such as <c>ships/corvette.model</c>.
/// </summary>
/// <param name="Package">The package id: lower-case letters, digits, dots, dashes and underscores.</param>
/// <param name="Path">The source path, relative, with forward slashes and no <c>..</c>.</param>
/// <param name="Piece">The piece's name, or null for the whole file.</param>
public sealed partial record AssetReference(string Package, string Path, string? Piece)
{
    /// <summary>
    /// Reads <c>package:path</c> or <c>package:path#piece</c>. Throws <see cref="ArgumentException"/> for text that breaks
    /// the form, such as a missing package, an empty fragment, or a path that leaves the package.
    /// </summary>
    public static AssetReference Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Form().Match(text) is not { Success: true } match)
            throw new ArgumentException($"{text} is not a reference of the form package:path or package:path#piece.", nameof(text));
        string path = match.Groups["path"].Value;
        bool leaves = path.Split('/').Any(segment => segment is ".." or "." or "");
        if (leaves)
            throw new ArgumentException($"The path of {text} must stay inside its package.", nameof(text));
        string? piece = match.Groups["piece"].Success ? match.Groups["piece"].Value : null;
        return new(Package: match.Groups["package"].Value, Path: path, Piece: piece);
    }

    public override string ToString() => Piece is null ? $"{Package}:{Path}" : $"{Package}:{Path}#{Piece}";

    [GeneratedRegex(@"^(?<package>[a-z0-9._-]+):(?<path>[^#\\:]+)(#(?<piece>[^#]+))?$")]
    private static partial Regex Form();
}
