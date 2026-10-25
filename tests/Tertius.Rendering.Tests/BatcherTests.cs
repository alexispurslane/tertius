using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>CPU-side batching: interpolation, culling, ranges, sorting and indirect commands. No GPU needed.</summary>
public sealed class BatcherTests
{
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f),
        new(FirstIndex: 36, IndexCount: 600, VertexOffset: 24, Radius: 1f),
        new(FirstIndex: 636, IndexCount: 6, VertexOffset: 300, Radius: 1.414f),
    ];

    // Puffs. Each puff of the snapshot draws as one GpuPuff, at its interpolated place and render age, or not at all. The
    // camera sits at the render origin and looks down −Z, with a vertical field of view of 60° at 1600 × 900.

    /// <summary>A glow look: life 2 s, a radius that grows from 1 m to 3 m, and an orange radiance of (4, 2, 1).</summary>
    private static readonly PuffLook GlowLook = new(
        Life: 2f,
        StartRadius: 1f,
        EndRadius: 3f,
        TemperatureRatio: 3f,
        Shading: PuffShading.Glow(new(4, 2, 1))
    );

    private static RenderSnapshot Empty() => Gpu.Snapshot(Vector3.UnitY);

    private static PreparedScene Prepare(
        RenderSnapshot snapshot,
        float alpha = 1f,
        int materials = 8,
        SceneBatcher? batcher = null,
        EnvironmentDraw environment = default
    )
    {
        var scene = new PreparedScene();
        (batcher ?? new SceneBatcher()).Prepare(
            snapshot: snapshot,
            alpha: alpha,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: materials,
            environment: environment,
            scene: scene
        );
        return scene;
    }

    [Fact]
    public void InstancesOfOneMeshInOneRangeShareOneCommand()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 100; i++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: new(i % 2),
                    material: new(i % 3),
                    position: new(i % 10 - 5, 0, -10 - i),
                    scale: Vector3.One
                )
            );
        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.VisibleCount.Should().Be(100);
        scene.InstanceCount.Should().Be(100);
        scene.CommandCount.Should().Be(2);
        scene.Opaque[DepthRanges.Near].Should().Be(new CommandRange(0, 2));
        DrawIndexedIndirectCommand first = scene.Commands[0],
            second = scene.Commands[1];
        first.InstanceCount.Should().Be(50u);
        second.InstanceCount.Should().Be(50u);
        first.FirstInstance.Should().Be(0u);
        second.FirstInstance.Should().Be(50u);
        second.FirstIndex.Should().Be(Meshes[1].FirstIndex);
        second.IndexCount.Should().Be(Meshes[1].IndexCount);
        second.VertexOffset.Should().Be(Meshes[1].VertexOffset);
    }

    [Fact]
    public void RangesAreOrderedFarToNearAndStraddlersAppearInBoth()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -10), scale: Vector3.One)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -50_000), scale: new(1_000))
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(1), material: new(0), position: new(0, 0, -3e7f), scale: new(6e6f))
        );
        // Radius 50 m around 2 km: drawn in the near and middle ranges.
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(1), material: new(0), position: new(0, 0, -2_000), scale: new(50))
        );
        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.VisibleCount.Should().Be(4);
        scene.InstanceCount.Should().Be(5, "the straddler is written once per range");
        scene.Opaque[DepthRanges.Far].Count.Should().Be(1);
        scene.Opaque[DepthRanges.Middle].Count.Should().Be(2);
        scene.Opaque[DepthRanges.Near].Count.Should().Be(2);
        scene.Opaque[DepthRanges.Far].First.Should().BeLessThan(scene.Opaque[DepthRanges.Middle].First);
        scene.Opaque[DepthRanges.Middle].First.Should().BeLessThan(scene.Opaque[DepthRanges.Near].First);
        // The planet's instance is the first one written, in the far range.
        scene
            .Instances[scene.Commands[scene.Opaque[DepthRanges.Far].First].FirstInstance]
            .ModelRow2.W.Should()
            .Be(-3e7f);
    }

    [Fact]
    public void TransparentInstancesAreSortedBackToFrontAcrossRanges()
    {
        RenderSnapshot snapshot = Empty();
        float[] depths = [5, 3_000, 20, 1e7f, 12, 700];
        foreach (float depth in depths)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: new(0),
                    material: new(0),
                    position: new(0, 0, -depth),
                    scale: Vector3.One,
                    flags: RenderFlags.Transparent
                )
            );
        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.InstanceCount.Should().Be(6);
        scene.Transparent[DepthRanges.Far].Should().Equal(new TransparentRun(new MeshCommands(new(0, 1))));
        scene.Transparent[DepthRanges.Middle].Should().Equal(new TransparentRun(new MeshCommands(new(1, 1))));
        scene.Transparent[DepthRanges.Near].Should().Equal(new TransparentRun(new MeshCommands(new(2, 1))));
        scene.Commands[2].InstanceCount.Should().Be(4u);
        float[] drawn = [.. scene.Instances.AsSpan(0, scene.InstanceCount).ToArray().Select(i => -i.ModelRow2.W)];
        drawn.Should().Equal(1e7f, 3_000f, 700f, 20f, 12f, 5f);
        scene.Opaque.Should().AllSatisfy(range => range.Count.Should().Be(0));
    }

    [Fact]
    public void CullingDropsInvisibleBehindAndOffscreenInstances()
    {
        RenderSnapshot snapshot = Empty();
        var box = new MeshId(0);
        var material = new MaterialId(0);
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: material, position: new(0, 0, -5), scale: Vector3.One)
        );
        snapshot.Instances.Add(Gpu.Instance(mesh: box, material: material, position: new(0, 0, 5), scale: Vector3.One)); // behind
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: material, position: new(100, 0, -5), scale: Vector3.One)
        ); // far right
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: material, position: new(0, -100, -5), scale: Vector3.One)
        ); // far below
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(7), material: material, position: new(0, 0, -5), scale: Vector3.One)
        ); // unknown mesh
        snapshot.Instances.Add(
            Gpu.Instance(mesh: MeshId.None, material: material, position: new(0, 0, -5), scale: Vector3.One)
        ); // no mesh
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: box,
                material: material,
                position: new(0, 0, -5),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 0f,
                hemisphere: HemisphereUp.Even,
                flags: RenderFlags.CastsShadow
            )
        ); // not Visible
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: material, position: new(0, 0, -5), scale: new(1, 0, 1))
        ); // flat
        snapshot.Instances.Add(default); // zeroed
        // Partly in view: the center is outside the right plane but the bounding sphere reaches in.
        snapshot.Instances.Add(
            Gpu.Instance(mesh: box, material: material, position: new(9.5f, 0, -5), scale: Vector3.One, radius: 4f)
        );
        PreparedScene scene = Prepare(snapshot: snapshot);
        scene.SubmittedInstances.Should().Be(10);
        scene.VisibleCount.Should().Be(2);
    }

    [Fact]
    public void InterpolationUsesAlphaForInstancesAndTheCamera()
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance instance = Gpu.Instance(
            mesh: new(0),
            material: new(0),
            position: new(4, 0, -10),
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
        );
        instance.PreviousPosition = new(-4, 0, -10);
        instance.PreviousRotation = Quaternion.Identity;
        snapshot.Instances.Add(instance);
        snapshot.Camera.PreviousPosition = new(0, 2, 0);

        PreparedScene quarter = Prepare(snapshot: snapshot, alpha: 0.25f);
        PreparedInstance prepared = quarter.Visible[0];
        // Instance at lerp(-4, 4, 0.25) = -2; the camera at lerp(2, 0, 0.25) = 1.5 is subtracted.
        prepared.Position.Should().Be(new Vector3(-2, -1.5f, -10));
        quarter.Camera.Offset.Should().Be(new Vector3(0, 1.5f, 0));
        float angle = 2 * MathF.Acos(Math.Clamp(prepared.Rotation.W, -1, 1));
        angle.Should().BeApproximately(MathF.PI / 8, 1e-4f);

        // Alpha outside [0, 1] is clamped.
        Prepare(snapshot: snapshot, alpha: 3f).Visible[0].Position.Should().Be(new Vector3(4, 0, -10));
    }

    [Fact]
    public void UnsetQuaternionsCountAsIdentityAndUnknownMaterialsAsDefault()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera = new() { VerticalFov = 0 };
        RenderInstance instance = Gpu.Instance(
            mesh: new(0),
            material: new(42),
            position: new(0, 0, -5),
            scale: Vector3.One
        );
        instance.Rotation = default;
        instance.PreviousRotation = default;
        snapshot.Instances.Add(instance);
        PreparedScene scene = Prepare(snapshot: snapshot, materials: 4);
        scene.Visible[0].Rotation.Should().Be(Quaternion.Identity);
        scene.Visible[0].Material.Should().Be(MaterialStore.DefaultSlot);
        scene.Camera.VerticalFov.Should().BeApproximately(MathF.PI / 3, 1e-5f);
    }

    [Fact]
    public void NoneMeansNoMeshOrTheDefaultMaterialAndRealIdsStartAtZero()
    {
        RenderSnapshot snapshot = Empty();
        // Four slots on the GPU: the default and materials 0 to 2.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: MaterialId.None,
                position: new(-2, 0, -5),
                scale: Vector3.One,
                entity: 1
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -5), scale: Vector3.One, entity: 2)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(2), position: new(2, 0, -5), scale: Vector3.One, entity: 3)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(3), position: new(0, 2, -5), scale: Vector3.One, entity: 4)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: new(0), material: new(-7), position: new(0, -2, -5), scale: Vector3.One, entity: 5)
        );
        snapshot.Instances.Add(
            Gpu.Instance(mesh: MeshId.None, material: new(0), position: new(0, 0, -6), scale: Vector3.One, entity: 6)
        );
        PreparedScene scene = Prepare(snapshot: snapshot, materials: MaterialStore.BuiltInCount + 3);

        Dictionary<uint, uint> materials = scene
            .VisibleSpan.ToArray()
            .ToDictionary(v => v.EntityIndex, v => v.Material);
        materials.Should().NotContainKey(6u, "MeshId.None draws nothing");
        materials[1].Should().Be(MaterialStore.DefaultSlot, "MaterialId.None asks for the default material");
        // Material 0 is a real material, not the default: ids are not confused with None.
        materials[2].Should().Be(MaterialStore.Slot(new(0))).And.NotBe(MaterialStore.DefaultSlot);
        materials[3].Should().Be(3u);
        materials[4].Should().Be(MaterialStore.DefaultSlot, "material 3 is not on the GPU");
        materials[5].Should().Be(MaterialStore.DefaultSlot, "no material has a negative id");

        MeshId.None.IsNone.Should().BeTrue();
        default(MeshId).Should().Be(MeshId.None);
        SceneBatcher.MeshIndex(MeshId.None, Meshes.Length).Should().Be(-1);
        SceneBatcher.MeshIndex(new(0), Meshes.Length).Should().Be(0);
        SceneBatcher.MeshIndex(new(Meshes.Length), Meshes.Length).Should().Be(-1);
    }

    [Fact]
    public void ThePartOfAnEntityReachesThePreparedInstance()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(-1, 0, -5),
                scale: Vector3.One,
                entity: 9,
                part: 0
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(1, 0, -5),
                scale: Vector3.One,
                entity: 9,
                part: 3
            )
        );
        PreparedScene scene = Prepare(snapshot: snapshot);
        scene.VisibleSpan.ToArray().Select(v => (v.EntityIndex, v.Part)).Should().Equal((9u, 0), (9u, 3));
    }

    [Theory]
    [InlineData(0f, 1f, 0f, false)]
    [InlineData(-2f, 1f, 0f, false)]
    [InlineData(float.NaN, 1f, 0f, false)]
    [InlineData(float.PositiveInfinity, 1f, 0f, false)]
    [InlineData(3f, 0f, 0f, false)] // no direction
    [InlineData(3f, float.PositiveInfinity, 0f, false)] // a direction that cannot be normalized
    [InlineData(3f, 1f, 0f, true)]
    [InlineData(0.001f, 0f, 5f, true)] // need not be normalized
    public void ASunWithoutAPositiveIntensityOrADirectionIsOff(float intensity, float y, float z, bool lit)
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Sun = new()
        {
            Direction = new(0, y, z),
            Color = Vector3.One,
            Intensity = intensity,
            AngularRadius = 0.005f,
        };
        SceneBatcher.HasSun(snapshot.Sun).Should().Be(lit);
        PreparedScene scene = Prepare(snapshot: snapshot);
        Vector4 radiance = scene.Constants.SunRadiance;
        if (lit)
        {
            radiance.X.Should().BeApproximately(intensity, 1e-6f);
            new Vector3(scene.Constants.SunDirection.X, scene.Constants.SunDirection.Y, scene.Constants.SunDirection.Z)
                .Length()
                .Should()
                .BeApproximately(1f, 1e-5f);
        }
        else
        {
            radiance.Should().Be(Vector4.Zero, "a sun that is off sends no light into the lighting shader");
        }
    }

    [Fact]
    public void LightsAreConvertedAndCulled()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera.PreviousPosition = new(0, 0, 2);
        snapshot.Lights.Add(
            new PointRenderLight(Position: new(0, 1, -5), Color: new(1, 0.5f, 0.25f), Intensity: 4, Range: 10)
        );
        snapshot.Lights.Add(
            new SpotRenderLight(
                Position: new(0, 3, -5),
                Direction: new(0, -2, 0),
                Color: Vector3.One,
                Intensity: 1,
                Range: 8,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            )
        );
        snapshot.Lights.Add(new PointRenderLight(Position: new(0, 0, 50), Color: Vector3.One, Intensity: 1, Range: 10)); // behind
        snapshot.Lights.Add(new PointRenderLight(Position: new(0, 0, -5), Color: Vector3.One, Intensity: 1, Range: 0)); // no range
        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        scene.LightCount.Should().Be(2);
        GpuLight point = scene.Lights[0],
            spot = scene.Lights[1];
        point.PositionRange.Should().Be(new Vector4(x: 0, y: 1, z: -6, w: 10));
        point.RadianceKind.Should().Be(new Vector4(x: 4, y: 2, z: 1, w: 0));
        // Each case has its own arm in the batcher, so each case's position relative to the camera, and its range, needs its own check.
        spot.PositionRange.Should().Be(new Vector4(x: 0, y: 3, z: -6, w: 8));
        spot.RadianceKind.W.Should().Be(1f);
        new Vector3(spot.DirectionCosOuter.X, spot.DirectionCosOuter.Y, spot.DirectionCosOuter.Z)
            .Should()
            .Be(new Vector3(0, -1, 0));
        spot.DirectionCosOuter.W.Should().BeApproximately(MathF.Cos(0.5f), 1e-5f);
        spot.Spot.X.Should().BeApproximately(MathF.Cos(0.3f), 1e-5f);
        scene.Constants.LightCount.Should().Be(2u);
    }

    [Fact]
    public void ALightThatCannotLightAnythingIsLeftOutWhateverItsCase()
    {
        RenderSnapshot snapshot = Empty();
        // The negative range is far ahead: close to the eye, the frustum test would leave a light of negative range out by itself.
        Vector3 ahead = new(0, 0, -5),
            farAhead = new(0, 0, -50),
            behind = new(0, 0, 50);
        // Each reason to leave a light out, once for each case: no range, a negative range, no intensity, and a sphere of reach outside the view.
        foreach (
            (Vector3 position, float intensity, float range) in new[]
            {
                (ahead, 1f, 0f),
                (farAhead, 1f, -3f),
                (ahead, 0f, 10f),
                (behind, 1f, 10f),
            }
        )
        {
            snapshot.Lights.Add(
                new PointRenderLight(Position: position, Color: Vector3.One, Intensity: intensity, Range: range)
            );
            snapshot.Lights.Add(
                new SpotRenderLight(
                    Position: position,
                    Direction: -Vector3.UnitY,
                    Color: Vector3.One,
                    Intensity: intensity,
                    Range: range,
                    InnerConeAngle: 0.3f,
                    OuterConeAngle: 0.5f
                )
            );
        }

        snapshot.Lights.Add(default);
        // One of each case that reaches the view, to show that the others were left out and not everything.
        snapshot.Lights.Add(new PointRenderLight(Position: ahead, Color: Vector3.One, Intensity: 1, Range: 10));
        snapshot.Lights.Add(
            new SpotRenderLight(
                Position: ahead,
                Direction: -Vector3.UnitY,
                Color: Vector3.One,
                Intensity: 1,
                Range: 10,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene
            .LightCount.Should()
            .Be(2, "the empty light holds no light, and the eight lights that reach nothing are left out");
        scene.Lights[0].RadianceKind.W.Should().Be(0f);
        scene.Lights[1].RadianceKind.W.Should().Be(1f);
    }

    [Fact]
    public void APointLightLeavesTheDirectionAndTheConeOfItsGpuLightZeroBecauseItHasNone()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(new PointRenderLight(Position: new(0, 1, -5), Color: Vector3.One, Intensity: 1, Range: 10));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(1);
        GpuLight point = scene.Lights[0];
        point.PositionRange.W.Should().Be(10f);
        point.RadianceKind.W.Should().Be(0f);
        point.DirectionCosOuter.Should().Be(Vector4.Zero);
        point.Spot.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void ASpotLightWithoutADirectionShinesStraightDown()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(
            new SpotRenderLight(
                Position: new(0, 1, -5),
                Direction: Vector3.Zero,
                Color: Vector3.One,
                Intensity: 1,
                Range: 10,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(1);
        GpuLight spot = scene.Lights[0];
        new Vector3(spot.DirectionCosOuter.X, spot.DirectionCosOuter.Y, spot.DirectionCosOuter.Z)
            .Should()
            .Be(new Vector3(0, -1, 0));
    }

    [Fact]
    public void ASpotLightWhoseInnerConeIsNotNarrowerThanItsOuterConeStillFadesOverANonEmptyInterval()
    {
        // The shader's smoothstep needs an inner cosine above the outer one, so the batcher widens an empty or inverted interval.
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(
            new SpotRenderLight(
                Position: new(0, 1, -5),
                Direction: -Vector3.UnitY,
                Color: Vector3.One,
                Intensity: 1,
                Range: 10,
                InnerConeAngle: 0.6f,
                OuterConeAngle: 0.4f
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(1);
        GpuLight spot = scene.Lights[0];
        spot.Spot.X.Should().BeGreaterThan(spot.DirectionCosOuter.W);
        spot.DirectionCosOuter.W.Should().BeApproximately(MathF.Cos(0.4f), 1e-5f);
    }

    [Fact]
    public void ManyLightsOfBothCasesGrowTheLightArrayAndKeepTheirOrder()
    {
        // The array starts at 16 lights. Light i has the intensity i + 1 and is a point light when i is even, a spot light when it is odd.
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 40; i++)
            snapshot.Lights.Add(
                i % 2 == 0
                    ? new PointRenderLight(Position: new(0, 1, -5), Color: Vector3.One, Intensity: i + 1, Range: 10)
                    : new SpotRenderLight(
                        Position: new(0, 1, -5),
                        Direction: -Vector3.UnitY,
                        Color: Vector3.One,
                        Intensity: i + 1,
                        Range: 10,
                        InnerConeAngle: 0.3f,
                        OuterConeAngle: 0.5f
                    )
            );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.LightCount.Should().Be(40);
        for (int i = 0; i < 40; i++)
        {
            scene.Lights[i].RadianceKind.X.Should().Be(i + 1, $"light {i} keeps its place");
            scene.Lights[i].RadianceKind.W.Should().Be(i % 2 == 0 ? 0f : 1f, $"light {i} keeps its case");
        }
    }

    [Fact]
    public void ProcessorsCanReplaceInstancesBeforeBatching()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 10; i++)
            snapshot.Instances.Add(
                Gpu.Instance(mesh: new(0), material: new(0), position: new(0, 0, -10 - i * 100), scale: Vector3.One)
            );
        var batcher = new SceneBatcher();
        batcher.Processors.Add(new BillboardSwap());
        PreparedScene scene = Prepare(snapshot: snapshot, batcher: batcher);
        // Objects beyond 300 m became mesh 2 "billboards".
        scene.VisibleCount.Should().Be(10);
        scene.CommandCount.Should().Be(2);
        scene.Commands[0].InstanceCount.Should().Be(3u);
        scene.Commands[1].InstanceCount.Should().Be(7u);
        scene.Commands[1].FirstIndex.Should().Be(Meshes[2].FirstIndex);
    }

    private sealed class BillboardSwap : IInstanceProcessor
    {
        public void Process(PreparedScene scene)
        {
            for (int i = scene.VisibleCount - 1; i >= 0; i--)
            {
                if (scene.Visible[i].Distance <= 300)
                    continue;
                PreparedInstance original = scene.Visible[i];
                scene.RemoveVisibleAt(i);
                ref PreparedInstance billboard = ref scene.AddVisible();
                billboard = original;
                billboard.Mesh = 2;
            }
        }
    }

    [Fact]
    public void GpuInstanceMatchesSystemNumericsTransforms()
    {
        var rotation = Quaternion.Normalize(new(x: 0.3f, y: -0.5f, z: 0.2f, w: 0.8f));
        var scale = new Vector3(2, 3, 0.5f);
        var position = new Vector3(10, -20, 30);
        var instance = GpuInstance.From(position: position, rotation: rotation, scale: scale, material: 7);
        var point = new Vector3(0.4f, -1.1f, 2.5f);
        var p = new Vector4(point, 1);
        var transformed = new Vector3(
            Vector4.Dot(instance.ModelRow0, p),
            Vector4.Dot(instance.ModelRow1, p),
            Vector4.Dot(instance.ModelRow2, p)
        );
        Vector3 expected = position + Vector3.Transform(point * scale, rotation);
        Vector3.Distance(expected, transformed).Should().BeLessThan(1e-4f, "the model matrix is T · R · S");

        // The shader's normal transform R · S⁻¹ · n from M · (n / s²).
        var normal = Vector3.Normalize(new(1, 2, 3));
        var linear = new Matrix4x4(
            m11: instance.ModelRow0.X,
            m12: instance.ModelRow1.X,
            m13: instance.ModelRow2.X,
            m14: 0,
            m21: instance.ModelRow0.Y,
            m22: instance.ModelRow1.Y,
            m23: instance.ModelRow2.Y,
            m24: 0,
            m31: instance.ModelRow0.Z,
            m32: instance.ModelRow1.Z,
            m33: instance.ModelRow2.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
        var shaderNormal = Vector3.Normalize(Vector3.TransformNormal(normal * instance.InverseScaleSquared, linear));
        var expectedNormal = Vector3.Normalize(Vector3.Transform(normal / scale, rotation));
        Vector3.Distance(expectedNormal, shaderNormal).Should().BeLessThan(1e-5f, "normals transform by R · S⁻¹");
        instance.Material.Should().Be(7u);
    }

    [Fact]
    public void EachInstanceTakesItsHemispheresUpToTheGpuRelativeToTheInterpolatedEye()
    {
        RenderSnapshot snapshot = Empty();
        // At alpha 0.5 the eye is 1 m along +X from the render origin, so every position moves 1 m along -X.
        snapshot.Camera.PreviousPosition = new(2, 0, 0);
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: new(0),
                material: new(1),
                position: new(0, 0, -10),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 1,
                hemisphere: HemisphereUp.Along(Vector3.UnitX)
            )
        );
        var moving = RenderInstance.Create(
            mesh: new(0),
            material: new(1),
            position: new(0, 1, -10),
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            boundingRadius: 1,
            hemisphere: HemisphereUp.AwayFrom(new(0, -50, 0))
        );
        moving.PreviousPosition = new(4, 1, -10);
        snapshot.Instances.Add(moving);
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: new(0),
                material: new(1),
                position: new(0, 2, -10),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 1,
                hemisphere: HemisphereUp.Even
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        // The kind is in w: 1 along a direction, 2 away from a point, 0 even. The point moves with its instance: halfway
        // from x = 4 to x = 0 is x = 2, less the eye's 1 m, so 50 m below (1, 1, -10).
        scene
            .Instances.AsSpan(0, 3)
            .ToArray()
            .Select(instance => instance.Hemisphere)
            .Should()
            .BeEquivalentTo([new(x: 1, y: 0, z: 0, w: 1), new(x: 1, y: -49, z: -10, w: 2), Vector4.Zero]);
    }

    [Fact]
    public void TheRootRotationBlendsFromThePreviousTickAsTheCameraDoes()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.PreviousRootRotation = Quaternion.Identity;
        snapshot.RootRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

        Vector4 root = Prepare(snapshot: snapshot, alpha: 0.25f).Constants.RootRotation;

        // A quarter of the way from the previous rotation to the current one: an eighth of a turn about +Y.
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 8);
        Vector4
            .Distance(root, new(x: expected.X, y: expected.Y, z: expected.Z, w: expected.W))
            .Should()
            .BeLessThan(1e-6f);
    }

    [Fact]
    public void AnUnsetRootRotationLeavesTheSkyInTheAxesOfTheCamerasFrame()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.RootRotation = default;
        snapshot.PreviousRootRotation = default;

        Prepare(snapshot: snapshot).Constants.RootRotation.Should().Be(new Vector4(x: 0, y: 0, z: 0, w: 1));
    }

    // The map's faces. The camera sits at the render origin and looks down −Z, so it sees nothing along ±X or ±Y.

    /// <summary>The map is on, draws <paramref name="faces"/> this frame, and has no irradiance reading yet.</summary>
    private static EnvironmentDraw MapOf(FaceMask faces) => new WithMap(Faces: faces, Irradiance: null);

    /// <summary>A unit box of mesh 0 at <paramref name="position"/>.</summary>
    private static RenderInstance Box(Vector3 position, RenderFlags flags = RenderFlags.None) =>
        Gpu.Instance(mesh: new(0), material: new(0), position: position, scale: Vector3.One, flags: flags);

    private static Vector3 Position(in GpuInstance instance) =>
        new(instance.ModelRow0.W, instance.ModelRow1.W, instance.ModelRow2.W);

    /// <summary>The places of the GPU instances that the commands of <paramref name="commands"/> draw, in draw order.</summary>
    private static List<Vector3> Placed(PreparedScene scene, CommandRange commands)
    {
        var positions = new List<Vector3>();
        for (int c = commands.First; c < commands.First + commands.Count; c++)
        {
            DrawIndexedIndirectCommand command = scene.Commands[c];
            for (uint i = command.FirstInstance; i < command.FirstInstance + command.InstanceCount; i++)
                positions.Add(Position(scene.Instances[i]));
        }

        return positions;
    }

    /// <summary>The places of the GPU instances that a face draws over its three depth ranges.</summary>
    private static List<Vector3> Placed(PreparedScene scene, EnvironmentFaceDraw face) =>
        [.. Placed(scene, face.Far), .. Placed(scene, face.Middle), .. Placed(scene, face.Near)];

    private static Vector3 LightPosition(in GpuLight light) =>
        new(light.PositionRange.X, light.PositionRange.Y, light.PositionRange.Z);

    [Fact]
    public void AFaceDrawsTheOpaqueInstancesInItsFrustumAndNoTransparentOne()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(10, 0, 0)));
        snapshot.Instances.Add(Box(new(10, 3, 0), RenderFlags.Transparent));
        snapshot.Instances.Add(Box(new(-10, 0, 0))); // behind the face
        snapshot.Instances.Add(Box(new(10, 30, 0))); // above its top plane
        snapshot.Instances.Add(Box(new(10, 0, 30))); // beyond its side plane
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: new(0),
                material: new(0),
                position: new(10, -3, 0),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 0f,
                hemisphere: HemisphereUp.Even,
                flags: RenderFlags.CastsShadow
            )
        ); // not Visible

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX));

        EnvironmentFaceDraw face = scene.EnvironmentFaces.Should().ContainSingle().Subject;
        face.Face.Should().Be(CubeFace.PositiveX);
        Placed(scene, face).Should().Equal(new Vector3(10, 0, 0));
    }

    [Fact]
    public void AFaceDrawsEachInstanceAtItsInterpolatedPlaceRelativeToTheInterpolatedEye()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera.PreviousPosition = new(0, 0, 0);
        snapshot.Camera.Position = new(0, 2, 0);
        RenderInstance box = Box(new(12, 0, 0));
        box.PreviousPosition = new(10, 0, 0);
        snapshot.Instances.Add(box);

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f, environment: MapOf(FaceMask.PositiveX));

        Placed(scene, scene.EnvironmentFaces[0]).Should().Equal(new Vector3(11, -1, 0));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI / 2)]
    [InlineData(MathF.PI)]
    [InlineData(-MathF.PI / 2)]
    public void AFaceSortsItsInstancesIntoTheRangesOfTheirDepthAlongItsOwnAxis(float cameraTurn)
    {
        // The camera turns about +Y: it looks down −Z, −X, +Z and +X in turn.
        RenderSnapshot snapshot = Empty();
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, cameraTurn);
        snapshot.Camera.Rotation = turn;
        snapshot.Camera.PreviousRotation = turn;
        snapshot.Instances.Add(Box(new(10_000, 0, 0)));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX));

        EnvironmentFaceDraw face = scene.EnvironmentFaces.Should().ContainSingle().Subject;
        Placed(scene, face[DepthRanges.Middle]).Should().Equal(new Vector3(10_000, 0, 0));
        face[DepthRanges.Far].Count.Should().Be(0);
        face[DepthRanges.Near].Count.Should().Be(0);
    }

    [Fact]
    public void ARefreshPreparesSixFaces()
    {
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        RenderSnapshot snapshot = Empty();
        foreach (Vector3 axis in axes)
            snapshot.Instances.Add(Box(axis * 10));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.All));

        scene
            .EnvironmentFaces.Select(face => face.Face)
            .Should()
            .Equal(
                CubeFace.PositiveX,
                CubeFace.NegativeX,
                CubeFace.PositiveY,
                CubeFace.NegativeY,
                CubeFace.PositiveZ,
                CubeFace.NegativeZ
            );
        for (int i = 0; i < CubeFaces.Count; i++)
            Placed(scene, scene.EnvironmentFaces[i])
                .Should()
                .Equal([axes[i] * 10], $"face {i} sees the box on its axis");
    }

    [Fact]
    public void TheFacesCommandsAndInstancesFollowTheMainViews()
    {
        // The box ahead is in the main view and in face −Z; the pane is in the main view's transparent pass.
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(0, 0, -10)));
        snapshot.Instances.Add(Box(new(0, 0, -20), RenderFlags.Transparent));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.NegativeZ));

        CommandRange near = scene.EnvironmentFaces.Should().ContainSingle().Subject[DepthRanges.Near];
        near.Should()
            .Be(new CommandRange(First: 2, Count: 1), "the main view's opaque and transparent commands come first");
        scene.Commands[near.First].FirstInstance.Should().Be(2u);
        scene.InstanceCount.Should().Be(3);
    }

    [Fact]
    public void AFaceDrawsAnObjectThatAProcessorTookOutOfTheMainView()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(0, 0, -10)));
        var batcher = new SceneBatcher();
        batcher.Processors.Add(new TakeAllOut());

        PreparedScene scene = Prepare(snapshot: snapshot, batcher: batcher, environment: MapOf(FaceMask.NegativeZ));

        scene.Opaque[DepthRanges.Near].Count.Should().Be(0);
        Placed(scene, scene.EnvironmentFaces[0]).Should().Equal(new Vector3(0, 0, -10));
    }

    /// <summary>Takes every visible instance out of the main view, as the impostor manager takes its impostors out.</summary>
    private sealed class TakeAllOut : IInstanceProcessor
    {
        public void Process(PreparedScene scene) => scene.VisibleCount = 0;
    }

    [Fact]
    public void AFaceKeepsALightThatTheMainViewLeavesOut()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(new PointRenderLight(Position: new(50, 0, 0), Color: Vector3.One, Intensity: 1, Range: 10));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX));

        scene.LightCount.Should().Be(1);
        scene.Lights[0].PositionRange.Should().Be(new Vector4(x: 50, y: 0, z: 0, w: 10));
        scene.Constants.LightCount.Should().Be(1u);
    }

    [Fact]
    public void ALightThatOnlyAFaceThatThisFrameSkipsCouldReachIsLeftOut()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(new PointRenderLight(Position: new(50, 0, 0), Color: Vector3.One, Intensity: 1, Range: 10));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.All & ~FaceMask.PositiveX));

        scene.LightCount.Should().Be(0);
    }

    [Fact]
    public void TheLightsKeepTheSnapshotsOrder()
    {
        // Faces only, the main view only, faces only, and one that lights nothing.
        Vector3[] places = [new(50, 0, 0), new(0, 0, -5), new(0, 50, 0), new(30, 0, 0)];
        float[] intensities = [1, 1, 1, 0];
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < places.Length; i++)
            snapshot.Lights.Add(
                new PointRenderLight(Position: places[i], Color: Vector3.One, Intensity: intensities[i], Range: 10)
            );

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.All));

        scene
            .Lights.AsSpan(0, scene.LightCount)
            .ToArray()
            .Select(light => LightPosition(light))
            .Should()
            .Equal(places[0], places[1], places[2]);
    }

    [Fact]
    public void WithoutTheMapTheLightsAreTheMainViewsAlone()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(new PointRenderLight(Position: new(50, 0, 0), Color: Vector3.One, Intensity: 1, Range: 10));
        snapshot.Lights.Add(new PointRenderLight(Position: new(0, 0, -5), Color: Vector3.One, Intensity: 1, Range: 10));
        snapshot.Instances.Add(Box(new(10, 0, 0)));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: new WithoutMap());

        scene.LightCount.Should().Be(1);
        LightPosition(scene.Lights[0]).Should().Be(new Vector3(0, 0, -5));
        scene.EnvironmentFaces.Should().BeEmpty();
    }

    [Fact]
    public void WithTheMapOnTheFrameConstantsHoldNoHemisphere()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Ambient = new()
        {
            SkyColor = new(0.2f, 0.3f, 0.4f),
            GroundColor = new(0.1f),
            Intensity = 2,
        };

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.None));

        scene.Constants.AmbientSky.Should().Be(Vector4.Zero);
        scene.Constants.AmbientGround.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void WithoutTheMapTheAmbientLightIsTheSnapshotsHemisphere()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Ambient = new()
        {
            SkyColor = new(0.2f, 0.3f, 0.4f),
            GroundColor = new(0.1f),
            Intensity = 2,
        };

        PreparedScene scene = Prepare(snapshot: snapshot, environment: new WithoutMap());

        scene.Ambient.Should().Be(new AmbientSource(new HemisphereAmbient(snapshot.Ambient)));
        scene.Constants.AmbientSky.Should().Be(new Vector4(x: 0.4f, y: 0.6f, z: 0.8f, w: 0));
        scene.Constants.AmbientGround.Should().Be(new Vector4(x: 0.2f, y: 0.2f, z: 0.2f, w: 0));
    }

    [Fact]
    public void WithTheMapTheAmbientLightIsTheMapWithItsLatestReading()
    {
        var coefficients = new Vector3[Irradiance.Coefficients];
        coefficients[0] = new(1, 2, 3);
        var reading = new IrradianceReading(Frame: 7, Irradiance: new(coefficients));

        PreparedScene scene = Prepare(
            snapshot: Empty(),
            environment: new WithMap(Faces: FaceMask.None, Irradiance: reading)
        );

        scene.Ambient.TryGetValue(out MapAmbient map).Should().BeTrue();
        map.Irradiance!.Value.Frame.Should().Be(7);
        map.Irradiance.Value.Irradiance[0].Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void APreparedSceneDropsTheFacesOfTheFrameBefore()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(10, 0, 0)));
        var scene = new PreparedScene();
        var batcher = new SceneBatcher();
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 1f,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: 8,
            environment: MapOf(FaceMask.All),
            scene: scene
        );
        scene.EnvironmentFaces.Should().HaveCount(6);

        batcher.Prepare(
            snapshot: snapshot,
            alpha: 1f,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: 8,
            environment: MapOf(FaceMask.NegativeY),
            scene: scene
        );

        scene.EnvironmentFaces.Select(face => face.Face).Should().Equal(CubeFace.NegativeY);
    }

    /// <summary>A still puff: its previous place is its place.</summary>
    private static RenderPuff Puff(Vector3 position, float age = 0.5f, int look = 0) =>
        new()
        {
            Position = position,
            PreviousPosition = position,
            Age = age,
            Look = look,
        };

    /// <summary>A snapshot whose look table holds <paramref name="looks"/>, or only <see cref="GlowLook"/>.</summary>
    private static RenderSnapshot PuffSnapshot(params PuffLook[] looks)
    {
        RenderSnapshot snapshot = Empty();
        snapshot.PuffLooks.AddRange(looks.Length > 0 ? looks : [GlowLook]);
        return snapshot;
    }

    private static GpuPuff[] Drawn(PreparedScene scene) => scene.Puffs.AsSpan(0, scene.PuffCount).ToArray();

    private static Vector3 Center(GpuPuff puff) =>
        new(puff.PositionRadius.X, puff.PositionRadius.Y, puff.PositionRadius.Z);

    private static Vector3 Color(GpuPuff puff) => new(puff.ColorKind.X, puff.ColorKind.Y, puff.ColorKind.Z);

    private static void ShouldBeClose(Vector3 actual, Vector3 expected, float precision = 1e-5f)
    {
        actual.X.Should().BeApproximately(expected.X, precision);
        actual.Y.Should().BeApproximately(expected.Y, precision);
        actual.Z.Should().BeApproximately(expected.Z, precision);
    }

    private static TransparentRun MeshRun(int first, int count) => new MeshCommands(new(first, count));

    private static TransparentRun PuffRun(int first, int count) => new PuffSprites(first, count);

    [Fact]
    public void PuffsInterpolatePositionAndAgeByAlpha()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.PuffAgeStep = 0.2f;
        snapshot.Puffs.Add(
            new()
            {
                Position = new(4, 0, -10),
                PreviousPosition = new(-4, 0, -10),
                Age = 1f,
                Look = 0,
            }
        );
        snapshot.Camera.PreviousPosition = new(0, 2, 0);

        GpuPuff puff = Drawn(Prepare(snapshot: snapshot, alpha: 0.25f)).Should().ContainSingle().Subject;

        // The place is lerp(−4, 4, 0.25) = −2, less the camera at lerp(2, 0, 0.25) = 1.5. The render age is
        // 1 − 0.75 · 0.2 = 0.85 s, so t = 0.425: the radius is 1 + 2 · 0.425 and the own share is 0.575.
        ShouldBeClose(Center(puff), new(-2, -1.5f, -10));
        puff.PositionRadius.W.Should().BeApproximately(1.85f, 1e-5f);
        ShouldBeClose(Color(puff), new Vector3(4, 2, 1) * 0.575f);
    }

    [Fact]
    public void AtAlphaOneAPuffDrawsAtItsCurrentPlaceAndAge()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.PuffAgeStep = 0.2f;
        snapshot.Puffs.Add(
            new()
            {
                Position = new(4, 0, -10),
                PreviousPosition = new(-4, 0, -10),
                Age = 1f,
                Look = 0,
            }
        );
        snapshot.Camera.PreviousPosition = new(0, 2, 0);

        GpuPuff puff = Drawn(Prepare(snapshot: snapshot, alpha: 1f)).Should().ContainSingle().Subject;

        ShouldBeClose(Center(puff), new(4, 0, -10));
        puff.PositionRadius.W.Should().BeApproximately(2f, 1e-5f);
        ShouldBeClose(Color(puff), new(2, 1, 0.5f));
    }

    [Fact]
    public void APuffIsNotDrawnBeforeItsBirthOrAfterItsLife()
    {
        // A newborn of age 0.04 s in a step of 0.1 s was born 0.06 s into the step. Its render age reaches 0 at alpha 0.6.
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.PuffAgeStep = 0.1f;
        snapshot.Puffs.Add(Puff(new(-2, 0, -10), age: 0.04f));
        // A puff at the end of its life: at alpha 1 its render age is its life, 2 s.
        snapshot.Puffs.Add(Puff(new(0, 0, -10), age: 2f));
        // A puff just before the end of its life.
        snapshot.Puffs.Add(Puff(new(2, 0, -10), age: 1.999f));

        Drawn(Prepare(snapshot: snapshot, alpha: 0.5f))
            .Select(p => p.PositionRadius.X)
            .Should()
            .BeEquivalentTo(
                [0f, 2f],
                "at alpha 0.5 the newborn's render age is −0.01 s, and the old puff's is 1.95 s, inside its life"
            );
        Drawn(Prepare(snapshot: snapshot, alpha: 0.7f))
            .Select(p => p.PositionRadius.X)
            .Should()
            .BeEquivalentTo([-2f, 0f, 2f], "at alpha 0.7 the newborn is 0.01 s old");
        Drawn(Prepare(snapshot: snapshot, alpha: 1f))
            .Select(p => p.PositionRadius.X)
            .Should()
            .BeEquivalentTo([-2f, 2f], "at alpha 1 the old puff's render age is its life, so it is dead");
    }

    [Fact]
    public void APuffsRadiusGrowsAndItsWeightFallsLinearlyOverItsLife()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        float[] ages = [0f, 0.5f, 1f, 1.5f];
        for (int i = 0; i < ages.Length; i++)
            snapshot.Puffs.Add(Puff(new(3 * i - 4.5f, 0, -12), age: ages[i]));

        GpuPuff[] drawn = [.. Drawn(Prepare(snapshot: snapshot)).OrderBy(p => p.PositionRadius.X)];

        drawn.Select(p => p.PositionRadius.W).Should().Equal([1f, 1.5f, 2f, 2.5f], (a, b) => MathF.Abs(a - b) < 1e-5f);
        drawn
            .Select(p => p.ColorKind.X / 4)
            .Should()
            .Equal([1f, 0.75f, 0.5f, 0.25f], (a, b) => MathF.Abs(a - b) < 1e-5f);
    }

    [Fact]
    public void APuffOutsideTheFrustumIsCulledByItsRadius()
    {
        // At 1600 × 900, tan(30°) · 16 / 9 = 1.0264, so the right plane passes x = 10.26 at 10 m ahead. A center at x = 12 lies
        // (12 − 10.264) / √(1 + 1.0264²) = 1.21 m outside it.
        RenderSnapshot snapshot = PuffSnapshot(
            GlowLook with
            {
                StartRadius = 1f,
                EndRadius = 1f,
            },
            GlowLook with
            {
                StartRadius = 1.5f,
                EndRadius = 1.5f,
            }
        );
        snapshot.Puffs.Add(Puff(new(12, 0, -10), look: 0));
        snapshot.Puffs.Add(Puff(new(12, 0, -10.0001f), look: 1));
        // Behind the eye: one wholly behind the near plane, and one whose sphere reaches in front of it.
        snapshot.Puffs.Add(Puff(new(0, 0, 5), look: 0));
        snapshot.Puffs.Add(Puff(new(0, 0, 0.5f), look: 0));

        GpuPuff[] drawn = Drawn(Prepare(snapshot: snapshot));

        drawn.Select(p => p.PositionRadius.W).Should().BeEquivalentTo([1.5f, 1f]);
        drawn.Select(Center).Should().BeEquivalentTo([new(12, 0, -10.0001f), new Vector3(0, 0, 0.5f)]);
    }

    [Fact]
    public void APuffWithANumberThatIsNotFiniteOrALookOutsideTheListIsLeftOut()
    {
        RenderSnapshot snapshot = PuffSnapshot(
            GlowLook,
            GlowLook with
            {
                StartRadius = 0f,
                EndRadius = 0f,
            },
            GlowLook with
            {
                StartRadius = -1f,
                EndRadius = -1f,
            },
            GlowLook with
            {
                Life = float.NaN,
            },
            GlowLook with
            {
                EndRadius = float.PositiveInfinity,
            }
        );
        var ahead = new Vector3(0, 0, -10);
        snapshot.Puffs.Add(Puff(ahead)); // drawn
        snapshot.Puffs.Add(Puff(new(float.NaN, 0, -10)));
        snapshot.Puffs.Add(
            new()
            {
                Position = ahead,
                PreviousPosition = new(float.PositiveInfinity, 0, -10),
                Look = 0,
                Age = 0.5f,
            }
        );
        snapshot.Puffs.Add(Puff(ahead, age: float.NaN));
        snapshot.Puffs.Add(Puff(ahead, look: 1)); // radius 0
        snapshot.Puffs.Add(Puff(ahead, look: 2)); // radius below 0
        snapshot.Puffs.Add(Puff(ahead, look: 3)); // life NaN
        snapshot.Puffs.Add(Puff(ahead, look: 4)); // infinite radius
        snapshot.Puffs.Add(Puff(ahead, look: 5)); // outside the list
        snapshot.Puffs.Add(Puff(ahead, look: -1));

        Drawn(Prepare(snapshot: snapshot, alpha: 0.5f))
            .Should()
            .ContainSingle()
            .Which.PositionRadius.W.Should()
            .BeApproximately(1.5f, 1e-5f);
    }

    [Fact]
    public void APuffWhoseLookHoldsNoShadingIsLeftOut()
    {
        // The second look holds no case, and the third is a free entry of the look table, all zero.
        RenderSnapshot snapshot = PuffSnapshot(GlowLook, GlowLook with { Shading = default }, default);
        snapshot.Puffs.Add(Puff(new(0, 0, -10), look: 0));
        snapshot.Puffs.Add(Puff(new(0, 0, -10), look: 1));
        snapshot.Puffs.Add(Puff(new(0, 0, -10), look: 2));

        Drawn(Prepare(snapshot: snapshot)).Should().ContainSingle();
    }

    [Fact]
    public void APuffBehindAPaneDrawsBeforeThePaneAndOneInFrontDrawsAfterIt()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: new(0),
                position: new(0, 0, -10),
                scale: Vector3.One,
                flags: RenderFlags.Transparent
            )
        );
        snapshot.Puffs.Add(Puff(new(0, 0, -5)));
        snapshot.Puffs.Add(Puff(new(0, 0, -15)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(PuffRun(0, 1), MeshRun(0, 1), PuffRun(1, 1));
        Drawn(scene).Select(p => p.PositionRadius.Z).Should().Equal(-15f, -5f);
        scene.Transparent[DepthRanges.Far].Should().BeEmpty();
        scene.Transparent[DepthRanges.Middle].Should().BeEmpty();
    }

    [Fact]
    public void PuffsBetweenTwoPanesFormOneRun()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: new(0),
                position: new(0, 0, -5),
                scale: Vector3.One,
                flags: RenderFlags.Transparent
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: new(0),
                position: new(0, 0, -20),
                scale: Vector3.One,
                flags: RenderFlags.Transparent
            )
        );
        snapshot.Puffs.Add(Puff(new(0, 0, -12)));
        snapshot.Puffs.Add(Puff(new(0, 0, -8)));
        snapshot.Puffs.Add(Puff(new(0, 0, -15)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        // The two panes share a mesh, but the puffs between them split their draws into two commands.
        scene.Transparent[DepthRanges.Near].Should().Equal(MeshRun(0, 1), PuffRun(0, 3), MeshRun(1, 1));
        Drawn(scene).Select(p => p.PositionRadius.Z).Should().Equal(-15f, -12f, -8f);
        scene.Commands[0].FirstInstance.Should().Be(0u);
        scene.Instances[0].ModelRow2.W.Should().Be(-20f);
        scene.Commands[1].FirstInstance.Should().Be(1u);
        scene.Instances[1].ModelRow2.W.Should().Be(-5f);
    }

    [Fact]
    public void AtEqualDistancesAPaneDrawsBeforeAPuffAndPuffsKeepTheOrderOfTheSnapshot()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Puffs.Add(Puff(new(0, 0, -10), age: 1.5f));
        snapshot.Puffs.Add(Puff(new(0, 0, -10), age: 0.5f));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: new(0),
                position: new(0, 0, -10),
                scale: Vector3.One,
                flags: RenderFlags.Transparent
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(MeshRun(0, 1), PuffRun(0, 2));
        Drawn(scene).Select(p => p.PositionRadius.W).Should().Equal(2.5f, 1.5f);
    }

    [Fact]
    public void APuffDrawsInTheRangeThatHoldsItsCenter()
    {
        // Each sphere of radius 30 m reaches across the border of 2 km, but a puff draws only in the range of its center,
        // with depth clamping, as a transparent instance does.
        RenderSnapshot snapshot = PuffSnapshot(GlowLook with { StartRadius = 30f, EndRadius = 30f });
        snapshot.Puffs.Add(Puff(new(0, 0, -1_990)));
        snapshot.Puffs.Add(Puff(new(0, 0, -2_010)));
        snapshot.Puffs.Add(Puff(new(0, 0, -3e6f)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Far].Should().Equal(PuffRun(0, 1));
        scene.Transparent[DepthRanges.Middle].Should().Equal(PuffRun(1, 1));
        scene.Transparent[DepthRanges.Near].Should().Equal(PuffRun(2, 1));
        Drawn(scene).Select(p => p.PositionRadius.Z).Should().Equal(-3e6f, -2_010f, -1_990f);
    }

    [Fact]
    public void AGpuPuffIs32BytesAndCarriesTheCaseOfItsShading()
    {
        Unsafe.SizeOf<GpuPuff>().Should().Be(32);
        ((int)Marshal.OffsetOf<GpuPuff>(nameof(GpuPuff.ColorKind))).Should().Be(16);
        RenderSnapshot snapshot = PuffSnapshot(
            GlowLook,
            GlowLook with
            {
                Shading = PuffShading.Lit(new(0.8f, 0.6f, 0.4f)),
            }
        );
        snapshot.Puffs.Add(Puff(new(-2, 0, -10), look: 0));
        snapshot.Puffs.Add(Puff(new(2, 0, -10), look: 1));

        GpuPuff[] drawn = [.. Drawn(Prepare(snapshot: snapshot)).OrderBy(p => p.PositionRadius.X)];

        // Age 0.5 s of a life of 2 s: the own share is 0.75.
        ShouldBeClose(Color(drawn[0]), new Vector3(4, 2, 1) * 0.75f);
        drawn[0].ColorKind.W.Should().Be(GpuPuff.GlowKind).And.Be(0f);
        ShouldBeClose(Color(drawn[1]), new Vector3(0.8f, 0.6f, 0.4f) * 0.75f);
        drawn[1].ColorKind.W.Should().Be(GpuPuff.LitKind).And.Be(1f);
    }

    [Fact]
    public void ManyPuffsGrowThePuffArrayAndDrawBackToFront()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        for (int i = 0; i < 300; i++)
            snapshot.Puffs.Add(Puff(new(0, 0, -5 - i * 37 % 300)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(PuffRun(0, 300));
        Drawn(scene).Select(p => -p.PositionRadius.Z).Should().BeInDescendingOrder().And.HaveCount(300);
    }

    [Fact]
    public void APreparedSceneDropsThePuffsAndTheRunsOfTheFrameBefore()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Puffs.Add(Puff(new(0, 0, -10)));
        var scene = new PreparedScene();
        var batcher = new SceneBatcher();
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 1f,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: 8,
            environment: default,
            scene: scene
        );
        scene.PuffCount.Should().Be(1);

        batcher.Prepare(
            snapshot: Empty(),
            alpha: 1f,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: 8,
            environment: default,
            scene: scene
        );

        scene.PuffCount.Should().Be(0);
        scene.Transparent.Should().AllSatisfy(runs => runs.Should().BeEmpty());
    }

    [Fact]
    public void RenderStatsCountsTheDrawnPuffs()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: new(0),
                position: new(0, 0, -5),
                scale: Vector3.One,
                flags: RenderFlags.Transparent
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: new(0),
                position: new(0, 0, -20),
                scale: Vector3.One,
                flags: RenderFlags.Transparent
            )
        );
        snapshot.Puffs.Add(Puff(new(0, 0, -12)));
        snapshot.Puffs.Add(Puff(new(0, 0, -8)));
        snapshot.Puffs.Add(Puff(new(0, 0, 30))); // behind the eye

        RenderStats stats = VulkanRenderer.StatsOf(
            scene: Prepare(snapshot: snapshot),
            impostors: null,
            indirectDrawCalls: 0,
            gpuTimings: null
        );

        stats.Puffs.Should().Be(2);
        stats.TransparentCommands.Should().Be(2, "a run of puffs adds no indirect command");
    }

    [Fact]
    public void GpuStructsMatchTheShaderLayouts()
    {
        Unsafe.SizeOf<GpuInstance>().Should().Be(80);
        Unsafe.SizeOf<GpuMaterial>().Should().Be(64, "std430 rounds a struct with a vec4 up to 16 bytes");
        ((int)Marshal.OffsetOf<GpuMaterial>(nameof(GpuMaterial.Metallic))).Should().Be(28);
        ((int)Marshal.OffsetOf<GpuMaterial>(nameof(GpuMaterial.MetallicRoughnessMap))).Should().Be(48);
        Unsafe.SizeOf<GpuLight>().Should().Be(64);
        Unsafe.SizeOf<GpuOutline>().Should().Be(48);
        ((int)Marshal.OffsetOf<GpuOutline>(nameof(GpuOutline.Radii))).Should().Be(32);
        Unsafe.SizeOf<OutlinePushConstants>().Should().Be(4);
        Unsafe.SizeOf<GpuFrameConstants>().Should().Be(720);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.ShadowViewProj))).Should().Be(352);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.RootRotation))).Should().Be(704);
        Unsafe.SizeOf<GpuPuff>().Should().Be(32);
        Unsafe.SizeOf<DrawPushConstants>().Should().Be(80);
        Unsafe.SizeOf<DrawIndexedIndirectCommand>().Should().Be(20);
        Unsafe.SizeOf<Vertex>().Should().Be(48);
    }

    [Theory]
    [InlineData("mesh.vert")]
    [InlineData("gbuffer.frag")]
    [InlineData("lighting.vert")]
    [InlineData("lighting.frag")]
    [InlineData("transparent.frag")]
    [InlineData("fullscreen.vert")]
    [InlineData("tonemap.frag")]
    [InlineData("shadow.vert")]
    [InlineData("impostor_capture.frag")]
    [InlineData("impostor.vert")]
    [InlineData("impostor.frag")]
    [InlineData("outline.vert")]
    [InlineData("outline.frag")]
    [InlineData("puff.vert")]
    [InlineData("puff.frag")]
    public void ShadersAreCompiledAndEmbedded(string file)
    {
        byte[] spirv = ShaderLibrary.LoadSpirv(file);
        spirv.Length.Should().BeGreaterThan(100);
        (spirv.Length % 4).Should().Be(0, "SPIR-V is a stream of 32-bit words");
    }
}
