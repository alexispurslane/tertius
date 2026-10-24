using System.Numerics;

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
    private const int Width = 640, Height = 360;

    /// <summary>The largest difference of one channel between the lit image and its glow twin: rounding to 8 bits.</summary>
    private const float Tolerance = 2.5f / 255;

    private static readonly Vector3 Albedo = new(0.8f, 0.6f, 0.4f);

    private static Vector2 Pixel(Vector3 point) => Gpu.Project(point: point, width: Width, height: Height);

    private static RenderPuff Puff(Vector3 position, int look) =>
        new() { Position = position, PreviousPosition = position, Age = 0f, Look = look };

    /// <summary>A snapshot with no sun, no ambient light and no lights, so that each test turns on only what it measures.</summary>
    private static RenderSnapshot Dark()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f), sunIntensity: 0f);
        snapshot.Ambient.Intensity = 0f;
        return snapshot;
    }

    /// <summary>Adds a lit puff of radius 0.5 m and albedo <paramref name="albedo"/> at <paramref name="position"/>, with its own look.</summary>
    private static void AddLitPuff(RenderSnapshot snapshot, Vector3 position, Vector3? albedo = null)
    {
        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 0.5f, EndRadius: 0.5f, TemperatureRatio: 0.5f,
            Shading: PuffShading.Lit(albedo ?? Albedo)));
        snapshot.Puffs.Add(Puff(position, snapshot.PuffLooks.Count - 1));
    }

    /// <summary>The mean over every facing of the hemisphere ambient light: the mean of the sky and the ground color.</summary>
    private static Vector3 MeanAmbient(RenderSnapshot snapshot) =>
        (snapshot.Ambient.SkyColor + snapshot.Ambient.GroundColor) * 0.5f * snapshot.Ambient.Intensity;

    /// <summary>The sun's radiance as the shader holds it.</summary>
    private static Vector3 SunRadiance(RenderSnapshot snapshot) => snapshot.Sun.Color * snapshot.Sun.Intensity;

    /// <summary>The irradiance of a light of radiance <paramref name="radiance"/> and range <paramref name="range"/> at a distance: the window of Karis 2013.</summary>
    private static Vector3 Irradiance(Vector3 radiance, float range, float distance)
    {
        float ratio = distance * distance / (range * range);
        float window = Math.Clamp(1f - ratio * ratio, 0f, 1f);
        return radiance * (window * window / MathF.Max(distance * distance, 1e-4f));
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

    private static void ShouldMatchGlowPuffs(VulkanRenderer renderer, RenderSnapshot snapshot, Vector3[] expected,
        string name)
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
            Vector3 difference = Vector3.Abs(lit.Color(x, y) - glow.Color(x, y));
            float channel = MathF.Max(difference.X, MathF.Max(difference.Y, difference.Z));
            if (channel > largest)
            {
                largest = channel;
                at = (x, y);
            }
        }

        largest.Should().BeLessThanOrEqualTo(Tolerance,
            "a lit puff scatters the mean over every facing of the light at its center, so it looks as a glow puff of that " +
            "radiance does, but pixel ({0}, {1}) is {2} in the lit image and {3} in its glow twin",
            at.X, at.Y, lit.Color(at.X, at.Y), glow.Color(at.X, at.Y));
    }

    [Fact]
    public void ALitPuffScattersTheMeanOfTheSkyAndTheGroundColor()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Ambient = new()
            { SkyColor = new(0.7f, 0.8f, 1.0f), GroundColor = new(0.3f, 0.2f, 0.1f), Intensity = 0.8f };
        AddLitPuff(snapshot, new(0, 0, -6));

        ShouldMatchGlowPuffs(snapshot, [Albedo * MeanAmbient(snapshot)], "lit-puff-ambient");
    }

    [Fact]
    public void ALitPuffScattersTheSunlightAtItsCenterOverFourPi()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f), sunIntensity: 6f);
        snapshot.Ambient.Intensity = 0f;
        AddLitPuff(snapshot, new(0, 0, -6));

        ShouldMatchGlowPuffs(snapshot, [Albedo * SunRadiance(snapshot) / (4 * MathF.PI)], "lit-puff-sun");
    }

    [Fact]
    public void ALitPuffScattersAPointLightByItsDistanceAttenuation()
    {
        RenderSnapshot snapshot = Dark();
        var puff = new Vector3(0, 0, -6);
        var lamp = new Vector3(1.2f, 1.6f, -6);
        var color = new Vector3(1f, 0.8f, 0.5f);
        // The puff is 2 m from the lamp, near the lamp's range, where the window of the attenuation is 0.42.
        const float intensity = 60f, range = 2.6f;
        snapshot.Lights.Add(new PointRenderLight(Position: lamp, Color: color, Intensity: intensity, Range: range));
        AddLitPuff(snapshot, puff);

        Vector3 irradiance = Irradiance(color * intensity, range, Vector3.Distance(lamp, puff));
        ShouldMatchGlowPuffs(snapshot, [Albedo * irradiance / (4 * MathF.PI)], "lit-puff-point");
    }

    [Fact]
    public void ALitPuffInsideASpotLightsConeIsLitByItAndOneOutsideTheConeIsNot()
    {
        RenderSnapshot snapshot = Dark();
        var inside = new Vector3(-1.5f, 0, -6);
        var outside = new Vector3(1.5f, 0, -6);
        var lamp = new Vector3(-1.5f, 2f, -6);
        var color = new Vector3(0.6f, 0.8f, 1f);
        const float intensity = 30f, range = 8f;
        snapshot.Lights.Add(new SpotRenderLight(Position: lamp, Direction: -Vector3.UnitY, Color: color,
            Intensity: intensity, Range: range, InnerConeAngle: 0.2f,
            OuterConeAngle: 0.3f));
        AddLitPuff(snapshot, inside);
        AddLitPuff(snapshot, outside);

        Vector3 irradiance = Irradiance(color * intensity, range, Vector3.Distance(lamp, inside));
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
                MaterialDesc.Color(new(x: 0.4f, y: 0.4f, z: 0.4f, w: 1), gloss: 0.2f) with
                {
                    SpecularIntensity = 0f
                });
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.1f, 1f, 0.05f), sunIntensity: 6f);
            var shaded = new Vector3(-1.5f, 0, -8);
            var open = new Vector3(1.5f, 0, -8);
            snapshot.Instances.Add(Gpu.Instance(mesh: box, material: gray,
                position: shaded + new Vector3(0.3f, 3f, 0.15f), scale: new(1.6f, 0.2f, 1.6f),
                flags: RenderFlags.CastsShadow));
            AddLitPuff(snapshot, shaded);
            AddLitPuff(snapshot, open);

            Vector3 ambient = Albedo * MeanAmbient(snapshot);
            ShouldMatchGlowPuffs(renderer: renderer, snapshot: snapshot,
                expected: [ambient, ambient + Albedo * SunRadiance(snapshot) / (4 * MathF.PI)],
                name: "lit-puff-box-shadow");
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
            MaterialId gray =
                renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.4f, y: 0.4f, z: 0.4f, w: 1), gloss: 0.2f));
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.1f, 1f, 0.2f), sunIntensity: 6f);
            var puff = new Vector3(0, 0, -20);
            snapshot.Instances.Add(Gpu.Instance(mesh: sphere, material: gray,
                position: puff + 3_000f * snapshot.Sun.Direction, scale: new(500f),
                flags: RenderFlags.CastsShadow));
            AddLitPuff(snapshot, puff);

            ShouldMatchGlowPuffs(renderer: renderer, snapshot: snapshot, expected: [Albedo * MeanAmbient(snapshot)],
                name: "lit-puff-far-shadow");
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
        snapshot.Ambient = new() { SkyColor = new(0.9f), GroundColor = new(0.5f), Intensity = 1f };
        AddLitPuff(snapshot, new(0, 0, -6));
        snapshot.Puffs[0] = snapshot.Puffs[0] with { Age = 1f };

        ShouldMatchGlowPuffs(snapshot, [Albedo * MeanAmbient(snapshot)], "lit-puff-own-share");
    }

    /// <summary>
    /// The scene of puffs-lit.png: a gray ground 1.5 m below the eye under a sun from above and to the right, and a gray roof
    /// 2 m above three pale lit puffs, 9 m to 10 m ahead. The puff at the right is in the sun. The roof covers the other two:
    /// the puff at the left takes only the ambient light, and the puff in the middle hangs 1 m below a warm lamp of range 2 m
    /// under the roof.
    /// </summary>
    private static RenderSnapshot LitScene(VulkanRenderer renderer, out Vector3 inSun, out Vector3 inShadow,
        out Vector3 underLamp)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId gray = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.35f, y: 0.35f, z: 0.35f, w: 1), gloss: 0.2f) with
            {
                SpecularIntensity = 0f
            });
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.9f, 0.3f), sunIntensity: 5f);
        inSun = new(2.5f, 0.2f, -9);
        inShadow = new(-2.8f, 0.2f, -9.6f);
        underLamp = new(-0.6f, 0.2f, -9.6f);
        snapshot.Instances.Add(Gpu.Instance(mesh: plane, material: gray, position: new(0, -1.5f, -20),
            scale: new(30, 1, 30),
            flags: RenderFlags.CastsShadow));
        // The sun rises 3 m for each 1 m that it moves along x and along z, so the roof's shadow 2 m below it is 0.67 m to the
        // left of the roof and 0.67 m nearer the camera.
        snapshot.Instances.Add(Gpu.Instance(mesh: box, material: gray, position: new(-1.1f, 2.35f, -8.93f),
            scale: new(5f, 0.3f, 2.6f),
            flags: RenderFlags.CastsShadow));
        snapshot.Lights.Add(new PointRenderLight(Position: underLamp + new Vector3(0, 1f, 0),
            Color: new(1f, 0.75f, 0.45f),
            Intensity: 8f, Range: 2f));
        snapshot.PuffLooks.Add(new(Life: 2f, StartRadius: 0.6f, EndRadius: 0.6f, TemperatureRatio: 0.5f,
            Shading: PuffShading.Lit(new(0.85f, 0.85f, 0.9f))));
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
            RenderSnapshot snapshot =
                LitScene(renderer: renderer, inSun: out Vector3 inSun, inShadow: out Vector3 inShadow,
                    underLamp: out Vector3 underLamp);
            List<RenderPuff> puffs = [.. snapshot.Puffs];
            snapshot.Puffs.Clear();
            Capture without = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "puffs-lit-without");
            snapshot.Puffs.AddRange(puffs);
            Capture with = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "puffs-lit");

            float Added(Vector3 place) =>
                LinearLuma(with.Color(Pixel(place))) - LinearLuma(without.Color(Pixel(place)));

            renderer.LastFrameStats.Puffs.Should().Be(3);
            float sun = Added(inSun), shadow = Added(inShadow), lamp = Added(underLamp);
            shadow.Should().BeGreaterThan(0.002f, "the ambient light still reaches a puff in the shadow");
            sun.Should().BeGreaterThan(3f * shadow,
                "the sun lights the puff in the open and the roof hides it from the other two");
            lamp.Should().BeGreaterThan(3f * shadow, "the lamp under the roof lights the puff below it");
            Vector3 lampAdded = Measure.ToLinear(with.Color(Pixel(underLamp))) -
                                Measure.ToLinear(without.Color(Pixel(underLamp)));
            lampAdded.X.Should().BeGreaterThan(lampAdded.Z, "the lamp is warm, so the puff under it is warm too");
            ReferenceImages.Verify(with, Path.Combine(ReferenceImages.Folder, "puffs-lit.png"));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
