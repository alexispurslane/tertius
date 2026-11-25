using System.Numerics;
using System.Text.Json.Nodes;
using Tertius.Assets;
using Tertius.Assets.Tests;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Prop = Tertius.Demo.DemoScenes.StationProps.Prop;
using PropKind = Tertius.Demo.DemoScenes.StationProps.PropKind;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;
using StationExit = Tertius.Demo.DemoScenes.StationExit;
using StationProps = Tertius.Demo.DemoScenes.StationProps;

namespace Tertius.Host.Tests;

/// <summary>
/// The places of the station's props (docs/plans/blue-danube-demo.md, "The fit to the art"), which need no art: every prop
/// keeps its kind's reach and a person's width clear of the person's way, the parked hull and the lift, and the terminal's
/// props lie antispinward of the lift's foot, away from the stroll and the coda. Every place is in the station's axes.
/// </summary>
public sealed class StationPropsTests
{
    /// <summary>The clear space between a prop's reach and a person's capsule on the way, in meters.</summary>
    private const double Clear = 0.5;

    private static StationLayout Layout => DemoStation.Layout;

    /// <summary>How far <paramref name="p"/> lies from the segment from <paramref name="a"/> to <paramref name="b"/>, across the axis X: in the deck's plane.</summary>
    private static double FromSegmentOnDeck(Double3 p, Double3 a, Double3 b)
    {
        Double3 flat = new(0, 1, 1);
        Double3 ab = (b - a) * flat,
            ap = (p - a) * flat;
        double t = Math.Clamp(Double3.Dot(ap, ab) / ab.LengthSquared, 0, 1);
        return (ap - ab * t).Length;
    }

    /// <summary>How far <paramref name="p"/> lies from <paramref name="box"/> in the deck's plane, Y and Z, or 0 inside it.</summary>
    private static double FromBoxOnDeck(Double3 p, BoundingBox box)
    {
        double dy = Math.Max(0, Math.Max(box.Min.Y - p.Y, p.Y - box.Max.Y)),
            dz = Math.Max(0, Math.Max(box.Min.Z - p.Z, p.Z - box.Max.Z));
        return Math.Sqrt(dy * dy + dz * dz);
    }

    /// <summary>
    /// Every prop of the bay's work area stands on the deck or hangs on the bay's spinward wall, inside the bay. Its reach
    /// keeps 0.5 m clear of a person on the way from the stair's landing down the ramp to the lift's middle, and of the
    /// parked hull's box and the lift's hole.
    /// </summary>
    [Fact]
    public void TheBayPropsKeepClearOfThePersonsWayTheParkedHullAndTheLift()
    {
        Waypoint[] way = StationExit.WayOut(cabin: default, station: default);
        Double3[] onDeck = [way[^3].Position, way[^2].Position, way[^1].Position];
        BoundingBox bay = Layout.Bay;

        foreach (Prop prop in StationProps.BayWorkArea)
        {
            double reach = StationProps.Reach(prop.Kind);
            for (int i = 0; i + 1 < onDeck.Length; i++)
                FromSegmentOnDeck(prop.Place, onDeck[i], onDeck[i + 1])
                    .Should()
                    .BeGreaterThan(reach + StationExit.PersonRadius + Clear, $"{prop.Id} keeps clear of the way");
            FromBoxOnDeck(prop.Place, Layout.ParkedBounds)
                .Should()
                .BeGreaterThan(reach + Clear, $"{prop.Id} keeps clear of the parked hull");
            FromBoxOnDeck(prop.Place, Layout.LiftHole)
                .Should()
                .BeGreaterThan(reach + Clear, $"{prop.Id} keeps clear of the lift's hole");
            prop.Place.Y.Should().BeInRange(bay.Min.Y + reach, bay.Max.Y - reach, $"{prop.Id} lies in the bay");
            if (prop.OnWall)
                prop.Place.Z.Should().Be(bay.Max.Z, $"{prop.Id} hangs on the bay's spinward wall");
            else
            {
                prop.Place.X.Should().Be(Layout.Deck, $"{prop.Id} stands on the deck");
                Math.Abs(prop.Place.Z)
                    .Should()
                    .BeLessThan(bay.Max.Z - reach, $"{prop.Id} stands inside the bay's walls");
            }
        }
    }

