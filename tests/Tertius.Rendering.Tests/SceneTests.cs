using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class SceneTests
{
    private const int Width = 1280;
    private const int Height = 720;

    [Fact]
    public void ReferenceSceneDrawsEveryDepthRange()
    {
        VulkanRenderer renderer = Gpu.Headless(Width, Height);
        try
        {
            var scene = TestScene.Create(renderer);
            RenderSnapshot snapshot = scene.Snapshot();
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "scene");
            RenderStats stats = renderer.LastFrameStats;

            Assert.Equal(snapshot.Instances.Count, stats.SubmittedInstances);
            Assert.Equal(snapshot.Instances.Count, stats.VisibleInstances);
            Assert.True(stats.FarCommands >= 1, "far range");
            Assert.True(stats.MiddleCommands >= 1, "middle range");
            Assert.True(stats.NearCommands >= 2, "near range");
            Assert.Equal(1, stats.TransparentCommands);
            Assert.Equal(2, stats.Lights);
            // The ground, three boxes and two spheres cast shadows into all four cascades.
            Assert.Equal(6, stats.ShadowCasters);
            Assert.Equal(4, stats.ShadowCascades);
            // One multi-draw per opaque range, one for the transparent range and one per shadow cascade.
            Assert.Equal(4 + ShadowCascades.Count, stats.IndirectDrawCalls);

            Vector2 Pixel(Vector3 p) => Gpu.Project(p, Width, Height);
            float sky = image.MeanLuma(new Vector2(Width * 0.5f, Height * 0.12f), 3);

            // The star at 1 AU is a small, saturated disc where the sun direction projects.
            Vector2 star = Pixel(TestScene.StarCenter);
            Assert.True(image.Luma(star) > 0.95f, $"star luma {image.Luma(star)}");

            // The planet's sunlit limb, a little inside the disc toward the sun, is lit; its far side is dark but
            // still covers the stars.
            Vector3 toSun = Vector3.Normalize(TestScene.StarCenter - TestScene.PlanetCenter);
            Vector3 viewDirection = Vector3.Normalize(TestScene.PlanetCenter);
            Vector3 limbDirection = Vector3.Normalize(toSun - Vector3.Dot(toSun, viewDirection) * viewDirection);
            Vector2 litLimb = Pixel(TestScene.PlanetCenter + limbDirection * TestScene.PlanetRadius * 0.85f);
            Assert.True(image.MeanLuma(litLimb, 2) > sky + 0.15f, $"planet limb {image.MeanLuma(litLimb, 2)} vs sky {sky}");

            // The station at 50 km: its sunlit face is brighter than the sky around it.
            Vector2 station = Pixel(TestScene.StationCenter);
            Assert.True(image.MaxLuma(station, 6) > sky + 0.2f, $"station {image.MaxLuma(station, 6)}");
            Assert.True(image.MeanLuma(station + new Vector2(0, -40), 2) < 0.1f, "sky above the station");

            // Lit near objects are brighter than the sky behind them.
            Assert.True(image.MeanLuma(Pixel(TestScene.RedSphere), 2) > sky + 0.1f, "red sphere");
            Assert.True(image.MeanLuma(Pixel(TestScene.TexturedBox + new Vector3(0, 0.89f, 0.3f)), 2) > sky + 0.15f, "top of the textured box");
            Vector3 red = image.Color(Pixel(TestScene.RedSphere));
            Assert.True(red.X > 2 * red.Y && red.X > 2 * red.Z, $"red sphere color {red}");

            // The spot light makes the top of the blue sphere brighter than its unlit lower half.
            Vector2 spotlit = Pixel(TestScene.BlueSphere + new Vector3(0.15f, 0.45f, 0.2f));
            Vector2 below = Pixel(TestScene.BlueSphere + new Vector3(0f, -0.45f, 0.3f));
            Assert.True(image.Luma(spotlit) > image.Luma(below) + 0.1f, $"spot {image.Luma(spotlit)} vs {image.Luma(below)}");

            // The transparent green sphere tints what is behind it.
            Vector3 glass = image.Color(Pixel(TestScene.GreenGlass));
            Assert.True(glass.Y > glass.X && glass.Y > glass.Z, $"glass color {glass}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void PlanetShadowDarkensAMoonInIt()
    {
        VulkanRenderer renderer = Gpu.Headless(960, 540);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            MaterialId rock = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.6f, 0.6f, 0.6f, 1), gloss: 0.1f));
            // The sun is due +X. The planet's shadow points to −X; one moon sits in it, the other on the day side.
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
            var planet = new Vector3(0, 0, -3.0e7f);
            var shadowed = new Vector3(-2.0e7f, 0, -3.0e7f);
            var lit = new Vector3(2.0e7f, 0, -3.0e7f);
            const float moonRadius = 3.0e6f;
            snapshot.Instances.Add(Gpu.Instance(sphere, rock, planet, new Vector3(6.0e6f)));
            snapshot.Instances.Add(Gpu.Instance(sphere, rock, shadowed, new Vector3(moonRadius)));
            snapshot.Instances.Add(Gpu.Instance(sphere, rock, lit, new Vector3(moonRadius)));
            snapshot.Occluders.Add(new RenderOccluder { Center = planet, Radius = 6.0e6f });

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "eclipse");
            // Sample both moons on the side that faces the sun.
            var sunward = Vector3.Normalize(new Vector3(0.6f, 0, 0.8f)) * moonRadius;
            float inShadow = image.MeanLuma(Gpu.Project(shadowed + sunward, 960, 540), 1);
            float inSun = image.MeanLuma(Gpu.Project(lit + sunward, 960, 540), 1);
            Assert.True(inSun > 0.4f, $"lit moon {inSun}");
            Assert.True(inShadow < 0.3f * inSun, $"shadowed moon {inShadow} vs lit moon {inSun}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OccluderSphereCastsASoftShadowOnTheGround()
    {
        VulkanRenderer renderer = Gpu.Headless(640, 360);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId ground = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.7f, 0.7f, 0.7f, 1), gloss: 0.2f));
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0, 1, 0));
            snapshot.Instances.Add(Gpu.Instance(plane, ground, new Vector3(0, -1.5f, -20), new Vector3(20, 1, 20)));
            // An invisible occluder 2 m above the ground, straight under the sun.
            snapshot.Occluders.Add(new RenderOccluder { Center = new Vector3(0, 0.5f, -8f), Radius = 1f });
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "occluder-shadow");
            float shadow = image.MeanLuma(Gpu.Project(new Vector3(0, -1.5f, -8f), 640, 360), 2);
            float sunlit = image.MeanLuma(Gpu.Project(new Vector3(3f, -1.5f, -8f), 640, 360), 2);
            Assert.True(sunlit > 0.3f, $"sunlit ground {sunlit}");
            Assert.True(shadow < 0.5f * sunlit, $"shadow {shadow} vs sunlit {sunlit}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
