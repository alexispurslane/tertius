using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shaders keep NaN and infinity out of the images that they write (ARCHITECTURE.md, "Portable Vulkan"). Each test
/// feeds an input that breaks a shader that relies on what Metal does where the Vulkan spec promises nothing, and reads
/// the half floats of the HDR image or of a bloom level back. Metal saturates an overflowing half float at 65,504 and
/// keeps a NaN, so a test that looks only at a NaN cannot fail for an overflow on this Mac: those tests also assert the
/// cap below the largest half float that leaves room for the rounding of a blend.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class PortableFloatTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The largest finite half float. A blend that rounds past it stores infinity on a GPU that is not Metal.</summary>
    private const float LargestHalf = 65504;

    /// <summary>
    /// The most that a pixel of the HDR image holds: the surfaces' cap and the puffs' budget, 32,256 each. It lies 992,
    /// 31 steps of the half float there, below the largest half float, for the rounding of the blends.
    /// </summary>
    private const float HdrBound = 64512;

    /// <summary>A plane facing the eye, 3 m ahead: the XZ plane turned so that its +Y faces +Z.</summary>
    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    private static VulkanRenderer Headless(float stops = 0, BloomSetting bloom = default) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                Exposure = new FixedExposure(stops),
                Bloom = bloom,
                AntiAliasing = AntiAliasing.None,
            }
        );

    /// <summary>The red, green and blue of the pixel at the center of a read-back HDR image.</summary>
    private static Vector3 Center(float[] image)
    {
        int i = (Height / 2 * Width + Width / 2) * 4;
        return new(image[i], image[i + 1], image[i + 2]);
    }

    /// <summary>The plane of 2 m that <see cref="Facing"/> turns toward the eye, with every normal set to <paramref name="normal"/>.</summary>
    private static MeshData PlaneWithNormals(Vector3 normal)
    {
        MeshData plane = Primitives.Plane(1);
        Vertex[] vertices = [.. plane.Vertices];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i].Normal = normal;
        return new(vertices, plane.Indices);
    }

    /// <summary>A white matte material.</summary>
    private static MaterialId Matte(VulkanRenderer renderer, float opacity = 1) =>
        renderer.CreateMaterial(new(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: opacity), Roughness: 0.6f));

    /// <summary>A sunlit scene of one plane 3 m ahead that faces the eye, with the sun behind the eye.</summary>
    private static RenderSnapshot PlaneAhead(MeshId plane, MaterialId material, RenderFlags flags = RenderFlags.None)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.2f, 0.4f, 1));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: material,
                position: new(0, 0, -3),
                scale: Vector3.One,
                rotation: Facing,
                flags: flags,
                radius: 1.5f
            )
        );
        return snapshot;
    }

    /// <summary>
    /// A sunlit scene of one plane 3 m ahead that faces the eye, seen along −X, with the sun behind the eye and above it,
    /// and a box out of view that casts a shadow, so that the shadow maps exist. The plane faces +X, so a normal read
    /// as any of the axes that a broken normal decodes to, such as +Z, faces away from the sun.
    /// </summary>
    private static RenderSnapshot PlaneToTheSide(
        VulkanRenderer renderer,
        MeshId plane,
        MaterialId material,
        RenderFlags flags = RenderFlags.None
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(1, 0.4f, -0.8f));
        var alongMinusX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        snapshot.Camera = snapshot.Camera with { Rotation = alongMinusX, PreviousRotation = alongMinusX };
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: material,
                position: new(-3, 0, 0),
                scale: Vector3.One,
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2),
                flags: flags,
                radius: 1.5f
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: material,
                position: new(0, 0, 20),
                scale: Vector3.One,
                flags: RenderFlags.CastsShadow,
                radius: 0.9f
            )
        );
        return snapshot;
    }

    /// <summary>A wall that fills the view 10 m ahead and emits <paramref name="radiance"/>.</summary>
    private static RenderInstance Wall(VulkanRenderer renderer, Vector3 radiance) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Plane(50)),
            material: Glow(renderer, radiance),
            position: new(0, 0, -10),
            scale: Vector3.One,
            rotation: Facing,
            radius: 71
        );

    /// <summary>The HDR image's pixel at the center after one frame of <paramref name="snapshot"/>.</summary>
    private static Vector3 HdrCenter(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        renderer.Render(snapshot, 1f);
        return Center(renderer.ReadHdrImage());
    }

    [Fact]
    public void AMeshWithZeroLengthNormalsShadesAsASurfaceThatFacesTheEye()
    {
        VulkanRenderer renderer = Headless();
        try
        {
            MaterialId matte = Matte(renderer);
            Vector3 zero = HdrCenter(
                renderer,
                PlaneToTheSide(
                    renderer: renderer,
                    plane: renderer.CreateMesh(PlaneWithNormals(Vector3.Zero)),
                    material: matte
                )
            );
            // The plane's mesh +Y faces the eye once it is turned.
            Vector3 facing = HdrCenter(
                renderer,
                PlaneToTheSide(
                    renderer: renderer,
                    plane: renderer.CreateMesh(PlaneWithNormals(Vector3.UnitY)),
                    material: matte
                )
            );

            facing.X.Should().BeGreaterThan(0.05f, "the sun behind the eye lights the plane");
            LargestChannelDifference(zero, facing)
                .Should()
                .BeLessThan(
                    0.01f * facing.X,
                    "a normal of length 0 has no direction, so the surface takes the direction toward the eye"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlassPaneWithZeroLengthNormalsShadesAsAPaneThatFacesTheEye()
    {
        VulkanRenderer renderer = Headless();
        try
        {
            MaterialId glass = Matte(renderer, opacity: 0.5f);
            Vector3 zero = HdrCenter(
                renderer,
                PlaneToTheSide(
                    renderer: renderer,
                    plane: renderer.CreateMesh(PlaneWithNormals(Vector3.Zero)),
                    material: glass,
                    flags: RenderFlags.Transparent
                )
            );
            Vector3 facing = HdrCenter(
                renderer,
                PlaneToTheSide(
                    renderer: renderer,
                    plane: renderer.CreateMesh(PlaneWithNormals(Vector3.UnitY)),
                    material: glass,
                    flags: RenderFlags.Transparent
                )
            );

            facing.X.Should().BeGreaterThan(0.02f, "the sun behind the eye lights the pane");
            LargestChannelDifference(zero, facing)
                .Should()
                .BeLessThan(
                    0.01f * facing.X,
                    "a normal of length 0 has no direction, so the pane takes the direction toward the eye"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EmissionThatTheExposureCarriesPastTheFloatRangeStoresTheCap()
    {
        // The brightest radiance that the renderer reads, 2^64, at the largest exposure, +64 stops, is 2^128, past the
        // largest single float. Multiplied first, it is infinity, and a min with infinity is not defined.
        VulkanRenderer renderer = Headless(stops: 64);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            snapshot.Instances.Add(Wall(renderer, new(ShaderInput.RadianceLimit)));

            Vector3 center = HdrCenter(renderer, snapshot);

            center.Should().Be(new Vector3(ShaderInput.MaxRadiance), "pre-exposed light stops at the HDR image's cap");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ReflectedLightThatTheExposureCarriesPastTheFloatRangeStoresTheCap()
    {
        VulkanRenderer renderer = Headless(stops: 64);
        try
        {
            RenderSnapshot snapshot = PlaneAhead(renderer.CreateMesh(PlaneWithNormals(Vector3.UnitY)), Matte(renderer));
            snapshot.Lights.Add(
                new PointRenderLight(
                    Position: new(0, 0, -2),
                    Color: Vector3.One,
                    Intensity: ShaderInput.RadianceLimit,
                    Range: 10
                )
            );

            Vector3 center = HdrCenter(renderer, snapshot);

            center.Should().Be(new Vector3(ShaderInput.MaxRadiance), "reflected light stops at the HDR image's cap");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlassPaneOverLightAtTheCapKeepsTheBlendAtTheCap()
    {
        // The pane's own light takes at most the cap times what it does not let through, so the blend's sum with the
        // light behind it, times what it lets through, stays at the cap.
        VulkanRenderer renderer = Headless();
        try
        {
            MaterialId pane = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 0.5f),
                    Roughness: 0.5f,
                    Emissive: new(ShaderInput.RadianceLimit)
                )
            );
            RenderSnapshot snapshot = PlaneAhead(
                renderer.CreateMesh(PlaneWithNormals(Vector3.UnitY)),
                pane,
                RenderFlags.Transparent
            );
            snapshot.Instances.Add(Wall(renderer, new(ShaderInput.RadianceLimit)));

            Vector3 center = HdrCenter(renderer, snapshot);

            center
                .X.Should()
                .BeInRange(0.99f * ShaderInput.MaxRadiance, ShaderInput.MaxRadiance, "the blend stays at the cap");
            center
                .Y.Should()
                .BeInRange(0.99f * ShaderInput.MaxRadiance, ShaderInput.MaxRadiance, "the blend stays at the cap");
            center
                .Z.Should()
                .BeInRange(0.99f * ShaderInput.MaxRadiance, ShaderInput.MaxRadiance, "the blend stays at the cap");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(Bloom.MaxLevels)]
    public void TheSummedBloomLevelsOfLightAtTheCapStayBelowTheLargestHalfFloat(int levels)
    {
        // Each level holds its share of the light, the cap over the levels, and level 0 ends holding every level summed.
        // Each half float that a level stores rounds, so the sum of the shares of 65,504 can round past it.
        VulkanRenderer renderer = Headless(
            bloom: new Bloom(Threshold: 0, Knee: 0, Strength: 1f / 65536, Levels: levels)
        );
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            snapshot.Instances.Add(Wall(renderer, new(ShaderInput.RadianceLimit)));
            renderer.Render(snapshot, 1f);

            float[] glow = renderer.ReadGlowLevel(0);

            glow.Should()
                .OnlyContain(
                    value => float.IsFinite(value) && value < LargestHalf,
                    "the {0} levels of light at the cap sum below the largest half float",
                    levels
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APuffWhoseLightIsNotFiniteAddsNothing()
    {
        // A glow puff of radiance −∞ would write −∞ into the HDR image, and NaN where its profile of 0 multiplies it.
        VulkanRenderer renderer = Headless();
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            snapshot.Instances.Add(Wall(renderer, new(0.2f)));
            snapshot.PuffLooks.Add(
                new(
                    Life: 100,
                    StartRadius: 1,
                    EndRadius: 1,
                    TemperatureRatio: 1,
                    Shading: PuffShading.Glow(new(float.NegativeInfinity))
                )
            );
            var position = new Vector3(0, 0, -5);
            snapshot.Puffs.Add(
                new()
                {
                    Position = position,
                    PreviousPosition = position,
                    Age = 1,
                    Look = 0,
                }
            );
            renderer.Render(snapshot, 1f);

            float[] hdr = renderer.ReadHdrImage();

            Center(hdr).Should().Be(new Vector3((float)(Half)0.2f), "the puff adds no light to the wall");
            hdr.Should()
                .OnlyContain(value => float.IsFinite(value), "no pixel of the HDR image holds a NaN or an infinity");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(63)]
    [InlineData(4096)]
    public void PuffsAtTheirCapOverASurfaceAtTheCapKeepEveryPixelWithinTheHdrBound(int puffs)
    {
        // Every puff sits at one place, 5 m ahead and 5 m before the wall, so each adds its full share at the center.
        // Each puff alone at the surfaces' cap would carry the sum of the blend past the largest half float.
        VulkanRenderer renderer = Headless();
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            snapshot.Instances.Add(Wall(renderer, new(ShaderInput.RadianceLimit)));
            snapshot.PuffLooks.Add(
                new(
                    Life: 100,
                    StartRadius: 1,
                    EndRadius: 1,
                    TemperatureRatio: 1,
                    Shading: PuffShading.Glow(new(ShaderInput.RadianceLimit))
                )
            );
            var position = new Vector3(0, 0, -5);
            for (int i = 0; i < puffs; i++)
                snapshot.Puffs.Add(
                    new()
                    {
                        Position = position,
                        PreviousPosition = position,
                        Age = 1,
                        Look = 0,
                    }
                );
            renderer.Render(snapshot, 1f);

            float[] hdr = renderer.ReadHdrImage();

            hdr.Should()
                .OnlyContain(
                    value => float.IsFinite(value) && value <= HdrBound,
                    "the wall holds at most {0}, and the {1} puffs over a pixel add at most {0} in all",
                    ShaderInput.MaxRadiance,
                    puffs
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