    /// <summary>
    /// Every prop of the terminal stands on the wheel's floor, or hangs on the terminal's post, antispinward of the lift's
    /// floor by more than its reach and 1 m. The stroll from the lift's foot and the coda's stack, boxes, marks and cameras
    /// lie spinward of the lift's antispinward edge, so no prop stands in them.
    /// </summary>
    [Fact]
    public void TheTerminalPropsLieAntispinwardOfTheLiftAwayFromTheStrollAndTheCoda()
    {
        BoundingBox lift = Layout.LiftFootprint;
        Double3[] coda =
        [
            DemoScenes.WheelCoda.Stand,
            DemoScenes.WheelCoda.StackCamera.Position,
            DemoScenes.WheelCoda.SpinwardCamera.Position,
            DemoScenes.WheelCoda.AntispinwardCamera.Position,
            .. StationExit.Stroll(default).Select(waypoint => waypoint.Position),
        ];
        coda.Should()
            .OnlyContain(place => place.Z > lift.Min.Z, "the stroll and the coda lie spinward of the lift's edge");

        foreach (Prop prop in StationProps.Terminal)
        {
            double reach = StationProps.Reach(prop.Kind);
            (prop.Place.Z + reach).Should().BeLessThan(lift.Min.Z - 1, $"{prop.Id} lies antispinward of the lift");
            Math.Abs(prop.Place.Y)
                .Should()
                .BeLessThan(Layout.HalfWidth - reach, $"{prop.Id} lies between the side walls");
            if (!prop.OnWall)
                Layout
                    .WheelFloor.Height(prop.Place)
                    .Should()
                    .BeApproximately(0, 1e-6, $"{prop.Id} stands on the floor");
        }
    }

    /// <summary>
    /// <see cref="StationProps.PoseOf"/> stands a floor prop with the middle of its model's bottom on its place, and hangs a
    /// wall prop with the middle of its model's back, its +Z face, on its place and its front toward the prop's front. The
    /// bounds are off center on every axis, so each anchor shows, and they are floats, so the places hold to 1e-6 m.
    /// </summary>
    [Fact]
    public void PoseOfStandsAFloorPropOnItsBottomAndHangsAWallPropByItsBack()
    {
        var bounds = new BoundingBox(new(-0.4f, -0.2f, -0.6f), new(0.8f, 1.0f, 0.2f));
        Prop floor = StationProps.BayWorkArea.First(prop => !prop.OnWall),
            wall = StationProps.BayWorkArea.First(prop => prop.OnWall);

        DPose standing = StationProps.PoseOf(floor, bounds),
            hanging = StationProps.PoseOf(wall, bounds);

        (standing.TransformPoint(new Double3(0.2, -0.2, -0.2)) - floor.Place)
            .Length.Should()
            .BeLessThan(1e-6, "the bottom's middle stands on the place");
        (hanging.TransformPoint(new Double3(0.2, 0.4, 0.2)) - wall.Place)
            .Length.Should()
            .BeLessThan(1e-6, "the back's middle hangs on the place");
        (hanging.TransformPoint(new Double3(0.2, 0.4, -0.6)) - (wall.Place + wall.Front * 0.8))
            .Length.Should()
            .BeLessThan(1e-6, "the front lies 0.8 m out from the wall");
    }

    /// <summary>
    /// Each prop's up is the up of the floor under it, or under the post that it hangs on: +X on the deck, and toward the
    /// axis across the facet of the wheel's floor. Its front lies across that up, so that its model stands upright.
    /// </summary>
    [Fact]
    public void EveryPropStandsUpright()
    {
        foreach (Prop prop in StationProps.BayWorkArea.Concat(StationProps.Terminal))
        {
            Double3 floorUp = StationProps.Terminal.Contains(prop)
                ? -Layout.WheelFloor.Normal(prop.OnWall ? StationProps.PostPlace : prop.Place)
                : Double3.UnitX;
            (prop.Up - floorUp).Length.Should().BeLessThan(1e-3, $"{prop.Id} stands on the floor's up");
            Double3.Dot(prop.Up, prop.Front).Should().BeApproximately(0, 1e-9, $"{prop.Id} faces across its up");
        }
    }
}

/// <summary>
/// The scene <c>demo-surface</c> drawn with art from a content root of tiny models, one triangle each, so that the test
/// needs no converted art: each model is uploaded once, every placement of a model shares its id, the props stand where
/// <see cref="StationProps.PoseOf"/> puts them, the passenger's model is scaled to the capsule's height, and the parts
/// that the art replaces are gone. Without art, the scene keeps its gray box: no prop, and the drawn body's visor.
/// </summary>
public sealed class BlueDanubeArtSceneTests : IDisposable
{
    /// <summary>The bounds of the tiny model: one triangle from the origin to (1, 1, 0).</summary>
    private static readonly BoundingBox Tiny = new(Vector3.Zero, new(1, 1, 0));

