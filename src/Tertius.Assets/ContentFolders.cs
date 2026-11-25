namespace Tertius.Assets;

/// <summary>
/// The content root: the folder that holds the package folders, each with its cooked output in <c>bin/assets/</c>
/// (docs/plans/content-packages.md). A cooked model lies in the folder named after its source file.
/// </summary>
/// <param name="root">The folder that holds the package folders, such as a checkout's <c>content</c> folder.</param>
public sealed class ContentFolders(string root)
{
    /// <summary>The environment variable that names the content root instead of the search.</summary>
    public const string OverrideVariable = "TERTIUS_CONTENT_ROOT";

    public string Root { get; } = root;

    /// <summary>
    /// The content root of this process: the folder that <see cref="OverrideVariable"/> names, or else the first folder
    /// <c>content</c> above the program's folder. Null when neither exists.
    /// </summary>
    public static ContentFolders? Find() =>
        Find(start: AppContext.BaseDirectory, overrideRoot: Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>
    /// <paramref name="overrideRoot"/> when it is not null or empty, or else the first folder <c>content</c> in
    /// <paramref name="start"/> or above it. Null when the search finds none.
    /// </summary>
    public static ContentFolders? Find(string start, string? overrideRoot)
    {
        if (!string.IsNullOrEmpty(overrideRoot))
            return new(overrideRoot);
        for (DirectoryInfo? folder = new(start); folder is not null; folder = folder.Parent)
        {
            string content = Path.Combine(folder.FullName, "content");
            if (Directory.Exists(content))
                return new(content);
        }

        return null;
    }

    /// <summary>The cooked file of <paramref name="reference"/>'s model, which may not exist.</summary>
    public string CookedModel(AssetReference reference) =>
        Path.Combine([Root, reference.Package, "bin", "assets", .. reference.Path.Split('/'), "model.glb"]);
}
