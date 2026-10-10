using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>Cascaded sun shadow maps on the GPU: what they shadow, how far, and that lit surfaces stay clean.</summary>
[Collection(GpuCollection.Name)]
public sealed class ShadowTests
{
    private const RenderFlags Caster = RenderFlags.CastsShadow;

    /// <summary>The meshes and materials most shadow scenes need.</summary>
    private sealed class Kit
    {
        public required MeshId Box { get; init; }
        public required MeshId Plane { get; init; }
        public required MeshId Sphere { get; init; }
        public required MaterialId Ground { get; init; }
        public required MaterialId White { get; init; }

        public static Kit Create(VulkanRenderer renderer) => new()
        {
            Box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f))),
            Plane = renderer.CreateMesh(Primitives.Plane(1f)),
            Sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
            Ground = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.6f, 0.6f, 0.6f, 1), gloss: 0.15f) with { SpecularIntensity = 0.3f }),
            White = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.8f, 0.8f, 1), gloss: 0.3f)),
        };
    }

    /// <summary>The ground point that the sun direction <paramref name="sun"/> projects <paramref name="point"/> onto.</summary>
    private static Vector3 ShadowOnGround(Vector3 point, Vector3 sun, float groundY)
    {
        Vector3 l = Vector3.Normalize(sun);
        return point - l * ((point.Y - groundY) / l.Y);
    }

    [Fact]
    public void BoxCastsAShadowOnTheGroundAndTheEclipseTermStillApplies()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            Kit kit = Kit.Create(renderer);
            // The sun is up and to the right, so the box's shadow falls to its left.
            var sun = new Vector3(0.6f, 0.8f, 0f);
            Quaternion camera = Gpu.PitchDown(0.25f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            const float ground = -3f;
            var box = new Vector3(0, ground + 0.75f, -8);
            snapshot.Instances.Add(Gpu.Instance(kit.Plane, kit.Ground, new Vector3(0, ground, -15), new Vector3(30, 1, 30), flags: Caster));
            // Turned so that its sunlit +X face looks toward the camera.
            snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, box, new Vector3(1.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f), flags: Caster));
            // An invisible occluder sphere straight above a patch of ground to the right: the analytic term.
            Vector3 eclipsed = new(4f, ground, -10f);
            snapshot.Occluders.Add(new RenderOccluder { Center = eclipsed + Vector3.Normalize(sun) * 4f, Radius = 0.8f });

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "shadow-box");
            RenderStats stats = renderer.LastFrameStats;
            Assert.Equal(4, stats.ShadowCascades);
            Assert.Equal(2, stats.ShadowCasters);

            Vector2 Pixel(Vector3 p) => Gpu.Project(p, camera, width, height);
            // The top's shadow lands 1.1 m left of the box center; a little further left it is clear of the box itself.
            Vector3 shadowCenter = ShadowOnGround(box + new Vector3(0, 0.75f, 0), sun, ground) - new Vector3(0.25f, 0, 0);
            float shadowed = image.MeanLuma(Pixel(shadowCenter), 2);
            float lit = image.MeanLuma(Pixel(new Vector3(-shadowCenter.X, ground, shadowCenter.Z)), 2);
            float litFar = image.MeanLuma(Pixel(new Vector3(-3f, ground, -12f)), 2);
            float top = image.MeanLuma(Pixel(box + new Vector3(0, 0.75f, 0)), 2);
            float occluded = image.MeanLuma(Pixel(eclipsed), 2);
            Assert.True(lit > 0.35f, $"lit ground {lit}");
            Assert.True(shadowed < 0.5f * lit, $"box shadow {shadowed} vs lit ground {lit}");
            Assert.True(shadowed < 0.5f * litFar, $"box shadow {shadowed} vs lit ground further back {litFar}");
            Assert.True(top > lit, $"the box's sunlit top {top} vs the ground {lit}");
            Assert.True(occluded < 0.5f * lit, $"occluder sphere shadow {occluded} vs lit ground {lit}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheLastCascadeStillShadowsAtOnePointFiveKilometres()
    {
        const int width = 960, height = 540;
        const float fov = MathF.PI / 6f;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            Kit kit = Kit.Create(renderer);
            // The camera looks 20 degrees down at a 40 m box 1.5 km away. The sun is 45 degrees up from +X.
            var sun = new Vector3(1f, 1f, 0f);
            Quaternion camera = Gpu.PitchDown(20f * MathF.PI / 180f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            float ground = -1_500f * MathF.Tan(20f * MathF.PI / 180f);
            var box = new Vector3(0, ground + 20f, -1_500f);
            snapshot.Instances.Add(Gpu.Instance(kit.Plane, kit.Ground, new Vector3(0, ground, -1_500f), new Vector3(2_500f, 1, 2_500f), flags: Caster));
            snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, box, new Vector3(40f), flags: Caster));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "shadow-1500m");
            Assert.Equal(4, renderer.LastFrameStats.ShadowCascades);

            // The shadow covers the ground from 20 m to 60 m left of the box center.
            var shadowPoint = new Vector3(-40f, ground, -1_500f);
            var litPoint = new Vector3(40f, ground, -1_500f);
            float depth = Gpu.ViewDepth(shadowPoint, camera);
            Assert.InRange(depth, ShadowCascades.Split(2), ShadowCascades.BlendStart(3));
            float shadowed = image.MeanLuma(Gpu.Project(shadowPoint, camera, width, height, fov), 2);
            float lit = image.MeanLuma(Gpu.Project(litPoint, camera, width, height, fov), 2);
            Assert.True(lit > 0.3f, $"lit ground {lit}");
            Assert.True(shadowed < 0.5f * lit, $"shadow at {depth:F0} m {shadowed} vs lit ground {lit}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ShadowMapsEndAtTwoKilometres()
    {
        const int width = 960, height = 540;
        const float fov = 40f * MathF.PI / 180f;
        const float pitch = 10f * MathF.PI / 180f;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            Kit kit = Kit.Create(renderer);
            // A 40 m wall runs straight away from the camera from 1.5 km to 3 km. The sun, 30 degrees up from +X,
            // throws its shadow 69 m to the left over its whole length. The camera is 300 m up, looking 10 degrees down.
            var sun = new Vector3(MathF.Cos(MathF.PI / 6f), MathF.Sin(MathF.PI / 6f), 0f);
            Quaternion camera = Gpu.PitchDown(pitch);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            const float ground = -300f;
            snapshot.Instances.Add(Gpu.Instance(kit.Plane, kit.Ground, new Vector3(0, ground, -2_500f), new Vector3(3_000f, 1, 3_000f), flags: Caster));
            // Its bounding sphere reaches within 1 km of the camera, so it is a caster.
            snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, new Vector3(0, ground + 20f, -2_250f), new Vector3(10f, 40f, 1_500f), flags: Caster));
            // A second wall entirely beyond 2 km is not a caster at all.
            snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, new Vector3(-600f, ground + 20f, -2_600f), new Vector3(10f, 40f, 200f), flags: Caster));

            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "shadow-2km-cutoff");
            Assert.Equal(2, renderer.LastFrameStats.ShadowCasters);
            float Luma(Vector3 p) => image.MeanLuma(Gpu.Project(p, camera, width, height, fov), 1);

            foreach (float z in (float[])[1_550f, 1_600f, 1_650f, 1_700f, 1_750f])
            {
                var shadowPoint = new Vector3(-40f, ground, -z);
                float depth = Gpu.ViewDepth(shadowPoint, camera);
                Assert.True(depth < ShadowCascades.BlendStart(3), $"{depth}");
                float shadowed = Luma(shadowPoint), lit = Luma(new Vector3(40f, ground, -z));
                Assert.True(shadowed < 0.5f * lit, $"at {depth:F0} m the shadow {shadowed} vs lit ground {lit}");
            }
            foreach (float z in (float[])[2_050f, 2_200f, 2_400f, 2_600f, 2_800f, 2_950f])
            {
                var shadowPoint = new Vector3(-40f, ground, -z);
                float depth = Gpu.ViewDepth(shadowPoint, camera);
                Assert.True(depth > ShadowCascades.Distance, $"{depth}");
                float shadowed = Luma(shadowPoint), lit = Luma(new Vector3(40f, ground, -z));
                Assert.True(MathF.Abs(shadowed - lit) < 0.02f, $"at {depth:F0} m, beyond the maps, {shadowed} vs {lit}");
                // The left side of the second wall is lit ground too.
                float beside = Luma(new Vector3(-660f, ground, -2_600f));
                Assert.True(MathF.Abs(beside - lit) < 0.03f, $"beside the far wall {beside} vs {lit}");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(20f)]
    [InlineData(8f)]
    public void LitGroundHasNoAcne(float sunElevationDegrees)
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            Kit kit = Kit.Create(renderer);
            float elevation = sunElevationDegrees * MathF.PI / 180f, azimuth = 140f * MathF.PI / 180f;
            var sun = new Vector3(MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), -MathF.Cos(azimuth) * MathF.Cos(elevation));

            // A low and a high view cover every cascade. In the reference the ground casts no shadow, so its lit
            // pixels cannot have acne; with self-shadowing they must come out the same.
            foreach ((string name, float heightAboveGround, float pitch) in (ReadOnlySpan<(string, float, float)>)[("low", 2f, 0.3f), ("high", 120f, 0.35f)])
            {
                Quaternion camera = Gpu.PitchDown(pitch);
                RenderSnapshot Scene(bool groundCasts)
                {
                    RenderSnapshot snapshot = Gpu.Snapshot(sun);
                    snapshot.Camera.Rotation = camera;
                    snapshot.Camera.PreviousRotation = camera;
                    snapshot.Instances.Add(Gpu.Instance(kit.Plane, kit.Ground, new Vector3(0, -heightAboveGround, -1_000f), new Vector3(1_500f, 1, 1_500f),
                        flags: groundCasts ? Caster : RenderFlags.None));
                    // Something else casts, so the cascades are drawn in both images.
                    snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, new Vector3(-40f, -heightAboveGround + 1f, -30f), new Vector3(2f), flags: Caster));
                    return snapshot;
                }

                Capture reference = Gpu.RenderAndCapture(renderer, Scene(groundCasts: false), $"acne-{sunElevationDegrees:F0}-{name}-reference");
                Capture image = Gpu.RenderAndCapture(renderer, Scene(groundCasts: true), $"acne-{sunElevationDegrees:F0}-{name}");
                Assert.Equal(4, renderer.LastFrameStats.ShadowCascades);

                int ground = 0, differing = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float expected = reference.Luma(x, y);
                        if (expected < 0.15f)
                            continue;
                        ground++;
                        if (MathF.Abs(image.Luma(x, y) - expected) > 2.5f / 255f)
                            differing++;
                    }
                }
                Assert.True(ground > width * height / 3, $"{name}: only {ground} lit ground pixels");
                Assert.True(differing <= ground / 1000, $"{name}: {differing} of {ground} lit ground pixels changed when the ground casts shadows");

                // And the lit ground is smooth: small variance in strips across the screen at several distances.
                for (int row = height / 2 + 10; row < height - 4; row += 40)
                {
                    float sum = 0, sumSquares = 0;
                    int count = 0;
                    for (int y = row - 2; y <= row + 2; y++)
                    {
                        for (int x = width / 2; x < width - 40; x++)
                        {
                            float luma = image.Luma(x, y);
                            sum += luma;
                            sumSquares += luma * luma;
                            count++;
                        }
                    }
                    float mean = sum / count, variance = sumSquares / count - mean * mean;
                    Assert.True(mean > 0.15f, $"{name}: row {row} is not lit ground ({mean})");
                    Assert.True(variance < 1e-4f, $"{name}: variance {variance} on lit ground at row {row}");
                }
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ShadowIsContinuousAcrossCascadeSeamsAndDoesNotCreepOntoTheSunlitSide()
    {
        const int width = 960, height = 540;
        const float fov = 50f * MathF.PI / 180f;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            Kit kit = Kit.Create(renderer);
            // A wall 16 m tall and 4 m thick runs straight away from 20 m to 440 m, 6 m to the right of the camera.
            // The sun, 30 degrees up from +X, throws a 28 m shadow to its left, through the cascade seams at about
            // 51 m, 109 m and 277 m. Every sample is compared with the same pixel rendered without the wall.
            float elevation = 30f * MathF.PI / 180f;
            var sun = new Vector3(MathF.Cos(elevation), MathF.Sin(elevation), 0f);
            const float wallHeight = 16f;

            (Capture Image, Capture Reference) Render(string name, float cameraHeight, float pitch, float wallX)
            {
                Quaternion camera = Gpu.PitchDown(pitch);
                RenderSnapshot Scene(bool wall)
                {
                    RenderSnapshot snapshot = Gpu.Snapshot(sun);
                    snapshot.Camera.Rotation = camera;
                    snapshot.Camera.PreviousRotation = camera;
                    snapshot.Camera.VerticalFov = fov;
                    snapshot.Instances.Add(Gpu.Instance(kit.Plane, kit.Ground, new Vector3(0, -cameraHeight, -400f), new Vector3(800f, 1, 800f), flags: Caster));
                    if (wall)
                        snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, new Vector3(wallX, -cameraHeight + wallHeight / 2, -230f), new Vector3(4f, wallHeight, 420f), flags: Caster));
                    return snapshot;
                }
                return (Gpu.RenderAndCapture(renderer, Scene(true), name), Gpu.RenderAndCapture(renderer, Scene(false), name + "-reference"));
            }

            // From 20 m up, looking 12 degrees down, with the wall to the right: the shadow side, from 30 m to 420 m.
            Quaternion low = Gpu.PitchDown(12f * MathF.PI / 180f);
            const float rightWall = 8f;
            float shadowCenterX = rightWall - 2f - 0.5f * wallHeight / MathF.Tan(elevation);
            (Capture image, Capture reference) = Render("shadow-seams", 20f, 12f * MathF.PI / 180f, rightWall);
            int seamsCrossed = 0;
            float previousDepth = 0;
            for (float z = 30f; z <= 420f; z += 2.5f)
            {
                var point = new Vector3(shadowCenterX, -20f, -z);
                Vector2 pixel = Gpu.Project(point, low, width, height, fov);
                float depth = Gpu.ViewDepth(point, low);
                for (int i = 0; i < ShadowCascades.Count - 1; i++)
                    seamsCrossed += previousDepth < ShadowCascades.Split(i) && depth >= ShadowCascades.Split(i) ? 1 : 0;
                previousDepth = depth;
                Assert.True(image.Luma(pixel) < 0.5f * reference.Luma(pixel), $"at view depth {depth:F1} m the shadow {image.Luma(pixel)} vs no wall {reference.Luma(pixel)}");
            }
            Assert.Equal(3, seamsCrossed);

            // From 60 m up, looking 25 degrees down, with the wall to the left so that its sunlit face looks at the
            // camera: the ground in front of that face is as bright as without the wall, in the coarse last cascade
            // too. A receiver-plane bias that also extrapolated downhill would let the kernel find the wall there and
            // darken several meters of it.
            Quaternion high = Gpu.PitchDown(25f * MathF.PI / 180f);
            const float leftWall = -8f;
            (Capture side, Capture sideReference) = Render("shadow-sunlit-side", 60f, 25f * MathF.PI / 180f, leftWall);
            foreach (float z in (float[])[80f, 150f, 250f, 320f, 400f])
            {
                foreach (float distance in (float[])[1.5f, 3f, 6f])
                {
                    var point = new Vector3(leftWall + 2f + distance, -60f, -z);
                    Vector2 pixel = Gpu.Project(point, high, width, height, fov);
                    float lit = side.Luma(pixel), expected = sideReference.Luma(pixel);
                    Assert.True(MathF.Abs(lit - expected) < 0.01f,
                        $"{distance} m in front of the sunlit face at view depth {Gpu.ViewDepth(point, high):F0} m: {lit} vs no wall {expected}");
                }
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void CastersAreInterpolatedLikeEverythingElse()
    {
        const int width = 640, height = 360;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            Kit kit = Kit.Create(renderer);
            var sun = new Vector3(0f, 1f, 0.3f);
            Quaternion camera = Gpu.PitchDown(0.5f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            const float ground = -4f;
            snapshot.Instances.Add(Gpu.Instance(kit.Plane, kit.Ground, new Vector3(0, ground, -10f), new Vector3(20f, 1, 20f), flags: Caster));
            // An invisible caster: only its shadow shows where it is. It moved from x = -3 to x = +3 during the tick.
            RenderInstance pole = Gpu.Instance(kit.Box, kit.White, new Vector3(3f, ground + 1.5f, -8f), new Vector3(1f, 3f, 1f), flags: Caster);
            pole.Flags = Caster;
            pole.PreviousPosition = new Vector3(-3f, ground + 1.5f, -8f);
            snapshot.Instances.Add(pole);

            Capture start = Gpu.RenderAndCapture(renderer, snapshot, "shadow-caster-alpha0", alpha: 0f);
            Capture end = Gpu.RenderAndCapture(renderer, snapshot, "shadow-caster-alpha1", alpha: 1f);
            Assert.Equal(1, renderer.LastFrameStats.VisibleInstances);
            Assert.Equal(2, renderer.LastFrameStats.ShadowCasters);
            Vector2 Pixel(float x) => Gpu.Project(ShadowOnGround(new Vector3(x, ground + 1.5f, -8f), sun, ground), camera, width, height);
            Assert.True(start.Luma(Pixel(-3f)) < 0.5f * end.Luma(Pixel(-3f)), "alpha 0: the shadow is at the previous position");
            Assert.True(end.Luma(Pixel(3f)) < 0.5f * start.Luma(Pixel(3f)), "alpha 1: the shadow is at the current position");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void CastersAreSelectedByDistanceFromTheCameraAndCulledPerCascade()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        try
        {
            Kit kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.2f, 1f, 0.1f));
            RenderInstance Box(Vector3 position, RenderFlags flags)
            {
                RenderInstance instance = Gpu.Instance(kit.Box, kit.White, position, Vector3.One);
                instance.Flags = flags;
                return instance;
            }
            snapshot.Instances.Add(Box(new Vector3(0, 0, -10), RenderFlags.Visible | Caster));          // selected
            snapshot.Instances.Add(Box(new Vector3(2, 0, -10), Caster));                                // invisible but casting: selected
            snapshot.Instances.Add(Box(new Vector3(-2, 0, -10), RenderFlags.Visible));                  // not a caster
            snapshot.Instances.Add(Box(new Vector3(0, 0, 15), RenderFlags.Visible | Caster));           // behind the camera: selected
            snapshot.Instances.Add(Box(new Vector3(0, -50, -1_000), RenderFlags.Visible | Caster));     // 1 km: only the last cascade
            snapshot.Instances.Add(Box(new Vector3(0, 0, -2_100), RenderFlags.Visible | Caster));       // beyond 2 km: not selected
            RenderInstance large = Box(new Vector3(0, 0, -2_100), RenderFlags.Visible | Caster);        // its sphere reaches within 2 km
            large.Scale = new Vector3(300f);
            snapshot.Instances.Add(large);

            renderer.Render(snapshot, 1f);
            RenderStats stats = renderer.LastFrameStats;
            Assert.Equal(5, stats.ShadowCasters);
            Assert.Equal(4, stats.ShadowCascades);
            Assert.Equal(stats.ShadowInstances, Enumerable.Range(0, ShadowCascades.Count).Sum(i => CountInstances(renderer.LastScene, i)));

            // Which cascades hold the caster at 1 km: the last one only.
            PreparedScene scene = renderer.LastScene;
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                bool holds = InstancesOf(scene, i).Any(instance => MathF.Abs(instance.ModelRow2.W + 1_000f) < 1f);
                Assert.Equal(i == ShadowCascades.Count - 1, holds);
            }
            // The near caster is in the first cascade, and the shadow draws are one indirect draw per cascade.
            Assert.Contains(InstancesOf(scene, 0), instance => MathF.Abs(instance.ModelRow2.W + 10f) < 0.01f);
            int shadowDraws = Enumerable.Range(0, ShadowCascades.Count).Count(i => scene.ShadowCommands[i].Count > 0);
            int rangeDraws = (stats.NearCommands > 0 ? 1 : 0) + (stats.MiddleCommands > 0 ? 1 : 0) + (stats.FarCommands > 0 ? 1 : 0);
            Assert.Equal(ShadowCascades.Count, shadowDraws);
            Assert.Equal(rangeDraws + shadowDraws, stats.IndirectDrawCalls);
            Assert.Equal(stats.NearCommands + stats.MiddleCommands + stats.FarCommands + stats.ShadowCommands, stats.IndirectCommands);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithoutASunOrACasterNoShadowMapIsDrawn()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        try
        {
            Kit kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, new Vector3(0, 0, -5), Vector3.One));
            renderer.Render(snapshot, 1f);
            Assert.Equal(0, renderer.LastFrameStats.ShadowCascades);
            Assert.Equal(1, renderer.LastFrameStats.IndirectDrawCalls);
            Assert.Equal(0u, renderer.LastScene.Constants.ShadowCascadeCount);

            snapshot.Instances[0] = Gpu.Instance(kit.Box, kit.White, new Vector3(0, 0, -5), Vector3.One, flags: Caster);
            renderer.Render(snapshot, 1f);
            Assert.Equal(4, renderer.LastFrameStats.ShadowCascades);
            Assert.Equal((uint)ShadowCascades.Count, renderer.LastScene.Constants.ShadowCascadeCount);

            snapshot.Sun.Intensity = 0f;
            renderer.Render(snapshot, 1f);
            Assert.Equal(0, renderer.LastFrameStats.ShadowCascades);
            Assert.Equal(0, renderer.LastFrameStats.ShadowCasters);
            Assert.Equal(1, renderer.LastFrameStats.IndirectDrawCalls);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void CascadesKeepTheirWorldTexelGridWhileTheCameraMovesAndTurns()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180);
        try
        {
            Kit kit = Kit.Create(renderer);
            int size = renderer.ShadowMap.Size;
            // Absolute world positions of the camera on five ticks, moving by amounts that are not whole texels.
            Vector3[] cameraPositions =
            [
                new(0f, 0f, 0f),
                new(0.123f, 0.0456f, -0.789f),
                new(1.2345f, -0.0123f, -3.4567f),
                new(10.01f, 0.5f, -20.7f),
                new(-37.3f, 2.25f, -61.05f),
            ];
            Quaternion[] rotations =
            [
                Quaternion.Identity,
                Quaternion.Identity,
                Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0f),
                Quaternion.CreateFromYawPitchRoll(-1.1f, 0.3f, 0.1f),
                Quaternion.CreateFromYawPitchRoll(2.5f, -0.5f, 0f),
            ];
            Vector3[] worldPoints = [new(3.3f, -1.7f, -12.9f), new(-25.1f, -2f, -48.2f), new(140.7f, -2f, -305.3f)];
            float[]? texels = null;
            Vector2[,]? fractions = null;

            for (int tick = 0; tick < cameraPositions.Length; tick++)
            {
                Vector3 eye = cameraPositions[tick];
                RenderSnapshot snapshot = Gpu.Snapshot(new Vector3(0.4f, 0.8f, -0.3f));
                snapshot.Tick = tick + 1;
                snapshot.Camera.Rotation = rotations[tick];
                snapshot.Camera.PreviousRotation = rotations[tick];
                snapshot.Camera.PreviousPosition = tick == 0 ? Vector3.Zero : cameraPositions[tick - 1] - eye;
                snapshot.Instances.Add(Gpu.Instance(kit.Box, kit.White, new Vector3(0, -2, -10) - eye, Vector3.One, flags: Caster));

                // Interpolated frames inside the tick must agree too: alpha moves the eye, not the grid.
                foreach (float alpha in (float[])[0.37f, 1f])
                {
                    renderer.Render(snapshot, alpha);
                    PreparedScene scene = renderer.LastScene;
                    Assert.Equal(4, scene.ShadowCascadeCount);
                    Vector3 offset = scene.Camera.Offset;
                    texels ??= Enumerable.Range(0, ShadowCascades.Count).Select(i => scene.Cascades[i].TexelSize).ToArray();
                    fractions ??= new Vector2[ShadowCascades.Count, worldPoints.Length];
                    for (int i = 0; i < ShadowCascades.Count; i++)
                    {
                        // Turning the camera never changes the texel size.
                        Assert.Equal(texels[i], scene.Cascades[i].TexelSize);
                        for (int p = 0; p < worldPoints.Length; p++)
                        {
                            Vector4 clip = Vector4.Transform(new Vector4(worldPoints[p] - eye - offset, 1f), scene.Cascades[i].ViewProjection);
                            var texel = new Vector2(clip.X * 0.5f + 0.5f, clip.Y * 0.5f + 0.5f) * size;
                            var fraction = new Vector2(texel.X - MathF.Floor(texel.X), texel.Y - MathF.Floor(texel.Y));
                            if (tick == 0 && alpha == 0.37f)
                            {
                                fractions[i, p] = fraction;
                                continue;
                            }
                            Vector2 expected = fractions[i, p];
                            // Compare on the circle: 0.999 and 0.001 are 0.002 apart.
                            float dx = MathF.Abs(fraction.X - expected.X), dy = MathF.Abs(fraction.Y - expected.Y);
                            Assert.True(MathF.Min(dx, 1 - dx) < 0.01f && MathF.Min(dy, 1 - dy) < 0.01f,
                                $"tick {tick + 1}, alpha {alpha}, cascade {i}, point {p}: texel fraction {fraction} vs {expected}");
                        }
                    }
                }
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheReferenceSceneCastsShadowsAwayFromTheSun()
    {
        const int width = 1280, height = 720;
        VulkanRenderer renderer = Gpu.Headless(width, height);
        try
        {
            var scene = TestScene.Create(renderer);
            Capture shadowed = Gpu.RenderAndCapture(renderer, scene.Snapshot(), "scene");
            Capture unshadowed = Gpu.RenderAndCapture(renderer, scene.Snapshot(shadows: false), "scene-without-shadow-maps");
            // The ground under the middle of the tall box's shadow: from there, the sun is behind the box.
            Vector3 ground = TestScene.TallBox + new Vector3(0, -1f, 0) - TestScene.SunDirection * (1f / TestScene.SunDirection.Y);
            Vector2 pixel = Gpu.Project(ground, width, height);
            Assert.True(shadowed.MeanLuma(pixel, 2) < 0.6f * unshadowed.MeanLuma(pixel, 2),
                $"behind the tall box: {shadowed.MeanLuma(pixel, 2)} with shadow maps vs {unshadowed.MeanLuma(pixel, 2)} without");
            // On the sunward side of the box the ground is the same with and without shadow maps.
            Vector2 sunward = Gpu.Project(TestScene.TallBox + new Vector3(1.5f, -1f, -1.2f), width, height);
            Assert.True(MathF.Abs(shadowed.MeanLuma(sunward, 1) - unshadowed.MeanLuma(sunward, 1)) < 0.01f, "the sunward side is lit");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static int CountInstances(PreparedScene scene, int cascade) => InstancesOf(scene, cascade).Count();

    private static IEnumerable<GpuInstance> InstancesOf(PreparedScene scene, int cascade)
    {
        CommandRange range = scene.ShadowCommands[cascade];
        for (int c = range.First; c < range.First + range.Count; c++)
        {
            Silk.NET.Vulkan.DrawIndexedIndirectCommand command = scene.Commands[c];
            for (uint k = 0; k < command.InstanceCount; k++)
                yield return scene.Instances[command.FirstInstance + k];
        }
    }
}