    private readonly World world = NewJoltWorld();
    private readonly RecordingResources resources = new();

    public void Dispose() => world.Dispose();

    private static ContentFolders TinyArt()
    {
        var content = new ContentFolders(NewFolder());
        var glb = new GlbBuilder();
        glb.Root(new JsonObject { ["name"] = "tiny" }, glb.Mesh(glb.Triangle()));
        byte[] model = glb.Build();
        foreach (AssetReference reference in BlueDanubeArt.All)
        {
            string path = content.CookedModel(reference);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, model);
        }

        return content;
    }

    private SceneInstance Build(ContentFolders? content) =>
        DemoScenes.For(content).Single(scene => scene.Name == "demo-surface").Build(world, resources);

    [Fact]
    public void EachModelIsUploadedOnceAndEveryPlacementOfAKindSharesIt()
    {
        SceneInstance scene = Build(TinyArt());

        resources.Models.Should().HaveCount(BlueDanubeArt.All.Count);
        foreach (
            IGrouping<PropKind, Prop> kind in StationProps
                .BayWorkArea.Concat(StationProps.Terminal)
                .GroupBy(p => p.Kind)
        )
            kind.Select(prop => world.GetRender(scene[prop.Id]).Model)
                .Distinct()
                .Should()
                .ContainSingle($"every {kind.Key} shares one model")
                .Which.IsNone.Should()
                .BeFalse();
        string[] drawn = ["station-art", "shuttle-art", "person-body"];
        drawn.Select(id => world.GetRender(scene[id]).Model).Should().OnlyContain(model => !model.IsNone);
    }

    [Fact]
    public void ThePropsStandWherePoseOfPutsThem()
    {
        SceneInstance scene = Build(TinyArt());
        FrameHandle station = world.GetOwnedFrame(scene["station"]);

        foreach (Prop prop in StationProps.BayWorkArea.Concat(StationProps.Terminal))
        {
            DPose expected = StationProps.PoseOf(prop, Tiny),
                pose = world.PoseIn(scene[prop.Id], station);
            (pose.Position - expected.Position).Length.Should().BeLessThan(1e-6, $"{prop.Id} stands at its place");
            pose.Rotation.Rotate(Double3.UnitY)
                .Should()
                .Match<Double3>(up => (up - prop.Up).Length < 1e-6, $"{prop.Id} stands upright");
        }
    }

    /// <summary>
    /// <see cref="DemoScenes.DemoSurface.SuitPlace"/> puts the middle of the passenger model's footprint on the capsule's
    /// bottom, under the person's center, for a model 1 m tall whose bounds are off center on every axis.
    /// </summary>
    [Fact]
    public void TheSuitStandsOnTheCapsulesBottomWithItsFootprintUnderThePerson()
    {
        var suit = new StoredModel(default, new BoundingBox(new(0.2f, 0.1f, -0.4f), new(1.0f, 1.1f, 0.2f)), 1);
        float scale = DemoScenes.DemoSurface.SuitScale(suit);

        Vector3 footprint = DemoScenes.DemoSurface.SuitPlace(suit) + new Vector3(0.6f, 0.1f, -0.1f) * scale;

        scale.Should().BeApproximately(2 * DemoScenes.DemoSurface.PersonHalf.Y, 1e-6f);
        (footprint - new Vector3(0, -DemoScenes.DemoSurface.PersonHalf.Y, 0)).Length().Should().BeLessThan(1e-5f);
    }

    [Fact]
    public void TheArtDrawsTheShuttleAndThePassengerAtTheirScalesAndHidesWhatItReplaces()
    {
        SceneInstance scene = Build(TinyArt());

        world.GetRender(scene["shuttle-art"]).Scale.Should().Be(new Vector3(Shuttle.ArtScale));
        world.GetRender(scene["person-body"]).Scale.Should().Be(new Vector3(2 * DemoScenes.DemoSurface.PersonHalf.Y));
        scene.EntityOf("person-visor").Should().BeNull("the passenger's model shows its own front");
        scene.EntityOf("station-hub-walls").Should().BeNull("the art draws the hub's outside");
        scene.EntityOf("station-bay-walls").Should().NotBeNull("the bay keeps its inside walls");
    }

    [Fact]
    public void WithoutTheArtTheSceneKeepsItsGrayBox()
    {
        SceneInstance scene = Build(null);

        StationProps
            .BayWorkArea.Concat(StationProps.Terminal)
            .Should()
            .OnlyContain(prop => scene.EntityOf(prop.Id) == null, "the gray box draws no prop");
        scene.EntityOf("terminal-post").Should().BeNull();
        scene.EntityOf("person-visor").Should().NotBeNull();
        scene.EntityOf("station-hub-walls").Should().NotBeNull();
        resources.Models.Should().BeEmpty();
    }
}

