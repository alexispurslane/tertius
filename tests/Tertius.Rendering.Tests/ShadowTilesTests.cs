using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="ShadowTiles"/>: which near lights get tiles of the atlas, which layer each tile takes, which casters each
/// tile draws, when a tile is drawn again, and each tile's projection (docs/plans/local-light-shadows.md, "The budget",
/// "Tile views", "Casters", "The cache"), the main view's lights before the lights that only a face of a map keeps
/// (docs/plans/environment-map.md, decision 20), and the margin by which a holder keeps its tiles (settled fork 2). Every
/// frame is submitted, then checks the invariants, unless a test says otherwise.
/// </summary>
public sealed class ShadowTilesTests
{
    private const int TileSize = 512;

    /// <summary>The reach of every test light, in m.</summary>
    private const float Reach = 50;

    /// <summary>The outer half-angle of a test spot light, in rad: about 29°.</summary>
    private const float Narrow = 0.5f;

    private static readonly LightShadow NoSlot = default;

    /// <summary>
    /// The camera of every test frame: at the origin, looking along +X, so that a test light at the origin or ahead on +X,
    /// aimed along +X, is in view, as its tier says.
    /// </summary>
    private static readonly CameraView Eye = ScreenTesting.Camera(width: 1600, height: 900, yaw: -MathF.PI / 2);

    private static readonly MeshData CylinderData = Primitives.BendingCylinder(radius: 0.2f, length: 2f);
    private static readonly MeshData BoxData = Primitives.MorphingBox(new(0.5f));

    /// <summary>
    /// A white spot light of <paramref name="intensity"/> W/sr that casts shadows, in the main view, ranked by its light at
    /// the eye, with the camera at the origin.
    /// </summary>
    private static PreparedLight Spot(
        uint entity,
        float intensity = 1,
        Vector3? position = null,
        Vector3? axis = null,
        float outerAngle = Narrow
    )
    {
        Vector3 place = position ?? Vector3.Zero;
        return new()
        {
            EntityIndex = entity,
            Position = place,
            RadiantIntensity = new(intensity),
            Reach = Reach,
            Distance = place.Length(),
            RankDistance = place.Length(),
            CastsShadow = true,
            Tier = MainViewTier.InView,
            Spread = new(Cone.Of(Vector3.Normalize(axis ?? Vector3.UnitX), innerAngle: 0, outerAngle: outerAngle)),
        };
    }

    /// <summary>
    /// A white point light of <paramref name="intensity"/> W/sr that casts shadows, in the main view, with the camera at
    /// the origin.
    /// </summary>
    private static PreparedLight Point(uint entity, float intensity = 1, Vector3? position = null)
    {
        Vector3 place = position ?? Vector3.Zero;
        return new()
        {
            EntityIndex = entity,
            Position = place,
            RadiantIntensity = new(intensity),
            Reach = Reach,
            Distance = place.Length(),
            RankDistance = place.Length(),
            CastsShadow = true,
            Tier = MainViewTier.InView,
            Spread = new EveryDirection(),
        };
    }

    /// <summary>An unrotated caster of unit scale, with the camera at the origin.</summary>
    private static PreparedInstance Caster(uint entity, Vector3 position, float radius = 1, int mesh = 0) =>
        new()
        {
            Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            Position = position,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Radius = radius,
            Distance = position.Length(),
            Mesh = mesh,
            EntityIndex = entity,
        };

    /// <summary>One submitted frame of the tiles with the camera in the frame of id 1, with the invariants checked after it.</summary>
    private static (List<TileView> Views, List<TileDraw> Draws) Frame(
        ShadowTiles tiles,
        PreparedLight[] lights,
        PreparedInstance[]? casters = null
    ) => FrameInOrigin(tiles: tiles, lights: lights, casters: casters ?? [], originFrameId: 1);

    /// <summary>
    /// The draws of one submitted frame of the tiles with the test spot light, whose casters' meshes are
    /// <paramref name="meshes"/>, with the invariants checked after it.
    /// </summary>
    private static List<TileDraw> PosedFrame(ShadowTiles tiles, PreparedInstance[] casters, FrameMeshes meshes) =>
        FrameInOrigin(
            tiles: tiles,
            lights: [Spot(entity: 1)],
            casters: casters,
            originFrameId: 1,
            meshes: meshes
        ).Draws;

    /// <summary>
    /// One submitted frame of the tiles with the camera in the frame <paramref name="originFrameId"/>, with the invariants
    /// checked after it.
    /// </summary>
    private static (List<TileView> Views, List<TileDraw> Draws) FrameInOrigin(
        ShadowTiles tiles,
        PreparedLight[] lights,
        PreparedInstance[] casters,
        long originFrameId,
        FrameMeshes? meshes = null
    )
    {
        List<TileView> views = [];
        List<TileDraw> draws = [];
        tiles.Update(
            lights: lights,
            camera: Eye,
            placed: casters,
            deformed: (meshes ?? FrameMeshes.Static).Deformed,
            originFrameId: originFrameId,
            views: views,
            draws: draws
        );
        tiles.Drew();
        tiles.AssertInvariants();
        return (views, draws);
    }

    private static LightShadow Tiles(int first, int count) => new(new AtlasTiles(First: first, Count: count));

    private static int[] LayersOf(List<TileView> views, int light) =>
        [.. views.Where(view => view.Light == light).Select(view => view.Layer)];

    private static int[] CastersOf(ShadowTiles tiles, TileDraw draw) =>
        [.. Enumerable.Range(draw.First, draw.Count).Select(index => tiles.CasterIndices[index])];

