namespace Tertius.Host.Tests;

/// <summary>SceneInstance: the map between scene ids and entities that scripts and dumps use to name entities.</summary>
public sealed class SceneInstanceTests
{
    private static readonly Entity Box = new(3, 1);
    private static readonly Entity Floor = new(4, 2);

    private static readonly Entity Chase = new(7, 1);
    private static readonly Entity Far = new(8, 1);

    [Fact]
    public void AnEntityIsFoundByItsSceneIdAndTheIdByTheEntity()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);

        scene["box"].Should().Be(Box);
        scene.IdOf(Box).Should().Be("box");
    }

    [Fact]
    public void AnEntityWithoutASceneIdIsNamedByItsIndexAndGeneration()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);

        scene.IdOf(Floor).Should().Be("#4.2");
    }

    [Fact]
    public void AnUnknownSceneIdIsNotFound()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);

        scene.EntityOf("crate").Should().BeNull();
        Action lookup = () => _ = scene["crate"];
        lookup.Should().Throw<KeyNotFoundException>().Which.Message.Should().Contain("crate");
    }

    [Fact]
    public void TwoEntitiesCannotShareASceneId()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);

        Action add = () => scene.Add("box", Floor);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OneEntityCannotHaveTwoSceneIds()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);

        Action add = () => scene.Add("crate", Box);

        add.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Box")]
    [InlineData("#3.1")]
    [InlineData("")]
    [InlineData("-box")]
    [InlineData("box crate")]
    public void ASceneIdOutsideTheGrammarIsRefused(string id)
    {
        Action add = () => new SceneInstance().Add(id, Box);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheNullEntityCannotHaveASceneId()
    {
        Action add = () => new SceneInstance().Add("box", Entity.Null);

        add.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("0box", true)]
    [InlineData("a_b.c-d", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("bOx", false)]
    [InlineData("a/b", false)]
    [InlineData(".hidden", false)]
    [InlineData("_box", false)]
    public void ASceneIdHasLowercaseLettersDigitsAndThreeMarksAndAtMost64Characters(string id, bool valid) =>
        SceneInstance.IsSceneId(id).Should().Be(valid);

    private static SceneInstance WithViews()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);
        scene.Add("chase", Chase);
        scene.Add("far", Far);
        scene.AddView("chase");
        scene.AddView("far");
        return scene;
    }

    [Fact]
    public void TheViewsCycleInTheOrderThatTheSceneAddedThem()
    {
        SceneInstance scene = WithViews();

        scene.Views.Should().Equal("chase", "far");
        scene.NextView(Chase).Should().Be(Far);
        scene.NextView(Far).Should().Be(Chase);
    }

    [Fact]
    public void ACameraThatIsNoViewGoesToTheFirstView() => WithViews().NextView(Box).Should().Be(Chase);

    [Fact]
    public void ACameraWithNoSceneIdGoesToTheFirstView() => WithViews().NextView(new(99, 1)).Should().Be(Chase);

    [Fact]
    public void ThreeViewsCycleForwardInTheOrderThatTheSceneAddedThem()
    {
        SceneInstance scene = WithViews();
        scene.AddView("box");

        scene.NextView(Chase).Should().Be(Far);
        scene.NextView(Far).Should().Be(Box);
        scene.NextView(Box).Should().Be(Chase);
    }

    [Fact]
    public void WithNoViewsTheCameraStays()
    {
        var scene = new SceneInstance();
        scene.Add("box", Box);

        scene.NextView(Box).Should().Be(Box);
    }

    [Fact]
    public void AViewMustNameAnEntityOfTheSceneOnce()
    {
        SceneInstance scene = WithViews();

        scene.Invoking(s => s.AddView("ghost")).Should().Throw<KeyNotFoundException>();
        scene.Invoking(s => s.AddView("far")).Should().Throw<ArgumentException>();
    }
}
