using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The skin scatter on the GPU (docs/plans/skin-and-hair.md, "Skin"): the lighting passes write D into the scatter image,
/// and the two scatter passes add S − D to the HDR image at skin pixels only, by the kernel of <see cref="SkinScatter"/>.
/// The exposure is fixed at 0 stops, so the images hold radiance. A frame "without the scatter" skips its two passes in
/// the same renderer, so that every other value of the frame keeps its bits.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class SkinScatterRenderTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The relative rounding of a half float, with room for a sum of two.</summary>
    private const float HalfRounding = 2f / 1024;

    private static readonly string[] ScatterPasses = ["scatter rows", "scatter columns"];

    /// <summary>Decoded sRGB bytes, so that the base color target stores them exactly.</summary>
    private static readonly Double3 SkinColor = new(Decoded(224), Decoded(172), Decoded(150));

    /// <summary>The head: a skin sphere of radius 0.1 m, its front 0.25 m ahead.</summary>
    private static readonly Vector3 HeadCenter = new(0, 0, -0.35f);

    /// <summary>A material of <paramref name="color"/> and <paramref name="model"/>, of roughness 0.5.</summary>
    private static MaterialId Material(VulkanRenderer renderer, ShadingModel model, Double3 color) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: (float)color.X, y: (float)color.Y, z: (float)color.Z, w: 1),
                Roughness: 0.5f,
                Model: model
            )
        );

    private static MaterialId Skin(VulkanRenderer renderer, Double3? color = null) =>
        Material(renderer, new SkinShading(), color ?? SkinColor);

    /// <summary>The rotation that turns the plane mesh's front, +Y, toward the camera, +Z.</summary>
    private static Quaternion FacingTheCamera => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>An instance of <paramref name="mesh"/> at <paramref name="position"/>, unturned: a plane faces up.</summary>
    private static RenderInstance Instance(MeshId mesh, MaterialId material, Vector3 position) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: position,
            scale: Vector3.One,
            radius: 3,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>An instance of the plane mesh <paramref name="plane"/> at <paramref name="position"/>, facing the camera.</summary>
    private static RenderInstance Facing(MeshId plane, MaterialId material, Vector3 position) =>
        Gpu.Instance(
            mesh: plane,
            material: material,
            position: position,
            scale: Vector3.One,
            rotation: FacingTheCamera,
            radius: 3,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A snapshot lit by the sun along <paramref name="toSun"/> and an even ambient light of <paramref name="ambient"/>.</summary>
    private static RenderSnapshot Lit(Vector3 toSun, float sunIrradiance, float ambient)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Normalize(toSun), starIrradiance: sunIrradiance);
        snapshot.Ambient = new()
        {
            SkyColor = Vector3.One,
            GroundColor = Vector3.One,
            Intensity = ambient,
        };
        return snapshot;
    }

    /// <summary>
    /// The head, with a small skin sphere in front as its nose, lit from its right, +X, by the sun, and dimly by the
    /// ambient light. The sun's rays are parallel, so the terminator is the plane x = 0, the middle column of the image.
    /// </summary>
    private static RenderSnapshot Head(VulkanRenderer renderer)
    {
        MeshId head = renderer.CreateMesh(Primitives.Sphere(0.1f));
        MeshId nose = renderer.CreateMesh(Primitives.Sphere(0.015f));
        MaterialId skin = Skin(renderer);
        RenderSnapshot snapshot = Lit(toSun: Vector3.UnitX, sunIrradiance: 4, ambient: 0.05f);
        snapshot.Instances.Add(Instance(head, skin, HeadCenter));
        snapshot.Instances.Add(Instance(nose, skin, HeadCenter + new Vector3(0, -0.01f, 0.11f)));
        return snapshot;
    }

    /// <summary>The HDR image of <paramref name="snapshot"/> without the scatter's passes, then with them.</summary>
    private static (float[] Without, float[] With) WithoutAndWith(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        renderer.Graph.Skipped.UnionWith(ScatterPasses);
        renderer.Render(snapshot, 1f);
        float[] without = renderer.ReadHdr();
        renderer.Graph.Skipped.ExceptWith(ScatterPasses);
        renderer.Render(snapshot, 1f);
        return (without, renderer.ReadHdr());
    }

    private static Vector3 At(float[] image, int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new(image[i], image[i + 1], image[i + 2]);
    }

    private static Double3 Rgb(float[] image, int pixel) =>
        new(image[pixel * 4], image[pixel * 4 + 1], image[pixel * 4 + 2]);

    private static VulkanRenderer Scattering(ReflectionSetting reflections = default) =>
        Gpu.Headless(width: Width, height: Height, subsurface: SeparableSubsurface.Default, reflections: reflections);

    private static Double3 Of(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Asserts that each channel of <paramref name="actual"/> is <paramref name="expected"/>'s within <paramref name="tolerance"/>.</summary>
    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because) =>
        LargestChannelDifference(actual.ToVector3(), expected.ToVector3())
            .Should()
            .BeLessThanOrEqualTo((float)tolerance, "{0}: shown {1}, expected {2}", because, actual, expected);

    /// <summary>The shading model's code of each pixel, from the shading target's alpha.</summary>
    private static uint[] Models(VulkanRenderer renderer)
    {
        byte[] shading = renderer.ReadShading();
        return [.. Enumerable.Range(0, Width * Height).Select(pixel => (uint)shading[pixel * 4 + 3])];
    }

    /// <summary>
    /// Asserts that the middle row's pixels from <paramref name="fromX"/> to <paramref name="toX"/> show the same light
    /// with and without the scatter, within the half float's rounding.
    /// </summary>
    private static void RowKeepsItsLight(float[] without, float[] with, int fromX, int toX, string because)
    {
        for (int x = fromX; x <= toX; x++)
        {
            Double3 kept = Of(At(without, x, Height / 2));
            Double3 shown = Of(At(with, x, Height / 2));
            ShouldBeNear(
                actual: shown,
                expected: kept,
                tolerance: HalfRounding * Math.Max(kept.X, Math.Max(kept.Y, kept.Z)) + 1e-6,
                because: $"{because}, x = {x}"
            );
        }
    }

    [Fact]
    public void TheScatterImageHoldsTheDiffuseLightOfSkinPixelsAndNothingElse()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            var toSun = Vector3.Normalize(new(0.3f, 0.5f, 1));
            RenderSnapshot snapshot = Lit(toSun: toSun, sunIrradiance: 3, ambient: 0.4f);
            snapshot.Instances.Add(Facing(plane, Skin(renderer), new(-0.5f, 0, -1)));
            snapshot.Instances.Add(
                Facing(plane, Material(renderer, new StandardShading(0), SkinColor), new(0.5f, 0, -1))
            );
            renderer.Render(snapshot, 1f);
            float[] scatter = renderer.ReadScatter();

            // D = the Lambert term of the sun plus the diffuse term of the even ambient light, with the diffuse color in
            // both: the normal faces the camera, and the occlusion is 1 under no screen-space light.
            Double3 sun = Of(Gpu.StarIrradianceRgb(snapshot)) * Vector3.Dot(toSun, Vector3.UnitZ);
            Double3 expected = SkinColor * sun / Math.PI + SkinColor * 0.4;
            int skin = Height / 2 * Width + 80;
            ShouldBeNear(
                actual: Rgb(scatter, skin),
                expected: expected,
                tolerance: HalfRounding * expected.X,
                because: "D of a skin pixel"
            );
            scatter[skin * 4 + 3].Should().Be(1, "the alpha of a skin pixel is the mask");
            int standard = Height / 2 * Width + 240;
            int sky = 2 * Width + 160;
            scatter[(standard * 4)..(standard * 4 + 4)].Should().AllBeEquivalentTo(0f, "a standard pixel does not scatter");
            scatter[(sky * 4)..(sky * 4 + 4)].Should().AllBeEquivalentTo(0f, "the sky does not scatter");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ThePassesMatchTheirCopyInCSharp()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            (float[] without, float[] with) = WithoutAndWith(renderer, Head(renderer));
            ScatterField field = FieldOf(renderer);
            ScatterKernel kernel = KernelAsTheGpuHoldsIt();
            Double3[] rows = RowsOf(field, kernel);

            int skinPixels = 0;
            for (int pixel = 0; pixel < rows.Length; pixel++)
            {
                if (!field.Skin[pixel])
                    continue;
                skinPixels++;
                Double3 scattered = SkinScatter.Pass(
                    field: field,
                    source: rows,
                    x: pixel % Width,
                    y: pixel / Width,
                    axis: ScatterAxis.Columns,
                    kernel: kernel
                );
                Double3 before = Rgb(without, pixel);
                Double3 light = field.Light[pixel];
                double largest = Math.Max(before.X, light.X);
                ShouldBeNear(
                    actual: Rgb(with, pixel),
                    expected: before + scattered - light,
                    tolerance: HalfRounding * largest + 1e-5,
                    because: $"pixel {pixel % Width}, {pixel / Width}"
                );
            }

            skinPixels.Should().BeGreaterThan(4000, "the head fills a disk of about 45 px radius");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The field as the passes read it: D and the mask of the scatter image, and z of the distance target.</summary>
    private static ScatterField FieldOf(VulkanRenderer renderer)
    {
        float[] scatter = renderer.ReadScatter();
        float[] distances = renderer.ReadDistance();
        var light = new Double3[Width * Height];
        var skin = new bool[Width * Height];
        var depths = new double[Width * Height];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int pixel = y * Width + x;
            light[pixel] = Rgb(scatter, pixel);
            skin[pixel] = scatter[pixel * 4 + 3] > 0;
            depths[pixel] = distances[pixel] * -Ray(new(x, y), Width, Height).Z;
        }

        return new ScatterField(
            Width: Width,
            Height: Height,
            Light: light,
            Skin: skin,
            ViewDepth: depths,
            TanHalfY: Math.Tan(Gpu.Fov / 2)
        );
    }

    /// <summary>The default kernel as the GPU holds it: singles.</summary>
    private static ScatterKernel KernelAsTheGpuHoldsIt() =>
        new(
            [
                .. SkinProfile
                    .Human.Kernel(SeparableSubsurface.Default.TapsPerSide)
                    .Taps.Select(tap => new ScatterTap(
                        Offset: (float)tap.Offset,
                        Weight: new((float)tap.Weight.X, (float)tap.Weight.Y, (float)tap.Weight.Z)
                    )),
            ]
        );

    /// <summary>The row image of the C# copy at each skin pixel, rounded to half floats as the GPU stores it.</summary>
    private static Double3[] RowsOf(ScatterField field, ScatterKernel kernel)
    {
        var rows = new Double3[Width * Height];
        for (int pixel = 0; pixel < rows.Length; pixel++)
            if (field.Skin[pixel])
            {
                Double3 sum = SkinScatter.Pass(
                    field: field,
                    source: field.Light,
                    x: pixel % Width,
                    y: pixel / Width,
                    axis: ScatterAxis.Rows,
                    kernel: kernel
                );
                rows[pixel] = new((double)(Half)sum.X, (double)(Half)sum.Y, (double)(Half)sum.Z);
            }

        return rows;
    }

    [Fact]
    public void ScatterCarriesRedPastTheTerminatorFartherThanBlue()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            (float[] without, float[] with) = WithoutAndWith(renderer, Head(renderer));

            // Row 70 crosses the head above its nose, and column 158 lies 2 px past the terminator into the dark, where
            // only the dim ambient light reaches the surface.
            // Row 70 crosses the head above its nose. Columns 158 and 159 lie past the terminator, in the dark, where only the
            // dim ambient light reaches the surface. The Lambert term rises from 0 at the terminator, so little light lies
            // within the kernel's reach of them.
            foreach (int x in (int[])[158, 159])
            {
                Vector3 dark = At(without, x, 70);
                Vector3 scattered = At(with, x, 70);
                scattered.X.Should().BeGreaterThan(dark.X, "light from the lit side scatters past the terminator, x = {0}", x);
                (scattered.X / scattered.Z)
                    .Should()
                    .BeGreaterThan(dark.X / dark.Z, "red light scatters farther under the skin than blue, x = {0}", x);
            }

            At(with, 159, 70)
                .X.Should()
                .BeGreaterThan(1.02f * At(without, 159, 70).X, "the first pixel past the terminator gains a visible red");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ScatterLeavesAnEvenlyLitSkinPlaneAsItWas()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            RenderSnapshot snapshot = Lit(toSun: Vector3.UnitZ, sunIrradiance: 3, ambient: 0.4f);
            snapshot.Instances.Add(Facing(plane, Skin(renderer), new(0, 0, -0.3f)));

            (float[] without, float[] with) = WithoutAndWith(renderer, snapshot);

            RowKeepsItsLight(without: without, with: with, fromX: 0, toX: Width - 1, because: "an even light scatters to itself, to the image's edges");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ScatterLeavesEveryPixelThatIsNotSkinAsItWas()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            RenderSnapshot snapshot = Head(renderer);
            MeshId floor = renderer.CreateMesh(Primitives.Plane(1));
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.05f));
            snapshot.Instances.Add(
                Instance(floor, Material(renderer, new StandardShading(0), SkinColor), new(0, -0.11f, -0.6f))
            );
            snapshot.Instances.Add(
                Facing(square, Glow(renderer, new(4, 2, 1)), new(-0.12f, 0, -0.35f))
            );

            (float[] without, float[] with) = WithoutAndWith(renderer, snapshot);
            uint[] models = Models(renderer);

            int others = 0;
            for (int pixel = 0; pixel < models.Length; pixel++)
            {
                if (models[pixel] == ShadingCode.Skin)
                    continue;
                others++;
                with[(pixel * 4)..(pixel * 4 + 4)]
                    .Should()
                    .Equal(without[(pixel * 4)..(pixel * 4 + 4)], "pixel {0}, {1} is not skin", pixel % Width, pixel / Width);
            }

            others.Should().BeGreaterThan(Width * Height / 2, "the floor, the glowing square and the sky fill most of the image");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ScatterTakesNoLightFromASurfaceThatIsNotSkin()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            // Skin on the left half, and a bright glowing surface that is not skin on the right half, in one plane.
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            RenderSnapshot snapshot = Lit(toSun: Vector3.UnitZ, sunIrradiance: 3, ambient: 0.4f);
            snapshot.Instances.Add(Facing(plane, Skin(renderer), new(-0.5f, 0, -0.3f)));
            snapshot.Instances.Add(Facing(plane, Glow(renderer, new(50, 50, 50)), new(0.5f, 0, -0.3f)));

            (float[] without, float[] with) = WithoutAndWith(renderer, snapshot);

            // The kernel reaches about 8 px at this distance, so the skin's last 12 columns take taps on the glow.
            RowKeepsItsLight(without: without, with: with, fromX: Width / 2 - 12, toX: Width / 2 - 1, because: "a tap on the glow takes the center's light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ScatterCrossesNoDepthStep()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            // A bright skin plane on the left half, 0.5 m in front of a dark skin plane. The sun comes from the left, so
            // that the front plane's shadow on the back plane hides behind it, and both are lit evenly where they show.
            MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
            MeshId wide = renderer.CreateMesh(Primitives.Plane(2));
            RenderSnapshot snapshot = Lit(toSun: new(-1, 0, 1), sunIrradiance: 3, ambient: 0.4f);
            snapshot.Instances.Add(Facing(plane, Skin(renderer), new(-0.5f, 0, -0.3f)));
            snapshot.Instances.Add(
                Facing(wide, Skin(renderer, color: new(Decoded(60), Decoded(40), Decoded(30))), new(0, 0, -0.8f))
            );

            (float[] without, float[] with) = WithoutAndWith(renderer, snapshot);

            RowKeepsItsLight(without: without, with: with, fromX: Width / 2 - 10, toX: Width / 2 + 10, because: "no tap crosses the step of 0.5 m");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ScatterSpansHalfThePixelsAtTwiceTheDistance()
    {
        VulkanRenderer renderer = Scattering();
        try
        {
            // At this view depth a meter covers 1,000 px, so the outermost tap, at the reach of 8 mm, lands 8 px away.
            float near = (float)(Height / (2 * 1000 * Math.Tan(Gpu.Fov / 2)));
            int nearSpan = DarkPixelsThatTheScatterLights(renderer, viewDepth: near);
            int farSpan = DarkPixelsThatTheScatterLights(renderer, viewDepth: 2 * near);

            nearSpan.Should().Be(8, "the outermost tap lands 8 px from the edge");
            farSpan.Should().Be(nearSpan / 2, "a meter covers half the pixels at twice the distance");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Shows a lit skin plane on the left half beside a black skin plane on the right half, at <paramref name="viewDepth"/>
    /// m, and counts the black pixels of the middle row, from the edge, that the scatter lights.
    /// </summary>
    private static int DarkPixelsThatTheScatterLights(VulkanRenderer renderer, float viewDepth)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
        RenderSnapshot snapshot = Lit(toSun: Vector3.UnitZ, sunIrradiance: 3, ambient: 0);
        snapshot.Instances.Add(Facing(plane, Skin(renderer), new(-0.5f, 0, -viewDepth)));
        // Black and with no specular reflection, so that only the scatter lights it.
        snapshot.Instances.Add(
            Facing(plane, Black(renderer), new(0.5f, 0, -viewDepth))
        );
        (float[] without, float[] with) = WithoutAndWith(renderer, snapshot);
        float[] scatter = renderer.ReadScatter();
        // Half the light that the outermost tap carries from the lit side, in red, the channel that reaches farthest.
        double outermost = SkinProfile.Human.Kernel(SeparableSubsurface.Default.TapsPerSide).Taps[^1].Weight.X;
        double threshold = 0.5 * outermost * scatter[(Height / 2 * Width + Width / 4) * 4];
        int lit = 0;
        while (At(with, Width / 2 + lit, Height / 2).X - At(without, Width / 2 + lit, Height / 2).X > threshold)
            lit++;
        return lit;
    }

    /// <summary>A black skin of reflectance 0: no light leaves it but what the scatter brings.</summary>
    private static MaterialId Black(VulkanRenderer renderer) =>
        renderer.CreateMaterial(
            new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 0.5f, Reflectance: 0, Model: new SkinShading())
        );

    [Fact]
    public void WithoutTheScatterSkinShadesAsTheStandardModel()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, subsurface: new NoSubsurface());
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(0.1f));
            MaterialId skin = Skin(renderer);
            MaterialId standard = Material(renderer, new StandardShading(0), SkinColor);
            RenderSnapshot asSkin = Lit(toSun: new(1, 0.3f, 0.5f), sunIrradiance: 3, ambient: 0.2f);
            asSkin.Instances.Add(Instance(sphere, skin, HeadCenter));
            RenderSnapshot asStandard = Lit(toSun: new(1, 0.3f, 0.5f), sunIrradiance: 3, ambient: 0.2f);
            asStandard.Instances.Add(Instance(sphere, standard, HeadCenter));

            renderer.Render(asSkin, 1f);
            float[] skinImage = renderer.ReadHdr();
            renderer.Render(asStandard, 1f);
            float[] standardImage = renderer.ReadHdr();

            skinImage.Should().Equal(standardImage, "skin's light in the lighting passes is the standard model's");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMirrorShowsTheScatteredSkin()
    {
        VulkanRenderer renderer = Scattering(reflections: ConeTracedReflections.Default);
        try
        {
            RenderSnapshot snapshot = Head(renderer);
            MeshId floor = renderer.CreateMesh(Primitives.Plane(1));
            MaterialId mirror = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.05f, Model: new StandardShading(Metallic: 1))
            );
            snapshot.Instances.Add(Instance(floor, mirror, new(0, -0.11f, -0.6f)));

            (float[] without, float[] with) = WithoutAndWith(renderer, snapshot);
            uint[] models = Models(renderer);
            float[] distances = renderer.ReadDistance();

            double onSkin = 0;
            double inMirror = 0;
            for (int pixel = 0; pixel < models.Length; pixel++)
            {
                double change = (Rgb(with, pixel) - Rgb(without, pixel)).Length;
                if (models[pixel] == ShadingCode.Skin)
                    onSkin += change;
                else if (distances[pixel] > 0)
                    inMirror += change;
            }

            onSkin.Should().BePositive("the scatter changes the skin near its terminator");
            inMirror
                .Should()
                .BeGreaterThan(0.05 * onSkin, "the reflections run after the scatter, so the mirror shows its change");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
