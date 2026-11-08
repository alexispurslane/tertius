using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The light list on the CPU (docs/plans/local-light-shadows.md, Part 4): which lights of the snapshot the batcher keeps,
/// interpolated by alpha, how a kept light reaches the shaders, and the far shadows of each far light, cut from the frame's
/// casters, which the batcher interpolates once. No GPU needed. The camera sits at the render origin and looks down −Z at
/// 1600 × 900 with a 60 degree field of view, so one pixel is about 1.28e-3 rad. Unless a test names another, the frame's
/// highest exposure is −10 stops, where the cutoff irradiance is π · 2^−13 · 2^10 = π / 8 W/m², so that a lamp's reach is
/// a few meters.
/// </summary>
public sealed class BatcherLightTests
{
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 600, VertexOffset: 0, Radius: 1f, Deform: null),
    ];

    private const float TenStopsDown = -10;

    /// <summary>The cutoff irradiance at −10 stops, in W/m².</summary>
    private static readonly double Cutoff = LightUnits.CutoffIrradiance(TenStopsDown);

    /// <summary>A snapshot with no light: the test star of <see cref="Gpu.Snapshot"/> has no power, so it does not shine.</summary>
    private static RenderSnapshot Dark() => Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);

    private static SceneBatcher Batcher(int cascadedLights = 1) => Batchers.WithCascadedLights(cascadedLights);

    private static PreparedScene Prepare(
        RenderSnapshot snapshot,
        float alpha = 1f,
        SceneBatcher? batcher = null,
        EnvironmentDraw? environment = null,
        float highestStops = TenStopsDown
    )
    {
        var scene = new PreparedScene();
        (batcher ?? Batcher()).Prepare(
            snapshot: snapshot,
            alpha: alpha,
            highestStops: highestStops,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            environment: environment ?? new WithoutMap(),
            region: DeformRegion.None,
            frame: Batchers.FirstFrame(),
            scene: scene
        );
        return scene;
    }

    /// <summary>A white lamp of radius 0 at <paramref name="position"/> whose reach at −10 stops is <paramref name="reach"/> m.</summary>
    private static PointRenderLight Lamp(Vector3 position, float reach = 10f) =>
        Gpu.Lamp(
            position: position,
            color: Vector3.One,
            intensity: (float)(reach * reach * Cutoff),
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A white spot light of radius 0 at <paramref name="position"/> along <paramref name="direction"/> whose reach is 10 m.</summary>
    private static SpotRenderLight Beam(Vector3 position, Vector3 direction) =>
        Gpu.Spot(
            position: position,
            direction: direction,
            color: Vector3.One,
            intensity: (float)(100 * Cutoff),
            innerConeAngle: 0.3f,
            outerConeAngle: 0.5f,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A white star 1.5 × 10¹¹ m along <paramref name="direction"/>, that casts shadows.</summary>
    private static PointRenderLight Star(Vector3 direction, float irradiance = 3f, float angularRadius = 0.00467f) =>
        Gpu.StarLight(
            direction: direction,
            irradiance: irradiance,
            angularRadius: angularRadius,
            color: Vector3.One,
            chainDepth: TestChains.Root
        );

    /// <summary>A sphere of mesh 0 that casts shadows, drawn or not.</summary>
    private static RenderInstance Caster(Vector3 position, float radius, bool visible = true) =>
        RenderInstance.Create(
            mesh: new(0),
            material: new(0),
            position: position,
            rotation: Quaternion.Identity,
            scale: new(radius),
            boundingRadius: 0,
            hemisphere: HemisphereUp.Even,
            chainDepth: TestChains.CamerasFrame,
            flags: (visible ? RenderFlags.Visible : RenderFlags.None) | RenderFlags.CastsShadow,
            entityIndex: 0,
            part: 0
        );

    private static Vector3 Xyz(Vector4 value) => new(value.X, value.Y, value.Z);

    /// <summary>The casters of the frame: its placed instances that cast shadows, in the snapshot's order.</summary>
    private static PreparedInstance[] Casters(PreparedScene scene) =>
        [.. scene.PlacedSpan.ToArray().Where(placed => placed.CastsShadow)];

    /// <summary>A unit instance of mesh 0 with <paramref name="flags"/> alone, named <paramref name="entity"/>.</summary>
    private static RenderInstance Flagged(uint entity, Vector3 position, RenderFlags flags) =>
        Gpu.Instance(
            mesh: new(0),
            material: new(0),
            position: position,
            scale: Vector3.One,
            entity: entity,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            Flags = flags,
        };

    /// <summary>The occluders of the far light <paramref name="light"/>: its own run of the frame's occluders.</summary>
    private static Vector4[] OccludersOf(PreparedScene scene, in PreparedLight light) =>
        light.Shadow switch
        {
            CascadeSet { Occluders: var range } => scene.Occluders.AsSpan(range.Start, range.Count).ToArray(),
            FarShadowsOnly(var range) => scene.Occluders.AsSpan(range.Start, range.Count).ToArray(),
            AtlasTiles or null => [],
        };

    // ---- Interpolation ----------------------------------------------------------------------------------------------

    [Fact]
    public void TheBatcherInterpolatesALightsPlaceByAlpha()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Camera.PreviousPosition = new(0, 0, 2);
        PointRenderLight lamp = Lamp(new(0, 1, -5));
        snapshot.Lights.Add(lamp with { PreviousPosition = new(0, 1, -3) });

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        // Halfway, the lamp is at (0, 1, -4) and the eye at (0, 0, 1).
        scene.LightCount.Should().Be(1);
        scene.Lights[0].Position.Should().Be(new Vector3(0, 1, -5));
        scene.Lights[0].Distance.Should().BeApproximately(Math.Sqrt(26), 1e-6);
    }

    [Fact]
    public void TheBatcherInterpolatesASpotLightsAxisByAlpha()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Beam(new(0, 1, -5), -Vector3.UnitY) with { PreviousDirection = Vector3.UnitX });

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        scene.Lights[0].Spread.TryGetValue(out Cone cone).Should().BeTrue();
        Vector3.Distance(cone.Axis, Vector3.Normalize(new(1, -1, 0))).Should().BeLessThan(1e-6f);
    }

    // ---- Whose light it is -------------------------------------------------------------------------------------------

    /// <summary>
    /// The shadow tiles follow a light from frame to frame by its whole entity, slot and generation, because a new entity
    /// can take the slot of a destroyed one (docs/plans/local-light-shadows.md, "The cache"). The batcher builds that entity.
    /// </summary>
    [Fact]
    public void AKeptLightCarriesTheSlotAndTheGenerationOfItsEntity()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(0, 1, -5)) with { EntityIndex = 4, EntityGeneration = 5 });
        snapshot.Lights.Add(Beam(new(0, 3, -5), -Vector3.UnitY) with { EntityIndex = 9, EntityGeneration = 2 });

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene
            .LightSpan.ToArray()
            .Select(light => light.Entity)
            .Should()
            .BeEquivalentTo([new Entity(Index: 4, Generation: 5), new Entity(Index: 9, Generation: 2)]);
    }

    // ---- Which lights are kept --------------------------------------------------------------------------------------

    /// <summary>Each reason why a light does not shine, by name.</summary>
    public static TheoryData<string> DarkLights =>
        [
            "no power",
            "a negative power",
            "a power that is not a number",
            "an infinite power",
            "a black color",
            "a color with a negative channel",
            "a color that is not a number",
            "an infinite color",
            "a negative radius",
            "a radius that is not a number",
            "an infinite radius",
            "a place that is not a number",
            "a previous place that is not a number",
            "an infinite spot axis",
            "a previous spot axis that is not a number",
            "an inner cone angle that is not a number",
            "an infinite outer cone angle",
        ];

    private static RenderLight DarkLight(string reason)
    {
        PointRenderLight lamp = Lamp(new(0, 0, -5));
        SpotRenderLight beam = Beam(new(0, 0, -5), -Vector3.UnitY);
        return reason switch
        {
            "no power" => lamp with { Power = 0 },
            "a negative power" => lamp with { Power = -1 },
            "a power that is not a number" => lamp with { Power = float.NaN },
            "an infinite power" => lamp with { Power = float.PositiveInfinity },
            "a black color" => lamp with { Color = Vector3.Zero },
            "a color with a negative channel" => lamp with { Color = new(1, -1, 1) },
            "a color that is not a number" => lamp with { Color = new(1, float.NaN, 1) },
            "an infinite color" => lamp with { Color = new(float.PositiveInfinity, 1, 1) },
            "a negative radius" => lamp with { Radius = -1 },
            "a radius that is not a number" => lamp with { Radius = float.NaN },
            "an infinite radius" => lamp with { Radius = float.PositiveInfinity },
            "a place that is not a number" => lamp with { Position = new(float.NaN, 0, -5) },
            "a previous place that is not a number" => beam with { PreviousPosition = new(0, float.NaN, -5) },
            "an infinite spot axis" => beam with { Direction = new(float.PositiveInfinity, -1, 0) },
            "a previous spot axis that is not a number" => beam with { PreviousDirection = new(0, float.NaN, 0) },
            "an inner cone angle that is not a number" => beam with { InnerConeAngle = float.NaN },
            "an infinite outer cone angle" => beam with { OuterConeAngle = float.PositiveInfinity },
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No such dark light."),
        };
    }

    [Theory]
    [MemberData(nameof(DarkLights))]
    public void ALightThatDoesNotShineIsLeftOut(string reason)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(DarkLight(reason));
        snapshot.Lights.Add(default);
        // One of each case that shines, to show that the others were left out and not everything.
        snapshot.Lights.Add(Lamp(new(0, 0, -6)));
        snapshot.Lights.Add(Beam(new(0, 0, -7), -Vector3.UnitY));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene
            .LightSpan.ToArray()
            .Select(light => light.Position.Z)
            .Should()
            .Equal([-6f, -7f], "a light with {0} does not shine, and the empty light holds no light", reason);
    }

    [Fact]
    public void ALightWhoseReachMissesEveryViewIsLeftOut()
    {
        RenderSnapshot snapshot = Dark();
        // Behind the camera: its reach of 10 m stops 40 m behind the eye. A lamp ahead, as far away, reaches the view.
        snapshot.Lights.Add(Lamp(new(0, 0, 50)));
        snapshot.Lights.Add(Beam(new(0, 0, 50), -Vector3.UnitY));
        snapshot.Lights.Add(Lamp(new(0, 0, -50)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightSpan.ToArray().Select(light => light.Position).Should().Equal(new Vector3(0, 0, -50));
    }

    [Fact]
    public void ALightsReachEndsWhereItsIrradianceFallsBelowTheCutoffOfTheFramesHighestExposure()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(0, 0, -5)));

        float reachAtTenStopsDown = Prepare(snapshot: snapshot).Lights[0].Reach;
        float reachAtEightStopsDown = Prepare(snapshot: snapshot, highestStops: -8).Lights[0].Reach;

        reachAtTenStopsDown.Should().BeApproximately(10, 1e-4f);
        reachAtEightStopsDown.Should().BeApproximately(20, 1e-4f, "two stops more exposure quarter the cutoff");
    }

    [Fact]
    public void ALightThatOnlyAHigherExposureLetsReachTheViewIsLeftOut()
    {
        RenderSnapshot snapshot = Dark();
        // 15 m behind the eye: its reach is 10 m at −10 stops and 20 m at −8 stops.
        snapshot.Lights.Add(Lamp(new(0, 0, 15)));

        Prepare(snapshot: snapshot).LightCount.Should().Be(0);
        Prepare(snapshot: snapshot, highestStops: -8).LightCount.Should().Be(1);
    }

    [Fact]
    public void ALightTooBrightForAFloatIsLeftOut()
    {
        // The largest channel of color × intensity may be at most 10^34 W/sr: a sum of such lights stays finite in the
        // shaders. Each of these is a finite float on the CPU.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(
            Gpu.Lamp(
                position: new(0, 0, -5),
                color: new(0.1f, 2, 0.1f),
                intensity: 1e34f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            Gpu.Lamp(
                position: new(0, 0, -6),
                color: Vector3.One,
                intensity: 0.5e34f,
                chainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(1);
        scene.Lights[0].Position.Z.Should().Be(-6);
        scene.Lights[0].RadiantIntensity.X.Should().BeApproximately(0.5e34f, 1e28f);
    }

    // ---- How a kept light reaches the shaders -----------------------------------------------------------------------

    [Fact]
    public void AKeptLightReachesTheShadersAsItsIntensityReachAndCone()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Camera.PreviousPosition = new(0, 0, 2);
        snapshot.Lights.Add(
            Gpu.Lamp(
                position: new(0, 1, -5),
                color: new(1, 0.5f, 0.25f),
                intensity: 4,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(Beam(new(0, 3, -5), new(0, -2, 0)));

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        scene.LightCount.Should().Be(2);
        scene.Constants.Counts.X.Should().Be(2u);
        GpuLight point = GpuLight.From(scene.Lights[0]),
            spot = GpuLight.From(scene.Lights[1]);
        // The eye is at (0, 0, 1) halfway. The reach of 4 W/sr is √(4 / (π / 8)) m.
        Xyz(point.PositionReach).Should().Be(new Vector3(0, 1, -6));
        point.PositionReach.W.Should().BeApproximately((float)Math.Sqrt(4 / Cutoff), 1e-4f);
        Xyz(point.IntensityKind).X.Should().BeApproximately(4, 1e-5f);
        Xyz(point.IntensityKind).Y.Should().BeApproximately(2, 1e-5f);
        Xyz(point.IntensityKind).Z.Should().BeApproximately(1, 1e-5f);
        point.IntensityKind.W.Should().Be(GpuLight.PointKind);
        Xyz(spot.PositionReach).Should().Be(new Vector3(0, 3, -6));
        spot.PositionReach.W.Should().BeApproximately(10, 1e-4f);
        spot.IntensityKind.W.Should().Be(GpuLight.SpotKind);
        Xyz(spot.DirectionCosOuter).Should().Be(new Vector3(0, -1, 0));
        spot.DirectionCosOuter.W.Should().BeApproximately(MathF.Cos(0.5f), 1e-5f);
        spot.SpotRadius.X.Should().BeApproximately(MathF.Cos(0.3f), 1e-5f);
    }

    [Fact]
    public void APointLightLeavesTheConeOfItsGpuLightZeroBecauseItHasNone()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(0, 1, -5)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(1);
        var point = GpuLight.From(scene.Lights[0]);
        point.DirectionCosOuter.Should().Be(Vector4.Zero);
        point.SpotRadius.X.Should().Be(0);
    }

    /// <summary>
    /// A white tube light of 40 W, 0.1 m thick and 2 m long, at <paramref name="position"/> now and at
    /// <paramref name="previousPosition"/> before, along <paramref name="axis"/> now and <paramref name="previousAxis"/>
    /// before. Its reach at −10 stops is about 2.7 m from its segment.
    /// </summary>
    private static TubeRenderLight Tube(
        Vector3 position,
        Vector3 previousPosition,
        Vector3 axis,
        Vector3 previousAxis,
        float length = 2
    ) =>
        new(
            EntityIndex: 5,
            EntityGeneration: 1,
            Position: position,
            PreviousPosition: previousPosition,
            Axis: axis,
            PreviousAxis: previousAxis,
            Color: Vector3.One,
            Power: 40,
            Radius: 0.1f,
            Length: length,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );

    [Fact]
    public void ATubeLightReachesTheShadersAsItsPeakIntensityItsHalfSegmentAndAReachThatHoldsItsSegment()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Camera.PreviousPosition = new(0, 0, 2);
        snapshot.Lights.Add(
            Tube(
                position: new(0, 1, -5),
                previousPosition: new(2, 1, -5),
                axis: new(0, 0, 3),
                previousAxis: new(3, 0, 0)
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        var tube = GpuLight.From(scene.Lights[0]);
        double intensity = LightUnits.TubeIntensity(power: 40, radius: 0.1f, length: 2);
        tube.IntensityKind.W.Should().Be(GpuLight.TubeKind);
        // The eye is at (0, 0, 1) halfway, and the axis halfway between X and Z.
        Xyz(tube.PositionReach).Should().Be(new Vector3(1, 1, -6));
        Xyz(tube.IntensityKind).X.Should().BeApproximately((float)intensity, 1e-5f);
        Xyz(tube.DirectionCosOuter).X.Should().BeApproximately(MathF.Sqrt(0.5f), 1e-6f, "half the length is 1 m");
        Xyz(tube.DirectionCosOuter).Y.Should().Be(0);
        Xyz(tube.DirectionCosOuter).Z.Should().BeApproximately(MathF.Sqrt(0.5f), 1e-6f);
        tube.PositionReach.W.Should()
            .BeApproximately(
                (float)(Math.Sqrt(intensity / Cutoff) + 1),
                1e-4f,
                "the peak intensity's reach from the segment, which ends 1 m from the middle"
            );
        tube.SpotRadius.Y.Should().Be(0.1f);
    }

    [Fact]
    public void ATubeLightOfLengthZeroReachesTheShadersAsThePointLightOfItsPower()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(
            Tube(
                position: new(0, 1, -5),
                previousPosition: new(0, 1, -5),
                axis: Vector3.UnitX,
                previousAxis: Vector3.UnitX,
                length: 0
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        var tube = GpuLight.From(scene.Lights[0]);
        tube.IntensityKind.W.Should().Be(GpuLight.PointKind);
        tube.DirectionCosOuter.Should().Be(Vector4.Zero);
        Xyz(tube.IntensityKind).X.Should().BeApproximately((float)(40 / (4 * Math.PI)), 1e-5f);
    }

    [Fact]
    public void ATubeLightWithoutAnAxisStandsUpright()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(
            Tube(
                position: new(0, 1, -5),
                previousPosition: new(0, 1, -5),
                axis: Vector3.Zero,
                previousAxis: Vector3.Zero
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        Xyz(GpuLight.From(scene.Lights[0]).DirectionCosOuter).Should().Be(new Vector3(0, 1, 0));
    }

    [Theory]
    [InlineData("axis")]
    [InlineData("previous-axis")]
    [InlineData("negative-length")]
    [InlineData("endless-length")]
    [InlineData("overlong-length")]
    [InlineData("too-strong")]
    public void ATubeLightWithABrokenFieldDoesNotShine(string broken)
    {
        TubeRenderLight tube = Tube(
            position: new(0, 1, -5),
            previousPosition: new(0, 1, -5),
            axis: Vector3.UnitX,
            previousAxis: Vector3.UnitX
        );
        TubeRenderLight wrong = broken switch
        {
            "axis" => tube with { Axis = new(float.NaN, 0, 0) },
            "previous-axis" => tube with { PreviousAxis = new(0, float.PositiveInfinity, 0) },
            "negative-length" => tube with { Length = -0.5f },
            "endless-length" => tube with { Length = float.PositiveInfinity },
            // Finite, with a finite peak intensity, but its ends lie beyond the position limit.
            "overlong-length" => tube with { Length = 1e30f },
            "too-strong" => tube with { Power = float.MaxValue, Color = new(1e30f, 1, 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(broken), broken, "No such field."),
        };

        SceneBatcher.Shines(tube).Should().BeTrue("the tube itself shines");
        SceneBatcher.Shines(wrong).Should().BeFalse("a tube light with a broken {0} adds no light", broken);
    }

    [Fact]
    public void ASpotLightWithoutAnAxisShinesStraightDown()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Beam(new(0, 1, -5), Vector3.Zero));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Lights[0].Spread.TryGetValue(out Cone cone).Should().BeTrue();
        cone.Axis.Should().Be(new Vector3(0, -1, 0));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.7f)]
    public void ASpotLightWhoseInnerConeReachesItsOuterConeStillFadesBetweenTwoDistinctEdges(float inner)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Beam(new(0, 1, -5), new(0, -1, 0)) with { InnerConeAngle = inner, OuterConeAngle = 0.5f });

        PreparedScene scene = Prepare(snapshot: snapshot);

        var spot = GpuLight.From(scene.Lights[0]);
        spot.SpotRadius.X.Should()
            .BeGreaterThan(spot.DirectionCosOuter.W, "smoothstep has no defined value when its edges meet or cross");
    }

    [Fact]
    public void ManyLightsOfBothCasesGrowTheLightListAndKeepTheirOrder()
    {
        // The list starts at 16 lights. Light i is a point light when i is even, a spot light when it is odd.
        RenderSnapshot snapshot = Dark();
        for (int i = 0; i < 40; i++)
        {
            Vector3 place = new(0, 1, -5 - 0.1f * i);
            snapshot.Lights.Add(i % 2 == 0 ? Lamp(place) : Beam(place, -Vector3.UnitY));
        }

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(40);
        for (int i = 0; i < 40; i++)
        {
            scene.Lights[i].Position.Z.Should().BeApproximately(-5 - 0.1f * i, 1e-5f, $"light {i} keeps its place");
            scene.Lights[i].Spread.HasValue.Should().BeTrue();
            (scene.Lights[i].Spread.Value is Cone).Should().Be(i % 2 == 1, $"light {i} keeps its case");
        }
    }

    // ---- Far shadows of each far light ------------------------------------------------------------------------------

    [Fact]
    public void EachShadowedFarLightHasItsOwnOccluders()
    {
        // One cascade set: the brighter star takes it, and the dimmer one gets far shadows only.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Star(Vector3.UnitX, irradiance: 1));
        snapshot.Lights.Add(Star(Vector3.UnitY, irradiance: 3));
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Lights[1].Shadow.Value.Should().BeOfType<CascadeSet>();
        scene.Lights[0].Shadow.Value.Should().BeOfType<FarShadowsOnly>();
        OccludersOf(scene, scene.Lights[0]).Should().Equal(new Vector4(x: 0, y: 0, z: -3e7f, w: 6e6f));
        OccludersOf(scene, scene.Lights[1]).Should().Equal(new Vector4(x: 0, y: 0, z: -3e7f, w: 6e6f));
        scene.OccluderCount.Should().Be(2, "each light has its own run, even of the same caster");
    }

    [Fact]
    public void TheFarShadowsOfALightArePrunedByItsOwnAngularRadius()
    {
        // A moon 9e-4 rad across, under a pixel: it hides a share of the small star near Saturn, 4.85e-4 rad in radius,
        // and too little of the star of 4.67e-3 rad to count.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Star(Vector3.UnitX, angularRadius: 0.00467f));
        snapshot.Lights.Add(Star(Vector3.UnitY, angularRadius: 4.85e-4f));
        snapshot.Instances.Add(Caster(new(0, 0, -1e9f), 4.5e5f));

        PreparedScene scene = Prepare(snapshot: snapshot, batcher: Batcher(cascadedLights: 0));

        OccludersOf(scene, scene.Lights[0]).Should().BeEmpty();
        OccludersOf(scene, scene.Lights[1]).Should().Equal(new Vector4(x: 0, y: 0, z: -1e9f, w: 4.5e5f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AnInstanceThatCastsNoShadowIsNoFarShadow(int cascadedLights)
    {
        // A planet beyond the shadow maps, then a ship of 50 m 500 m away, which the maps would also draw: both drawn, and
        // neither casts a shadow.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Star(Vector3.UnitY, irradiance: 3));
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f) with { Flags = RenderFlags.Visible });
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50) with { Flags = RenderFlags.Visible });

        PreparedScene scene = Prepare(snapshot: snapshot, batcher: Batcher(cascadedLights: cascadedLights));

        scene.VisibleCount.Should().Be(2);
        OccludersOf(scene, scene.Lights[0]).Should().BeEmpty();
    }

    [Fact]
    public void OnlyACascadeSetPutsItsMappedCastersFirst()
    {
        // A planet beyond the shadow maps, then a ship of 50 m 500 m away, which the maps also draw.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Star(Vector3.UnitY, irradiance: 3));
        snapshot.Lights.Add(Star(Vector3.UnitX, irradiance: 1));
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Lights[0].Shadow.TryGetValue(out CascadeSet set).Should().BeTrue();
        set.Mapped.Should().Be(1);
        OccludersOf(scene, scene.Lights[0]).Select(occluder => occluder.Z).Should().Equal(-500f, -3e7f);
        scene.Lights[1].Shadow.Value.Should().BeOfType<FarShadowsOnly>();
        OccludersOf(scene, scene.Lights[1]).Select(occluder => occluder.Z).Should().Equal(-3e7f, -500f);
        GpuLight.From(scene.Lights[0]).Occluders.Should().Be(new UInt3(X: 0, Y: 2, Z: 1));
        GpuLight.From(scene.Lights[1]).Occluders.Should().Be(new UInt3(X: 2, Y: 2, Z: 0));
    }

    [Fact]
    public void ANearLightHasNoFarShadows()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(0, 1, -5)) with { CastsShadow = true });
        snapshot.Lights.Add(Star(Vector3.UnitY) with { CastsShadow = false });
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(2);
        scene.Lights[0].Shadow.HasValue.Should().BeFalse("a near light is not far");
        scene.Lights[1].Shadow.HasValue.Should().BeFalse("a far light that casts no shadow has no slot");
        scene.OccluderCount.Should().Be(0);
    }

    [Fact]
    public void TheCastersOfAFrameAreInterpolatedOnce()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Camera.PreviousPosition = new(0, 0, 8);
        RenderInstance moving = Caster(new(0, 0, -5_000), 1_000, visible: false);
        moving.PreviousPosition = new(0, 2_000, -5_000);
        snapshot.Instances.Add(moving);
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(0, 0, -20),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(Star(Vector3.UnitY));
        snapshot.Lights.Add(Star(Vector3.UnitX, irradiance: 1));

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.25f);

        // A quarter of the way, the moving caster is at (0, 1,500, -5,000) and the camera at (0, 0, 6). The third
        // instance casts no shadow.
        Casters(scene)
            .Select(caster => caster.Position)
            .Should()
            .Equal(new Vector3(0, 1_500, -5_006), new Vector3(0, 0, -506));
        // Both lights read the one interpolated place.
        OccludersOf(scene, scene.Lights[0]).Should().Contain(new Vector4(x: 0, y: 1_500, z: -5_006, w: 1_000));
        OccludersOf(scene, scene.Lights[1]).Should().Contain(new Vector4(x: 0, y: 1_500, z: -5_006, w: 1_000));
    }

    [Fact]
    public void EachInstanceThatSomeViewMayDrawIsPlacedOnceInTheSnapshotsOrder()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Flagged(entity: 1, position: new(0, 0, -10), flags: RenderFlags.Visible));
        snapshot.Instances.Add(Flagged(entity: 2, position: new(0, 0, -10), flags: RenderFlags.CastsShadow));
        snapshot.Instances.Add(
            Flagged(entity: 3, position: new(0, 0, -10), flags: RenderFlags.Visible | RenderFlags.CastsShadow)
        );
        snapshot.Instances.Add(Flagged(entity: 4, position: new(0, 0, -10), flags: RenderFlags.None));
        snapshot.Instances.Add(
            Flagged(entity: 5, position: new(0, 0, -10), flags: RenderFlags.Visible) with
            {
                Mesh = MeshId.None,
            }
        );
        // Behind the camera, so that the main view culls it.
        snapshot.Instances.Add(Flagged(entity: 6, position: new(0, 0, 10), flags: RenderFlags.Visible));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene
            .PlacedSpan.ToArray()
            .Select(placed => placed.Entity.Index)
            .Should()
            .Equal([1u, 2u, 3u, 6u], "no view draws the fourth, and the fifth has no mesh on the GPU");
        scene.VisibleSpan.ToArray().Select(visible => visible.Entity.Index).Should().Equal(1u, 3u);
    }

    [Fact]
    public void AVisibleCasterCastsFromThePlaceAndTheTurnThatTheMainViewDraws()
    {
        RenderSnapshot snapshot = Dark();
        RenderInstance moving = Caster(new(0, 0, -50), 1) with
        {
            PreviousPosition = new(4, 0, -50),
            PreviousRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f),
        };
        snapshot.Instances.Add(moving);

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.25f);

        PreparedInstance caster = Casters(scene).Should().ContainSingle().Subject;
        PreparedInstance drawn = scene.VisibleSpan.ToArray().Should().ContainSingle().Subject;
        (caster.Position, caster.Rotation).Should().Be((drawn.Position, drawn.Rotation));
        caster.Position.Should().Be(new Vector3(3, 0, -50));
    }

    [Fact]
    public void ACasterThatIsNotVisibleCastsFromItsInterpolatedTurn()
    {
        RenderSnapshot snapshot = Dark();
        RenderInstance hidden = Caster(new(0, 0, -50), 1, visible: false) with
        {
            PreviousRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f),
        };
        snapshot.Instances.Add(hidden);

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.25f);

        Quaternion turn = Casters(scene).Should().ContainSingle().Subject.Rotation;
        Quaternion
            .Dot(turn, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f))
            .Should()
            .BeApproximately(1f, 1e-6f, "a quarter of the way from 1 rad to 0.2 rad is 0.8 rad");
        scene.VisibleCount.Should().Be(0);
    }

    [Fact]
    public void ACasterThatTheMainViewCullsStillCasts()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(Caster(new(0, 0, 50), 1));

        PreparedScene scene = Prepare(snapshot: snapshot);

        Casters(scene).Select(caster => caster.Position).Should().Equal(new Vector3(0, 0, 50));
        scene.VisibleCount.Should().Be(0, "the caster is behind the camera");
    }
}
