using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The fringe of a fringed material in the transparent pass (docs/plans/skin-and-hair.md, "The fringe"): a texel whose
/// alpha a is above 0 and below the cutoff covers a share a of its pixel, "over" what lies behind, and a clear texel draws
/// nothing. The cards are the plane mesh, 2 m square, facing the camera 3 m ahead, with a cutoff of 0.5, in front of a
/// square 6 m ahead that emits light. The exposure is fixed at 0 stops. Each test reads the HDR image.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class FringeRenderTests
{
    private const int Width = 160,
        Height = 90;

    private const float Cutoff = 0.5f;

    /// <summary>The alpha of a fringe texel in these tests: 102 / 255 in an image, and the same as a base color.</summary>
    private const float FringeAlpha = 0.4f;

    /// <summary>The relative rounding of a half float, with room for the sum of two.</summary>
    private const float HalfRounding = 2f / 1024;

    private static readonly Quaternion FacingTheCamera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>The pixel straight ahead.</summary>
    private static readonly Vector2 Ahead = Gpu.Project(point: new(0, 0, -3), width: Width, height: Height);

    /// <summary>A snapshot with no ambient light, and the sun along <paramref name="starDirection"/>.</summary>
    private static RenderSnapshot Unlit(Vector3 starDirection, float starIrradiance)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(starDirection, starIrradiance: starIrradiance);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>A plane mesh of half size 1, facing the camera at <paramref name="center"/>, scaled by <paramref name="scale"/>.</summary>
    private static RenderInstance Facing(MeshId plane, MaterialId material, Vector3 center, float scale = 1) =>
        Gpu.Instance(
            mesh: plane,
            material: material,
            position: center,
            scale: new(scale),
            rotation: FacingTheCamera,
            radius: 1.5f * scale,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A black material of reflectance 0 and alpha <paramref name="alpha"/> that emits <paramref name="radiance"/>.</summary>
    private static MaterialId GlowingCard(VulkanRenderer renderer, Vector3 radiance, float alpha, AlphaMode mode) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: 0, y: 0, z: 0, w: alpha),
                Roughness: 0.5f,
                Reflectance: 0,
                Emissive: radiance,
                Alpha: mode
            )
        );

    /// <summary>Renders <paramref name="snapshot"/> with <paramref name="instances"/> added and gives the HDR pixel straight ahead.</summary>
    private static Vector3 AheadIn(VulkanRenderer renderer, RenderSnapshot snapshot, params RenderInstance[] instances)
    {
        snapshot.Instances.AddRange(instances);
        renderer.Render(snapshot, 1f);
        float[] hdr = renderer.ReadHdr();
        int i = ((int)Ahead.Y * Width + (int)Ahead.X) * 4;
        return new(hdr[i], hdr[i + 1], hdr[i + 2]);
    }

    private static RenderSnapshot Dark() => Unlit(Vector3.UnitY, starIrradiance: 0);

    /// <summary>Asserts that each channel of <paramref name="actual"/> is <paramref name="expected"/>'s within the rounding of half floats.</summary>
    private static void ShouldBeNear(Vector3 actual, Vector3 expected, string because)
    {
        float tolerance = HalfRounding * MathF.Max(MathF.Max(expected.X, expected.Y), MathF.Max(expected.Z, 1e-3f));
        LargestChannelDifference(actual, expected)
            .Should()
            .BeLessThanOrEqualTo(tolerance, "{0}: shown {1}, expected {2}", because, actual, expected);
    }

    /// <summary>
    /// Eight white texels in a row whose left half is clear and right half covered, so that the middle of each half
    /// filters to its own alpha alone.
    /// </summary>
    private static TextureId ClearAndCoveredHalves(VulkanRenderer renderer) =>
        renderer.CreateTexture(
            new(
                Width: 8,
                Height: 1,
                Rgba:
                [
                    .. Enumerable
                        .Range(0, 8)
                        .SelectMany(t => new byte[] { 255, 255, 255, t < 4 ? (byte)0 : (byte)255 }),
                ]
            )
        );

    /// <summary>A dark, smooth, fully reflective card whose base color, alpha included, comes from <paramref name="map"/>.</summary>
    private static MaterialId ShinyCard(VulkanRenderer renderer, TextureId map, AlphaMode alpha) =>
        renderer.CreateMaterial(
            new MaterialDesc(BaseColor: new(x: 0.1f, y: 0.1f, z: 0.1f, w: 1), Roughness: 0.3f, Reflectance: 1) with
            {
                BaseColorMap = map,
                Alpha = alpha,
            }
        );

    [Fact]
    public void AClearTexelOfAFringedCardLeavesThePixelBehindItAsItWas()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            MaterialId backdrop = Glow(renderer, new(0, 0.5f, 0));
            TextureId halves = ClearAndCoveredHalves(renderer);
            MaterialId Card(AlphaMode alpha) => ShinyCard(renderer: renderer, map: halves, alpha: alpha);
            // The sun stands behind the eye, so its highlight lies straight ahead, in the middle of the clear half.
            RenderSnapshot Lit() => Unlit(Vector3.UnitZ, starIrradiance: 0.3f);
            var clearHalfAhead = new Vector3(0.5f, 0, -3);

            Vector3 behind = AheadIn(
                renderer,
                Lit(),
                Facing(plane: plane, material: backdrop, center: new(0, 0, -6), scale: 3)
            );
            Vector3 fringed = AheadIn(
                renderer,
                Lit(),
                Facing(plane: plane, material: backdrop, center: new(0, 0, -6), scale: 3),
                Facing(plane: plane, material: Card(new FringedAlpha(Cutoff)), center: clearHalfAhead)
            );
            Vector3 glass = AheadIn(
                renderer,
                Lit(),
                Facing(plane: plane, material: backdrop, center: new(0, 0, -6), scale: 3),
                Facing(plane: plane, material: Card(new GlassAlpha()), center: clearHalfAhead)
            );

            fringed.Should().Be(behind, "a clear texel of a fringed card draws nothing, not even a highlight");
            (glass.X - behind.X)
                .Should()
                .BeGreaterThan(0.05f, "a clear pane of glass shows the sun's highlight there, so the test can fail");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.6f)]
    [InlineData(Cutoff)]
    public void ACoreTexelOfAFringedCardDrawsNothingInTheTransparentPass(float alpha)
    {
        // A lit, matte card: the G-buffer's 8-bit base color and 16-bit normals make its forward light differ from its
        // deferred light, so a second draw of the core over its own G-buffer pixel would show.
        static float[] Render(AlphaMode mode, float alpha)
        {
            VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                MaterialId card = renderer.CreateMaterial(
                    new(
                        BaseColor: new(x: 0.6f, y: 0.35f, z: 0.2f, w: alpha),
                        Roughness: 0.5f,
                        Reflectance: 0.5f,
                        Alpha: mode
                    )
                );
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Normalize(new(0.3f, 0.6f, 0.75f)), starIrradiance: 2);
                snapshot.Instances.Add(
                    Facing(plane: plane, material: Glow(renderer, new(0, 0.5f, 1)), center: new(0, 0, -6), scale: 3)
                );
                snapshot.Instances.Add(Facing(plane: plane, material: card, center: new(0, 0, -3)));
                renderer.Render(snapshot, 1f);
                return renderer.ReadHdr();
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Render(new FringedAlpha(Cutoff), alpha)
            .Should()
            .Equal(
                Render(new CutoutAlpha(Cutoff), alpha),
                "a texel at the cutoff or above is core, which the G-buffer pass draws alone"
            );
    }

    [Fact]
    public void AFringeTexelCoversThePixelByItsAlpha()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderInstance backdrop = Facing(
                plane: plane,
                material: Glow(renderer, new(0, 0.5f, 1)),
                center: new(0, 0, -6),
                scale: 3
            );
            var light = new Vector3(2, 0.2f, 0);

            Vector3 behind = AheadIn(renderer, Dark(), backdrop);
            Vector3 opaque = AheadIn(
                renderer,
                Dark(),
                backdrop,
                Facing(
                    plane: plane,
                    material: GlowingCard(
                        renderer: renderer,
                        radiance: light,
                        alpha: FringeAlpha,
                        mode: new OpaqueAlpha()
                    ),
                    center: new(0, 0, -3)
                )
            );
            Vector3 fringe = AheadIn(
                renderer,
                Dark(),
                backdrop,
                Facing(
                    plane: plane,
                    material: GlowingCard(
                        renderer: renderer,
                        radiance: light,
                        alpha: FringeAlpha,
                        mode: new FringedAlpha(Cutoff)
                    ),
                    center: new(0, 0, -3)
                )
            );

            ShouldBeNear(
                fringe,
                FringeAlpha * opaque + (1 - FringeAlpha) * behind,
                "the fringe is the card over the backdrop"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFringeBehindAnOpaqueSurfaceDrawsNothing()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderInstance front = Facing(
                plane: plane,
                material: Glow(renderer, new(0, 0.5f, 1)),
                center: new(0, 0, -2)
            );
            RenderInstance fringe = Facing(
                plane: plane,
                material: GlowingCard(
                    renderer: renderer,
                    radiance: new(2, 0.2f, 0),
                    alpha: FringeAlpha,
                    mode: new FringedAlpha(Cutoff)
                ),
                center: new(0, 0, -3)
            );

            Vector3 alone = AheadIn(renderer, Dark(), front);
            Vector3 withFringe = AheadIn(renderer, Dark(), front, fringe);

            withFringe.Should().Be(alone, "the opaque surface in front hides the fringe");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFringeOfANearCardBlendsOverTheCoreOfAFarCard()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            var nearLight = new Vector3(2, 0.2f, 0);
            RenderInstance farCore = Facing(
                plane: plane,
                material: GlowingCard(
                    renderer: renderer,
                    radiance: new(0, 0.3f, 1),
                    alpha: 1,
                    mode: new FringedAlpha(Cutoff)
                ),
                center: new(0, 0, -4)
            );

            Vector3 far = AheadIn(renderer, Dark(), farCore);
            Vector3 nearOpaque = AheadIn(
                renderer,
                Dark(),
                Facing(
                    plane: plane,
                    material: GlowingCard(
                        renderer: renderer,
                        radiance: nearLight,
                        alpha: FringeAlpha,
                        mode: new OpaqueAlpha()
                    ),
                    center: new(0, 0, -3)
                )
            );
            Vector3 both = AheadIn(
                renderer,
                Dark(),
                farCore,
                Facing(
                    plane: plane,
                    material: GlowingCard(
                        renderer: renderer,
                        radiance: nearLight,
                        alpha: FringeAlpha,
                        mode: new FringedAlpha(Cutoff)
                    ),
                    center: new(0, 0, -3)
                )
            );

            ShouldBeNear(
                both,
                FringeAlpha * nearOpaque + (1 - FringeAlpha) * far,
                "the near fringe lies over the far core"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFringeAtTheCapKeepsTheSurfacesShareOfTheHdrImage()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderInstance fringe = Facing(
                plane: plane,
                material: GlowingCard(
                    renderer: renderer,
                    radiance: new(1e6f),
                    alpha: FringeAlpha,
                    mode: new FringedAlpha(Cutoff)
                ),
                center: new(0, 0, -3)
            );
            const float cap = ShaderInput.MaxRadiance;

            Vector3 overBlack = AheadIn(
                renderer,
                Dark(),
                Facing(plane: plane, material: Glow(renderer, Vector3.Zero), center: new(0, 0, -6), scale: 3),
                fringe
            );
            Vector3 overCapped = AheadIn(
                renderer,
                Dark(),
                Facing(plane: plane, material: Glow(renderer, new(1e6f)), center: new(0, 0, -6), scale: 3),
                fringe
            );

            ShouldBeNear(overBlack, new(FringeAlpha * cap), "the fringe's light is its share of the cap");
            float largest = MathF.Max(overCapped.X, MathF.Max(overCapped.Y, overCapped.Z));
            float.IsFinite(largest).Should().BeTrue();
            largest
                .Should()
                .BeLessThanOrEqualTo(
                    cap * (1 + HalfRounding),
                    "over a surface at the cap, the pixel stays within the surfaces' share"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
