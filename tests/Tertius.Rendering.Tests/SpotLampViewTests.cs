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

    /// <summary>The entity of each light of <paramref name="scene"/> that holds a tile of a 1-layer atlas after one frame.</summary>
    private static uint[] TileHolders(PreparedScene scene)
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        tiles.Update(
            lights: scene.LightSpan,
            placed: scene.PlacedSpan,
            deformed: scene.Deformed,
            originFrameId: 1,
            views: [],
            draws: []
        );
        return
        [
            .. scene.LightSpan.ToArray().Where(light => light.Shadow.HasValue).Select(light => light.EntityIndex),
        ];
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
}
