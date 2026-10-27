using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

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

        public static Kit Create(VulkanRenderer renderer) =>
            new()
            {
                Box = renderer.CreateMesh(Primitives.Box(new(0.5f))),
                Plane = renderer.CreateMesh(Primitives.Plane(1f)),
                Sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                Ground = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.6f, y: 0.6f, z: 0.6f, w: 1), roughness: 0.7f) with
                    {
                        Reflectance = 0.3f,
                    }
                ),
                White = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.55f)
                ),
            };
    }

    /// <summary>The ground point that the sun direction <paramref name="sun"/> projects <paramref name="point"/> onto.</summary>
    private static Vector3 ShadowOnGround(Vector3 point, Vector3 sun, float groundY)
    {
        var l = Vector3.Normalize(sun);
        return point - l * ((point.Y - groundY) / l.Y);
    }

    [Fact]
    public void BoxCastsAShadowOnTheGroundAndTheEclipseTermStillApplies()
    {
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            // The sun is up and to the right, so the box's shadow falls to its left.
            var sun = new Vector3(0.6f, 0.8f, 0f);
            Quaternion camera = Gpu.PitchDown(0.25f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            const float ground = -3f;
            var box = new Vector3(0, ground + 0.75f, -8);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Plane,
                    material: kit.Ground,
                    position: new(0, ground, -15),
                    scale: new(30, 1, 30),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // Turned so that its sunlit +X face looks toward the camera.
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Box,
                    material: kit.White,
                    position: box,
                    scale: new(1.5f),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // The analytic term: an invisible sphere of 20 m radius 2.2 km toward the sun from the ground at (28, ground, -10).
            // It stays farther than 2 km from the camera, so it casts a far shadow, an ellipse on the ground 25 m along x and
            // 20 m along z. Its edge passes between the box, which stays lit, and the patch at x = 4, which it covers. A sun
            // with almost no disc keeps that edge sharp; the disc plays no part in the shadow maps.
            Vector3 eclipsed = new(4f, ground, -10f);
            Gpu.Star(snapshot: snapshot, angularRadius: 0f);
            snapshot.Instances.Add(
                RenderInstance.Create(
                    mesh: kit.Sphere,
                    material: kit.White,
                    position: new Vector3(28f, ground, -10f) + Vector3.Normalize(sun) * 2_200f,
                    rotation: Quaternion.Identity,
                    scale: new(20f),
                    boundingRadius: 0,
                    hemisphere: HemisphereUp.Even,
                    chainDepth: TestChains.CamerasFrame,
                    flags: RenderFlags.CastsShadow,
                    entityIndex: 0,
                    part: 0
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "shadow-box");
            RenderStats stats = renderer.LastFrameStats;
            stats.ShadowCascades.Should().Be(4);
            stats.ShadowCasters.Should().Be(2);

            Vector2 Pixel(Vector3 p) => Gpu.Project(point: p, cameraRotation: camera, width: width, height: height);
            // The top's shadow lands 1.1 m left of the box center; a little further left it is clear of the box itself.
            Vector3 shadowCenter =
                ShadowOnGround(box + new Vector3(0, 0.75f, 0), sun, ground) - new Vector3(0.25f, 0, 0);
            float shadowed = image.MeanLuma(Pixel(shadowCenter), 2);
            float lit = image.MeanLuma(Pixel(new(-shadowCenter.X, ground, shadowCenter.Z)), 2);
            float litFar = image.MeanLuma(Pixel(new(-3f, ground, -12f)), 2);
            float top = image.MeanLuma(Pixel(box + new Vector3(0, 0.75f, 0)), 2);
            float occluded = image.MeanLuma(Pixel(eclipsed), 2);
            lit.Should().BeGreaterThan(0.35f, "the ground beside the box is in the sun");
            shadowed.Should().BeLessThan(0.5f * lit, "the box shadows the ground beside it");
            shadowed.Should().BeLessThan(0.5f * litFar, "the shadow is darker than lit ground further back");
            top.Should().BeGreaterThan(lit, "the box's top faces the sun more than the ground does");
            occluded
                .Should()
                .BeLessThan(0.5f * lit, "the far sphere's analytic shadow multiplies with the shadow maps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnObjectInsideTheBoundingSphereOfAFarCasterIsNotShadowedByIt()
    {
        const int width = 640,
            height = 360;
        const float fov = 0.02f;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            // The sun is up and toward the camera. An invisible caster that is not round, 1 km in radius and 5 km away,
            // holds a 20 m box 500 m below its center, so the caster's center lies toward the sun from the box. A narrow view
            // looks straight at the box.
            var sun = new Vector3(0, 1, 1);
            var box = new Vector3(0, -500, -5_000);
            Quaternion camera = Gpu.PitchDown(MathF.Atan2(500, 5_000));
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Box,
                    material: kit.White,
                    position: box,
                    scale: new(10f),
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Instances.Add(
                RenderInstance.Create(
                    mesh: kit.Sphere,
                    material: kit.White,
                    position: new(0, 0, -5_000),
                    rotation: Quaternion.Identity,
                    scale: new(1_000f),
                    boundingRadius: 0,
                    hemisphere: HemisphereUp.Even,
                    chainDepth: TestChains.CamerasFrame,
                    flags: Caster,
                    entityIndex: 0,
                    part: 0
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "far-caster-inside");

            // The box's face toward the camera faces the sun at 45 degrees, so the sun lights it.
            float face = image.MeanLuma(
                Gpu.Project(
                    point: box + new Vector3(0, 0, 5f),
                    cameraRotation: camera,
                    width: width,
                    height: height,
                    fov: fov
                ),
                2
            );
            face.Should().BeGreaterThan(0.3f, "a bounding sphere does not shadow what lies inside it");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACasterInTheShadowMapsAlsoShadowsTheGroundBeyondThemWithItsSphere()
    {
        const int width = 960,
            height = 540;
        const float fov = 40f * MathF.PI / 180f;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            // The camera is 300 m up. The sun is 5 degrees up, behind the camera and to its right. A sphere of 150 m radius,
            // 1.5 km away, stands 150 m above the ground, so it is in the shadow maps. Its shadow falls 1.7 km from it, to the
            // left and away from the camera, about 2.7 km deep: beyond the maps.
            float elevation = 5f * MathF.PI / 180f,
                across = MathF.Cos(elevation) * MathF.Sqrt(0.5f);
            var sun = new Vector3(across, MathF.Sin(elevation), across);
            const float ground = -300f;
            var center = new Vector3(0, ground + 150f, -1_500f);
            Vector3 shadow = center - Vector3.Normalize(sun) * (150f / MathF.Sin(elevation));
            Quaternion camera = Gpu.PitchDown(6f * MathF.PI / 180f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            // No ambient light, so that the low sun alone lights the ground and the shadow shows plainly.
            snapshot.Ambient = default;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Plane,
                    material: kit.Ground,
                    position: new(0, ground, -3_000f),
                    scale: new(3_000f, 1, 3_000f),
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Sphere,
                    material: kit.White,
                    position: center,
                    scale: new(150f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "mapped-caster-far-shadow"
            );

            Vector2 Pixel(Vector3 p) =>
                Gpu.Project(point: p, cameraRotation: camera, width: width, height: height, fov: fov);

            Gpu.ViewDepth(shadow, camera)
                .Should()
                .BeGreaterThan(ShadowCascades.Distance, "the shadow lies beyond the maps");
            float shadowed = image.MeanLuma(Pixel(shadow), 2);
            float lit = image.MeanLuma(Pixel(new(600f, ground, shadow.Z)), 2);
            lit.Should().BeGreaterThan(0.03f, "the ground away from the shadow is in the low sun");
            shadowed.Should().BeLessThan(0.1f * lit, "the sphere shadows the ground beyond the maps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheLastCascadeStillShadowsAtOnePointFiveKilometres()
    {
        const int width = 960,
            height = 540;
        const float fov = MathF.PI / 6f;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            // The camera looks 20 degrees down at a 40 m box 1.5 km away. The sun is 45 degrees up from +X.
            var sun = new Vector3(1f, 1f, 0f);
            Quaternion camera = Gpu.PitchDown(20f * MathF.PI / 180f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            float ground = -1_500f * MathF.Tan(20f * MathF.PI / 180f);
            var box = new Vector3(0, ground + 20f, -1_500f);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Plane,
                    material: kit.Ground,
                    position: new(0, ground, -1_500f),
                    scale: new(2_500f, 1, 2_500f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Box,
                    material: kit.White,
                    position: box,
                    scale: new(40f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "shadow-1500m");
            renderer.LastFrameStats.ShadowCascades.Should().Be(4);

            // The shadow covers the ground from 20 m to 60 m left of the box center.
            var shadowPoint = new Vector3(-40f, ground, -1_500f);
            var litPoint = new Vector3(40f, ground, -1_500f);
            float depth = Gpu.ViewDepth(shadowPoint, camera);
            depth
                .Should()
                .BeInRange(ShadowCascades.Split(2), ShadowCascades.BlendStart(3), "the sample is in the last cascade");
            float shadowed = image.MeanLuma(
                Gpu.Project(point: shadowPoint, cameraRotation: camera, width: width, height: height, fov: fov),
                2
            );
            float lit = image.MeanLuma(
                Gpu.Project(point: litPoint, cameraRotation: camera, width: width, height: height, fov: fov),
                2
            );
            lit.Should().BeGreaterThan(0.3f, "the ground on the sunward side is lit");
            shadowed.Should().BeLessThan(0.5f * lit, "the last cascade shadows at {0:F0} m", depth);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ShadowMapsEndAtTwoKilometres()
    {
        const int width = 960,
            height = 540;
        const float fov = 40f * MathF.PI / 180f;
        const float pitch = 10f * MathF.PI / 180f;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            // A 40 m wall runs straight away from the camera from 1.5 km to 3 km. The sun, 30 degrees up from +X,
            // throws its shadow 69 m to the left over its whole length. The camera is 300 m up, looking 10 degrees down.
            var sun = new Vector3(MathF.Cos(MathF.PI / 6f), MathF.Sin(MathF.PI / 6f), 0f);
            Quaternion camera = Gpu.PitchDown(pitch);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            snapshot.Camera.VerticalFov = fov;
            const float ground = -300f;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Plane,
                    material: kit.Ground,
                    position: new(0, ground, -2_500f),
                    scale: new(3_000f, 1, 3_000f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // Its bounding sphere reaches within 1 km of the camera, so it is a caster.
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Box,
                    material: kit.White,
                    position: new(0, ground + 20f, -2_250f),
                    scale: new(10f, 40f, 1_500f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // A second wall entirely beyond 2 km is not in the shadow maps. It casts a far shadow instead: the sphere of its
            // bounding radius, 173 m.
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Box,
                    material: kit.White,
                    position: new(-600f, ground + 20f, -2_600f),
                    scale: new(10f, 40f, 200f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "shadow-2km-cutoff");
            renderer.LastFrameStats.ShadowCasters.Should().Be(2, "the wall beyond 2 km is not in the shadow maps");

            float Luma(Vector3 p) =>
                image.MeanLuma(
                    Gpu.Project(point: p, cameraRotation: camera, width: width, height: height, fov: fov),
                    1
                );

            foreach (float z in (float[])[1_550f, 1_600f, 1_650f, 1_700f, 1_750f])
            {
                var shadowPoint = new Vector3(-40f, ground, -z);
                float depth = Gpu.ViewDepth(shadowPoint, camera);
                depth.Should().BeLessThan(ShadowCascades.BlendStart(3), "the sample is inside the maps");
                float shadowed = Luma(shadowPoint),
                    lit = Luma(new(40f, ground, -z));
                shadowed.Should().BeLessThan(0.5f * lit, "the wall shadows the ground at {0:F0} m", depth);
            }

            foreach (float z in (float[])[2_050f, 2_200f, 2_400f, 2_600f, 2_800f, 2_950f])
            {
                var shadowPoint = new Vector3(-40f, ground, -z);
                float depth = Gpu.ViewDepth(shadowPoint, camera);
                depth.Should().BeGreaterThan(ShadowCascades.Distance, "the sample is beyond the maps");
                float shadowed = Luma(shadowPoint),
                    lit = Luma(new(40f, ground, -z));
                MathF
                    .Abs(shadowed - lit)
                    .Should()
                    .BeLessThan(
                        0.02f,
                        "at {0:F0} m, beyond the maps, nothing is shadowed ({1} vs {2})",
                        depth,
                        shadowed,
                        lit
                    );
                // 300 m left of the second wall: outside its bounding sphere, inside that sphere's shadow.
                float beside = Luma(new(-900f, ground, -2_600f));
                beside
                    .Should()
                    .BeLessThan(
                        0.5f * lit,
                        "the far wall shadows the ground beside it as a sphere ({0} vs {1})",
                        beside,
                        lit
                    );
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
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            float elevation = sunElevationDegrees * MathF.PI / 180f,
                azimuth = 140f * MathF.PI / 180f;
            var sun = new Vector3(
                MathF.Sin(azimuth) * MathF.Cos(elevation),
                MathF.Sin(elevation),
                -MathF.Cos(azimuth) * MathF.Cos(elevation)
            );

            // A low and a high view cover every cascade. In the reference the ground casts no shadow, so its lit
            // pixels cannot have acne; with self-shadowing they must come out the same.
            foreach (
                (string name, float heightAboveGround, float pitch) in (ReadOnlySpan<(
                    string Name,
                    float HeightAboveGround,
                    float Pitch
                )>)
                    [("low", 2f, 0.3f), ("high", 120f, 0.35f)]
            )
            {
                Quaternion camera = Gpu.PitchDown(pitch);

                RenderSnapshot Scene(bool groundCasts)
                {
                    RenderSnapshot snapshot = Gpu.Snapshot(sun);
                    snapshot.Camera.Rotation = camera;
                    snapshot.Camera.PreviousRotation = camera;
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: kit.Plane,
                            material: kit.Ground,
                            position: new(0, -heightAboveGround, -1_000f),
                            scale: new(1_500f, 1, 1_500f),
                            flags: groundCasts ? Caster : RenderFlags.None,
                            chainDepth: TestChains.CamerasFrame
                        )
                    );
                    // Something else casts, so the cascades are drawn in both images.
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: kit.Box,
                            material: kit.White,
                            position: new(-40f, -heightAboveGround + 1f, -30f),
                            scale: new(2f),
                            flags: Caster,
                            chainDepth: TestChains.CamerasFrame
                        )
                    );
                    return snapshot;
                }

                Capture reference = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: Scene(groundCasts: false),
                    name: $"acne-{sunElevationDegrees:F0}-{name}-reference"
                );
                Capture image = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: Scene(groundCasts: true),
                    name: $"acne-{sunElevationDegrees:F0}-{name}"
                );
                renderer.LastFrameStats.ShadowCascades.Should().Be(4);

                int ground = 0,
                    differing = 0;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float expected = reference.Luma(x, y);
                    if (expected < 0.15f)
                        continue;
                    ground++;
                    if (MathF.Abs(image.Luma(x, y) - expected) > 2.5f / 255f)
                        differing++;
                }

                ground.Should().BeGreaterThan(width * height / 3, "the {0} view is mostly lit ground", name);
                differing
                    .Should()
                    .BeLessThanOrEqualTo(
                        ground / 1000,
                        "in the {0} view, lit ground must not change when it casts shadows itself ({1} pixels)",
                        name,
                        ground
                    );

                // And the lit ground is smooth: small variance in strips across the screen at several distances.
                for (int row = height / 2 + 10; row < height - 4; row += 40)
                {
                    float sum = 0,
                        sumSquares = 0;
                    int count = 0;
                    for (int y = row - 2; y <= row + 2; y++)
                    for (int x = width / 2; x < width - 40; x++)
                    {
                        float luma = image.Luma(x, y);
                        sum += luma;
                        sumSquares += luma * luma;
                        count++;
                    }

                    float mean = sum / count,
                        variance = sumSquares / count - mean * mean;
                    mean.Should().BeGreaterThan(0.15f, "row {0} of the {1} view is lit ground", row, name);
                    variance.Should().BeLessThan(1e-4f, "lit ground at row {0} of the {1} view is smooth", row, name);
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
        const int width = 960,
            height = 540;
        const float fov = 50f * MathF.PI / 180f;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
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
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: kit.Plane,
                            material: kit.Ground,
                            position: new(0, -cameraHeight, -400f),
                            scale: new(800f, 1, 800f),
                            flags: Caster,
                            chainDepth: TestChains.CamerasFrame
                        )
                    );
                    if (wall)
                        snapshot.Instances.Add(
                            Gpu.Instance(
                                mesh: kit.Box,
                                material: kit.White,
                                position: new(wallX, -cameraHeight + wallHeight / 2, -230f),
                                scale: new(4f, wallHeight, 420f),
                                flags: Caster,
                                chainDepth: TestChains.CamerasFrame
                            )
                        );
                    return snapshot;
                }

                return (
                    Gpu.RenderAndCapture(renderer: renderer, snapshot: Scene(true), name: name),
                    Gpu.RenderAndCapture(renderer: renderer, snapshot: Scene(false), name: name + "-reference")
                );
            }

            // From 20 m up, looking 12 degrees down, with the wall to the right: the shadow side, from 30 m to 420 m.
            Quaternion low = Gpu.PitchDown(12f * MathF.PI / 180f);
            const float rightWall = 8f;
            float shadowCenterX = rightWall - 2f - 0.5f * wallHeight / MathF.Tan(elevation);
            (Capture image, Capture reference) = Render(
                name: "shadow-seams",
                cameraHeight: 20f,
                pitch: 12f * MathF.PI / 180f,
                wallX: rightWall
            );
            int seamsCrossed = 0;
            float previousDepth = 0;
            for (float z = 30f; z <= 420f; z += 2.5f)
            {
                var point = new Vector3(shadowCenterX, -20f, -z);
                Vector2 pixel = Gpu.Project(point: point, cameraRotation: low, width: width, height: height, fov: fov);
                float depth = Gpu.ViewDepth(point, low);
                for (int i = 0; i < ShadowCascades.Count - 1; i++)
                    seamsCrossed += previousDepth < ShadowCascades.Split(i) && depth >= ShadowCascades.Split(i) ? 1 : 0;
                previousDepth = depth;
                image
                    .Luma(pixel)
                    .Should()
                    .BeLessThan(
                        0.5f * reference.Luma(pixel),
                        "the wall's shadow continues at view depth {0:F1} m",
                        depth
                    );
            }

            seamsCrossed.Should().Be(3, "the samples cross every seam between cascades");

            // From 60 m up, looking 25 degrees down, with the wall to the left so that its sunlit face looks at the
            // camera: the ground in front of that face is as bright as without the wall, in the coarse last cascade
            // too. A receiver-plane bias that also extrapolated downhill would let the kernel find the wall there and
            // darken several meters of it.
            Quaternion high = Gpu.PitchDown(25f * MathF.PI / 180f);
            const float leftWall = -8f;
            (Capture side, Capture sideReference) = Render(
                name: "shadow-sunlit-side",
                cameraHeight: 60f,
                pitch: 25f * MathF.PI / 180f,
                wallX: leftWall
            );
            foreach (float z in (float[])[80f, 150f, 250f, 320f, 400f])
            foreach (float distance in (float[])[1.5f, 3f, 6f])
            {
                var point = new Vector3(leftWall + 2f + distance, -60f, -z);
                Vector2 pixel = Gpu.Project(point: point, cameraRotation: high, width: width, height: height, fov: fov);
                float lit = side.Luma(pixel),
                    expected = sideReference.Luma(pixel);
                MathF
                    .Abs(lit - expected)
                    .Should()
                    .BeLessThan(
                        0.01f,
                        "{0} m in front of the sunlit face at view depth {1:F0} m the ground is as bright as without the wall ({2} vs {3})",
                        distance,
                        Gpu.ViewDepth(point, high),
                        lit,
                        expected
                    );
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
        const int width = 640,
            height = 360;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var kit = Kit.Create(renderer);
            var sun = new Vector3(0f, 1f, 0.3f);
            Quaternion camera = Gpu.PitchDown(0.5f);
            RenderSnapshot snapshot = Gpu.Snapshot(sun);
            snapshot.Camera.Rotation = camera;
            snapshot.Camera.PreviousRotation = camera;
            const float ground = -4f;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Plane,
                    material: kit.Ground,
                    position: new(0, ground, -10f),
                    scale: new(20f, 1, 20f),
                    flags: Caster,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            // An invisible caster (CastsShadow without Visible): only its shadow shows where it is. It moved from
            // x = -3 to x = +3 during the tick.
            var pole = RenderInstance.Create(
                mesh: kit.Box,
                material: kit.White,
                position: new(3f, ground + 1.5f, -8f),
                rotation: Quaternion.Identity,
                scale: new(1f, 3f, 1f),
                boundingRadius: 0f,
                hemisphere: HemisphereUp.Along(Vector3.UnitY),
                chainDepth: TestChains.CamerasFrame,
                flags: Caster
            );
            pole.PreviousPosition = new(-3f, ground + 1.5f, -8f);
            snapshot.Instances.Add(pole);

            Capture start = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "shadow-caster-alpha0",
                alpha: 0f
            );
            Capture end = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "shadow-caster-alpha1",
                alpha: 1f
            );
            renderer.LastFrameStats.VisibleInstances.Should().Be(1, "the pole is not Visible");
            renderer.LastFrameStats.ShadowCasters.Should().Be(2, "the pole casts a shadow all the same");

            Vector2 Pixel(float x) =>
                Gpu.Project(
                    point: ShadowOnGround(new(x, ground + 1.5f, -8f), sun, ground),
                    cameraRotation: camera,
                    width: width,
                    height: height
                );

            start
                .Luma(Pixel(-3f))
                .Should()
                .BeLessThan(0.5f * end.Luma(Pixel(-3f)), "at alpha 0 the shadow is at the previous position");
            end.Luma(Pixel(3f))
                .Should()
                .BeLessThan(0.5f * start.Luma(Pixel(3f)), "at alpha 1 the shadow is at the current position");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void CastersAreSelectedByDistanceFromTheCameraAndCulledPerCascade()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        try
        {
            var kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.2f, 1f, 0.1f));

            RenderInstance Box(Vector3 position, RenderFlags flags) =>
                RenderInstance.Create(
                    mesh: kit.Box,
                    material: kit.White,
                    position: position,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    boundingRadius: 0f,
                    hemisphere: HemisphereUp.Along(Vector3.UnitY),
                    chainDepth: TestChains.CamerasFrame,
                    flags: flags
                );

            snapshot.Instances.Add(Box(new(0, 0, -10), RenderFlags.Visible | Caster)); // selected
            snapshot.Instances.Add(Box(new(2, 0, -10), Caster)); // invisible but casting: selected
            snapshot.Instances.Add(Box(new(-2, 0, -10), RenderFlags.Visible)); // not a caster
            snapshot.Instances.Add(Box(new(0, 0, 15), RenderFlags.Visible | Caster)); // behind the camera: selected
            snapshot.Instances.Add(Box(new(0, -50, -1_000), RenderFlags.Visible | Caster)); // 1 km: only the last cascade
            snapshot.Instances.Add(Box(new(0, 0, -2_100), RenderFlags.Visible | Caster)); // beyond 2 km: not selected
            RenderInstance large = Box(new(0, 0, -2_100), RenderFlags.Visible | Caster); // its sphere reaches within 2 km
            large.Scale = new(300f);
            snapshot.Instances.Add(large);
            RenderInstance meshless = Box(new(1, 0, -10), RenderFlags.Visible | Caster); // MeshId.None: nothing to cast
            meshless.Mesh = MeshId.None;
            snapshot.Instances.Add(meshless);
            RenderInstance flat = Box(new(-1, 0, -10), Caster); // no volume: nothing to cast
            flat.Scale = new(1f, 0f, 1f);
            snapshot.Instances.Add(flat);

            renderer.Render(snapshot, 1f);
            RenderStats stats = renderer.LastFrameStats;
            stats.ShadowCasters.Should().Be(5);
            stats.ShadowCascades.Should().Be(4);
            Enumerable
                .Range(0, ShadowCascades.Count)
                .Sum(i => CountInstances(renderer.LastScene, i))
                .Should()
                .Be(stats.ShadowInstances);

            // Which cascades hold the caster at 1 km: the last one only.
            PreparedScene scene = renderer.LastScene;
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                bool holds = InstancesOf(scene, i).Any(instance => MathF.Abs(instance.ModelRow2.W + 1_000f) < 1f);
                holds
                    .Should()
                    .Be(
                        i == ShadowCascades.Count - 1,
                        "only the last cascade reaches the caster at 1 km (cascade {0})",
                        i
                    );
            }

            // The near caster is in the first cascade, and the shadow draws are one indirect draw per cascade.
            InstancesOf(scene, 0).Should().Contain(instance => MathF.Abs(instance.ModelRow2.W + 10f) < 0.01f);
            int shadowDraws = Enumerable.Range(0, ShadowCascades.Count).Count(i => scene.ShadowCommands[i].Count > 0);
            int rangeDraws =
                (stats.NearCommands > 0 ? 1 : 0) + (stats.MiddleCommands > 0 ? 1 : 0) + (stats.FarCommands > 0 ? 1 : 0);
            shadowDraws.Should().Be(ShadowCascades.Count);
            stats.IndirectDrawCalls.Should().Be(rangeDraws + shadowDraws);
            stats
                .IndirectCommands.Should()
                .Be(stats.NearCommands + stats.MiddleCommands + stats.FarCommands + stats.ShadowCommands);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithoutAStarOrACasterNoShadowMapIsDrawn()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        try
        {
            var kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.2f));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: kit.Box,
                    material: kit.White,
                    position: new(0, 0, -5),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.ShadowCascades.Should().Be(0, "nothing casts a shadow");
            renderer.LastFrameStats.IndirectDrawCalls.Should().Be(1);
            renderer.LastScene.Constants.Counts.Z.Should().Be(0u);

            snapshot.Instances[0] = Gpu.Instance(
                mesh: kit.Box,
                material: kit.White,
                position: new(0, 0, -5),
                scale: Vector3.One,
                flags: Caster,
                chainDepth: TestChains.CamerasFrame
            );
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.ShadowCascades.Should().Be(4);
            renderer.LastScene.Constants.Counts.Z.Should().Be(ShadowCascades.Count);

            // A star of no power, or of a negative power, does not shine, so it has no shadow pass either.
            foreach (float irradiance in (float[])[0f, -2f])
            {
                Gpu.Star(snapshot: snapshot, irradiance: irradiance);
                renderer.Render(snapshot, 1f);
                renderer.LastFrameStats.ShadowCascades.Should().Be(0, "a star of irradiance {0} is off", irradiance);
                renderer.LastFrameStats.ShadowCasters.Should().Be(0);
                renderer.LastFrameStats.IndirectDrawCalls.Should().Be(1);
                renderer.LastScene.LightCount.Should().Be(0);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The fraction of a texel at which a camera-relative point falls in one cascade, in [0, 1)².</summary>
    private static Vector2 TexelFraction(in ShadowCascade cascade, Vector3 point, int size)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), cascade.ViewProjection);
        Vector2 texel = new Vector2(clip.X * 0.5f + 0.5f, clip.Y * 0.5f + 0.5f) * size;
        return new(texel.X - MathF.Floor(texel.X), texel.Y - MathF.Floor(texel.Y));
    }

    /// <summary>How far apart two texel fractions are, on the circle: 0.999 and 0.001 are 0.002 apart.</summary>
    private static float FractionDistance(Vector2 a, Vector2 b)
    {
        float dx = MathF.Abs(a.X - b.X),
            dy = MathF.Abs(a.Y - b.Y);
        return MathF.Max(MathF.Min(dx, 1 - dx), MathF.Min(dy, 1 - dy));
    }

    /// <summary>
    /// Renders one tick seen from a camera at <paramref name="eye"/> in its reference frame, with a caster fixed in
    /// the frame so that the cascades are drawn, and returns the prepared scene (valid until the next render). The
    /// camera's previous position is <paramref name="previousEye"/>, in the same frame: where it was on the tick
    /// before, which the renderer need not have seen.
    /// </summary>
    private static PreparedScene RenderTick(
        VulkanRenderer renderer,
        Kit kit,
        long tick,
        long frameId,
        Double3 eye,
        Double3 previousEye,
        Quaternion rotation,
        float alpha,
        Double3 casterInFrame
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, -0.3f));
        snapshot.Tick = tick;
        TestChains.Of(snapshot, (frameId, Quaternion.Identity));
        snapshot.OriginInFrame = eye;
        snapshot.Camera.Rotation = rotation;
        snapshot.Camera.PreviousRotation = rotation;
        snapshot.Camera.PreviousPosition = (previousEye - eye).ToVector3();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: kit.Box,
                material: kit.White,
                position: (casterInFrame - eye).ToVector3(),
                scale: Vector3.One,
                flags: Caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        renderer.Render(snapshot, alpha);
        renderer.LastScene.CascadeSetCount.Should().Be(1, "a caster is in range of the star");
        return renderer.LastScene;
    }

    /// <summary>
    /// The texel fractions, in every cascade of the last frame, of points given in the coordinates of the camera's
    /// frame: [point][cascade].
    /// </summary>
    private static Vector2[][] Fractions(PreparedScene scene, Double3 eye, Double3[] pointsInFrame, int size) =>
        [
            .. pointsInFrame.Select(point =>
            {
                Vector3 relative = (point - eye).ToVector3() - scene.Camera.Offset;
                return Enumerable
                    .Range(0, ShadowCascades.Count)
                    .Select(i => TexelFraction(scene.CascadeSets[0][i], relative, size))
                    .ToArray();
            }),
        ];

    private static void AssertSameFractions(Vector2[][] current, Vector2[][] reference, string context)
    {
        for (int p = 0; p < current.Length; p++)
        for (int i = 0; i < ShadowCascades.Count; i++)
            FractionDistance(current[p][i], reference[p][i])
                .Should()
                .BeLessThan(
                    0.01f,
                    "{0}: point {1} keeps its place among the texels of cascade {2} ({3} vs {4})",
                    context,
                    p,
                    i,
                    current[p][i],
                    reference[p][i]
                );
    }

    [Fact]
    public void CascadesKeepTheirWorldTexelGridWhileTheCameraMovesAndTurns()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        try
        {
            var kit = Kit.Create(renderer);
            int size = renderer.ShadowMap.Size;
            // Positions of the camera in its frame on five ticks, moving by amounts that are not whole texels.
            Double3[] eyes =
            [
                new(0, 0, 0),
                new(0.123, 0.0456, -0.789),
                new(1.2345, -0.0123, -3.4567),
                new(10.01, 0.5, -20.7),
                new(-37.3, 2.25, -61.05),
            ];
            Quaternion[] rotations =
            [
                Quaternion.Identity,
                Quaternion.Identity,
                Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0f),
                Quaternion.CreateFromYawPitchRoll(-1.1f, 0.3f, 0.1f),
                Quaternion.CreateFromYawPitchRoll(2.5f, -0.5f, 0f),
            ];
            Double3[] points = [new(3.3, -1.7, -12.9), new(-25.1, -2, -48.2), new(140.7, -2, -305.3)];
            float[]? texels = null;
            Vector2[][]? reference = null;

            for (int tick = 0; tick < eyes.Length; tick++)
                // Interpolated frames inside the tick must agree too: alpha moves the eye, not the grid.
                foreach (float alpha in (float[])[0.37f, 1f])
                {
                    PreparedScene scene = RenderTick(
                        renderer: renderer,
                        kit: kit,
                        tick: tick + 1,
                        frameId: 1,
                        eye: eyes[tick],
                        previousEye: eyes[Math.Max(tick - 1, 0)],
                        rotation: rotations[tick],
                        alpha: alpha,
                        casterInFrame: new(0, -2, -10)
                    );
                    texels ??=
                    [
                        .. Enumerable.Range(0, ShadowCascades.Count).Select(i => scene.CascadeSets[0][i].TexelSize),
                    ];
                    for (int i = 0; i < ShadowCascades.Count; i++)
                        scene
                            .CascadeSets[0][i]
                            .TexelSize.Should()
                            .Be(texels[i], "turning the camera never changes the texel size");
                    Vector2[][] current = Fractions(scene: scene, eye: eyes[tick], pointsInFrame: points, size: size);
                    reference ??= current;
                    AssertSameFractions(current, reference, $"tick {tick + 1}, alpha {alpha}");
                }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASkippedTickDoesNotShiftTheTexelGrid()
    {
        // The render thread draws the latest snapshot, so it can miss ticks. The camera walks on a planet, 6,371 km
        // from its frame's origin, by steps that are not whole texels, and the renderer sees ticks 1, 2, 4 and 7 only.
        // Accumulating the camera's motion from the ticks it sees would lose ticks 3, 5 and 6.
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        try
        {
            var kit = Kit.Create(renderer);
            int size = renderer.ShadowMap.Size;
            var surface = new Double3(0, 6_371_000, 0);
            Double3 EyeAt(long tick) => surface + new Double3(0.731 * tick, 0.013 * tick * tick, -1.379 * tick);
            Double3[] points =
            [
                surface + new Double3(3.3, -1.7, -12.9),
                surface + new Double3(-25.1, -2, -48.2),
                surface + new Double3(140.7, -2, -305.3),
            ];
            Vector2[][]? reference = null;
            foreach (long tick in (long[])[1, 2, 4, 7])
            foreach (float alpha in (float[])[0.5f, 1f])
            {
                PreparedScene scene = RenderTick(
                    renderer: renderer,
                    kit: kit,
                    tick: tick,
                    frameId: 1,
                    eye: EyeAt(tick),
                    previousEye: EyeAt(tick - 1),
                    rotation: Quaternion.Identity,
                    alpha: alpha,
                    casterInFrame: surface + new Double3(0, -2, -10)
                );
                Vector2[][] current = Fractions(scene: scene, eye: EyeAt(tick), pointsInFrame: points, size: size);
                reference ??= current;
                AssertSameFractions(current, reference, $"tick {tick}, alpha {alpha}");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameChangeMovesTheGridToTheNewFrameOriginAndBackCleanly()
    {
        // The camera walks on the ground (frame 1), steps into a ship (frame 2) whose origin is 40 m away, walks in
        // it for a few ticks with one skipped, and steps back out. The frames share their axes, so a camera position
        // in frame 2 is the one in frame 1 minus the ship's origin.
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180);
        try
        {
            var kit = Kit.Create(renderer);
            int size = renderer.ShadowMap.Size;
            var ship = new Double3(23.417, 1.093, -31.771);
            Double3 OnGround(long tick) => new(0.611 * tick, 0.2, -0.917 * tick);
            Double3 Eye(long tick, long frame) => frame == 1 ? OnGround(tick) : OnGround(tick) - ship;

            Double3 FrameOrigin(long frame) => frame == 1 ? Double3.Zero : ship;
            // Points fixed on the ground, in ground coordinates.
            Double3[] points = [new(3.3, -1.7, -12.9), new(-25.1, -2, -48.2), new(17.9, -2, -40.3)];
            Double3[] InFrame(long frame) => [.. points.Select(p => p - FrameOrigin(frame))];

            var reference = new Dictionary<long, Vector2[][]>();
            Vector2[][]? lastOnGround = null;
            foreach (
                (long tick, long frame) in ((long Tick, long Frame)[])
                    [(1, 1), (2, 1), (3, 2), (4, 2), (6, 2), (7, 1), (8, 1)]
            )
            {
                PreparedScene scene = RenderTick(
                    renderer: renderer,
                    kit: kit,
                    tick: tick,
                    frameId: frame,
                    eye: Eye(tick, frame),
                    previousEye: Eye(tick - 1, frame),
                    rotation: Quaternion.Identity,
                    alpha: 1f,
                    casterInFrame: new Double3(0, -2, -10) - FrameOrigin(frame)
                );

                // The grid passes through the origin of the frame the camera is in: it falls on a texel corner.
                Vector2[] origin = Fractions(
                    scene: scene,
                    eye: Eye(tick, frame),
                    pointsInFrame: [Double3.Zero],
                    size: size
                )[0];
                for (int i = 0; i < ShadowCascades.Count; i++)
                    FractionDistance(origin[i], Vector2.Zero)
                        .Should()
                        .BeLessThan(
                            0.01f,
                            "on tick {0} the grid of cascade {1} passes through the origin of frame {2}",
                            tick,
                            i,
                            frame
                        );

                Vector2[][] current = Fractions(
                    scene: scene,
                    eye: Eye(tick, frame),
                    pointsInFrame: InFrame(frame),
                    size: size
                );
                if (frame == 2 && lastOnGround != null)
                {
                    // Stepping into the ship moved the grid to the ship's origin, which is not a whole number of texels away.
                    bool moved = Enumerable
                        .Range(0, ShadowCascades.Count)
                        .Any(i => FractionDistance(current[0][i], lastOnGround[0][i]) > 0.05f);
                    moved.Should().BeTrue("the grid follows the new frame's origin on tick {0}", tick);
                    lastOnGround = null;
                }

                if (frame == 1)
                    lastOnGround = current;
                // Within a frame, and on coming back to it, fixed points keep their texels: nothing carried over.
                if (reference.TryGetValue(frame, out Vector2[][]? expected))
                    AssertSameFractions(current, expected, $"tick {tick} in frame {frame}");
                else
                    reference[frame] = current;
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
        const int width = 1280,
            height = 720;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            var scene = TestScene.Create(renderer);
            Capture shadowed = Gpu.RenderAndCapture(renderer: renderer, snapshot: scene.Snapshot(), name: "scene");
            Capture unshadowed = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: scene.Snapshot(shadows: false),
                name: "scene-without-shadow-maps"
            );
            // The ground under the middle of the tall box's shadow: from there, the sun is behind the box.
            Vector3 ground =
                TestScene.TallBox + new Vector3(0, -1f, 0) - TestScene.SunDirection * (1f / TestScene.SunDirection.Y);
            Vector2 pixel = Gpu.Project(point: ground, width: width, height: height);
            shadowed
                .MeanLuma(pixel, 2)
                .Should()
                .BeLessThan(0.6f * unshadowed.MeanLuma(pixel, 2), "the ground behind the tall box is in its shadow");
            // On the sunward side of the box the ground is the same with and without shadow maps.
            Vector2 sunward = Gpu.Project(
                point: TestScene.TallBox + new Vector3(1.5f, -1f, -1.2f),
                width: width,
                height: height
            );
            MathF
                .Abs(shadowed.MeanLuma(sunward, 1) - unshadowed.MeanLuma(sunward, 1))
                .Should()
                .BeLessThan(0.01f, "the sunward side is lit");
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
            DrawIndexedIndirectCommand command = scene.Commands[c];
            for (uint k = 0; k < command.InstanceCount; k++)
                yield return scene.Instances[command.FirstInstance + k];
        }
    }
}