/// <summary>
/// The converted Blue Danube art against the numbers that the fit takes from it (docs/plans/blue-danube-demo.md, "The fit
/// to the art"). The tests skip without the converted art.
/// </summary>
public sealed class BlueDanubeArtFitTests
{
    private readonly RecordingResources resources = new();

    private StoredModel Loaded(AssetReference reference)
    {
        var content = ContentFolders.Find();
        Assert.SkipWhen(
            content is null || BlueDanubeArt.All.Any(reference => !File.Exists(content.CookedModel(reference))),
            $"The Blue Danube art is not converted: run `{BlueDanubeArt.SetupCommand}`."
        );
        return new ModelStore(resources, content).Load(reference).Ok
            ?? throw new InvalidOperationException($"The converted model {reference} does not load.");
    }

    /// <summary>The Lunar Shuttle's model has the bounds that <see cref="Shuttle.ArtBounds"/> holds, within 1 mm.</summary>
    [Fact]
    public void TheShuttlesModelHasTheBoundsThatTheHullFits()
    {
        BoundingBox bounds = Loaded(BlueDanubeArt.Shuttle).Bounds;

        (bounds.Min - Shuttle.ArtBounds.Min).Length().Should().BeLessThan(1e-3f);
        (bounds.Max - Shuttle.ArtBounds.Max).Length().Should().BeLessThan(1e-3f);
    }

    /// <summary>
    /// The station's model at <see cref="DemoStation.ArtPose"/> lies inside the visible hull of the layout,
    /// <see cref="StationLayout.HullRadius"/> from the axis and <see cref="StationLayout.HullHalfAxial"/> along it, and
    /// reaches each within 2 m.
    /// </summary>
    [Fact]
    public void TheStationsModelFillsTheVisibleHull()
    {
        BoundingBox bounds = Loaded(BlueDanubeArt.Station).Bounds;
        double up = DemoStation.ArtPose.Position.Y;
        StationLayout layout = DemoStation.Layout;

        double radius = Math.Max(
            Math.Max(Math.Abs(bounds.Min.X), bounds.Max.X),
            Math.Max(Math.Abs(bounds.Min.Z), bounds.Max.Z)
        );
        radius.Should().BeInRange(layout.HullRadius - 2, layout.HullRadius);
        Math.Max(-(bounds.Min.Y + up), bounds.Max.Y + up)
            .Should()
            .BeInRange(layout.HullHalfAxial - 2, layout.HullHalfAxial);
    }

    /// <summary>The passenger's model is 1.8 m tall, the capsule's height, so its scale on the drawn body is 1.</summary>
    [Fact]
    public void ThePassengersModelIsAsTallAsTheCapsule() =>
        DemoScenes.DemoSurface.SuitScale(Loaded(BlueDanubeArt.Passenger)).Should().BeApproximately(1, 1e-3f);

    /// <summary>Each prop's model reaches across its up no farther than <see cref="StationProps.Reach"/> of its kind.</summary>
    [Fact]
    public void EachPropsModelLiesWithinItsKindsReach()
    {
        foreach (PropKind kind in Enum.GetValues<PropKind>())
        {
            BoundingBox bounds = Loaded(ReferenceOf(kind)).Bounds;
            Vector3 half = (bounds.Max - bounds.Min) / 2;
            Math.Sqrt(half.X * half.X + half.Z * half.Z)
                .Should()
                .BeLessThanOrEqualTo(StationProps.Reach(kind), $"the {kind} lies within its reach");
        }
    }

    private static AssetReference ReferenceOf(PropKind kind) =>
        kind switch
        {
            PropKind.CargoPod => BlueDanubeArt.CargoPod,
            PropKind.StorageCart => BlueDanubeArt.StorageCart,
            PropKind.Toolbox => BlueDanubeArt.Toolbox,
            PropKind.Thermos => BlueDanubeArt.Thermos,
            PropKind.Stool => BlueDanubeArt.Stool,
            PropKind.StreetSeating => BlueDanubeArt.StreetSeating,
            PropKind.CagedSconce => BlueDanubeArt.CagedSconce,
        };
}
