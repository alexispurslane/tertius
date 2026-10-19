using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Tertius.Rendering;

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

    private static RenderSnapshot Empty() => Gpu.Snapshot(Vector3.UnitY);

    private static PreparedScene Prepare(RenderSnapshot snapshot, float alpha = 1f, int materials = 8, SceneBatcher? batcher = null)
    {
        var scene = new PreparedScene();
        (batcher ?? new SceneBatcher()).Prepare(snapshot, alpha, 1600, 900, Meshes, materials, scene);
        return scene;
    }

    [Fact]
    public void InstancesOfOneMeshInOneRangeShareOneCommand()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 100; i++)
            snapshot.Instances.Add(Gpu.Instance(new MeshId(i % 2), new MaterialId(i % 3), new Vector3(i % 10 - 5, 0, -10 - i), Vector3.One));
        PreparedScene scene = Prepare(snapshot);

        scene.VisibleCount.Should().Be(100);
        scene.InstanceCount.Should().Be(100);
        scene.CommandCount.Should().Be(2);
        scene.Opaque[DepthRanges.Near].Should().Be(new CommandRange(0, 2));
        DrawIndexedIndirectCommand first = scene.Commands[0], second = scene.Commands[1];
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
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(0, 0, -10), Vector3.One));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(0, 0, -50_000), new Vector3(1_000)));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(1), new MaterialId(0), new Vector3(0, 0, -3e7f), new Vector3(6e6f)));
        // Radius 50 m around 2 km: drawn in the near and middle ranges.
        snapshot.Instances.Add(Gpu.Instance(new MeshId(1), new MaterialId(0), new Vector3(0, 0, -2_000), new Vector3(50)));
        PreparedScene scene = Prepare(snapshot);

        scene.VisibleCount.Should().Be(4);
        scene.InstanceCount.Should().Be(5, "the straddler is written once per range");
        scene.Opaque[DepthRanges.Far].Count.Should().Be(1);
        scene.Opaque[DepthRanges.Middle].Count.Should().Be(2);
        scene.Opaque[DepthRanges.Near].Count.Should().Be(2);
        scene.Opaque[DepthRanges.Far].First.Should().BeLessThan(scene.Opaque[DepthRanges.Middle].First);
        scene.Opaque[DepthRanges.Middle].First.Should().BeLessThan(scene.Opaque[DepthRanges.Near].First);
        // The planet's instance is the first one written, in the far range.
        scene.Instances[scene.Commands[scene.Opaque[DepthRanges.Far].First].FirstInstance].ModelRow2.W.Should().Be(-3e7f);
    }

    [Fact]
    public void TransparentInstancesAreSortedBackToFrontAcrossRanges()
    {
        RenderSnapshot snapshot = Empty();
        float[] depths = [5, 3_000, 20, 1e7f, 12, 700];
        foreach (float depth in depths)
            snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(0, 0, -depth), Vector3.One, flags: RenderFlags.Transparent));
        PreparedScene scene = Prepare(snapshot);

        scene.InstanceCount.Should().Be(6);
        scene.Transparent[DepthRanges.Far].Should().Be(new CommandRange(0, 1));
        scene.Transparent[DepthRanges.Middle].Should().Be(new CommandRange(1, 1));
        scene.Transparent[DepthRanges.Near].Should().Be(new CommandRange(2, 1));
        scene.Commands[2].InstanceCount.Should().Be(4u);
        float[] drawn = scene.Instances.AsSpan(0, scene.InstanceCount).ToArray().Select(i => -i.ModelRow2.W).ToArray();
        drawn.Should().Equal(1e7f, 3_000f, 700f, 20f, 12f, 5f);
        scene.Opaque.Should().AllSatisfy(range => range.Count.Should().Be(0));
    }

    [Fact]
    public void CullingDropsInvisibleBehindAndOffscreenInstances()
    {
        RenderSnapshot snapshot = Empty();
        var box = new MeshId(0);
        var material = new MaterialId(0);
        snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(0, 0, -5), Vector3.One));
        snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(0, 0, 5), Vector3.One));          // behind
        snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(100, 0, -5), Vector3.One));       // far right
        snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(0, -100, -5), Vector3.One));      // far below
        snapshot.Instances.Add(Gpu.Instance(new MeshId(7), material, new Vector3(0, 0, -5), Vector3.One)); // unknown mesh
        snapshot.Instances.Add(Gpu.Instance(MeshId.None, material, new Vector3(0, 0, -5), Vector3.One)); // no mesh
        snapshot.Instances.Add(RenderInstance.Create(box, material, new Vector3(0, 0, -5), Quaternion.Identity, Vector3.One, 0f, HemisphereUp.Even,
            RenderFlags.CastsShadow));                                                                   // not Visible
        snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(0, 0, -5), new Vector3(1, 0, 1))); // flat
        snapshot.Instances.Add(default);                                                                 // zeroed
        // Partly in view: the center is outside the right plane but the bounding sphere reaches in.
        snapshot.Instances.Add(Gpu.Instance(box, material, new Vector3(9.5f, 0, -5), Vector3.One, radius: 4f));
        PreparedScene scene = Prepare(snapshot);
        scene.SubmittedInstances.Should().Be(10);
        scene.VisibleCount.Should().Be(2);
    }

    [Fact]
    public void InterpolationUsesAlphaForInstancesAndTheCamera()
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance instance = Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(4, 0, -10), Vector3.One,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        instance.PreviousPosition = new Vector3(-4, 0, -10);
        instance.PreviousRotation = Quaternion.Identity;
        snapshot.Instances.Add(instance);
        snapshot.Camera.PreviousPosition = new Vector3(0, 2, 0);

        PreparedScene quarter = Prepare(snapshot, alpha: 0.25f);
        PreparedInstance prepared = quarter.Visible[0];
        // Instance at lerp(-4, 4, 0.25) = -2; the camera at lerp(2, 0, 0.25) = 1.5 is subtracted.
        prepared.Position.Should().Be(new Vector3(-2, -1.5f, -10));
        quarter.Camera.Offset.Should().Be(new Vector3(0, 1.5f, 0));
        float angle = 2 * MathF.Acos(Math.Clamp(prepared.Rotation.W, -1, 1));
        angle.Should().BeApproximately(MathF.PI / 8, 1e-4f);

        // Alpha outside [0, 1] is clamped.
        Prepare(snapshot, alpha: 3f).Visible[0].Position.Should().Be(new Vector3(4, 0, -10));
    }

    [Fact]
    public void UnsetQuaternionsCountAsIdentityAndUnknownMaterialsAsDefault()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera = new RenderCamera { VerticalFov = 0 };
        RenderInstance instance = Gpu.Instance(new MeshId(0), new MaterialId(42), new Vector3(0, 0, -5), Vector3.One);
        instance.Rotation = default;
        instance.PreviousRotation = default;
        snapshot.Instances.Add(instance);
        PreparedScene scene = Prepare(snapshot, materials: 4);
        scene.Visible[0].Rotation.Should().Be(Quaternion.Identity);
        scene.Visible[0].Material.Should().Be(MaterialStore.DefaultSlot);
        scene.Camera.VerticalFov.Should().BeApproximately(MathF.PI / 3, 1e-5f);
    }

    [Fact]
    public void NoneMeansNoMeshOrTheDefaultMaterialAndRealIdsStartAtZero()
    {
        RenderSnapshot snapshot = Empty();
        // Four slots on the GPU: the default and materials 0 to 2.
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), MaterialId.None, new Vector3(-2, 0, -5), Vector3.One, entity: 1));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(0, 0, -5), Vector3.One, entity: 2));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(2), new Vector3(2, 0, -5), Vector3.One, entity: 3));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(3), new Vector3(0, 2, -5), Vector3.One, entity: 4));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(-7), new Vector3(0, -2, -5), Vector3.One, entity: 5));
        snapshot.Instances.Add(Gpu.Instance(MeshId.None, new MaterialId(0), new Vector3(0, 0, -6), Vector3.One, entity: 6));
        PreparedScene scene = Prepare(snapshot, materials: MaterialStore.BuiltInCount + 3);

        Dictionary<uint, uint> materials = scene.VisibleSpan.ToArray().ToDictionary(v => v.EntityIndex, v => v.Material);
        materials.Should().NotContainKey(6u, "MeshId.None draws nothing");
        materials[1].Should().Be(MaterialStore.DefaultSlot, "MaterialId.None asks for the default material");
        // Material 0 is a real material, not the default: ids are not confused with None.
        materials[2].Should().Be(MaterialStore.Slot(new MaterialId(0))).And.NotBe(MaterialStore.DefaultSlot);
        materials[3].Should().Be(3u);
        materials[4].Should().Be(MaterialStore.DefaultSlot, "material 3 is not on the GPU");
        materials[5].Should().Be(MaterialStore.DefaultSlot, "no material has a negative id");

        MeshId.None.IsNone.Should().BeTrue();
        default(MeshId).Should().Be(MeshId.None);
        SceneBatcher.MeshIndex(MeshId.None, Meshes.Length).Should().Be(-1);
        SceneBatcher.MeshIndex(new MeshId(0), Meshes.Length).Should().Be(0);
        SceneBatcher.MeshIndex(new MeshId(Meshes.Length), Meshes.Length).Should().Be(-1);
    }

    [Fact]
    public void ThePartOfAnEntityReachesThePreparedInstance()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(-1, 0, -5), Vector3.One, entity: 9, part: 0));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(1, 0, -5), Vector3.One, entity: 9, part: 3));
        PreparedScene scene = Prepare(snapshot);
        scene.VisibleSpan.ToArray().Select(v => (v.EntityIndex, v.Part)).Should().Equal((9u, (ushort)0), (9u, (ushort)3));
    }

    [Theory]
    [InlineData(0f, 1f, 0f, false)]
    [InlineData(-2f, 1f, 0f, false)]
    [InlineData(float.NaN, 1f, 0f, false)]
    [InlineData(float.PositiveInfinity, 1f, 0f, false)]
    [InlineData(3f, 0f, 0f, false)]                      // no direction
    [InlineData(3f, float.PositiveInfinity, 0f, false)]  // a direction that cannot be normalized
    [InlineData(3f, 1f, 0f, true)]
    [InlineData(0.001f, 0f, 5f, true)]                   // need not be normalized
    public void ASunWithoutAPositiveIntensityOrADirectionIsOff(float intensity, float y, float z, bool lit)
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Sun = new RenderSun { Direction = new Vector3(0, y, z), Color = Vector3.One, Intensity = intensity, AngularRadius = 0.005f };
        SceneBatcher.HasSun(snapshot.Sun).Should().Be(lit);
        PreparedScene scene = Prepare(snapshot);
        Vector4 radiance = scene.Constants.SunRadiance;
        if (lit)
        {
            radiance.X.Should().BeApproximately(intensity, 1e-6f);
            new Vector3(scene.Constants.SunDirection.X, scene.Constants.SunDirection.Y, scene.Constants.SunDirection.Z).Length().Should().BeApproximately(1f, 1e-5f);
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
        snapshot.Camera.PreviousPosition = new Vector3(0, 0, 2);
        snapshot.Lights.Add(new PointRenderLight(new Vector3(0, 1, -5), new Vector3(1, 0.5f, 0.25f), Intensity: 4, Range: 10));
        snapshot.Lights.Add(new SpotRenderLight(new Vector3(0, 3, -5), new Vector3(0, -2, 0), Vector3.One, Intensity: 1, Range: 8, InnerConeAngle: 0.3f, OuterConeAngle: 0.5f));
        snapshot.Lights.Add(new PointRenderLight(new Vector3(0, 0, 50), Vector3.One, Intensity: 1, Range: 10));   // behind
        snapshot.Lights.Add(new PointRenderLight(new Vector3(0, 0, -5), Vector3.One, Intensity: 1, Range: 0));    // no range
        PreparedScene scene = Prepare(snapshot, alpha: 0.5f);

        scene.LightCount.Should().Be(2);
        GpuLight point = scene.Lights[0], spot = scene.Lights[1];
        point.PositionRange.Should().Be(new Vector4(0, 1, -6, 10));
        point.RadianceKind.Should().Be(new Vector4(4, 2, 1, 0));
        // Each case has its own arm in the batcher, so each case's position relative to the camera, and its range, needs its own check.
        spot.PositionRange.Should().Be(new Vector4(0, 3, -6, 8));
        spot.RadianceKind.W.Should().Be(1f);
        new Vector3(spot.DirectionCosOuter.X, spot.DirectionCosOuter.Y, spot.DirectionCosOuter.Z).Should().Be(new Vector3(0, -1, 0));
        spot.DirectionCosOuter.W.Should().BeApproximately(MathF.Cos(0.5f), 1e-5f);
        spot.Spot.X.Should().BeApproximately(MathF.Cos(0.3f), 1e-5f);
        scene.Constants.LightCount.Should().Be(2u);
    }

    [Fact]
    public void ALightThatCannotLightAnythingIsLeftOutWhateverItsCase()
    {
        RenderSnapshot snapshot = Empty();
        // The negative range is far ahead: close to the eye, the frustum test would leave a light of negative range out by itself.
        Vector3 ahead = new(0, 0, -5), farAhead = new(0, 0, -50), behind = new(0, 0, 50);
        // Each reason to leave a light out, once for each case: no range, a negative range, no intensity, and a sphere of reach outside the view.
        foreach ((Vector3 position, float intensity, float range) in new[] { (ahead, 1f, 0f), (farAhead, 1f, -3f), (ahead, 0f, 10f), (behind, 1f, 10f) })
        {
            snapshot.Lights.Add(new PointRenderLight(position, Vector3.One, intensity, range));
            snapshot.Lights.Add(new SpotRenderLight(position, -Vector3.UnitY, Vector3.One, intensity, range, 0.3f, 0.5f));
        }
        snapshot.Lights.Add(default);
        // One of each case that reaches the view, to show that the others were left out and not everything.
        snapshot.Lights.Add(new PointRenderLight(ahead, Vector3.One, 1, 10));
        snapshot.Lights.Add(new SpotRenderLight(ahead, -Vector3.UnitY, Vector3.One, 1, 10, 0.3f, 0.5f));

        PreparedScene scene = Prepare(snapshot);

        scene.LightCount.Should().Be(2, "the empty light holds no light, and the eight lights that reach nothing are left out");
        scene.Lights[0].RadianceKind.W.Should().Be(0f);
        scene.Lights[1].RadianceKind.W.Should().Be(1f);
    }

    [Fact]
    public void APointLightLeavesTheDirectionAndTheConeOfItsGpuLightZeroBecauseItHasNone()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(new PointRenderLight(new Vector3(0, 1, -5), Vector3.One, 1, 10));

        PreparedScene scene = Prepare(snapshot);

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
        snapshot.Lights.Add(new SpotRenderLight(new Vector3(0, 1, -5), Vector3.Zero, Vector3.One, 1, 10, 0.3f, 0.5f));

        PreparedScene scene = Prepare(snapshot);

        scene.LightCount.Should().Be(1);
        GpuLight spot = scene.Lights[0];
        new Vector3(spot.DirectionCosOuter.X, spot.DirectionCosOuter.Y, spot.DirectionCosOuter.Z).Should().Be(new Vector3(0, -1, 0));
    }

    [Fact]
    public void ASpotLightWhoseInnerConeIsNotNarrowerThanItsOuterConeStillFadesOverANonEmptyInterval()
    {
        // The shader's smoothstep needs an inner cosine above the outer one, so the batcher widens an empty or inverted interval.
        RenderSnapshot snapshot = Empty();
        snapshot.Lights.Add(new SpotRenderLight(new Vector3(0, 1, -5), -Vector3.UnitY, Vector3.One, 1, 10, InnerConeAngle: 0.6f, OuterConeAngle: 0.4f));

        PreparedScene scene = Prepare(snapshot);

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
        {
            snapshot.Lights.Add(i % 2 == 0
                ? new PointRenderLight(new Vector3(0, 1, -5), Vector3.One, Intensity: i + 1, Range: 10)
                : new SpotRenderLight(new Vector3(0, 1, -5), -Vector3.UnitY, Vector3.One, Intensity: i + 1, Range: 10, InnerConeAngle: 0.3f, OuterConeAngle: 0.5f));
        }

        PreparedScene scene = Prepare(snapshot);

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
            snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(0), new Vector3(0, 0, -10 - i * 100), Vector3.One));
        var batcher = new SceneBatcher();
        batcher.Processors.Add(new BillboardSwap());
        PreparedScene scene = Prepare(snapshot, batcher: batcher);
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
        var rotation = Quaternion.Normalize(new Quaternion(0.3f, -0.5f, 0.2f, 0.8f));
        var scale = new Vector3(2, 3, 0.5f);
        var position = new Vector3(10, -20, 30);
        GpuInstance instance = GpuInstance.From(position, rotation, scale, 7);
        var point = new Vector3(0.4f, -1.1f, 2.5f);
        var p = new Vector4(point, 1);
        var transformed = new Vector3(Vector4.Dot(instance.ModelRow0, p), Vector4.Dot(instance.ModelRow1, p), Vector4.Dot(instance.ModelRow2, p));
        Vector3 expected = position + Vector3.Transform(point * scale, rotation);
        Vector3.Distance(expected, transformed).Should().BeLessThan(1e-4f, "the model matrix is T · R · S");

        // The shader's normal transform R · S⁻¹ · n from M · (n / s²).
        var normal = Vector3.Normalize(new Vector3(1, 2, 3));
        var linear = new Matrix4x4(
            instance.ModelRow0.X, instance.ModelRow1.X, instance.ModelRow2.X, 0,
            instance.ModelRow0.Y, instance.ModelRow1.Y, instance.ModelRow2.Y, 0,
            instance.ModelRow0.Z, instance.ModelRow1.Z, instance.ModelRow2.Z, 0,
            0, 0, 0, 1);
        Vector3 shaderNormal = Vector3.Normalize(Vector3.TransformNormal(normal * instance.InverseScaleSquared, linear));
        Vector3 expectedNormal = Vector3.Normalize(Vector3.Transform(normal / scale, rotation));
        Vector3.Distance(expectedNormal, shaderNormal).Should().BeLessThan(1e-5f, "normals transform by R · S⁻¹");
        instance.Material.Should().Be(7u);
    }

    [Fact]
    public void EachInstanceTakesItsHemispheresUpToTheGpuRelativeToTheInterpolatedEye()
    {
        RenderSnapshot snapshot = Empty();
        // At alpha 0.5 the eye is 1 m along +X from the render origin, so every position moves 1 m along -X.
        snapshot.Camera.PreviousPosition = new Vector3(2, 0, 0);
        snapshot.Instances.Add(RenderInstance.Create(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -10), Quaternion.Identity, Vector3.One, 1,
            HemisphereUp.Along(Vector3.UnitX)));
        RenderInstance moving = RenderInstance.Create(new MeshId(0), new MaterialId(1), new Vector3(0, 1, -10), Quaternion.Identity, Vector3.One, 1,
            HemisphereUp.AwayFrom(new Vector3(0, -50, 0)));
        moving.PreviousPosition = new Vector3(4, 1, -10);
        snapshot.Instances.Add(moving);
        snapshot.Instances.Add(RenderInstance.Create(new MeshId(0), new MaterialId(1), new Vector3(0, 2, -10), Quaternion.Identity, Vector3.One, 1,
            HemisphereUp.Even));

        PreparedScene scene = Prepare(snapshot, alpha: 0.5f);

        // The kind is in w: 1 along a direction, 2 away from a point, 0 even. The point moves with its instance: halfway
        // from x = 4 to x = 0 is x = 2, less the eye's 1 m, so 50 m below (1, 1, -10).
        scene.Instances.AsSpan(0, 3).ToArray().Select(instance => instance.Hemisphere).Should().BeEquivalentTo(new[]
        {
            new Vector4(1, 0, 0, 1),
            new Vector4(1, -49, -10, 2),
            Vector4.Zero,
        });
    }

    [Fact]
    public void TheRootRotationBlendsFromThePreviousTickAsTheCameraDoes()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.PreviousRootRotation = Quaternion.Identity;
        snapshot.RootRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

        Vector4 root = Prepare(snapshot, alpha: 0.25f).Constants.RootRotation;

        // A quarter of the way from the previous rotation to the current one: an eighth of a turn about +Y.
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 8);
        Vector4.Distance(root, new Vector4(expected.X, expected.Y, expected.Z, expected.W)).Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void AnUnsetRootRotationLeavesTheSkyInTheAxesOfTheCamerasFrame()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.RootRotation = default;
        snapshot.PreviousRootRotation = default;

        Prepare(snapshot).Constants.RootRotation.Should().Be(new Vector4(0, 0, 0, 1));
    }

    [Fact]
    public void GpuStructsMatchTheShaderLayouts()
    {
        Unsafe.SizeOf<GpuInstance>().Should().Be(80);
        Unsafe.SizeOf<GpuMaterial>().Should().Be(48);
        Unsafe.SizeOf<GpuLight>().Should().Be(64);
        Unsafe.SizeOf<GpuOutline>().Should().Be(48);
        ((int)Marshal.OffsetOf<GpuOutline>(nameof(GpuOutline.Radii))).Should().Be(32);
        Unsafe.SizeOf<OutlinePushConstants>().Should().Be(4);
        Unsafe.SizeOf<GpuFrameConstants>().Should().Be(720);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.ShadowViewProj))).Should().Be(352);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.RootRotation))).Should().Be(704);
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
    public void ShadersAreCompiledAndEmbedded(string file)
    {
        byte[] spirv = ShaderLibrary.LoadSpirv(file);
        spirv.Length.Should().BeGreaterThan(100);
        (spirv.Length % 4).Should().Be(0, "SPIR-V is a stream of 32-bit words");
    }
}
