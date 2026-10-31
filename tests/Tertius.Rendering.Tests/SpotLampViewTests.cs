using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Whether the main view keeps a spot lamp, by its cone cut at its reach, and how the shadow tiles rank the spot lamps that
/// it keeps, from the snapshot through the batcher to <see cref="ShadowTiles"/> (ARCHITECTURE.md, "Local shadows"). No GPU
/// needed. The camera sits at the render origin and looks down −Z at 1600 × 900 with a 60 degree field of view. Unless a
/// test names another, the frame's highest exposure is −10 stops, where the cutoff irradiance is π / 8 W/m².
/// </summary>
public sealed class SpotLampViewTests
{
    private const float TenStopsDown = -10;

    private const int TileSize = 512;

    /// <summary>The outer half-angle of a test spot lamp, in rad: about 29°.</summary>
    private const float Narrow = 0.5f;

    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 600, VertexOffset: 0, Radius: 1f, Deform: null),
    ];

    /// <summary>The cutoff irradiance at −10 stops, in W/m².</summary>
    private static readonly double Cutoff = LightUnits.CutoffIrradiance(TenStopsDown);

    /// <summary>A snapshot with no light: the test star of <see cref="Gpu.Snapshot"/> has no power, so it does not shine.</summary>
    private static RenderSnapshot Dark() => Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);

    private static PreparedScene Prepare(RenderSnapshot snapshot, float highestStops = TenStopsDown)
    {
        var scene = new PreparedScene();
        Batchers
            .Default()
            .Prepare(
                snapshot: snapshot,
                alpha: 1f,
                highestStops: highestStops,
                width: 1600,
                height: 900,
                meshes: Meshes,
                materials: MaterialSlots.Of(8),
                environment: new WithoutMap(),
                region: DeformRegion.None,
                scene: scene
            );
        return scene;
    }

    /// <summary>
    /// A white spot lamp of radius 0 at <paramref name="position"/> along <paramref name="direction"/>, named
    /// <paramref name="entity"/>, that casts shadows, whose reach at −10 stops is <paramref name="reach"/> m.
    /// </summary>
    private static SpotRenderLight Lamp(
        uint entity,
        Vector3 position,
        Vector3 direction,
        float reach,
        float outerConeAngle = Narrow
    ) =>
        Gpu.Spot(
            position: position,
            direction: direction,
            color: Vector3.One,
            intensity: (float)(reach * reach * Cutoff),
            innerConeAngle: 0,
            outerConeAngle: outerConeAngle,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            EntityIndex = entity,
            CastsShadow = true,
        };

    /// <summary>
    /// A white point lamp of radius 0 at <paramref name="position"/>, named <paramref name="entity"/>, that casts shadows,
    /// whose reach at −10 stops is <paramref name="reach"/> m.
    /// </summary>
    private static PointRenderLight Bulb(uint entity, Vector3 position, float reach) =>
        Gpu.Lamp(
            position: position,
            color: Vector3.One,
            intensity: (float)(reach * reach * Cutoff),
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            EntityIndex = entity,
            CastsShadow = true,
        };

    /// <summary>
    /// The entity of each light of <paramref name="scene"/> that holds a tile of an atlas of <paramref name="layers"/>
    /// layers after one frame.
    /// </summary>
    private static uint[] TileHolders(PreparedScene scene, int layers = 1)
    {
        var tiles = new ShadowTiles(layers: layers, tileSize: TileSize);
        tiles.Update(
            lights: scene.LightSpan,
            placed: scene.PlacedSpan,
            deformed: scene.Deformed,
            originFrameId: 1,
            views: [],
            draws: []
        );
        return [.. scene.LightSpan.ToArray().Where(light => light.Shadow.HasValue).Select(light => light.EntityIndex)];
    }

    /// <summary>
    /// The ter-ox1a example: lamp X, 30 m behind the eye and aimed away from the view, and lamp Z, 60 m ahead and lighting
    /// the floor below it, both with a reach of 25 m at −10 stops and 35 m one stop higher.
    /// </summary>
    private static RenderSnapshot LampBehindAndLampAhead()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(entity: 1, position: new(0, 3, 30), direction: Vector3.UnitZ, reach: 25));
        snapshot.Lights.Add(Lamp(entity: 2, position: new(0, 3, -60), direction: -Vector3.UnitY, reach: 25));
        return snapshot;
    }

    [Theory]
    [InlineData(TenStopsDown)]
    [InlineData(TenStopsDown + 1)]
    public void ASpotLampAimedAwayFromTheViewIsNotInTheMainViewEvenWhenItsReachHoldsTheEye(float highestStops)
    {
        PreparedScene scene = Prepare(snapshot: LampBehindAndLampAhead(), highestStops: highestStops);

        scene
            .LightSpan.ToArray()
            .Where(light => light.InMainView)
            .Select(light => light.EntityIndex)
            .Should()
            .Equal([2u], "lamp X's cone points away from the view, and one stop up its reach of 35 m holds the eye");
    }

    [Theory]
    [InlineData(TenStopsDown)]
    [InlineData(TenStopsDown + 1)]
    public void TheLampThatLightsTheFloorAheadKeepsTheOnlyTileAtBothExposures(float highestStops)
    {
        PreparedScene scene = Prepare(snapshot: LampBehindAndLampAhead(), highestStops: highestStops);

        TileHolders(scene)
            .Should()
            .Equal([2u], "lamp X lights nothing in view, though at the eye its light is 4 times lamp Z's");
    }

    [Fact]
    public void ASpotLampAimedIntoTheViewFromOutsideItStaysInTheMainView()
    {
        RenderSnapshot snapshot = Dark();
        // Right of the view's right edge, which lies 5.1 m from the axis of the view at 5 m ahead.
        snapshot.Lights.Add(Lamp(entity: 1, position: new(20, 0, -5), direction: -Vector3.UnitX, reach: 25));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightSpan.ToArray().Select(light => light.InMainView).Should().Equal(true);
    }

    [Fact]
    public void TheTilesGoToTheNearestOfTheLampsBelowTheEyeThatPointDown()
    {
        RenderSnapshot snapshot = Dark();
        // A row of lamps 2 m below the eye, aimed down, the farthest first in the list. Each axis leaves the view before
        // it passes under the eye, so the point of each axis nearest the eye in view is its lamp.
        snapshot.Lights.Add(Lamp(entity: 1, position: new(0, -2, -20), direction: -Vector3.UnitY, reach: 30));
        snapshot.Lights.Add(Lamp(entity: 2, position: new(0, -2, -15), direction: -Vector3.UnitY, reach: 30));
        snapshot.Lights.Add(Lamp(entity: 3, position: new(0, -2, -10), direction: -Vector3.UnitY, reach: 30));
        snapshot.Lights.Add(Lamp(entity: 4, position: new(0, -2, -5), direction: -Vector3.UnitY, reach: 30));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightSpan.ToArray().Select(light => light.InMainView).Should().Equal(true, true, true, true);
        TileHolders(scene, layers: 2)
            .Should()
            .Equal([3u, 4u], "a spot of light D from the eye shows on screen in proportion to 1 / D²");
    }

    [Fact]
    public void ASpotLampWhoseBeamOnlyClipsTheEdgeOfTheViewRanksBelowOneThatLightsTheFloorAhead()
    {
        RenderSnapshot snapshot = Dark();
        // Just right of the view's right edge, aimed ahead and a little inward: its axis enters the view 0.66 m from the
        // lamp, 15.3 m from the eye. At the eye its light is 1.1 times the other lamp's.
        snapshot.Lights.Add(
            Lamp(entity: 1, position: new(11, 0, -10), direction: new(-0.1f, 0, -1), reach: 35, outerConeAngle: 0.3f)
        );
        // 12 m ahead and 6 m up, aimed down: its axis passes 12 m from the eye, 6 m from the lamp, in the middle of the view.
        snapshot.Lights.Add(Lamp(entity: 2, position: new(0, 6, -12), direction: -Vector3.UnitY, reach: 30));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightSpan.ToArray().Select(light => light.InMainView).Should().Equal(true, true);
        TileHolders(scene)
            .Should()
            .Equal(
                [2u],
                "where each axis in view comes nearest the eye, the first lamp's light is 35² / 15.3² cutoffs, the second's 30² / 12²"
            );
    }

    [Fact]
    public void ASpotLampWhoseConeReachesTheViewButWhoseAxisMissesItRanksByItsLightAtItsReach()
    {
        RenderSnapshot snapshot = Dark();
        // Right of the view, aimed further right: the edge of its 60° cone comes into the view 18 m from the lamp, its axis
        // never does. At the eye its light is 3.5 times the other lamp's.
        snapshot.Lights.Add(
            Lamp(entity: 1, position: new(12, 0, -5), direction: Vector3.UnitX, reach: 30, outerConeAngle: MathF.PI / 3)
        );
        // 20 m ahead and 3 m up, aimed down at the floor in view: its axis passes 20 m from the eye.
        snapshot.Lights.Add(Lamp(entity: 2, position: new(0, 3, -20), direction: -Vector3.UnitY, reach: 25));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightSpan.ToArray().Select(light => light.InMainView).Should().Equal(true, true);
        TileHolders(scene)
            .Should()
            .Equal(
                [2u],
                "at its reach the first lamp's light is the cutoff, the second's on its axis 25² / 20² cutoffs"
            );
    }

    [Fact]
    public void ASpotLampBehindTheEyeAimedIntoTheViewRanksByWhereItsAxisEntersTheView()
    {
        RenderSnapshot snapshot = Dark();
        // 10 m behind the eye and 2 m up, aimed ahead: its axis enters the view through the top plane 4 m from the eye.
        snapshot.Lights.Add(Lamp(entity: 1, position: new(0, 2, 10), direction: -Vector3.UnitZ, reach: 30));
        // 6 m ahead and 4 m up, aimed down: its axis passes 6 m from the eye. At the eye its light is twice the first's.
        snapshot.Lights.Add(Lamp(entity: 2, position: new(0, 4, -6), direction: -Vector3.UnitY, reach: 30));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightSpan[0].RankDistance.Should().BeApproximately(4, 1e-4);
        TileHolders(scene)
            .Should()
            .Equal([1u], "the first lamp's axis in view comes 4 m from the eye, the second's 6 m");
    }

    [Fact]
    public void PointLampsRankByTheirLightAtTheEye()
    {
        RenderSnapshot snapshot = Dark();
        // At the eye the far lamp gives 30² / 20² = 2.25 cutoffs, the near one 10² / 5² = 4.
        snapshot.Lights.Add(Bulb(entity: 1, position: new(0, 0, -20), reach: 30));
        snapshot.Lights.Add(Bulb(entity: 2, position: new(0, 0, -5), reach: 10));

        PreparedScene scene = Prepare(snapshot: snapshot);

        TileHolders(scene, layers: 6).Should().Equal([2u], "a point lamp takes all six layers");
    }
}