    /// <summary>
    /// The draws of a frame with one spot light and one caster of <paramref name="scale"/>, 1 by default, then with the
    /// caster changed by <paramref name="change"/>.
    /// </summary>
    private static List<TileDraw> DrawsAfterTheCasterChanges(
        Func<PreparedInstance, PreparedInstance> change,
        float scale = 1
    )
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance caster = Caster(entity: 7, position: new(10, 0, 0)) with { Scale = new(scale) };
        Frame(tiles, [Spot(entity: 1)], [caster]);
        return Frame(tiles, [Spot(entity: 1)], [change(caster)]).Draws;
    }

    /// <summary>
    /// A quarter of a texel of the test spot light's tile at the test caster's nearest point, 9 m away, in m. The caster's
    /// sphere shows out to asin 0.1 off the axis, where a texel spans cos² = 0.99 of a texel on the axis.
    /// </summary>
    private static float QuarterTexelAtTheCaster =>
        0.25f * ShadowTiles.TexelPerMeter(Spot(entity: 1), new ConeAim(), TileSize) * 9 * 0.99f;

    /// <summary>The normalized device coordinates of <paramref name="point"/>, camera-relative, in a tile's clip space.</summary>
    private static Vector3 Ndc(Matrix4x4 viewProjection, Vector3 point)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
        return new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
    }

    [Fact]
    public void TheBrightestNearLightsAtTheEyeGetTiles()
    {
        var tiles = new ShadowTiles(layers: 2, tileSize: TileSize);
        PreparedLight[] lights =
        [
            Spot(entity: 1, intensity: 1),
            Spot(entity: 2, intensity: 5),
            Spot(entity: 3, intensity: 3),
        ];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(NoSlot, Tiles(first: 0, count: 1), Tiles(first: 1, count: 1));
    }

    [Fact]
    public void ANearLightNearerTheEyeOutranksAMoreIntenseOneFartherAway()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights =
        [
            Spot(entity: 1, intensity: 4, position: new(10, 0, 0)),
            Spot(entity: 2, intensity: 1, position: new(2, 0, 0)),
        ];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [NoSlot, Tiles(first: 0, count: 1)],
                "at the eye the nearer light gives 1/4 W/m², the farther one 4/100 W/m²"
            );
    }

    [Fact]
    public void ANearLightOfTheMainViewOutranksABrighterOneThatOnlyAFaceKeeps()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights =
        [
            Spot(entity: 1, intensity: 5) with
            {
                Tier = MainViewTier.FaceOnly,
            },
            Spot(entity: 2, intensity: 1),
        ];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [NoSlot, Tiles(first: 0, count: 1)],
                "a light that only a face keeps must not change the main view's pixels"
            );
    }

    [Fact]
    public void RankingLightsOfTwoTiersAllocatesNothing()
    {
        var tiles = new ShadowTiles(layers: 2, tileSize: TileSize);
        PreparedLight[] lights =
        [
            Spot(entity: 1, intensity: 5) with
            {
                Tier = MainViewTier.FaceOnly,
            },
            Spot(entity: 2, intensity: 1),
        ];
        List<TileView> views = [];
        List<TileDraw> draws = [];
        Frame(tiles, lights);

        Allocations
            .Fewest(() =>
            {
                tiles.Update(
                    lights: lights,
                    camera: Eye,
                    placed: [],
                    deformed: FrameMeshes.Static.Deformed,
                    originFrameId: 1,
                    views: views,
                    draws: draws
                );
                tiles.Drew();
            })
            .Should()
            .Be(0, "the tiles rank their lights every frame");
    }

    [Fact]
    public void ANearLightThatOnlyAFaceKeepsTakesTheTilesThatTheMainViewLeavesFree()
    {
        var tiles = new ShadowTiles(layers: 7, tileSize: TileSize);
        PreparedLight[] lights =
        [
            Point(entity: 1, intensity: 5) with
            {
                Tier = MainViewTier.FaceOnly,
            },
            Spot(entity: 2, intensity: 1),
            Spot(entity: 3, intensity: 3) with
            {
                Tier = MainViewTier.FaceOnly,
            },
        ];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [Tiles(first: 0, count: 6), Tiles(first: 6, count: 1), NoSlot],
                "after the main view's spot light, the brighter light that only a face keeps takes the six layers left"
            );
    }

    [Fact]
    public void APointLightTakesSixTilesAndASpotLightOne()
    {
        var tiles = new ShadowTiles(layers: 7, tileSize: TileSize);
        PreparedLight[] lights = [Point(entity: 1), Spot(entity: 2)];

        List<TileView> views = Frame(tiles, lights).Views;

        lights.Select(light => light.Shadow).Should().Equal(Tiles(first: 0, count: 6), Tiles(first: 6, count: 1));
        views
            .Select(view => view.Aim)
            .Should()
            .Equal(
                [
                    .. Enum.GetValues<CubeFace>().Select(face => new TileAim(new FaceAim(face))),
                    new TileAim(new ConeAim()),
                ],
                "a point light's tiles follow the cube faces in Vulkan's layer order"
            );
    }

    [Fact]
    public void ASpotLightWiderThanSixtyDegreesTakesSixTiles()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1, outerAngle: MathF.PI / 3 + 0.01f)];

        List<TileView> views = Frame(tiles, lights).Views;

        lights[0].Shadow.Should().Be(Tiles(first: 0, count: 6));
        views.Select(view => view.Aim).Should().AllSatisfy(aim => aim.TryGetValue(out FaceAim _).Should().BeTrue());
    }

    [Fact]
    public void ASpotLightOfSixtyDegreesTakesOneTile() =>
        ShadowTiles.TilesOf(Spot(entity: 1, outerAngle: MathF.PI / 3)).Should().Be(1);

    [Fact]
    public void ALightThatDoesNotFitIsSkippedAndALaterOneThatFitsGetsItsTiles()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        PreparedLight[] lights =
        [
            Spot(entity: 1, intensity: 3),
            Point(entity: 2, intensity: 2),
            Spot(entity: 3, intensity: 1),
        ];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [Tiles(first: 0, count: 1), NoSlot, Tiles(first: 1, count: 1)],
                "after the brightest spot light, the point light's six tiles do not fit the five layers left"
            );
    }

    [Fact]
    public void EqualLightsKeepTheOrderOfTheList()
    {
        var tiles = new ShadowTiles(layers: 2, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1), Spot(entity: 2), Spot(entity: 3)];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(Tiles(first: 0, count: 1), Tiles(first: 1, count: 1), NoSlot);
    }

    [Fact]
    public void AFarLightAndALightThatCastsNoShadowGetNoTiles()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        var farOnly = new LightShadow(new FarShadowsOnly(default));
        PreparedLight[] lights =
        [
            Spot(entity: 1, intensity: 1e30f) with
            {
                Distance = 1e8,
                Shadow = farOnly,
            },
            Spot(entity: 2, intensity: 5) with
            {
                CastsShadow = false,
            },
        ];

        List<TileView> views = Frame(tiles, lights).Views;

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal([farOnly, NoSlot], "the far light keeps the slot that it had");
        views.Should().BeEmpty();
    }

    [Fact]
    public void ANearLightThatLosesItsTilesHoldsNoSlot()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1, intensity: 2), Spot(entity: 2, intensity: 1)];
        Frame(tiles, lights);
        lights[1].RadiantIntensity = new(5);

        Frame(tiles, lights);

        lights.Select(light => light.Shadow).Should().Equal(NoSlot, Tiles(first: 0, count: 1));
    }

    [Fact]
    public void AHolderKeepsItsTilesWhileAChallengerOfItsTierIsLessThanTwiceAsBright()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1, intensity: 1), Spot(entity: 2, intensity: 0.5f)];
        Frame(tiles, lights);
        lights[1].RadiantIntensity = new(1.9f);

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [Tiles(first: 0, count: 1), NoSlot],
                "the holder ranks at twice its 1 W/sr, above the challenger's 1.9 W/sr at the same distance"
            );
    }

    [Fact]
    public void AChallengerMoreThanTwiceAsBrightTakesTheTilesInTheFirstFrameItIs()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1, intensity: 1), Spot(entity: 2, intensity: 0.5f)];
        Frame(tiles, lights);
        lights[1].RadiantIntensity = new(2.1f);

        Frame(tiles, lights);

        lights.Select(light => light.Shadow).Should().Equal(NoSlot, Tiles(first: 0, count: 1));
    }

    [Fact]
    public void AChallengerExactlyTwiceAsBrightTiesWithTheHolderAndTheFirstInTheListWins()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        Frame(tiles, [Spot(entity: 2, intensity: 1)]);
        PreparedLight[] lights = [Spot(entity: 1, intensity: 2), Spot(entity: 2, intensity: 1)];

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal([Tiles(first: 0, count: 1), NoSlot], "a tie goes by list order, whichever light holds the tiles");
    }

    [Fact]
    public void ARankingThatSwingsByLessThanTheMarginAtTheBudgetsEdgeKeepsOneOwner()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1), Spot(entity: 2)];
        List<uint> owners = [];

        for (int frame = 0; frame < 20; frame++)
        {
            // The two lamps trade places every frame, each 1.5 times as bright as the other in turn.
            bool firstLeads = frame % 2 == 0;
            lights[0].RadiantIntensity = new(firstLeads ? 1.5f : 1);
            lights[1].RadiantIntensity = new(firstLeads ? 1 : 1.5f);
            Frame(tiles, lights);
            owners.AddRange(lights.ToArray().Where(light => light.Shadow.HasValue).Select(light => light.EntityIndex));
        }

        owners.Should().Equal(Enumerable.Repeat(1u, 20), "a swing of 1.5 times stays inside the margin of 2");
    }

    [Fact]
    public void ALightThatLostItsTilesNeedsMoreThanTwiceTheNewHoldersLightToTakeThemBack()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1, intensity: 1), Spot(entity: 2, intensity: 0.5f)];
        Frame(tiles, lights);
        lights[1].RadiantIntensity = new(3);
        Frame(tiles, lights);
        lights[0].RadiantIntensity = new(5);

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [NoSlot, Tiles(first: 0, count: 1)],
                "the new holder ranks at twice its 3 W/sr, above the 5 W/sr of the light that held the tile before it"
            );
    }

    [Fact]
    public void AFrameThatWasNeverSubmittedMakesNoHolder()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1, intensity: 1), Spot(entity: 2, intensity: 0.5f)];
        Frame(tiles, lights);
        // A frame in which the challenger takes the tile throws before its submission, so it records nothing.
        lights[1].RadiantIntensity = new(3);
        tiles.Update(
            lights: lights,
            camera: Eye,
            placed: [],
            deformed: FrameMeshes.Static.Deformed,
            originFrameId: 1,
            views: [],
            draws: []
        );
        lights[1].RadiantIntensity = new(1.9f);

        Frame(tiles, lights);

        lights
            .Select(light => light.Shadow)
            .Should()
            .Equal(
                [Tiles(first: 0, count: 1), NoSlot],
                "the records still name the first light, which ranks at twice its 1 W/sr, above the 1.9 W/sr"
            );
    }

    [Fact]
    public void ALightThatStaysInTheBudgetKeepsItsLayers()
    {
        var tiles = new ShadowTiles(layers: 3, tileSize: TileSize);
        Frame(tiles, [Spot(entity: 1), Spot(entity: 2)]);

        List<TileView> views = Frame(tiles, [Spot(entity: 3, intensity: 9), Spot(entity: 1), Spot(entity: 2)]).Views;

        views
            .Select(view => view.Layer)
            .Should()
            .Equal(
                [2, 0, 1],
                "the two lights keep their layers, though the brighter new light comes first in the list"
            );
    }

    [Fact]
    public void ALightNewToTheBudgetTakesTheLowestFreeLayers()
    {
        var tiles = new ShadowTiles(layers: 8, tileSize: TileSize);
        Frame(tiles, [Spot(entity: 1), Point(entity: 2), Spot(entity: 3)]);

        List<TileView> views = Frame(tiles, [Spot(entity: 3), Point(entity: 4)]).Views;

        LayersOf(views, light: 0).Should().Equal([7], "the spot light keeps its layer");
        LayersOf(views, light: 1).Should().Equal([0, 1, 2, 3, 4, 5], "the layers that the lights that left gave up");
    }

    [Fact]
    public void ATileWhoseLightAndCastersStayIsNotDrawnAgain()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(10, 0, 0))];
        List<TileDraw> first = Frame(tiles, [Spot(entity: 1)], casters).Draws;

        List<TileDraw> second = Frame(tiles, [Spot(entity: 1)], casters).Draws;

        first.Should().Equal(new TileDraw(View: 0, First: 0, Count: 1));
        second.Should().BeEmpty();
    }

    [Fact]
    public void ACameraThatMovesLeavesTheTilesCached()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        Double3 light = new(5.3, 1.7, -2.2);
        Double3 caster = new(13.4, 2.03, -1.97);
        Double3[] cameras = [new(0, 0, 0), new(1.9, -1.6, 1.7)];

        List<TileDraw>[] draws =
        [
            .. cameras.Select(camera =>
                Frame(
                    tiles,
                    [Spot(entity: 1, position: (light - camera).ToVector3())],
                    [Caster(entity: 7, position: (caster - camera).ToVector3())]
                ).Draws
            ),
        ];

        draws[1]
            .Should()
            .BeEmpty("the camera moved 3 m, which changes only the low bits of the camera-relative floats");
    }

    [Fact]
    public void ACasterThatMovesAQuarterOfATexelDrawsItsTileAgain() =>
        DrawsAfterTheCasterChanges(caster =>
                caster with
                {
                    Position = caster.Position + new Vector3(0, 1.02f * QuarterTexelAtTheCaster, 0),
                }
            )
            .Should()
            .ContainSingle();

    [Fact]
    public void ACasterThatMovesLessKeepsTheTile() =>
        DrawsAfterTheCasterChanges(caster =>
                caster with
                {
                    Position = caster.Position + new Vector3(0, 0.98f * QuarterTexelAtTheCaster, 0),
                }
            )
            .Should()
            .BeEmpty();

    [Theory]
    [InlineData(1.02f, 1)]
    [InlineData(0.98f, 0)]
    public void ACasterThatTurnsAQuarterOfATexelAtItsRadiusDrawsItsTileAgain(float share, int expectedDraws)
    {
        // A turn by θ moves a point at the radius, 1 m, by the chord 2 sin(θ / 2).
        float angle = 2 * MathF.Asin(share * QuarterTexelAtTheCaster / 2);

        List<TileDraw> draws = DrawsAfterTheCasterChanges(caster =>
            caster with
            {
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle),
            }
        );

        draws.Should().HaveCount(expectedDraws);
    }

    [Theory]
    [InlineData(1.02f, 1)]
    [InlineData(0.98f, 0)]
    public void ACasterThatScalesByAQuarterOfATexelAtItsRadiusDrawsItsTileAgain(float share, int expectedDraws)
    {
        List<TileDraw> draws = DrawsAfterTheCasterChanges(
            caster => caster with { Scale = new(2, 2, 2 * (1 + share * QuarterTexelAtTheCaster)) },
            scale: 2
        );

        draws
            .Should()
            .HaveCount(
                expectedDraws,
                "the radius is 1 m, so a scale that grows by a share moves the farthest point by that share of a metre"
            );
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("part")]
    [InlineData("mesh")]
    [InlineData("material")]
    public void ACasterOfAnotherEntityPartMeshOrMaterialDrawsItsTileAgain(string change) =>
        DrawsAfterTheCasterChanges(caster =>
                change switch
                {
                    "entity" => caster with { EntityIndex = 8 },
                    "part" => caster with { Part = 1 },
                    "mesh" => caster with { Mesh = 3 },
                    // A cutout material shadows only its core (docs/plans/skin-and-hair.md, "The cutout test").
                    _ => caster with { Material = 5, Alpha = new CutoutAlpha(Cutoff: 0.5f) },
                }
            )
            .Should()
            .ContainSingle();

    // Posed casters (docs/plans/skinning-and-morphs.md, fork 1, B): the record keeps a posed caster's static mesh and pose
    // rows. A posed caster draws frame mesh 2, the copy of static mesh 1, the cylinder or the box.

    [Fact]
    public void TileWithAStillPosedCasterStaysCached()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(10, 0, 0), mesh: 2)];
        RigPose rest = CylinderData.Rigging!.Rig.Rest;
        PosedFrame(tiles, casters, FrameMeshes.Posed(CylinderData, rest));

        List<TileDraw> draws = PosedFrame(tiles, casters, FrameMeshes.Posed(CylinderData, rest));

        draws.Should().BeEmpty("a still rig gives the same pose rows in every frame");
    }

    [Fact]
    public void TileRedrawsWhenAJointOfAPosedCasterTurns()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(10, 0, 0), mesh: 2)];
        RigPose rest = CylinderData.Rigging!.Rig.Rest;
        PosedFrame(tiles, casters, FrameMeshes.Posed(CylinderData, rest));
        RigPose bent = rest with
        {
            Joints = rest.Joints.SetItem(
                1,
                rest.Joints[1] with
                {
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.01f),
                }
            ),
        };

        List<TileDraw> draws = PosedFrame(tiles, casters, FrameMeshes.Posed(CylinderData, bent));

        draws.Should().ContainSingle("the caster changed its shape without moving");
    }

    [Fact]
    public void TileRedrawsWhenAMorphWeightOfAPosedCasterChanges()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(10, 0, 0), mesh: 2)];
        RigPose rest = BoxData.Rigging!.Rig.Rest;
        PosedFrame(tiles, casters, FrameMeshes.Posed(BoxData, rest));

        List<TileDraw> draws = PosedFrame(
            tiles,
            casters,
            FrameMeshes.Posed(BoxData, rest with { Weights = [0, 0.01f] })
        );

        draws.Should().ContainSingle();
    }

    [Fact]
    public void TileRedrawsWhenACasterTurnsFromStaticToPosed()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        var posed = FrameMeshes.Posed(CylinderData, CylinderData.Rigging!.Rig.Rest);
        PosedFrame(tiles, [Caster(entity: 7, position: new(10, 0, 0), mesh: 1)], posed);

        List<TileDraw> draws = PosedFrame(tiles, [Caster(entity: 7, position: new(10, 0, 0), mesh: 2)], posed);

        draws.Should().ContainSingle("the copy at rest can still differ from the uploaded mesh");
    }

    [Fact]
    public void TileWithAStillPosedCasterKeepsItsTileWhenItsCopyMovesInTheFrameMeshes()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        RigPose rest = CylinderData.Rigging!.Rig.Rest;
        PosedFrame(tiles, [Caster(entity: 7, position: new(10, 0, 0), mesh: 2)], FrameMeshes.Posed(CylinderData, rest));

        // Another posed instance, which casts into no tile, gained the job before the caster's: its copy is frame mesh 3.
        List<TileDraw> draws = PosedFrame(
            tiles,
            [Caster(entity: 7, position: new(10, 0, 0), mesh: 3)],
            FrameMeshes.PosedTwice(CylinderData, rest)
        );

        draws.Should().BeEmpty("the record names the copy's static mesh, which stays");
    }

    [Fact]
    public void TileWithTwoStillPosedCastersOfDifferentPosesStaysCached()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        RigPose rest = CylinderData.Rigging!.Rig.Rest;
        RigPose bent = rest with
        {
            Joints = rest.Joints.SetItem(
                1,
                rest.Joints[1] with
                {
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f),
                }
            ),
        };
        PreparedInstance[] casters =
        [
            Caster(entity: 7, position: new(10, 0, 0), mesh: 2),
            Caster(entity: 8, position: new(12, 0, 0), mesh: 3),
        ];
        PosedFrame(tiles, casters, FrameMeshes.PosedTwice(CylinderData, rest, bent));

        List<TileDraw> draws = PosedFrame(tiles, casters, FrameMeshes.PosedTwice(CylinderData, rest, bent));

        draws.Should().BeEmpty("each caster's rows are compared with its own recorded rows");
    }

    [Fact]
    public void ACasterAroundTheLightThatStaysKeepsTheTile()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(0.5f, 0, 0), radius: 1)];
        Frame(tiles, [Spot(entity: 1)], casters);

        Frame(tiles, [Spot(entity: 1)], casters)
            .Draws.Should()
            .BeEmpty("the threshold at a caster around the light is a quarter of a texel at the near plane");
    }

    [Theory]
    [InlineData(1.02f, false, true)]
    [InlineData(0.98f, false, false)]
    [InlineData(1.02f, true, true)]
    [InlineData(0.98f, true, false)]
    public void ACasterAroundTheLightThatMovesAQuarterOfAnEdgeTexelAtTheNearPlaneDrawsItsTilesAgain(
        float share,
        bool point,
        bool stale
    )
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        PreparedLight light = point ? Point(entity: 1) : Spot(entity: 1);
        TileAim aim = point ? new(new FaceAim(CubeFace.PositiveX)) : new(new ConeAim());
        float texelPerMeter = ShadowTiles.TexelPerMeter(light, aim, TileSize);
        float tanHalf = texelPerMeter * TileSize / 2;
        // The sphere holds the light, so it shows out to the tile's edge: the cone's rim, or the corner of a face.
        float edgeTanSquared = point ? 2 * tanHalf * tanHalf : tanHalf * tanHalf;
        float quarter = 0.25f * texelPerMeter * DepthRanges.NearPlane / (1 + edgeTanSquared);
        PreparedInstance caster = Caster(entity: 7, position: new(0.5f, 0, 0), radius: 1);
        Frame(tiles, [light], [caster]);

        List<TileDraw> draws = Frame(
            tiles,
            [light],
            [caster with { Position = caster.Position + new Vector3(0, share * quarter, 0) }]
        ).Draws;

        draws.Should().HaveCount(stale ? ShadowTiles.TilesOf(light) : 0);
    }

    [Theory]
    [InlineData(1.02f, 1)]
    [InlineData(0.98f, 0)]
    public void ACasterNearTheRimOfAWideConeThatMovesAQuarterOfARimTexelDrawsItsTileAgain(
        float share,
        int expectedDraws
    )
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight light = Spot(entity: 1, outerAngle: ShadowTiles.WidestCone);
        const float angle = 0.95f;
        const float distance = 5;
        // 0.95 rad off the axis, a texel spans cos² 0.95 = 0.34 of a texel on the axis at the same distance.
        float quarter =
            0.25f
            * ShadowTiles.TexelPerMeter(light, new ConeAim(), TileSize)
            * distance
            * MathF.Cos(angle)
            * MathF.Cos(angle);
        Vector3 position = distance * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
        var across = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0);
        PreparedInstance caster = Caster(entity: 7, position: position, radius: 1e-3f);
        Frame(tiles, [light], [caster]);

        List<TileDraw> draws = Frame(
            tiles,
            [light],
            [caster with { Position = position + share * quarter * across }]
        ).Draws;

        draws.Should().HaveCount(expectedDraws);
    }

    [Theory]
    [InlineData(700, 150, -220)]
    [InlineData(3, -2, 1)]
    public void AHullAroundTheLampWhoseFloatsOnlyRoundDifferentlyKeepsItsTiles(double x, double y, double z)
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        Double3 lamp = new(5.3, 1.7, -2.2);
        Double3 hull = new(x, y, z);
        // The batcher's slerp and normalization round this rotation differently at each alpha below, by about 7e-8.
        var turned = Quaternion.CreateFromYawPitchRoll(-2, -0.8f, 1.1f);
        (Double3 Camera, float Alpha)[] frames =
        [
            (new(0, 0, 0), 0.13f),
            (new(1.9, -1.6, 1.7), 1),
            (new(-3.1, 0.4, 2.6), 0.91f),
            (new(0.7, 2.2, -1.4), 0.36f),
        ];

        List<TileDraw>[] draws =
        [
            .. frames.Select(frame =>
                Frame(
                    tiles,
                    [Point(entity: 1, position: (lamp - frame.Camera).ToVector3())],
                    [
                        Caster(entity: 7, position: (hull - frame.Camera).ToVector3(), radius: 800) with
                        {
                            // The batcher interpolates a hull that stays between two equal rotations.
                            Rotation = SceneBatcher.Interpolate(turned, turned, frame.Alpha),
                        },
                    ]
                ).Draws
            ),
        ];

        draws
            .Skip(1)
            .Should()
            .AllSatisfy(frame =>
                frame
                    .Should()
                    .BeEmpty(
                        "the hull and the lamp stay; only the float rounding of their camera-relative pose changes, "
                            + "by up to a few ulps of the hull's distance or radius"
                    )
            );
    }

    [Theory]
    [InlineData(1.02f, 1)]
    [InlineData(0.98f, 0)]
    public void ACasterThatShowsOutToTheRimDrawsItsTileAgainWhenItMovesAQuarterOfARimTexel(
        float share,
        int expectedDraws
    )
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight light = Spot(entity: 1);
        float texelPerMeter = ShadowTiles.TexelPerMeter(light, new ConeAim(), TileSize);
        float tanHalf = texelPerMeter * TileSize / 2;
        // The sphere spans asin(2 / 3) = 0.73 rad off the axis, past the rim of the widened cone at 0.507 rad.
        PreparedInstance caster = Caster(entity: 7, position: new(3, 0, 0), radius: 2);
        float quarter = 0.25f * texelPerMeter * 1 / (1 + tanHalf * tanHalf);
        Frame(tiles, [light], [caster]);

        List<TileDraw> draws = Frame(
            tiles,
            [light],
            [caster with { Position = caster.Position + new Vector3(0, share * quarter, 0) }]
        ).Draws;

        draws.Should().HaveCount(expectedDraws, "the nearest point is 1 m away, and the rim's texel is the smallest");
    }

    [Fact]
    public void TwoLightsInOnePlaceKeepTheirOwnCachedTiles()
    {
        var tiles = new ShadowTiles(layers: 2, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(10, 0, 0))];
        Frame(tiles, [Spot(entity: 1), Spot(entity: 2)], casters);

        List<TileDraw> draws = Frame(tiles, [Spot(entity: 1), Spot(entity: 2)], casters).Draws;

        draws.Should().BeEmpty("each light finds its own record, which the invariants check against its entity");
    }

    [Fact]
    public void TheInvariantsHoldAfterTheCallerReusesItsViewList()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight[] lights = [Spot(entity: 1)];
        List<TileView> views = [];
        tiles.Update(
            lights: lights,
            camera: Eye,
            placed: [],
            deformed: FrameMeshes.Static.Deformed,
            originFrameId: 1,
            views: views,
            draws: []
        );
        tiles.Drew();

        views.Clear();

        tiles
            .Invoking(checkedTiles => checkedTiles.AssertInvariants())
            .Should()
            .NotThrow("the tiles check their own copy of the frame's views");
    }

    [Fact]
    public void ASpotLightThatWidensPastSixtyDegreesTakesSixLayersForItsOne()
    {
        var tiles = new ShadowTiles(layers: 7, tileSize: TileSize);
        Frame(tiles, [Spot(entity: 1)]);

        List<TileView> views = Frame(tiles, [Spot(entity: 1, outerAngle: 1.2f)]).Views;

        views.Select(view => view.Layer).Should().Equal(0, 1, 2, 3, 4, 5);
    }

    [Theory]
    [InlineData(1.02f, 1)]
    [InlineData(0.98f, 0)]
    public void ASpotLightThatTurnsAQuarterOfARimTexelsAngleDrawsItsTileAgain(float share, int expectedDraws)
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedLight light = Spot(entity: 1);
        float tangent = ShadowTiles.TexelPerMeter(light, new ConeAim(), TileSize) * TileSize / 2;
        // A turn moves the image at the rim, where tan = tangent, by 1 + tangent² times as much as at the axis.
        float angle = share * 0.5f * tangent / TileSize / (1 + tangent * tangent);
        Frame(tiles, [light]);

        List<TileDraw> draws = Frame(
            tiles,
            [
                Spot(
                    entity: 1,
                    axis: Vector3.Transform(Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle))
                ),
            ]
        ).Draws;

        draws.Should().HaveCount(expectedDraws);
    }

    [Fact]
    public void ASpotLightWhoseConeWidensDrawsItsTileAgain()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        Frame(tiles, [Spot(entity: 1)]);

        Frame(tiles, [Spot(entity: 1, outerAngle: Narrow + 1e-3f)]).Draws.Should().ContainSingle();
    }

    [Fact]
    public void ALightWhoseRadiusGrowsPastTheNearPlaneDrawsItsTileAgain()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        Frame(tiles, [Spot(entity: 1)]);

        Frame(tiles, [Spot(entity: 1) with { Radius = 0.2f }]).Draws.Should().ContainSingle();
    }

    [Fact]
    public void APlacedInstanceThatCastsNoShadowIsNoCasterOfATile()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance drawnOnly = Caster(entity: 8, position: new(10, 0, 0)) with { Flags = RenderFlags.Visible };

        List<TileDraw> draws = Frame(
            tiles,
            [Spot(entity: 1)],
            [drawnOnly, Caster(entity: 7, position: new(10, 0, 0))]
        ).Draws;

        CastersOf(tiles, draws.Should().ContainSingle().Subject)
            .Should()
            .Equal([1], "a tile's casters index the placed instances, and only the second casts a shadow");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACasterThatEntersOrLeavesTheReachDrawsTheTileAgain(bool enters)
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance inside = Caster(entity: 7, position: new(Reach, 0, 0));
        PreparedInstance outside = Caster(entity: 7, position: new(Reach + 2, 0, 0));
        Frame(tiles, [Spot(entity: 1)], [enters ? outside : inside]);

        List<TileDraw> draws = Frame(tiles, [Spot(entity: 1)], [enters ? inside : outside]).Draws;

        draws.Should().ContainSingle();
    }

    [Fact]
    public void ACameraThatChangesFrameDrawsEveryTileAgain()
    {
        var tiles = new ShadowTiles(layers: 7, tileSize: TileSize);
        PreparedLight[] lights = [Point(entity: 1), Spot(entity: 2)];
        FrameInOrigin(tiles: tiles, lights: lights, casters: [], originFrameId: 1);

        List<TileDraw> draws = FrameInOrigin(tiles: tiles, lights: lights, casters: [], originFrameId: 2).Draws;

        draws.Select(draw => draw.View).Should().Equal(0, 1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void AFrameThatWasNeverSubmittedLeavesTheNextFrameToDrawEveryViewItWouldHaveDrawn()
    {
        var tiles = new ShadowTiles(layers: 7, tileSize: TileSize);
        PreparedLight[] lights = [Point(entity: 1), Spot(entity: 2, position: new(0, 3, 0))];
        // The first frame chooses its layers and draws, then throws before its submission, so it records nothing.
        tiles.Update(
            lights: lights,
            camera: Eye,
            placed: [],
            deformed: FrameMeshes.Static.Deformed,
            originFrameId: 1,
            views: [],
            draws: []
        );

        List<TileDraw> draws = Frame(tiles, lights).Draws;

        draws
            .Should()
            .HaveCount(7, "no frame has drawn the layers, so they hold the image's undefined content (ter-uhls)");
    }

    [Fact]
    public void ALayerThatPassesToAnotherLightIsDrawnAgain()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(10, 0, 0))];
        Frame(tiles, [Spot(entity: 1)], casters);

        (List<TileView> views, List<TileDraw> draws) = Frame(tiles, [Spot(entity: 2)], casters);

        views.Should().Equal(new TileView(Layer: 0, Light: 0, Aim: new ConeAim()));
        draws.Should().ContainSingle("the light of another entity in the same place cannot reuse the layer");
    }

    [Fact]
    public void ANewTileWithNoCastersIsDrawnToClearIt()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);

        Frame(tiles, [Spot(entity: 1)]).Draws.Should().Equal(new TileDraw(View: 0, First: 0, Count: 0));
    }

    [Theory]
    [InlineData(-0.01f, 1)]
    [InlineData(0, 0)]
    public void ACasterBeyondTheReachIsNotDrawn(float beyondReach, int expectedCasters)
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance caster = Caster(entity: 7, position: new(Reach + 1 + beyondReach, 0, 0), radius: 1);

        List<TileDraw> draws = Frame(tiles, [Spot(entity: 1)], [caster]).Draws;

        draws.Single().Count.Should().Be(expectedCasters, "a caster is drawn when its sphere comes within the reach");
    }

    [Fact]
    public void ACasterOutsideAFaceIsNotDrawnInThatFace()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(5, 0, 0), radius: 0.5f)];

        (List<TileView> views, List<TileDraw> draws) = Frame(tiles, [Point(entity: 1)], casters);

        draws
            .Where(draw => draw.Count > 0)
            .Select(draw => views[draw.View].Aim)
            .Should()
            .Equal(new TileAim(new FaceAim(CubeFace.PositiveX)));
    }

    [Fact]
    public void ACasterJustOutsideAFacesQuarterTurnIsDrawnInItsMargin()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        // 0.2° past the edge of the +X face toward +Y, inside the 4 texels of the margin, about 0.45°.
        float angle = MathF.PI / 4 + 0.2f * MathF.PI / 180;
        Vector3 position = 5 * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
        PreparedInstance[] casters = [Caster(entity: 7, position: position, radius: 1e-3f)];

        (List<TileView> views, List<TileDraw> draws) = Frame(tiles, [Point(entity: 1)], casters);

        draws
            .Where(draw => draw.Count > 0)
            .Select(draw => views[draw.View].Aim)
            .Should()
            .Equal(new TileAim(new FaceAim(CubeFace.PositiveX)), new TileAim(new FaceAim(CubeFace.PositiveY)));
    }

    [Theory]
    [InlineData(0.4f, 1)]
    [InlineData(0.8f, 0)]
    public void ACasterOutsideASpotLightsConeIsNotDrawn(float angle, int expectedCasters)
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        Vector3 position = 10 * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
        PreparedInstance[] casters = [Caster(entity: 7, position: position, radius: 0.1f)];

        List<TileDraw> draws = Frame(tiles, [Spot(entity: 1)], casters).Draws;

        draws.Single().Count.Should().Be(expectedCasters);
    }

    [Fact]
    public void ACasterJustOutsideASpotLightsConeIsDrawnInTheTilesMargin()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        // 0.005 rad past the cone, inside the margin of the tile, about 0.0066 rad.
        float angle = Narrow + 0.005f;
        Vector3 position = 10 * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
        PreparedInstance[] casters = [Caster(entity: 7, position: position, radius: 1e-3f)];

        List<TileDraw> draws = Frame(tiles, [Spot(entity: 1)], casters).Draws;

        draws.Single().Count.Should().Be(1);
    }

    [Fact]
    public void ACasterBehindASpotLightIsNotDrawn()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(-1.2f, 0, 0), radius: 1)];

        Frame(tiles, [Spot(entity: 1)], casters)
            .Draws.Single()
            .Count.Should()
            .Be(0, "the sphere lies wholly behind the light, though it comes nearer the cone's side than its radius");
    }

    [Fact]
    public void ACasterThatReachesIntoASpotLightsConeFromOutsideIsDrawn()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        // At 0.6 rad, the center lies 0.93 m from the side of the widened cone, of half-angle 0.507 rad.
        Vector3 position = 10 * new Vector3(MathF.Cos(0.6f), MathF.Sin(0.6f), 0);
        PreparedInstance[] casters = [Caster(entity: 7, position: position, radius: 1)];

        Frame(tiles, [Spot(entity: 1)], casters).Draws.Single().Count.Should().Be(1);
    }

    [Fact]
    public void ACasterInTheCornerOfASpotLightsTileOutsideItsConeIsNotDrawn()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        // Half the depth across on each side axis is inside the square of tan 0.507 = 0.555, and 0.71 outside the cone.
        Vector3 position = 10 * Vector3.Normalize(new Vector3(1, 0.5f, 0.5f));
        PreparedInstance[] casters = [Caster(entity: 7, position: position, radius: 0.01f)];

        Frame(tiles, [Spot(entity: 1)], casters).Draws.Single().Count.Should().Be(0);
    }

    [Fact]
    public void ACasterThatReachesIntoAFaceFromOutsideIsDrawnInIt()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        // At 50° from +X toward +Y, the center lies 0.40 m outside the side plane of the widened +X face.
        float angle = 50 * MathF.PI / 180;
        Vector3 position = 5 * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
        PreparedInstance[] casters = [Caster(entity: 7, position: position, radius: 0.45f)];

        (List<TileView> views, List<TileDraw> draws) = Frame(tiles, [Point(entity: 1)], casters);

        draws
            .Where(draw => draw.Count > 0)
            .Select(draw => views[draw.View].Aim)
            .Should()
            .Contain(new TileAim(new FaceAim(CubeFace.PositiveX)));
    }

    [Fact]
    public void ACasterAroundASpotLightIsDrawn()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters = [Caster(entity: 7, position: new(-0.5f, 0, 0), radius: 1)];

        Frame(tiles, [Spot(entity: 1)], casters).Draws.Single().Count.Should().Be(1);
    }

    [Fact]
    public void TheCastersOfADrawAreInMeshOrderAndThenInTheOrderOfTheList()
    {
        var tiles = new ShadowTiles(layers: 1, tileSize: TileSize);
        PreparedInstance[] casters =
        [
            Caster(entity: 7, position: new(10, 0, 0), mesh: 2),
            Caster(entity: 8, position: new(11, 0, 0), mesh: 0),
            Caster(entity: 9, position: new(12, 0, 0), mesh: 1),
            Caster(entity: 10, position: new(13, 0, 0), mesh: 0),
        ];

        TileDraw draw = Frame(tiles, [Spot(entity: 1)], casters).Draws.Single();

        CastersOf(tiles, draw).Should().Equal(1, 3, 2, 0);
    }

    [Fact]
    public void EachDrawHasItsOwnRunOfCasters()
    {
        var tiles = new ShadowTiles(layers: 6, tileSize: TileSize);
        PreparedInstance[] casters =
        [
            Caster(entity: 7, position: new(0, 0, -5), radius: 0.5f),
            Caster(entity: 8, position: new(5, 0, 0), radius: 0.5f),
        ];

        (List<TileView> views, List<TileDraw> draws) = Frame(tiles, [Point(entity: 1)], casters);

        draws
            .Where(draw => draw.Count > 0)
            .Select(draw => (views[draw.View].Aim, Casters: CastersOf(tiles, draw).Single()))
            .Should()
            .Equal(
                (Aim: new TileAim(new FaceAim(CubeFace.PositiveX)), Casters: 1),
                (Aim: new TileAim(new FaceAim(CubeFace.NegativeZ)), Casters: 0)
            );
    }

    [Fact]
    public void EachFaceOfAPointLightLooksAlongItsCubeFace()
    {
        var place = new Vector3(3, -2, 7);
        float widening = (TileSize - 2f * ShadowTiles.MarginTexels) / TileSize;

        foreach (CubeFace face in Enum.GetValues<CubeFace>())
        {
            Matrix4x4 tile = ShadowTiles.ViewProjection(Point(entity: 1, position: place), new FaceAim(face), TileSize);
            Matrix4x4 cube = CubeFaces.ViewProjection(CubeFaces.Basis(face), DepthRanges.Near);
            FaceBasis basis = CubeFaces.Basis(face);
            Vector3 offset = 4 * basis.Forward + 1.5f * basis.Right - 2.5f * basis.Up;

            Vector3 inTile = Ndc(tile, place + offset);
            Vector3 inCube = Ndc(cube, offset);

            Vector2
                .Distance(new(inTile.X, inTile.Y), new Vector2(inCube.X, inCube.Y) * widening)
                .Should()
                .BeLessThan(1e-5f, $"the tile of {face} is its cube face, widened");
        }
    }

    [Fact]
    public void ATileWidensByItsMarginOnEverySide()
    {
        PreparedLight light = Point(entity: 1);
        Matrix4x4 tile = ShadowTiles.ViewProjection(light, new FaceAim(CubeFace.PositiveZ), TileSize);
        float edge = (TileSize - 2f * ShadowTiles.MarginTexels) / TileSize;

        Vector3[] corners = [new(1, 1, 1), new(-1, 1, 1), new(1, -1, 1), new(-1, -1, 1)];

        corners
            .Select(corner => Ndc(tile, corner))
            .Select(ndc => new Vector2(MathF.Abs(ndc.X), MathF.Abs(ndc.Y)))
            .Should()
            .AllSatisfy(ndc => Vector2.Distance(ndc, new(edge)).Should().BeLessThan(1e-6f));
    }

    [Fact]
    public void ASpotLightsTileWidensItsConeByTheMarginOnEverySide()
    {
        PreparedLight light = Spot(entity: 1, axis: new(0, 0, -1));
        Matrix4x4 tile = ShadowTiles.ViewProjection(light, new ConeAim(), TileSize);
        float edge = (TileSize - 2f * ShadowTiles.MarginTexels) / TileSize;
        float side = MathF.Tan(Narrow);

        Vector3[] edges = [new(side, 0, -1), new(-side, 0, -1), new(0, side, -1), new(0, -side, -1)];

        edges
            .Select(point => Ndc(tile, point))
            .Select(ndc => MathF.Max(MathF.Abs(ndc.X), MathF.Abs(ndc.Y)))
            .Should()
            .AllSatisfy(extent => extent.Should().BeApproximately(edge, 1e-5f));
    }

    [Fact]
    public void ASpotLightsTileLooksAlongItsAxis()
    {
        var place = new Vector3(1, 2, 3);
        var axis = Vector3.Normalize(new Vector3(1, -2, 0.5f));
        Matrix4x4 tile = ShadowTiles.ViewProjection(
            Spot(entity: 1, position: place, axis: axis),
            new ConeAim(),
            TileSize
        );

        Vector3 ndc = Ndc(tile, place + 6 * axis);

        new Vector2(ndc.X, ndc.Y).Length().Should().BeLessThan(1e-6f);
        ndc.Z.Should()
            .BeInRange(0, 1, "a point in front of the light has a depth between infinity's and the near plane's");
    }

    [Theory]
    [InlineData(0f, 0.05f)]
    [InlineData(0.2f, 0.2f)]
    public void ATileMapsItsNearPlaneToDepthOne(float radius, float near)
    {
        PreparedLight light = Point(entity: 1) with { Radius = radius };
        Matrix4x4 tile = ShadowTiles.ViewProjection(light, new FaceAim(CubeFace.PositiveX), TileSize);

        Ndc(tile, new(near, 0, 0)).Z.Should().BeApproximately(1, 1e-6f, "the near plane is max(R, 5 cm)");
    }

    [Fact]
    public void ATilesDepthFallsTowardZeroAtInfinity()
    {
        PreparedLight light = Point(entity: 1);
        Matrix4x4 tile = ShadowTiles.ViewProjection(light, new FaceAim(CubeFace.PositiveX), TileSize);

        Ndc(tile, new(1e6f, 0, 0))
            .Z.Should()
            .BeApproximately(0.05f / 1e6f, 1e-12f, "reversed Z with an infinite far plane");
    }

    [Fact]
    public void ATexelOfAFaceAtOneMetreIsTwiceItsWidenedTangentOverTheTileSize() =>
        ShadowTiles
            .TexelPerMeter(Point(entity: 1), new FaceAim(CubeFace.NegativeY), TileSize)
            .Should()
            .BeApproximately(2f * TileSize / (TileSize - 2 * ShadowTiles.MarginTexels) / TileSize, 1e-8f);

    [Fact]
    public void ATileAlongTheConeOfASpotLightWiderThanSixtyDegreesIsRefused()
    {
        PreparedLight light = Spot(entity: 1, outerAngle: 2);

        FluentActions
            .Invoking(() => ShadowTiles.ViewProjection(light, new ConeAim(), TileSize))
            .Should()
            .Throw<ArgumentException>("such a light takes six faces, and the tangent of its half-angle is negative");
    }

    [Fact]
    public void NoTwoTileViewsOfAFrameShareALayer()
    {
        var tiles = new ShadowTiles(layers: 14, tileSize: TileSize);
        PreparedLight[][] frames =
        [
            [Spot(entity: 1), Point(entity: 2), Spot(entity: 3)],
            [Point(entity: 4, intensity: 9), Spot(entity: 1), Spot(entity: 3, outerAngle: 1.2f)],
            [Spot(entity: 5), Point(entity: 4), Point(entity: 2, intensity: 3), Spot(entity: 1)],
            [Spot(entity: 1, outerAngle: 1.2f), Point(entity: 2), Spot(entity: 6)],
        ];

        List<TileView>[] views = [.. frames.Select(lights => Frame(tiles, lights).Views)];

        views.Should().AllSatisfy(frame => frame.Select(view => view.Layer).Should().OnlyHaveUniqueItems());
    }

    /// <summary>The frame meshes of a test frame: static meshes, then one deformed copy per job.</summary>
    private sealed record FrameMeshes(MeshInfo[] Meshes, int StaticMeshCount, DeformJob[] Jobs, Vector4[] Poses)
    {
        /// <summary>Four static meshes and no copy.</summary>
        public static readonly FrameMeshes Static = new(
            Meshes: [.. Enumerable.Repeat(StaticMesh(deform: null), 4)],
            StaticMeshCount: 4,
            Jobs: [],
            Poses: []
        );

        public DeformedMeshes Deformed =>
            new(meshes: Meshes, staticMeshCount: StaticMeshCount, jobs: Jobs, poses: Poses);

        /// <summary>A rigid mesh 0, <paramref name="mesh"/> as static mesh 1, and its copy in <paramref name="pose"/> as frame mesh 2.</summary>
        public static FrameMeshes Posed(MeshData mesh, RigPose pose)
        {
            MeshDeform deform = MeshDeform.Pack(StagedMesh.Of(mesh))!.Deform;
            var rows = new Vector4[deform.PoseRows];
            float bound = deform.PoseInto(
                previousJoints: pose.Joints.AsSpan(),
                joints: pose.Joints.AsSpan(),
                previousWeights: pose.Weights.AsSpan(),
                weights: pose.Weights.AsSpan(),
                alpha: 1,
                rows: rows
            );
            return new(
                Meshes:
                [
                    StaticMesh(deform: null),
                    StaticMesh(deform),
                    StaticMesh(deform: null) with
                    {
                        Radius = bound,
                    },
                ],
                StaticMeshCount: 2,
                Jobs: [new(Mesh: 1, Target: 0, Pose: 0)],
                Poses: rows
            );
        }

        /// <summary>
        /// A rigid mesh 0, <paramref name="mesh"/> as static mesh 1, and two copies of it: frame mesh 2 in
        /// <paramref name="first"/>, and frame mesh 3 in <paramref name="second"/>, or in <paramref name="first"/> too.
        /// </summary>
        public static FrameMeshes PosedTwice(MeshData mesh, RigPose first, RigPose? second = null)
        {
            FrameMeshes one = Posed(mesh, first),
                two = Posed(mesh, second ?? first);
            MeshDeform deform = MeshDeform.Pack(StagedMesh.Of(mesh))!.Deform;
            return new(
                Meshes: [.. one.Meshes, two.Meshes[2]],
                StaticMeshCount: 2,
                Jobs:
                [
                    new(Mesh: 1, Target: 0, Pose: 0),
                    new(Mesh: 1, Target: deform.VertexCount, Pose: deform.PoseRows),
                ],
                Poses: [.. one.Poses, .. two.Poses]
            );
        }

        private static MeshInfo StaticMesh(MeshDeform? deform) =>
            new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 1, Deform: deform);
    }
}
