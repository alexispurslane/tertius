using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A rectangle light on the GPU (ARCHITECTURE.md, "Rectangle lights"): a matte square in front of it takes the C#
/// rectangle's light, also where the panel crosses the square's horizon and where the square stands behind it, a rough
/// square shows the C# LTC shine, and a mirror shows the panel at its size.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class RectLightRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The square faces the camera from this distance, in m, centered on the view axis.</summary>
    private const float SquareDistance = 3;

    /// <summary>A rectangle light that casts no shadow, centered on <paramref name="position"/> and turned by <paramref name="rotation"/>.</summary>
    private static RectRenderLight Panel(
        Vector3 position,
        Quaternion rotation,
        float power,
        float width,
        float height
    ) =>
        new(
            EntityIndex: Gpu.LampEntity,
            EntityGeneration: 0,
            Position: position,
            PreviousPosition: position,
            Rotation: rotation,
            PreviousRotation: rotation,
            Color: Vector3.One,
            Power: power,
            Width: width,
            Height: height,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );

    /// <summary>The half sides of <paramref name="panel"/>, in m.</summary>
    private static (Double3 HalfWidth, Double3 HalfHeight) HalfSides(RectRenderLight panel) =>
        (
            Vector3.Transform(Vector3.UnitX, panel.Rotation) * (panel.Width / 2),
            Vector3.Transform(Vector3.UnitY, panel.Rotation) * (panel.Height / 2)
        );

    /// <summary>
    /// The face radiance of <paramref name="panel"/>, in W/(m²·sr), times the window of its reach at the point
    /// <paramref name="toCenter"/> from its center: its peak intensity P / π over its area W H, and the window
    /// (1 − (d / reach)⁴)², with d the distance to its face and the reach that of its peak intensity at the cutoff
    /// irradiance of 0 stops.
    /// </summary>
    private static double FaceRadianceInReach(RectRenderLight panel, Double3 toCenter)
    {
        double intensity = LightUnits.RectIntensity(panel.Power);
        double faceRadiance = intensity / ((double)panel.Width * panel.Height);
        double reach = LightUnits.Reach(intensity, LightUnits.CutoffIrradiance(0));
        (Double3 halfWidth, Double3 halfHeight) = HalfSides(panel);
        Double3 fromCenter = -toCenter;
        double across = Math.Clamp(Double3.Dot(fromCenter, halfWidth) / halfWidth.LengthSquared, -1, 1),
            up = Math.Clamp(Double3.Dot(fromCenter, halfHeight) / halfHeight.LengthSquared, -1, 1);
        double distanceSquared = (fromCenter - across * halfWidth - up * halfHeight).LengthSquared;
        double ratio = distanceSquared / (reach * reach);
        double window = 1 - ratio * ratio;
        return faceRadiance * window * window;
    }

    /// <summary>
    /// A dark snapshot with <paramref name="panel"/> and a square of <paramref name="desc"/> that faces the camera
    /// <see cref="SquareDistance"/> ahead, turned by <paramref name="tilt"/> rad about the vertical, under a view of height
    /// <paramref name="fov"/> rad: its capture.
    /// </summary>
    private static Capture RenderSquare(
        VulkanRenderer renderer,
        RectRenderLight panel,
        MaterialDesc desc,
        float fov,
        string name,
        float tilt = 0
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        snapshot.Camera.VerticalFov = fov;
        snapshot.Lights.Add(panel);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(1.5f)),
                material: renderer.CreateMaterial(desc),
                position: new(0, 0, -SquareDistance),
                scale: Vector3.One,
                rotation: SquareTurn(tilt),
                radius: 2.2f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>The turn of the square, whose plane faces +Y, to face the camera along +Z and then lean <paramref name="tilt"/> rad about +Y.</summary>
    private static Quaternion SquareTurn(float tilt) =>
        Quaternion.Concatenate(
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, tilt)
        );

    /// <summary>A panel in front of the matte square by name.</summary>
    public static TheoryData<string> MattePanels => ["facing-the-square", "tilted-beside", "through-the-square"];

    /// <summary>
    /// The panel of <see cref="MattePanels"/> by its name: from the square's center to its center, in m, its turn, and its
    /// width and height in m.
    /// </summary>
    private static (Double3 ToCenter, Quaternion Rotation, float Width, float Height) MattePanelNamed(string name) =>
        name switch
        {
            // A panel 0.8 m by 0.5 m, 0.6 m in front of the center, facing it.
            "facing-the-square" => (
                new(0, 0, 0.6),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI),
                0.8f,
                0.5f
            ),
            // The cabin lamp's panel 0.7 m beside the center and 0.5 m out, turned to face the center at a slant.
            "tilted-beside" => (new(0.7, 0.1, 0.5), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -2.2f), 1.2f, 1.2f),
            // A panel that stands slanted through the square above its center, facing down toward it: only the part in front
            // of the square lights it, and the cut at the horizon decides how much.
            "through-the-square" => (new(0, 0.3, 0.1), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.2f), 1.0f, 0.6f),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such panel."),
        };

    [Theory]
    [MemberData(nameof(MattePanels))]
    public void ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(string name)
    {
        (Double3 toCenter, Quaternion rotation, float width, float height) = MattePanelNamed(name);
        RectRenderLight panel = Panel(
            position: new Vector3(0, 0, -SquareDistance) + toCenter.ToVector3(),
            rotation: rotation,
            power: 6,
            width: width,
            height: height
        );
        (Double3 halfWidth, Double3 halfHeight) = HalfSides(panel);
        var matte = new MaterialDesc(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), Roughness: 1, Reflectance: 0.5f);
        DirectTerms terms = TestMaterials
            .SampleOf(matte)
            .RectTerms(
                normal: Double3.UnitZ,
                toView: Double3.UnitZ,
                rect: new RectView(toCenter: toCenter, halfWidth: halfWidth, halfHeight: halfHeight),
                tables: BrdfTables.Shared,
                ltc: LtcTable.Shared
            );
        Double3 expected = (terms.Diffuse + terms.Specular) * FaceRadianceInReach(panel, toCenter);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderSquare(
                renderer: renderer,
                panel: panel,
                desc: matte,
                fov: Gpu.Fov,
                name: $"rect-light-matte-{name}"
            );
            Vector3[] shown = Displayed(
                renderer,
                [(expected * 0.98).ToVector3(), (expected * 1.02).ToVector3()],
                $"rect-light-matte-{name}-glow"
            );

            expected.X.Should().BePositive("the panel lights the square's center");
            image
                .Color(Width / 2, Height / 2)
                .X.Should()
                .BeInRange(
                    shown[0].X,
                    shown[1].X,
                    "the square's center takes the C# rectangle's light {0} within 2 %",
                    expected.X
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASquareBehindARectangleLightTakesNoneOfItsLight()
    {
        // The panel of "facing-the-square" turned to face away from the square, toward the camera.
        RectRenderLight panel = Panel(
            position: new(0, 0, -SquareDistance + 0.6f),
            rotation: Quaternion.Identity,
            power: 6,
            width: 0.8f,
            height: 0.5f
        );
        var matte = new MaterialDesc(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), Roughness: 1, Reflectance: 0.5f);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderSquare(
                renderer: renderer,
                panel: panel,
                desc: matte,
                fov: Gpu.Fov,
                name: "rect-light-behind"
            );

            image.Color(Width / 2, Height / 2).Should().Be(Vector3.Zero, "the square lies behind the panel's face");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARoughSquareShowsTheCSharpLtcShineOfARectangleLight()
    {
        // A rough white metal square, roughness 0.5, and a panel 1 m square 1.2 m in front of it and 0.6 m to the side,
        // turned to face the square's center: the camera sees its wide highlight at the center.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-0.6f, -1.2f));
        var toCenter = new Double3(0.6, 0, 1.2);
        RectRenderLight panel = Panel(
            position: new Vector3(0, 0, -SquareDistance) + toCenter.ToVector3(),
            rotation: rotation,
            power: 4,
            width: 1,
            height: 1
        );
        (Double3 halfWidth, Double3 halfHeight) = HalfSides(panel);
        var metal = new MaterialDesc(
            BaseColor: new(x: 1, y: 1, z: 1, w: 1),
            Roughness: 0.5f,
            Reflectance: 0.5f,
            Model: new StandardShading(Metallic: 1)
        );
        DirectTerms terms = TestMaterials
            .SampleOf(metal)
            .RectTerms(
                normal: Double3.UnitZ,
                toView: Double3.UnitZ,
                rect: new RectView(toCenter: toCenter, halfWidth: halfWidth, halfHeight: halfHeight),
                tables: BrdfTables.Shared,
                ltc: LtcTable.Shared
            );
        Double3 expected = terms.Specular * FaceRadianceInReach(panel, toCenter);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderSquare(
                renderer: renderer,
                panel: panel,
                desc: metal,
                fov: Gpu.Fov,
                name: "rect-light-rough"
            );
            Vector3[] shown = Displayed(
                renderer,
                [(expected * 0.98).ToVector3(), (expected * 1.02).ToVector3()],
                "rect-light-rough-glow"
            );

            image
                .Color(Width / 2, Height / 2)
                .X.Should()
                .BeInRange(
                    shown[0].X,
                    shown[1].X,
                    "the square's center shows the C# LTC shine {0} within 2 %",
                    expected.X
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight()
    {
        // The rough square leans 0.95 rad, 54°, away from the camera, so the shine reads the table's leaning cosines and
        // turns the face into the frame whose first tangent points toward the view. The panel, 1 m square, stands 1.2 m
        // from the square's center near the mirror ray and 0.35 m above the plane of view, facing the center, so its
        // corners use both tangents.
        const float tilt = 0.95f;
        var normal = Vector3.Transform(Vector3.UnitY, SquareTurn(tilt));
        var mirror = Vector3.Reflect(-Vector3.UnitZ, normal);
        var toPanel = Vector3.Normalize(mirror + new Vector3(0, 0.35f, 0));
        Vector3 back = -toPanel;
        var rotation = Quaternion.CreateFromAxisAngle(
            Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, back)),
            MathF.Acos(back.Z)
        );
        var toCenter = (Double3)(1.2f * toPanel);
        RectRenderLight panel = Panel(
            position: new Vector3(0, 0, -SquareDistance) + toCenter.ToVector3(),
            rotation: rotation,
            power: 4,
            width: 1,
            height: 1
        );
        (Double3 halfWidth, Double3 halfHeight) = HalfSides(panel);
        var metal = new MaterialDesc(
            BaseColor: new(x: 1, y: 1, z: 1, w: 1),
            Roughness: 0.5f,
            Reflectance: 0.5f,
            Model: new StandardShading(Metallic: 1)
        );
        DirectTerms terms = TestMaterials
            .SampleOf(metal)
            .RectTerms(
                normal: normal,
                toView: Double3.UnitZ,
                rect: new RectView(toCenter: toCenter, halfWidth: halfWidth, halfHeight: halfHeight),
                tables: BrdfTables.Shared,
                ltc: LtcTable.Shared
            );
        Double3 expected = terms.Specular * FaceRadianceInReach(panel, toCenter);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderSquare(
                renderer: renderer,
                panel: panel,
                desc: metal,
                fov: Gpu.Fov,
                name: "rect-light-slant",
                tilt: tilt
            );
            // 3 %: the bilinear weights of a table sample between texels are as coarse as the device's sub-texel
            // precision, which Vulkan lets be 4 bits, and the leaning cosine changes along both of the table's axes here.
            Vector3[] shown = Displayed(
                renderer,
                [(expected * 0.97).ToVector3(), (expected * 1.03).ToVector3()],
                "rect-light-slant-glow"
            );

            image
                .Color(Width / 2, Height / 2)
                .X.Should()
                .BeInRange(
                    shown[0].X,
                    shown[1].X,
                    "the slanted square shows the C# LTC shine {0} within 3 %",
                    expected.X
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMirrorReflectsARectangleLightAtItsSize()
    {
        // A black glossy mirror of roughness 0 and F0 = 0.04 faces the camera 3 m ahead, and a panel 0.6 m wide and 0.2 m
        // high stands 2 m in front of it, facing it. The camera sees the panel's image 5 m away.
        const float fov = 0.25f;
        RectRenderLight panel = Panel(
            position: new(0, 0, -SquareDistance + 2),
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI),
            power: 2,
            width: 0.6f,
            height: 0.2f
        );
        (Double3 halfWidth, Double3 halfHeight) = HalfSides(panel);
        var mirror = new MaterialDesc(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 0, Reflectance: 0.5f);
        var toCenter = new Double3(0, 0, 2);
        Double3 plateau =
            TestMaterials
                .SampleOf(mirror)
                .RectTerms(
                    normal: Double3.UnitZ,
                    toView: Double3.UnitZ,
                    rect: new RectView(toCenter: toCenter, halfWidth: halfWidth, halfHeight: halfHeight),
                    tables: BrdfTables.Shared,
                    ltc: LtcTable.Shared
                )
                .Specular * FaceRadianceInReach(panel, toCenter);
        double focalLength = Height / 2.0 / Math.Tan(fov / 2);

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            Capture image = RenderSquare(
                renderer: renderer,
                panel: panel,
                desc: mirror,
                fov: fov,
                name: "rect-light-mirror"
            );
            Vector3[] shown = Displayed(
                renderer,
                [(plateau * 0.5).ToVector3(), (plateau * 0.98).ToVector3(), (plateau * 1.02).ToVector3()],
                "rect-light-mirror-glow"
            );
            int across = HalfMaximumRun(image, horizontal: true, threshold: shown[0].X);
            int up = HalfMaximumRun(image, horizontal: false, threshold: shown[0].X);

            image
                .Color(Width / 2, Height / 2)
                .X.Should()
                .BeInRange(
                    shown[1].X,
                    shown[2].X,
                    "the image's middle shows the C# radiance {0} within 2 %",
                    plateau.X
                );
            // The floored lobe widens each edge by about a pixel at this view.
            across
                .Should()
                .BeCloseTo((int)Math.Round(2 * Math.Atan(0.3 / 5) * focalLength), 4, "the image is the panel's width");
            up.Should()
                .BeCloseTo((int)Math.Round(2 * Math.Atan(0.1 / 5) * focalLength), 4, "the image is the panel's height");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The number of pixels on the middle row or column whose red is above <paramref name="threshold"/>, in one run.</summary>
    private static int HalfMaximumRun(Capture image, bool horizontal, float threshold)
    {
        int length = horizontal ? Width : Height;
        int best = 0,
            run = 0;
        for (int i = 0; i < length; i++)
        {
            float red = horizontal ? image.Color(i, Height / 2).X : image.Color(Width / 2, i).X;
            run = red > threshold ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return best;
    }
}
