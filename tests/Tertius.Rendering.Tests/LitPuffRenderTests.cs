using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Lit puffs on the GPU (docs/plans/particles.md, "Lit puffs"). A lit puff scatters the light that reaches its center
/// equally in every direction: L = albedo × (mean ambient + (E_sun · V_sun + Σ E_light) / (4π)). The camera is at the render
/// origin with identity rotation, so it looks down −Z with +Y up.
/// </summary>
/// <remarks>
/// A glow puff sends its radiance through the same sprite, profile, fades and tone mapping as a lit puff. So most tests
/// render a scene twice: once with lit puffs, and once with each lit puff replaced by a glow puff whose radiance is the
/// light that the model gives the lit puff. The two images must match pixel for pixel. The expected radiance comes from
/// the formula of the doc, computed here on the CPU. The shader's function has no CPU twin in the renderer.
/// </remarks>
[Collection(GpuCollection.Name)]
public sealed class LitPuffRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The largest difference of one channel between the lit image and its glow twin: rounding to 8 bits.</summary>
    private const float Tolerance = 2.5f / 255;

    private static readonly Vector3 Albedo = new(0.8f, 0.6f, 0.4f);

    private static Vector2 Pixel(Vector3 point) => Gpu.Project(point: point, width: Width, height: Height);

    private static RenderPuff Puff(Vector3 position, int look) =>
        new()
        {
            Position = position,
            PreviousPosition = position,
            Age = 0f,
            Look = look,
        };

    /// <summary>A snapshot with no sun, no ambient light and no lights, so that each test turns on only what it measures.</summary>
    private static RenderSnapshot Dark()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f), starIrradiance: 0f);
        snapshot.Ambient.Intensity = 0f;
        return snapshot;
    }

    /// <summary>Adds a lit puff of radius 0.5 m and albedo <paramref name="albedo"/> at <paramref name="position"/>, with its own look.</summary>
    private static void AddLitPuff(RenderSnapshot snapshot, Vector3 position, Vector3? albedo = null)
    {
        snapshot.PuffLooks.Add(
            new(
                Life: 2f,
                StartRadius: 0.5f,
                EndRadius: 0.5f,
                TemperatureRatio: 0.5f,
                Shading: PuffShading.Lit(albedo ?? Albedo)
            )
        );
        snapshot.Puffs.Add(Puff(position, snapshot.PuffLooks.Count - 1));
    }

    /// <summary>The mean over every facing of the hemisphere ambient light: the mean of the sky and the ground color.</summary>
    private static Vector3 MeanAmbient(RenderSnapshot snapshot) =>
        (snapshot.Ambient.SkyColor + snapshot.Ambient.GroundColor) * 0.5f * snapshot.Ambient.Intensity;

    /// <summary>The test star's irradiance per channel at the camera, which the puffs near it share.</summary>
    private static Vector3 StarIrradiance(RenderSnapshot snapshot) => Gpu.StarIrradianceRgb(snapshot);

    /// <summary>
    /// The irradiance of a light of radiant intensity <paramref name="intensity"/>, in W/sr per channel, at a distance: I / d²
    /// times the window of Karis 2013 at the light's reach, for the default exposure of the test renderer, FixedExposure(0).
    /// </summary>
    private static Vector3 Irradiance(Vector3 intensity, float distance)
    {
        double reach = LightUnits.Reach(
            Math.Max(intensity.X, Math.Max(intensity.Y, intensity.Z)),
            LightUnits.CutoffIrradiance(0)
        );
        float ratio = (float)(distance * distance / (reach * reach));
        float window = Math.Clamp(1f - ratio * ratio, 0f, 1f);
        return intensity
            * (
                window
                * window
                / MathF.Max(distance * distance, LightUnits.NearestDistance * LightUnits.NearestDistance)
            );
    }

    /// <summary>
    /// Renders <paramref name="snapshot"/> with its lit puffs, then with each puff's look replaced by a glow of the matching
    /// entry of <paramref name="expected"/>, and requires the two images to match. Each puff must have its own look.
    /// </summary>
    private static void ShouldMatchGlowPuffs(RenderSnapshot snapshot, Vector3[] expected, string name)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            ShouldMatchGlowPuffs(renderer: renderer, snapshot: snapshot, expected: expected, name: name);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void ShouldMatchGlowPuffs(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        Vector3[] expected,
        string name
    )
    {
        snapshot.PuffLooks.Count.Should().Be(expected.Length);
        List<PuffLook> litLooks = [.. snapshot.PuffLooks];
        Capture lit = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name + "-lit");
        snapshot.PuffLooks.Clear();
        for (int i = 0; i < litLooks.Count; i++)
            snapshot.PuffLooks.Add(litLooks[i] with { Shading = PuffShading.Glow(expected[i]) });
        Capture glow = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name + "-glow");
        snapshot.PuffLooks.Clear();
        snapshot.PuffLooks.AddRange(litLooks);

        float largest = 0f;
        (int X, int Y) at = (0, 0);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var difference = Vector3.Abs(lit.Color(x, y) - glow.Color(x, y));
            float channel = MathF.Max(difference.X, MathF.Max(difference.Y, difference.Z));
            if (channel > largest)
            {
                largest = channel;
                at = (x, y);
            }
        }

        largest
            .Should()
            .BeLessThanOrEqualTo(
                Tolerance,
                "a lit puff scatters the mean over every facing of the light at its center, so it looks as a glow puff of that "
                    + "radiance does, but pixel ({0}, {1}) is {2} in the lit image and {3} in its glow twin",
                at.X,
                at.Y,
                lit.Color(at.X, at.Y),
                glow.Color(at.X, at.Y)
            );
    }

    [Fact]
    public void ALitPuffScattersTheMeanOfTheSkyAndTheGroundColor()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Ambient = new()
        {
            SkyColor = new(0.7f, 0.8f, 1.0f),
            GroundColor = new(0.3f, 0.2f, 0.1f),
            Intensity = 0.8f,
        };
        AddLitPuff(snapshot, new(0, 0, -6));

        ShouldMatchGlowPuffs(snapshot, [Albedo * MeanAmbient(snapshot)], "lit-puff-ambient");
    }

    [Fact]
    public void ALitPuffScattersTheSunlightAtItsCenterOverFourPi()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f), starIrradiance: 6f);
        snapshot.Ambient.Intensity = 0f;
        AddLitPuff(snapshot, new(0, 0, -6));

        ShouldMatchGlowPuffs(snapshot, [Albedo * StarIrradiance(snapshot) / (4 * MathF.PI)], "lit-puff-sun");
    }

    [Fact]
    public void ALitPuffScattersAPointLightByItsDistanceAttenuation()
    {
        RenderSnapshot snapshot = Dark();
        var puff = new Vector3(0, 0, -6);
        var lamp = new Vector3(1.2f, 1.6f, -6);
        var color = new Vector3(1f, 0.8f, 0.5f);
        // The puff is 2 m from the lamp, far inside the lamp's reach of about 400 m.
        const float intensity = 60f;
        snapshot.Lights.Add(
            Gpu.Lamp(position: lamp, color: color, intensity: intensity, chainDepth: TestChains.CamerasFrame)
        );
        AddLitPuff(snapshot, puff);

        Vector3 irradiance = Irradiance(color * intensity, Vector3.Distance(lamp, puff));
        ShouldMatchGlowPuffs(snapshot, [Albedo * irradiance / (4 * MathF.PI)], "lit-puff-point");
    }

    /// <summary>
    /// A panel 1.2 m by 0.8 m that casts no shadow, 1.5 m above <paramref name="puff"/> and 0.4 m aside, turned so that
    /// its front faces down when <paramref name="facesDown"/>, else up, with a power of 8 W.
    /// </summary>
    private static RectRenderLight PanelAbove(Vector3 puff, bool facesDown)
    {
        Vector3 position = puff + new Vector3(0.4f, 1.5f, 0);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (facesDown ? 1 : -1) * MathF.PI / 2);
        return new(
            EntityIndex: Gpu.LampEntity,
            EntityGeneration: 0,
            Position: position,
            PreviousPosition: position,
            Rotation: rotation,
            PreviousRotation: rotation,
            Color: new(1f, 0.8f, 0.5f),
            Power: 8,
            Width: 1.2f,
            Height: 0.8f,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );
    }

    [Fact]
    public void ALitPuffScattersARectangleLightsFaceRadianceTimesTheSolidAngleOfItsFace()
    {
        RenderSnapshot snapshot = Dark();
        var puff = new Vector3(0, 0, -6);
        RectRenderLight panel = PanelAbove(puff, facesDown: true);
        snapshot.Lights.Add(panel);
        AddLitPuff(snapshot, puff);

        Double3 halfWidth = Vector3.Transform(Vector3.UnitX, panel.Rotation) * (panel.Width / 2),
            halfHeight = Vector3.Transform(Vector3.UnitY, panel.Rotation) * (panel.Height / 2);
        Double3 toCenter = panel.Position - puff;
        double solidAngle = new RectView(toCenter: toCenter, halfWidth: halfWidth, halfHeight: halfHeight).SolidAngle;
        double intensity = LightUnits.RectIntensity(panel.Power);
        double reach = LightUnits.Reach(intensity, LightUnits.CutoffIrradiance(0));
        // The puff lies under the face, so the face's nearest point is straight above it, 1.5 m away.
        double ratio = 1.5 * 1.5 / (reach * reach);
        double window = (1 - ratio * ratio) * (1 - ratio * ratio);
        double faceRadiance = intensity / ((double)panel.Width * panel.Height);
        Vector3 irradiance = panel.Color * (float)(faceRadiance * window * solidAngle);
        solidAngle.Should().BePositive("the puff lies in front of the face");
        ShouldMatchGlowPuffs(snapshot, [Albedo * irradiance / (4 * MathF.PI)], "lit-puff-rect");
    }

    [Fact]
    public void ALitPuffBehindARectangleLightTakesNoneOfItsLight()
    {
        RenderSnapshot snapshot = Dark();
        var puff = new Vector3(0, 0, -6);
        snapshot.Lights.Add(PanelAbove(puff, facesDown: false));
        AddLitPuff(snapshot, puff);

        ShouldMatchGlowPuffs(snapshot, [Vector3.Zero], "lit-puff-behind-rect");
    }

    [Fact]
    public void ALitPuffAtALightsExactCenterTakesNoLightFromIt()
    {
        // At the lamp's center the direction toward it is a zero vector, which the far shadows' angle cannot take. A
        // surface there takes nothing either, because dot(N, L) is 0. Within the floor of 1 cm, the lamp would send
        // I / (1 cm)² = 1 W/m², which the puff would show as a dim glow.
        RenderSnapshot snapshot = Dark();
        var center = new Vector3(0, 0, -6);
        snapshot.Lights.Add(
            Gpu.Lamp(position: center, color: Vector3.One, intensity: 1e-4f, chainDepth: TestChains.CamerasFrame)
        );
        AddLitPuff(snapshot, center);

        ShouldMatchGlowPuffs(snapshot, [Vector3.Zero], "lit-puff-at-a-lights-center");
    }

    [Fact]
    public void ALitPuffTakesEveryLight()
    {
        // A star, a lamp and a spot light at once: the puff scatters the sum of their irradiances at its center.
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f), starIrradiance: 0.5f);
        snapshot.Ambient.Intensity = 0f;
        var puff = new Vector3(0, 0, -6);
        var lamp = new Vector3(1.2f, 1.6f, -6);
        var spot = new Vector3(-1.5f, 2f, -6);
        snapshot.Lights.Add(
            Gpu.Lamp(position: lamp, color: new(1f, 0.8f, 0.5f), intensity: 2f, chainDepth: TestChains.CamerasFrame)
        );
        snapshot.Lights.Add(
            Gpu.Spot(
                position: spot,
                direction: Vector3.Normalize(puff - spot),
                color: new(0.6f, 0.8f, 1f),
                intensity: 3f,
                innerConeAngle: 0.2f,
                outerConeAngle: 0.3f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        AddLitPuff(snapshot, puff);

        Vector3 irradiance =
            StarIrradiance(snapshot)
            + Irradiance(new Vector3(1f, 0.8f, 0.5f) * 2f, Vector3.Distance(lamp, puff))
            + Irradiance(new Vector3(0.6f, 0.8f, 1f) * 3f, Vector3.Distance(spot, puff));
        ShouldMatchGlowPuffs(snapshot, [Albedo * irradiance / (4 * MathF.PI)], "lit-puff-every-light");
    }

    /// <summary>
    /// The image of a lit puff of radius 1 m in the sun, whose center lies 0.5 m outside the view's right side plane, 10 m
    /// from the eye, so that its inner half shows at the right edge, under an environment map that draws all six faces in
    /// the frame. With <paramref name="withTheLamp"/>, a lamp stands 1 cm farther out than the puff's center, with a reach
    /// of 0.5 m: its reach sphere lies 1 cm outside the plane, so only a face of the map keeps it, and the puff's center
    /// lies within it.
    /// </summary>
    private static (Capture Image, int Lights) PuffAtTheRightEdge(bool withTheLamp)
    {
        // The right side plane passes through the eye: its outward normal, and its direction in the plane y = 0.
        float tanHalfX = MathF.Tan(Gpu.Fov / 2) * Width / Height;
        var outward = Vector3.Normalize(new(1, 0, tanHalfX));
        var along = Vector3.Normalize(new(tanHalfX, 0, -1));
        Vector3 puff = 10 * along + 0.5f * outward;
        const float reach = 0.5f;
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = EnvironmentMapSetting.MinFaceSize,
                FacesPerFrame = CubeFaces.Count,
            }
        );
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f), starIrradiance: 1f);
            snapshot.Ambient.Intensity = 0f;
            snapshot.PuffLooks.Add(
                new(Life: 2f, StartRadius: 1f, EndRadius: 1f, TemperatureRatio: 0.5f, Shading: PuffShading.Lit(Albedo))
            );
            snapshot.Puffs.Add(Puff(puff, look: 0));
            if (withTheLamp)
                snapshot.Lights.Add(
                    Gpu.Lamp(
                        position: puff + 0.01f * outward,
                        color: Vector3.One,
                        intensity: (float)(reach * reach * LightUnits.CutoffIrradiance(0)),
                        chainDepth: TestChains.CamerasFrame
                    )
                );
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: withTheLamp ? "lit-puff-face-only-lamp" : "lit-puff-no-lamp"
            );
            return (image, renderer.LastFrameStats.Lights);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitPuffWhoseCenterIsOffTheScreenTakesNoLightFromALampThatOnlyAFaceOfTheMapKeeps()
    {
        (Capture withIt, int lights) = PuffAtTheRightEdge(withTheLamp: true);
        (Capture withoutIt, _) = PuffAtTheRightEdge(withTheLamp: false);

        lights.Should().Be(2, "a face keeps the lamp, so the frame's one list holds the sun and the lamp");
        withIt
            .MeanLuma(new(Width - 2, Height / 2f), radius: 1)
            .Should()
            .BeGreaterThan(0.05f, "the sunlit inner half of the puff shows at the right edge");
        withIt
            .Rgba.Should()
            .Equal(withoutIt.Rgba, "the main view's puff takes only the lights whose reach touches the main view");
    }

    [Fact]
    public void ALitPuffInsideASpotLightsConeIsLitByItAndOneOutsideTheConeIsNot()
    {
        RenderSnapshot snapshot = Dark();
        var inside = new Vector3(-1.5f, 0, -6);
        var outside = new Vector3(1.5f, 0, -6);
        var lamp = new Vector3(-1.5f, 2f, -6);
        var color = new Vector3(0.6f, 0.8f, 1f);
        const float intensity = 30f;
        snapshot.Lights.Add(
            Gpu.Spot(
                position: lamp,
                direction: -Vector3.UnitY,
                color: color,
                intensity: intensity,
                innerConeAngle: 0.2f,
                outerConeAngle: 0.3f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        AddLitPuff(snapshot, inside);
        AddLitPuff(snapshot, outside);

        Vector3 irradiance = Irradiance(color * intensity, Vector3.Distance(lamp, inside));
        ShouldMatchGlowPuffs(snapshot, [Albedo * irradiance / (4 * MathF.PI), Vector3.Zero], "lit-puff-spot");
    }

    [Fact]
    public void ALitPuffInTheShadowOfABoxTakesOnlyTheAmbientLightAndItsTwinInTheSunTakesBoth()
    {
        // The sun is almost straight above. A slab 3 m above the left puff shadows it through the shadow maps. The slab's
        // bounding radius of 1.4 m is too small for a far shadow (SceneBatcher.CollectFarShadows), so only the maps shadow it.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId gray = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.4f, y: 0.4f, z: 0.4f, w: 1), roughness: 0.65f) with
                {
                    Reflectance = 0f,
                }
            );
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.1f, 1f, 0.05f), starIrradiance: 6f);
            var shaded = new Vector3(-1.5f, 0, -8);
            var open = new Vector3(1.5f, 0, -8);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: gray,
                    position: shaded + new Vector3(0.3f, 3f, 0.15f),
                    scale: new(1.6f, 0.2f, 1.6f),
                    flags: RenderFlags.CastsShadow,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            AddLitPuff(snapshot, shaded);
            AddLitPuff(snapshot, open);

            Vector3 ambient = Albedo * MeanAmbient(snapshot);
            ShouldMatchGlowPuffs(
                renderer: renderer,
                snapshot: snapshot,
                expected: [ambient, ambient + Albedo * StarIrradiance(snapshot) / (4 * MathF.PI)],
                name: "lit-puff-box-shadow"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitPuffJustBelowAThinCasterTakesItsShadow()
    {
        // A sheet that only casts hovers 1.2 texels of the first cascade above the puff's center, under a high sun. A
        // puff samples the maps with L as its normal, so the normal offset, which scales with sin(angle to the light),
        // does not move its lookup, and the sheet shadows it past the constant bias of half a texel. An offset of a full
        // texel toward the sun would carry the lookup over the sheet.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MaterialId gray = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.4f, y: 0.4f, z: 0.4f, w: 1), roughness: 0.65f)
            );
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.03f, 1f, 0.02f), starIrradiance: 6f);
            var puff = new Vector3(0, 0, -8);
            float gap = 1.2f * Gpu.FirstCascadeTexel(width: Width, height: Height);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: gray,
                    position: puff + new Vector3(0, gap, 0),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                ) with
                {
                    Flags = RenderFlags.CastsShadow,
                }
            );
            AddLitPuff(snapshot, puff);

            ShouldMatchGlowPuffs(
                renderer: renderer,
                snapshot: snapshot,
                expected: [Albedo * MeanAmbient(snapshot)],
                name: "lit-puff-thin-caster"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitPuffInTheFarShadowOfABigCasterTakesOnlyTheAmbientLight()
    {
        // A caster of radius 500 m, 3 km from the puff toward the sun, is beyond the shadow maps: only its far shadow, the
        // analytic sphere, hides the sun from the puff.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f));
            MaterialId gray = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.4f, y: 0.4f, z: 0.4f, w: 1), roughness: 0.65f)
            );
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.1f, 1f, 0.2f), starIrradiance: 6f);
            var puff = new Vector3(0, 0, -20);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: gray,
                    position: puff + 3_000f * Gpu.StarDirection(snapshot),
                    scale: new(500f),
                    flags: RenderFlags.CastsShadow,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            AddLitPuff(snapshot, puff);

            ShouldMatchGlowPuffs(
                renderer: renderer,
                snapshot: snapshot,
                expected: [Albedo * MeanAmbient(snapshot)],
                name: "lit-puff-far-shadow"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitPuffKeepsOnlyItsOwnShareOfItsAlbedo()
    {
        // Half way through its life, a puff holds half of its own gas, so it scatters half the light, as a glow puff of the
        // same age sends half its radiance. So the lit puff matches the glow puff of the full radiance at the same age.
        RenderSnapshot snapshot = Dark();
        snapshot.Ambient = new()
        {
            SkyColor = new(0.9f),
            GroundColor = new(0.5f),
            Intensity = 1f,
        };
        AddLitPuff(snapshot, new(0, 0, -6));
        snapshot.Puffs[0] = snapshot.Puffs[0] with { Age = 1f };

        ShouldMatchGlowPuffs(snapshot, [Albedo * MeanAmbient(snapshot)], "lit-puff-own-share");
    }

    /// <summary>
    /// The scene of puffs-lit.png: a gray ground 1.5 m below the eye under a sun from above and to the right, and a gray roof
    /// 2 m above three pale lit puffs, 9 m to 10 m ahead. The puff at the right is in the sun. The roof covers the other two:
    /// the puff in the middle hangs 1 m below a warm lamp of 8 W/sr under the roof, and the puff at the left takes the ambient
    /// light and the lamp's light from 2.4 m away, a fifth of what the middle puff takes.
    /// </summary>
    private static RenderSnapshot LitScene(
        VulkanRenderer renderer,
        out Vector3 inSun,
        out Vector3 inShadow,
        out Vector3 underLamp
    )
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.35f, y: 0.35f, z: 0.35f, w: 1), roughness: 0.65f) with
            {
                Reflectance = 0f,
            }
        );
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.9f, 0.3f), starIrradiance: 5f);
        inSun = new(2.5f, 0.2f, -9);
        inShadow = new(-2.8f, 0.2f, -9.6f);
        underLamp = new(-0.6f, 0.2f, -9.6f);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: gray,
                position: new(0, -1.5f, -20),
                scale: new(30, 1, 30),
                flags: RenderFlags.CastsShadow,
                chainDepth: TestChains.CamerasFrame
            )
        );
        // The sun rises 3 m for each 1 m that it moves along x and along z, so the roof's shadow 2 m below it is 0.67 m to the
        // left of the roof and 0.67 m nearer the camera.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: gray,
                position: new(-1.1f, 2.35f, -8.93f),
                scale: new(5f, 0.3f, 2.6f),
                flags: RenderFlags.CastsShadow,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Lights.Add(
            Gpu.Lamp(
                position: underLamp + new Vector3(0, 1f, 0),
                color: new(1f, 0.75f, 0.45f),
                intensity: 8f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.PuffLooks.Add(
            new(
                Life: 2f,
                StartRadius: 0.6f,
                EndRadius: 0.6f,
                TemperatureRatio: 0.5f,
                Shading: PuffShading.Lit(new(0.85f, 0.85f, 0.9f))
            )
        );
        snapshot.Puffs.Add(Puff(inSun, 0));
        snapshot.Puffs.Add(Puff(inShadow, 0));
        snapshot.Puffs.Add(Puff(underLamp, 0));
        return snapshot;
    }

    private static float LinearLuma(Vector3 display)
    {
        Vector3 c = Measure.ToLinear(display);
        return 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    }

    [Fact]
    public void LitPuffsAreBrightInTheSunDimInTheShadowOfABoxAndWarmUnderALamp()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = LitScene(
                renderer: renderer,
                inSun: out Vector3 inSun,
                inShadow: out Vector3 inShadow,
                underLamp: out Vector3 underLamp
            );
            List<RenderPuff> puffs = [.. snapshot.Puffs];
            snapshot.Puffs.Clear();
            Capture without = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "puffs-lit-without");
            snapshot.Puffs.AddRange(puffs);
            Capture with = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "puffs-lit");

            float Added(Vector3 place) =>
                LinearLuma(with.Color(Pixel(place))) - LinearLuma(without.Color(Pixel(place)));

            renderer.LastFrameStats.Puffs.Should().Be(3);
            float sun = Added(inSun),
                shadow = Added(inShadow),
                lamp = Added(underLamp);
            shadow.Should().BeGreaterThan(0.002f, "the ambient light still reaches a puff in the shadow");
            sun.Should()
                .BeGreaterThan(
                    3f * shadow,
                    "the sun lights the puff in the open and the roof hides it from the other two"
                );
            lamp.Should().BeGreaterThan(3f * shadow, "the lamp under the roof lights the puff below it");
            Vector3 lampAdded =
                Measure.ToLinear(with.Color(Pixel(underLamp))) - Measure.ToLinear(without.Color(Pixel(underLamp)));
            lampAdded.X.Should().BeGreaterThan(lampAdded.Z, "the lamp is warm, so the puff under it is warm too");
            ReferenceImages.Verify(with, Path.Combine(ReferenceImages.Folder, "puffs-lit.png"));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
