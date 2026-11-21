using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The back-face pass on the GPU (docs/plans/back-face-depth-layer.md, decision 4): the back share of each pixel, its
/// front distance over the distance of the nearest counted fragment of the pass, read back after one frame. Every box face
/// that a test reads lies across the view axis, so a pixel's share is the ratio of two view depths, whatever its ray.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BackFacePassTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The tolerance of a share between 0 and 1: a half float holds it to about 1 part in 2,048.</summary>
    private const float ShareTolerance = 1e-3f;

    /// <summary>A card that faces the camera: the plane mesh's front, +Y, turned to +Z.</summary>
    private static readonly Quaternion FacingTheCamera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>A headless renderer with the screen tracer, whose frames run the pass.</summary>
    private static VulkanRenderer Traced(UpscalingSetting upscaling = default) =>
        Gpu.Headless(width: Width, height: Height, screenTrace: ScreenTrace.Default, upscaling: upscaling);

    /// <summary>A gray one-sided material.</summary>
    private static MaterialId Matte(VulkanRenderer renderer, AlphaMode alpha = default, TextureData? map = null) =>
        renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.6f, y: 0.6f, z: 0.6f, w: 1), roughness: 0.6f) with
            {
                Alpha = alpha,
                BaseColorMap = map is null ? default : renderer.CreateTexture(map),
            }
        );

    /// <summary>A brown hair material, which the passes draw on both faces.</summary>
    private static MaterialId Hair(VulkanRenderer renderer, AlphaMode alpha = default, TextureData? map = null) =>
        renderer.CreateMaterial(
            new MaterialDesc(
                BaseColor: new(x: 0.3f, y: 0.2f, z: 0.1f, w: 1),
                Roughness: 0.4f,
                Reflectance: 0.04f,
                Model: new HairShading(StrandDirection.PlusV),
                Alpha: alpha
            ) with
            {
                BaseColorMap = map is null ? default : renderer.CreateTexture(map),
            }
        );

    /// <summary>A snapshot of the sun from above and no other light. The pass reads no light.</summary>
    private static RenderSnapshot Lit() => Gpu.Snapshot(Vector3.UnitY, starIrradiance: 1);

    /// <summary>An instance of <paramref name="mesh"/> at <paramref name="position"/> in the camera's frame.</summary>
    private static RenderInstance Placed(
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        float radius,
        Quaternion? rotation = null
    ) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: position,
            scale: Vector3.One,
            rotation: rotation,
            radius: radius,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>The pixel that holds a camera-relative point.</summary>
    private static (int X, int Y) PixelOf(Vector3 point)
    {
        Vector2 pixel = Gpu.Project(point: point, width: Width, height: Height);
        return ((int)pixel.X, (int)pixel.Y);
    }

    /// <summary>The value of a one-channel image at the pixel that holds <paramref name="point"/>.</summary>
    private static float At(float[] image, Vector3 point)
    {
        (int x, int y) = PixelOf(point);
        return image[y * Width + x];
    }

    /// <summary>
    /// A square of quads that face each other: the plane mesh once in each winding. The pairing rule calls it closed, and
    /// its back face lies on its front face.
    /// </summary>
    private static MeshData CoincidentFaces(float halfSize)
    {
        MeshData plane = Primitives.Plane(halfSize);
        return new(plane.Vertices, [0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2]);
    }

    /// <summary>
    /// The image of a cutout box: 12 × 1 white texels whose alphas are 0, 102 and 255 in stripes of 4, so that the middle
    /// half of each stripe filters to its alpha alone: 0, 0.4 and 1. With <paramref name="opaque"/>, every alpha is 1.
    /// </summary>
    private static TextureData Stripes(bool opaque = false)
    {
        byte[] rgba = new byte[12 * 4];
        for (int texel = 0; texel < 12; texel++)
        {
            rgba[texel * 4] = rgba[texel * 4 + 1] = rgba[texel * 4 + 2] = 255;
            byte stripe = (texel / 4) switch
            {
                0 => 0,
                1 => 102,
                _ => 255,
            };
            rgba[texel * 4 + 3] = opaque ? (byte)255 : stripe;
        }

        return new(Width: 12, Height: 1, Rgba: rgba);
    }

    /// <summary>
    /// The image of a hair card of fine strands: 128 × 128 white texels, each 8 columns a strand of 2 opaque columns and
    /// 6 clear ones. Seen 3 m away at 160 × 90 with a cutoff of 0.6, it gives 510 strand pixels under FSR's mip bias of
    /// −1, and 416 without it.
    /// </summary>
    private static TextureData FineStrands()
    {
        const int side = 128;
        byte[] rgba = new byte[side * side * 4];
        for (int texel = 0; texel < side * side; texel++)
        {
            rgba[texel * 4] = rgba[texel * 4 + 1] = rgba[texel * 4 + 2] = 255;
            rgba[texel * 4 + 3] = texel % side % 8 < 2 ? (byte)255 : (byte)0;
        }

        return new(Width: side, Height: side, Rgba: rgba);
    }

    [Fact]
    public void APixelOfAClosedBoxHoldsItsFrontDistanceOverTheDistanceOfTheBoxsBackFace()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(Vector3.One));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -5), radius: 1.8f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            foreach (Vector3 point in (Vector3[])[new(0, 0, -4), new(0.5f, 0.5f, -4), new(-0.6f, -0.3f, -4)])
                At(share, point)
                    .Should()
                    .BeApproximately(
                        4f / 6,
                        ShareTolerance,
                        "the box's front face at {0} lies 4 m deep and its back face 6 m deep",
                        point
                    );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelOfATwoSidedCardOrOfAnOpenQuadHoldsExactlyOne()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.8f));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(
                Placed(
                    mesh: plane,
                    material: Hair(renderer),
                    position: new(-1.2f, 0, -4),
                    radius: 1.2f,
                    rotation: FacingTheCamera
                )
            );
            snapshot.Instances.Add(
                Placed(
                    mesh: plane,
                    material: Matte(renderer),
                    position: new(1.2f, 0, -4),
                    radius: 1.2f,
                    rotation: FacingTheCamera
                )
            );
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(-1.2f, 0.3f, -4)).Should().Be(1f, "the two-sided card's own fragment ends its slab");
            At(share, new(1.2f, -0.3f, -4)).Should().Be(1f, "the open quad's own fragment ends its slab");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ATwoSidedCardInFrontOfABoxHoldsOneNotTheBoxsBackFace()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(2, 2, 1)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -6), radius: 3.1f));
            snapshot.Instances.Add(
                Placed(
                    mesh: plane,
                    material: Hair(renderer),
                    position: new(0, 0, -3),
                    radius: 1.2f,
                    rotation: FacingTheCamera
                )
            );
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0.2f, 0.1f, -3)).Should().Be(1f, "the card is the pixel's front surface, and it ends its slab");
            At(share, new(1.2f, 0, -5))
                .Should()
                .BeApproximately(5f / 7, ShareTolerance, "beside the card, the box's front lies 5 m deep and its back 7 m");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AThinClosedPlateHoldsTheShareOfItsOwnBackFace()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId plate = renderer.CreateMesh(Primitives.Box(new(1, 1, 0.02f)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: plate, material: Matte(renderer), position: new(0, 0, -5), radius: 1.5f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0.2f, 0.3f, -4.98f))
                .Should()
                .BeApproximately(
                    4.98f / 5.02f,
                    ShareTolerance,
                    "the tolerance of the front's own distance is a float tolerance, not a thickness, so a plate 4 cm thick keeps its back face"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AClosedPlateThinnerThanTheOwnSurfaceToleranceHoldsNoBackFace()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId plate = renderer.CreateMesh(Primitives.Box(new(1, 1, 5e-5f)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: plate, material: Matte(renderer), position: new(0, 0, -4), radius: 1.5f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0.2f, 0.3f, -4))
                .Should()
                .Be(
                    0f,
                    "the plate's back face lies 0.1 mm behind its front, within 10⁻⁴ of the front's 4 m, so it counts as the front's own surface"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnOpenQuadSeenFromBehindDoesNotCountForTheBoxBehindIt()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(2, 2, 1)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -6), radius: 3.1f));
            snapshot.Instances.Add(
                Placed(
                    mesh: plane,
                    material: Matte(renderer),
                    position: new(0, 0, -3),
                    radius: 0.8f,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2)
                )
            );
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0.1f, 0.1f, -3))
                .Should()
                .BeApproximately(
                    5f / 7,
                    ShareTolerance,
                    "the G-buffer culls the quad's back face, and the pass draws it in front of the box's front, where no fragment counts"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSkyHoldsZero()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(Vector3.One));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -5), radius: 1.8f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            share[5 * Width + 5].Should().Be(0f, "the corner pixel shows the sky, which has no front surface");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASurfaceWithNothingBehindItAndNoBackFaceOfItsOwnHoldsZero()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId faces = renderer.CreateMesh(CoincidentFaces(1));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(
                Placed(
                    mesh: faces,
                    material: Matte(renderer),
                    position: new(0, 0, -4),
                    radius: 1.5f,
                    rotation: FacingTheCamera
                )
            );
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            renderer.ReadDistance()[45 * Width + 80].Should().BeGreaterThan(0, "the pixel shows the closed square");
            At(share, new(0.3f, 0.2f, -4))
                .Should()
                .Be(0f, "the square's back face lies on its front face, so it does not count, and nothing lies behind");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameInWhichThePassDrawsNothingHoldsZeroEverywhere()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(Vector3.One));
            RenderSnapshot withBox = Lit();
            withBox.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -5), radius: 1.8f));
            renderer.Render(withBox, 1f);
            renderer.Render(Lit(), 1f);
            float[] share = renderer.ReadBackShare();

            share.Should().OnlyContain(value => value == 0f, "the frame's share clears once, also with no draw of the pass");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFacesOfAClosedMeshAroundTheEyeDoNotCountForTheSurfacesBeyondThem()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId room = renderer.CreateMesh(Primitives.Box(new(3)));
            MeshId box = renderer.CreateMesh(Primitives.Box(Vector3.One));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: room, material: Matte(renderer), position: Vector3.Zero, radius: 5.2f));
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -8), radius: 1.8f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0.3f, 0.2f, -7))
                .Should()
                .BeApproximately(
                    7f / 9,
                    ShareTolerance,
                    "the box beyond the closed mesh around the eye has its front 7 m deep and its back 9 m"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelOfAClosedCutoutMeshCountsABackFaceTexelAtTheCutoffOrAbove()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(2, 1, 0.5f)));
            MaterialId cutout = Matte(renderer, alpha: new CutoutAlpha(0.5f), map: Stripes(opaque: true));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: cutout, position: new(0, 0, -5), radius: 2.4f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(1.2f, 0, -4.5f))
                .Should()
                .BeApproximately(4.5f / 5.5f, ShareTolerance, "every texel of the box is opaque");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelOfAClosedCutoutMeshCountsNoBackFaceTexelBelowTheCutoff()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(2, 1, 0.5f)));
            MeshId plane = renderer.CreateMesh(Primitives.Plane(8));
            MaterialId cutout = Matte(renderer, alpha: new CutoutAlpha(0.5f), map: Stripes());
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: cutout, position: new(0, 0, -5), radius: 2.4f));
            snapshot.Instances.Add(
                Placed(
                    mesh: plane,
                    material: Matte(renderer),
                    position: new(0, 0, -10),
                    radius: 11.4f,
                    rotation: FacingTheCamera
                )
            );
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            // The front face's u runs along +X and the back face's along −X, so the front's opaque stripe at x = 1.2 m
            // lies in front of the back face's clear stripe, at x = 1.47 m.
            At(share, new(1.2f, 0, -4.5f))
                .Should()
                .BeApproximately(
                    4.5f / 10,
                    ShareTolerance,
                    "the back face's texel there is clear, so the nearest counted fragment is the open quad 10 m deep"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelOfACutoutCardHoldsOneAndAPixelOfAHoleHoldsTheShareOfTheSurfaceBehindAlsoAtAStrandsEdge()
    {
        // FSR's mip bias of −1 makes the G-buffer sample a finer mip, which keeps about 90 strand pixels more than a
        // sample without the bias: a pass without the main view's bias would drop them.
        VulkanRenderer renderer = Traced(upscaling: new Fsr3Upscaling(new FixedRenderScale(1), Sharpness: 0));
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(4, 3, 1)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -7), radius: 5.2f));
            snapshot.Instances.Add(
                Placed(
                    mesh: plane,
                    material: Hair(renderer, alpha: new CutoutAlpha(0.6f), map: FineStrands()),
                    position: new(0, 0, -3),
                    radius: 1.5f,
                    rotation: FacingTheCamera
                )
            );
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();
            float[] front = renderer.ReadDistance();

            (int left, int top) = PixelOf(new(-0.9f, 0.9f, -3));
            (int right, int bottom) = PixelOf(new(0.9f, -0.9f, -3));
            int strands = 0,
                holes = 0;
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int i = y * Width + x;
                if (front[i] < 4.5f)
                {
                    strands++;
                    share[i].Should().Be(1f, "pixel ({0}, {1}) shows the card, whose own fragment ends its slab", x, y);
                }
                else
                {
                    holes++;
                    share[i]
                        .Should()
                        .BeApproximately(
                            6f / 8,
                            ShareTolerance,
                            "pixel ({0}, {1}) shows the box through a hole, with its front 6 m deep and its back 8 m",
                            x,
                            y
                        );
                }
            }

            strands.Should().BeGreaterThan(50, "the G-buffer keeps the strands of mip 1");
            holes.Should().BeGreaterThan(50, "the strands cover a quarter of the card");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EachPixelOfANearObjectInFrontOfAFarObjectHoldsTheBackFaceOfItsOwnFrontSurface()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId near = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MeshId far = renderer.CreateMesh(Primitives.Box(new(3e6f)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: far, material: Matte(renderer), position: new(0, 0, -1e7f), radius: 5.2e6f));
            snapshot.Instances.Add(Placed(mesh: near, material: Matte(renderer), position: new(0, 0, -5), radius: 0.9f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0, 0, -4.5f))
                .Should()
                .BeApproximately(4.5f / 5.5f, ShareTolerance, "the near box's front lies 4.5 m deep and its back 5.5 m");
            At(share, new(1.2e6f, 0, -7e6f))
                .Should()
                .BeApproximately(7f / 13, ShareTolerance, "beside the near box, the far box's front lies 7,000 km deep and its back 13,000 km");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelOfAnObjectInTheMiddleRangeHoldsTheShareOfItsOwnBackFace()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(2e4f)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: box, material: Matte(renderer), position: new(0, 0, -1e5f), radius: 3.5e4f));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(1e4f, 0, -8e4f))
                .Should()
                .BeApproximately(
                    8f / 12,
                    ShareTolerance,
                    "the box lies between 2 km and 2,000 km, with its front 80 km deep and its back 120 km, and its range draws with its own projection and depth"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANearerRangeCountsNoFragmentBeyondItsOwnFarBoundary()
    {
        VulkanRenderer renderer = Traced();
        try
        {
            // The large box spans the near and the middle range, so both draw it. The small box inside it lies in the
            // middle range alone, and its back face is the nearest counted fragment behind the large box's front.
            MeshId large = renderer.CreateMesh(Primitives.Box(new(500, 500, 1000)));
            MeshId small = renderer.CreateMesh(Primitives.Box(new(100, 100, 50)));
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(Placed(mesh: large, material: Matte(renderer), position: new(0, 0, -2000), radius: 1225));
            snapshot.Instances.Add(Placed(mesh: small, material: Matte(renderer), position: new(0, 0, -2550), radius: 150));
            renderer.Render(snapshot, 1f);
            float[] share = renderer.ReadBackShare();

            At(share, new(0, 0, -1000))
                .Should()
                .BeApproximately(
                    1000f / 2600,
                    ShareTolerance,
                    "the near range cuts the large box's back face 3 km deep, so the small box's back face 2.6 km deep stays"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UnderJitterEveryPixelOfATiltedTwoSidedCardHoldsExactlyOneInEachFrame()
    {
        // The card's distance changes by about 2 % per pixel, so a pass that drew without the frame's own jitter would
        // put its fragment far outside the tolerance of the front's own distance.
        // Without warm-up draws, each Render draws one frame, and the time step keeps the second frame from restarting
        // the jitter as a still does.
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(1), Sharpness: 0),
            warmUpFrames: 0
        );
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            Quaternion tilted = Quaternion.Concatenate(
                FacingTheCamera,
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(1, 1, 0)), 1.1f)
            );
            RenderSnapshot snapshot = Lit();
            snapshot.Instances.Add(
                Placed(mesh: plane, material: Hair(renderer), position: new(0, 0, -3), radius: 1.5f, rotation: tilted)
            );
            var fronts = new List<float[]>();
            for (int frame = 0; frame < 2; frame++)
            {
                snapshot.Time = frame / 60.0;
                renderer.Render(snapshot, 1f);
                float[] share = renderer.ReadBackShare();
                float[] front = renderer.ReadDistance();
                for (int i = 0; i < front.Length; i++)
                    share[i]
                        .Should()
                        .Be(
                            front[i] > 0 ? 1f : 0f,
                            "in frame {0}, pixel {1} shows {2}",
                            frame,
                            i,
                            front[i] > 0 ? "the card, whose own fragment ends its slab" : "the sky"
                        );
                fronts.Add(front);
            }

            fronts[0]
                .Zip(fronts[1], (first, second) => first > 0 && second > 0 && MathF.Abs(first - second) > 1e-3f * first)
                .Should()
                .Contain(true, "the two frames draw with different jitter");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
