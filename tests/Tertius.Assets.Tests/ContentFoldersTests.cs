namespace Tertius.Assets.Tests;

/// <summary>
/// References to cooked models (<see cref="AssetReference"/>) and the folders that hold them (<see cref="ContentFolders"/>):
/// a reference names a package, a source path and maybe a piece, and the content root turns it into its cooked file.
/// </summary>
public sealed class ContentFoldersTests
{
    private static string NewFolder() => Directory.CreateTempSubdirectory("tertius-assets-").FullName;

    [Fact]
    public void AReferenceNamesItsPackagePathAndPiece()
    {
        var reference = AssetReference.Parse("tertius.demo:ships/corvette.model#Hull");

        reference.Should().Be(new AssetReference(Package: "tertius.demo", Path: "ships/corvette.model", Piece: "Hull"));
        reference.ToString().Should().Be("tertius.demo:ships/corvette.model#Hull");
    }

    [Fact]
    public void AReferenceWithoutAFragmentNamesTheWholeFile() =>
        AssetReference.Parse("pack:props/crate.gltf").Piece.Should().BeNull();

    [Theory]
    [InlineData("no-package-separator")]
    [InlineData(":path/without/package")]
    [InlineData("pack:")]
    [InlineData("pack:../outside.gltf")]
    [InlineData("pack:/rooted.gltf")]
    [InlineData("pack:windows\\path.gltf")]
    [InlineData("pack:crate.gltf#")]
    [InlineData("Pack Name:crate.gltf")]
    public void AMalformedReferenceIsAnArgumentError(string text)
    {
        Action parse = () => AssetReference.Parse(text);

        parse.Should().Throw<ArgumentException>().WithMessage($"*{text}*");
    }

    [Fact]
    public void TheCookedModelLiesInTheCookedFolderNamedAfterItsSource()
    {
        string root = NewFolder();
        var folders = new ContentFolders(root);

        folders
            .CookedModel(AssetReference.Parse("pack:ships/corvette.model#Hull"))
            .Should()
            .Be(Path.Combine(root, "pack", "bin", "assets", "ships", "corvette.model", "model.glb"));
    }

    [Fact]
    public void TheSearchWalksUpFromTheStartToTheFolderThatHoldsContent()
    {
        string repository = NewFolder();
        Directory.CreateDirectory(Path.Combine(repository, "content", "pack"));
        string start = Directory.CreateDirectory(Path.Combine(repository, "src", "Game", "bin", "Release")).FullName;

        ContentFolders.Find(start: start, overrideRoot: null)!.Root.Should().Be(Path.Combine(repository, "content"));
    }

    [Fact]
    public void AnOverrideWinsOverTheSearch()
    {
        string repository = NewFolder();
        Directory.CreateDirectory(Path.Combine(repository, "content"));
        string other = NewFolder();

        ContentFolders.Find(start: repository, overrideRoot: other)!.Root.Should().Be(other);
    }

    [Fact]
    public void NoContentFolderAboveTheStartFindsNothing() =>
        // The temporary folder's ancestors hold no content folder on a test machine.
        ContentFolders.Find(start: NewFolder(), overrideRoot: null).Should().BeNull();

    [Fact]
    public void TheLibraryKnowsNoGameOrAssetByName()
    {
        // The library resolves any package's references. A name of the demo's content in its code would tie it to one game.
        string[] names =
        [
            "tertius.demo",
            "blue-danube",
            "blue danube",
            "bluedanube",
            "pioneer",
            "poly haven",
            "polyhaven",
            "shuttle",
            "station",
            "cargo",
            "habitat",
        ];
        string source = Path.Combine(RepositoryRoot(), "src", "Tertius.Assets");

        var hits = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file =>
                names
                    .Where(name => File.ReadAllText(file).Contains(name, StringComparison.OrdinalIgnoreCase))
                    .Select(name => $"{Path.GetFileName(file)}: {name}")
            )
            .ToList();

        hits.Should().BeEmpty();
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Tertius.slnx")))
                return folder.FullName;
        throw new InvalidOperationException("No Tertius.slnx above the test's folder.");
    }
}
