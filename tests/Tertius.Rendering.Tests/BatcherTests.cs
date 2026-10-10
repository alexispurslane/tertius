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
            snapshot.Instances.Add(Gpu.Instance(new MeshId(i % 2), new MaterialId(1 + i % 3), new Vector3(i % 10 - 5, 0, -10 - i), Vector3.One));
        PreparedScene scene = Prepare(snapshot);

        Assert.Equal(100, scene.VisibleCount);
        Assert.Equal(100, scene.InstanceCount);
        Assert.Equal(2, scene.CommandCount);
        Assert.Equal(new CommandRange(0, 2), scene.Opaque[DepthRanges.Near]);
        DrawIndexedIndirectCommand first = scene.Commands[0], second = scene.Commands[1];
        Assert.Equal(50u, first.InstanceCount);
        Assert.Equal(50u, second.InstanceCount);
        Assert.Equal(0u, first.FirstInstance);
        Assert.Equal(50u, second.FirstInstance);
        Assert.Equal(Meshes[1].FirstIndex, second.FirstIndex);
        Assert.Equal(Meshes[1].IndexCount, second.IndexCount);
        Assert.Equal(Meshes[1].VertexOffset, second.VertexOffset);
    }

    [Fact]
    public void RangesAreOrderedFarToNearAndStraddlersAppearInBoth()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -10), Vector3.One));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -50_000), new Vector3(1_000)));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(1), new MaterialId(1), new Vector3(0, 0, -3e7f), new Vector3(6e6f)));
        // Radius 50 m around 2 km: drawn in the near and middle ranges.
        snapshot.Instances.Add(Gpu.Instance(new MeshId(1), new MaterialId(1), new Vector3(0, 0, -2_000), new Vector3(50)));
        PreparedScene scene = Prepare(snapshot);

        Assert.Equal(4, scene.VisibleCount);
        Assert.Equal(5, scene.InstanceCount);
        Assert.Equal(1, scene.Opaque[DepthRanges.Far].Count);
        Assert.Equal(2, scene.Opaque[DepthRanges.Middle].Count);
        Assert.Equal(2, scene.Opaque[DepthRanges.Near].Count);
        Assert.True(scene.Opaque[DepthRanges.Far].First < scene.Opaque[DepthRanges.Middle].First);
        Assert.True(scene.Opaque[DepthRanges.Middle].First < scene.Opaque[DepthRanges.Near].First);
        // The planet's instance is the first one written, in the far range.
        Assert.Equal(-3e7f, scene.Instances[scene.Commands[scene.Opaque[DepthRanges.Far].First].FirstInstance].ModelRow2.W);
    }

    [Fact]
    public void TransparentInstancesAreSortedBackToFrontAcrossRanges()
    {
        RenderSnapshot snapshot = Empty();
        float[] depths = [5, 3_000, 20, 1e7f, 12, 700];
        foreach (float depth in depths)
            snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -depth), Vector3.One, flags: RenderFlags.Transparent));
        PreparedScene scene = Prepare(snapshot);

        Assert.Equal(6, scene.InstanceCount);
        Assert.Equal(new CommandRange(0, 1), scene.Transparent[DepthRanges.Far]);
        Assert.Equal(new CommandRange(1, 1), scene.Transparent[DepthRanges.Middle]);
        Assert.Equal(new CommandRange(2, 1), scene.Transparent[DepthRanges.Near]);
        Assert.Equal(4u, scene.Commands[2].InstanceCount);
        float[] drawn = scene.Instances.AsSpan(0, scene.InstanceCount).ToArray().Select(i => -i.ModelRow2.W).ToArray();
        Assert.Equal([1e7f, 3_000, 700, 20, 12, 5], drawn);
        Assert.All(scene.Opaque, range => Assert.Equal(0, range.Count));
    }

    [Fact]
    public void CullingDropsInvisibleBehindAndOffscreenInstances()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -5), Vector3.One));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, 5), Vector3.One));        // behind
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(100, 0, -5), Vector3.One));     // far right
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, -100, -5), Vector3.One));    // far below
        snapshot.Instances.Add(Gpu.Instance(new MeshId(7), new MaterialId(1), new Vector3(0, 0, -5), Vector3.One));       // unknown mesh
        RenderInstance hidden = Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -5), Vector3.One);
        hidden.Flags = RenderFlags.CastsShadow;                                                                         // not Visible
        snapshot.Instances.Add(hidden);
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -5), new Vector3(1, 0, 1))); // flat
        // Partly in view: the center is outside the right plane but the bounding sphere reaches in.
        snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(9.5f, 0, -5), Vector3.One, radius: 4f));
        PreparedScene scene = Prepare(snapshot);
        Assert.Equal(8, scene.SubmittedInstances);
        Assert.Equal(2, scene.VisibleCount);
    }

    [Fact]
    public void InterpolationUsesAlphaForInstancesAndTheCamera()
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance instance = Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(4, 0, -10), Vector3.One);
        instance.PreviousPosition = new Vector3(-4, 0, -10);
        instance.PreviousRotation = Quaternion.Identity;
        instance.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        snapshot.Instances.Add(instance);
        snapshot.Camera.PreviousPosition = new Vector3(0, 2, 0);

        PreparedScene quarter = Prepare(snapshot, alpha: 0.25f);
        PreparedInstance prepared = quarter.Visible[0];
        // Instance at lerp(-4, 4, 0.25) = -2; the camera at lerp(2, 0, 0.25) = 1.5 is subtracted.
        Assert.Equal(new Vector3(-2, -1.5f, -10), prepared.Position);
        Assert.Equal(new Vector3(0, 1.5f, 0), quarter.Camera.Offset);
        float angle = 2 * MathF.Acos(Math.Clamp(prepared.Rotation.W, -1, 1));
        Assert.Equal(MathF.PI / 8, angle, 4);

        // Alpha outside [0, 1] is clamped.
        Assert.Equal(new Vector3(4, 0, -10), Prepare(snapshot, alpha: 3f).Visible[0].Position);
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
        Assert.Equal(Quaternion.Identity, scene.Visible[0].Rotation);
        Assert.Equal(0u, scene.Visible[0].Material);
        Assert.Equal(MathF.PI / 3, scene.Camera.VerticalFov, 5);
    }

    [Fact]
    public void LightsAreConvertedAndCulled()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera.PreviousPosition = new Vector3(0, 0, 2);
        snapshot.Lights.Add(new RenderLight { Kind = LightKind.Point, Position = new Vector3(0, 1, -5), Color = new Vector3(1, 0.5f, 0.25f), Intensity = 4, Range = 10 });
        snapshot.Lights.Add(new RenderLight { Kind = LightKind.Spot, Position = new Vector3(0, 3, -5), Direction = new Vector3(0, -2, 0), Color = Vector3.One, Intensity = 1, Range = 8, InnerConeAngle = 0.3f, OuterConeAngle = 0.5f });
        snapshot.Lights.Add(new RenderLight { Kind = LightKind.Point, Position = new Vector3(0, 0, 50), Color = Vector3.One, Intensity = 1, Range = 10 });   // behind
        snapshot.Lights.Add(new RenderLight { Kind = LightKind.Point, Position = new Vector3(0, 0, -5), Color = Vector3.One, Intensity = 1, Range = 0 });    // no range
        snapshot.Occluders.Add(new RenderOccluder { Center = new Vector3(0, 0, -100), Radius = 10 });
        PreparedScene scene = Prepare(snapshot, alpha: 0.5f);

        Assert.Equal(2, scene.LightCount);
        GpuLight point = scene.Lights[0], spot = scene.Lights[1];
        Assert.Equal(new Vector4(0, 1, -6, 10), point.PositionRange);
        Assert.Equal(new Vector4(4, 2, 1, 0), point.RadianceKind);
        Assert.Equal(1f, spot.RadianceKind.W);
        Assert.Equal(new Vector3(0, -1, 0), new Vector3(spot.DirectionCosOuter.X, spot.DirectionCosOuter.Y, spot.DirectionCosOuter.Z));
        Assert.Equal(MathF.Cos(0.5f), spot.DirectionCosOuter.W, 5);
        Assert.Equal(MathF.Cos(0.3f), spot.Spot.X, 5);
        Assert.Equal(1, scene.OccluderCount);
        Assert.Equal(new Vector4(0, 0, -101, 10), scene.Occluders[0]);
        Assert.Equal(2u, scene.Constants.LightCount);
        Assert.Equal(1u, scene.Constants.OccluderCount);
    }

    [Fact]
    public void ProcessorsCanReplaceInstancesBeforeBatching()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 10; i++)
            snapshot.Instances.Add(Gpu.Instance(new MeshId(0), new MaterialId(1), new Vector3(0, 0, -10 - i * 100), Vector3.One));
        var batcher = new SceneBatcher();
        batcher.Processors.Add(new BillboardSwap());
        PreparedScene scene = Prepare(snapshot, batcher: batcher);
        // Objects beyond 300 m became mesh 2 "billboards".
        Assert.Equal(10, scene.VisibleCount);
        Assert.Equal(2, scene.CommandCount);
        Assert.Equal(3u, scene.Commands[0].InstanceCount);
        Assert.Equal(7u, scene.Commands[1].InstanceCount);
        Assert.Equal(Meshes[2].FirstIndex, scene.Commands[1].FirstIndex);
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
        Assert.True(Vector3.Distance(expected, transformed) < 1e-4f, $"{transformed} vs {expected}");

        // The shader's normal transform R · S⁻¹ · n from M · (n / s²).
        var normal = Vector3.Normalize(new Vector3(1, 2, 3));
        var linear = new Matrix4x4(
            instance.ModelRow0.X, instance.ModelRow1.X, instance.ModelRow2.X, 0,
            instance.ModelRow0.Y, instance.ModelRow1.Y, instance.ModelRow2.Y, 0,
            instance.ModelRow0.Z, instance.ModelRow1.Z, instance.ModelRow2.Z, 0,
            0, 0, 0, 1);
        Vector3 shaderNormal = Vector3.Normalize(Vector3.TransformNormal(normal * instance.InverseScaleSquared, linear));
        Vector3 expectedNormal = Vector3.Normalize(Vector3.Transform(normal / scale, rotation));
        Assert.True(Vector3.Distance(expectedNormal, shaderNormal) < 1e-5f, $"{shaderNormal} vs {expectedNormal}");
        Assert.Equal(7u, instance.Material);
    }

    [Fact]
    public void GpuStructsMatchTheShaderLayouts()
    {
        Assert.Equal(64, Unsafe.SizeOf<GpuInstance>());
        Assert.Equal(48, Unsafe.SizeOf<GpuMaterial>());
        Assert.Equal(64, Unsafe.SizeOf<GpuLight>());
        Assert.Equal(704, Unsafe.SizeOf<GpuFrameConstants>());
        Assert.Equal(352, (int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.ShadowViewProj)));
        Assert.Equal(80, Unsafe.SizeOf<DrawPushConstants>());
        Assert.Equal(20, Unsafe.SizeOf<DrawIndexedIndirectCommand>());
        Assert.Equal(48, Unsafe.SizeOf<Vertex>());
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
    public void ShadersAreCompiledAndEmbedded(string file)
    {
        byte[] spirv = ShaderLibrary.LoadSpirv(file);
        Assert.True(spirv.Length > 100);
        Assert.Equal(0, spirv.Length % 4);
    }
}
