using System.Numerics;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class SceneTests
{
    private const int Width = 1280;
    private const int Height = 720;

    [Fact]
    public void ReferenceSceneDrawsEveryDepthRange()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            var scene = TestScene.Create(renderer);
            RenderSnapshot snapshot = scene.Snapshot();
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "scene");
            RenderStats stats = renderer.LastFrameStats;

            stats.SubmittedInstances.Should().Be(snapshot.Instances.Count);
            stats.VisibleInstances.Should().Be(snapshot.Instances.Count);
            stats.FarCommands.Should().BeGreaterThanOrEqualTo(1, "the planet and the star are in the far range");
            stats.MiddleCommands.Should().BeGreaterThanOrEqualTo(1, "the station is in the middle range");
            stats.NearCommands.Should()
                .BeGreaterThanOrEqualTo(2, "boxes, spheres and the ground are in the near range");
            stats.TransparentCommands.Should().Be(1);
            stats.Lights.Should().Be(2);
            // The ground, three boxes and two spheres cast shadows into all four cascades.
            stats.ShadowCasters.Should().Be(6);
            stats.ShadowCascades.Should().Be(4);
            // One multi-draw per opaque range, one for the transparent range and one per shadow cascade.
            stats.IndirectDrawCalls.Should().Be(4 + ShadowCascades.Count);

            static Vector2 Pixel(Vector3 p) => Gpu.Project(point: p, width: Width, height: Height);
            float sky = image.MeanLuma(new(Width * 0.5f, Height * 0.12f), 3);

            // The star at 1 AU is a small, saturated disc where the sun direction projects.
            Vector2 star = Pixel(TestScene.StarCenter);
            image.Luma(star).Should().BeGreaterThan(0.95f, "the star is saturated");

            // The planet's sunlit limb, a little inside the disc toward the sun, is lit; its far side is dark but
            // still covers the stars.
            Vector3 toSun = Vector3.Normalize(TestScene.StarCenter - TestScene.PlanetCenter);
            Vector3 viewDirection = Vector3.Normalize(TestScene.PlanetCenter);
            Vector3 limbDirection = Vector3.Normalize(toSun - Vector3.Dot(toSun, viewDirection) * viewDirection);
            Vector2 litLimb = Pixel(TestScene.PlanetCenter + limbDirection * TestScene.PlanetRadius * 0.85f);
            image.MeanLuma(litLimb, 2).Should().BeGreaterThan(sky + 0.15f,
                "the planet's sunlit limb is brighter than the sky ({0})", sky);

            // The station at 50 km: its sunlit face is brighter than the sky around it.
            Vector2 station = Pixel(TestScene.StationCenter);
            image.MaxLuma(station, 6).Should().BeGreaterThan(sky + 0.2f,
                "the station's sunlit face is brighter than the sky ({0})", sky);
            image.MeanLuma(station + new Vector2(0, -40), 2).Should()
                .BeLessThan(0.1f, "the sky above the station is dark");

            // Lit near objects are brighter than the sky behind them.
            image.MeanLuma(Pixel(TestScene.RedSphere), 2).Should().BeGreaterThan(sky + 0.1f, "the red sphere is lit");
            image.MeanLuma(Pixel(TestScene.TexturedBox + new Vector3(0, 0.89f, 0.3f)), 2).Should()
                .BeGreaterThan(sky + 0.15f, "the top of the textured box is lit");
            Vector3 red = image.Color(Pixel(TestScene.RedSphere));
            red.X.Should().BeGreaterThan(2 * red.Y, "the red sphere is red: {0}", red).And
                .BeGreaterThan(2 * red.Z, "the red sphere is red: {0}", red);

            // The spot light makes the top of the blue sphere brighter than its unlit lower half.
            Vector2 spotlit = Pixel(TestScene.BlueSphere + new Vector3(0.15f, 0.45f, 0.2f));
            Vector2 below = Pixel(TestScene.BlueSphere + new Vector3(0f, -0.45f, 0.3f));
            image.Luma(spotlit).Should().BeGreaterThan(image.Luma(below) + 0.1f,
                "the spot light reaches the top of the blue sphere");

            // The transparent green sphere tints what is behind it.
            Vector3 glass = image.Color(Pixel(TestScene.GreenGlass));
            glass.Y.Should().BeGreaterThan(glass.X, "the glass tints green: {0}", glass).And
                .BeGreaterThan(glass.Z, "the glass tints green: {0}", glass);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void PlanetShadowDarkensAMoonInIt()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 960, height: 540);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            MaterialId rock =
                renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.6f, y: 0.6f, z: 0.6f, w: 1), gloss: 0.1f));
            // The sun is due +X. The planet's shadow points to −X; one moon sits in it, the other on the day side.
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
            var planet = new Vector3(0, 0, -3.0e7f);
            var shadowed = new Vector3(-2.0e7f, 0, -3.0e7f);
            var lit = new Vector3(2.0e7f, 0, -3.0e7f);
            const float moonRadius = 3.0e6f;
            snapshot.Instances.Add(Gpu.Instance(mesh: sphere, material: rock, position: planet, scale: new(6.0e6f),
                flags: RenderFlags.CastsShadow));
            snapshot.Instances.Add(Gpu.Instance(mesh: sphere, material: rock, position: shadowed,
                scale: new(moonRadius)));
            snapshot.Instances.Add(Gpu.Instance(mesh: sphere, material: rock, position: lit, scale: new(moonRadius)));

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "eclipse");
            // Sample both moons on the side that faces the sun.
            Vector3 sunward = Vector3.Normalize(new(0.6f, 0, 0.8f)) * moonRadius;
            float inShadow = image.MeanLuma(Gpu.Project(point: shadowed + sunward, width: 960, height: 540), 1);
            float inSun = image.MeanLuma(Gpu.Project(point: lit + sunward, width: 960, height: 540), 1);
            inSun.Should().BeGreaterThan(0.4f, "the moon on the day side is lit");
            inShadow.Should().BeLessThan(0.3f * inSun, "the moon in the planet's shadow is dark");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFarSphereCastsASoftEdgedShadowOnTheGroundNearTheCamera()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 640, height: 360);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 32, 16));
            MaterialId ground =
                renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.7f, y: 0.7f, z: 0.7f, w: 1), gloss: 0.2f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0, 1, 0));
            snapshot.Instances.Add(Gpu.Instance(mesh: plane, material: ground, position: new(0, -1.5f, -20),
                scale: new(20, 1, 20)));
            // An invisible caster of 200 m radius, 2.4 km straight up from a point 200 m to the left of the view. It stays
            // farther than 2 km from the camera, so it casts a far shadow, whose edge crosses the ground at x = 0. The sun's
            // disc, 4.67e-3 rad in radius, spreads that edge over 2 × 4.67e-3 × 2,400 m, about 22 m.
            snapshot.Instances.Add(RenderInstance.Create(mesh: sphere, material: ground,
                position: new(-200f, 2_398.5f, -25f),
                rotation: Quaternion.Identity, scale: new(200f), boundingRadius: 0, hemisphere: HemisphereUp.Even,
                flags: RenderFlags.CastsShadow, entityIndex: 0, part: 0));
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "occluder-shadow");
            float shadow = image.MeanLuma(Gpu.Project(point: new(-16f, -1.5f, -25f), width: 640, height: 360), 2);
            float edge = image.MeanLuma(Gpu.Project(point: new(0f, -1.5f, -25f), width: 640, height: 360), 2);
            float sunlit = image.MeanLuma(Gpu.Project(point: new(16f, -1.5f, -25f), width: 640, height: 360), 2);
            sunlit.Should().BeGreaterThan(0.3f, "the ground beyond the penumbra is lit");
            shadow.Should().BeLessThan(0.5f * sunlit, "the far sphere shadows the ground under it");
            edge.Should().BeInRange(shadow + 0.2f * (sunlit - shadow), sunlit - 0.2f * (sunlit - shadow),
                "the edge is soft: half of the sun shows there");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
