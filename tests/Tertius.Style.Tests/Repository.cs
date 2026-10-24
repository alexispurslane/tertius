namespace Tertius.Style.Tests;

/// <summary>The repository that holds this test project.</summary>
internal static class Repository
{
    /// <summary>The root folder of the repository, found by its solution file above the test binaries.</summary>
    public static string Root()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Tertius.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("No Tertius.slnx above the test binaries.");
    }
}
