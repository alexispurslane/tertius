using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The reference scene. The camera is at the origin with identity rotation, so it looks down −Z with +Y up.
/// <list type="bullet">
/// <item>Near range: a checker-textured, normal-mapped ground plane 1.5 m below the eye; three boxes (one with the
/// checker and normal map), two spheres, a transparent green sphere, a warm point light and a cool spot light.</item>
/// <item>Middle range: a 1 km station box at 50 km.</item>
/// <item>Far range: a planet of radius 6,000 km at 30,000 km (also the eclipse occluder) and an emissive star of
/// radius 700,000 km at 1 AU in the sun's direction.</item>
/// </list>
/// The sun shines from the front right, low over the ground, so the planet shows a crescent. The near opaque objects
/// and the ground cast shadow-map shadows; the glass, the station and the celestial bodies do not.
/// </summary>
internal sealed class TestScene
{
    public const float PlanetRadius = 6.0e6f;
    public const float StarRadius = 7.0e8f;
    public const float StarDistance = 1.496e11f;
    public static readonly Vector3 SunDirection = Vector3.Normalize(new(0.6f, 0.3f, -0.74f));
    public static readonly Vector3 PlanetCenter = Vector3.Normalize(new(-1.6f, 0.5f, -2.5f)) * 3.0e7f;
    public static readonly Vector3 StarCenter = SunDirection * StarDistance;
    public static readonly Vector3 StationCenter = new(-8_000f, 3_000f, -50_000f);
    public static readonly Vector3 RedSphere = new(-1.6f, -0.7f, -6f);
    public static readonly Vector3 BlueSphere = new(1.9f, -0.9f, -5f);
    public static readonly Vector3 TexturedBox = new(0.2f, -0.6f, -9f);
    public static readonly Vector3 GreenGlass = new(0.2f, -1.0f, -4f);

    public static readonly Vector3 TallBox = new(-3.6f, -0.5f, -10f);

    public MeshId Box;
    public MeshId Sphere;
    public MeshId Plane;
    public MaterialId Ground;
    public MaterialId Textured;
    public MaterialId Red;
    public MaterialId Blue;
    public MaterialId White;
    public MaterialId Orange;
    public MaterialId Glass;
    public MaterialId Station;
    public MaterialId PlanetMaterial;
    public MaterialId StarMaterial;

    public static TestScene Create(VulkanRenderer renderer)
    {
        var scene = new TestScene
        {
            Box = renderer.CreateMesh(Primitives.Box(new(0.5f))),
            Sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32)),
            Plane = renderer.CreateMesh(Primitives.Plane(1f, 10f))
        };
        TextureId groundChecker = renderer.CreateTexture(TestTextures.Checker(256, 8, (200, 200, 200), (90, 90, 95)));
        TextureId boxChecker = renderer.CreateTexture(TestTextures.Checker(256, 4, (230, 180, 60), (60, 90, 170)));
        TextureId tiles = renderer.CreateTexture(TestTextures.BeveledTiles(256, 8));
        TextureId boxTiles = renderer.CreateTexture(TestTextures.BeveledTiles(256, 4));
        scene.Ground = renderer.CreateMaterial(new(Vector4.One, SpecularIntensity: 0.5f, Gloss: 0.45f,
            Albedo: groundChecker, Normal: tiles, SpecularGloss: TextureId.None));
        scene.Textured = renderer.CreateMaterial(new(Vector4.One, SpecularIntensity: 0.6f, Gloss: 0.6f,
            Albedo: boxChecker, Normal: boxTiles, SpecularGloss: TextureId.None));
        scene.Red = renderer.CreateMaterial(MaterialDesc.Color(new(0.75f, 0.06f, 0.05f, 1), gloss: 0.85f) with
        {
            SpecularIntensity = 1f
        });
        scene.Blue = renderer.CreateMaterial(MaterialDesc.Color(new(0.1f, 0.25f, 0.8f, 1), gloss: 0.2f));
        scene.White = renderer.CreateMaterial(MaterialDesc.Color(new(0.8f, 0.8f, 0.8f, 1), gloss: 0.5f));
        scene.Orange = renderer.CreateMaterial(MaterialDesc.Color(new(0.9f, 0.4f, 0.08f, 1), gloss: 0.7f));
        scene.Glass = renderer.CreateMaterial(MaterialDesc.Color(new(0.2f, 0.9f, 0.3f, 0.45f), gloss: 0.9f) with
        {
            SpecularIntensity = 1f
        });
        scene.Station = renderer.CreateMaterial(MaterialDesc.Color(new(0.7f, 0.7f, 0.75f, 1), gloss: 0.3f));
        scene.PlanetMaterial = renderer.CreateMaterial(MaterialDesc.Color(new(0.25f, 0.4f, 0.6f, 1), gloss: 0.1f) with
        {
            SpecularIntensity = 0.2f
        });
        scene.StarMaterial =
            renderer.CreateMaterial(MaterialDesc.Color(new(1, 1, 1, 1)) with { Emissive = new(60f, 55f, 45f) });
        return scene;
    }

    public RenderSnapshot Snapshot(bool shadows = true)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(SunDirection);
        List<RenderInstance> instances = snapshot.Instances;
        RenderFlags caster = shadows ? RenderFlags.CastsShadow : RenderFlags.None;

        // Near range.
        instances.Add(Gpu.Instance(Plane, Ground, new(0, -1.5f, -40f), new(40f, 1f, 40f), flags: caster));
        instances.Add(Gpu.Instance(Box, Textured, TexturedBox, new(1.8f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f), flags: caster));
        instances.Add(Gpu.Instance(Box, White, TallBox, new(1.2f, 2f, 1.2f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.3f), flags: caster));
        instances.Add(Gpu.Instance(Box, Orange, new(3.8f, -1.0f, -9f), new(1f),
            Quaternion.CreateFromYawPitchRoll(0.4f, 0f, 0f), flags: caster));
        instances.Add(Gpu.Instance(Sphere, Red, RedSphere, new(0.8f), flags: caster));
        instances.Add(Gpu.Instance(Sphere, Blue, BlueSphere, new(0.6f), flags: caster));
        instances.Add(Gpu.Instance(Sphere, Glass, GreenGlass, new(0.5f), flags: RenderFlags.Transparent));

        // Middle range: a 1 km station turned so one face catches the sun.
        instances.Add(Gpu.Instance(Box, Station, StationCenter, new(1_000f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.35f)));

        // Far range.
        // The planet casts a far shadow: an eclipse of the sun for whatever lies behind it.
        instances.Add(Gpu.Instance(Sphere, PlanetMaterial, PlanetCenter, new(PlanetRadius),
            flags: RenderFlags.Celestial | RenderFlags.CastsShadow));
        instances.Add(Gpu.Instance(Sphere, StarMaterial, StarCenter, new(StarRadius), flags: RenderFlags.Celestial));

        snapshot.Lights.Add(new PointRenderLight(
            Position: new(-0.4f, 0.4f, -4.4f),
            Color: new(1f, 0.7f, 0.4f),
            Intensity: 6f,
            Range: 10f));
        Vector3 spotPosition = new(2.5f, 3.5f, -4.2f);
        snapshot.Lights.Add(new SpotRenderLight(
            Position: spotPosition,
            Direction: Vector3.Normalize(BlueSphere - spotPosition),
            Color: new(0.6f, 0.8f, 1f),
            Intensity: 40f,
            Range: 15f,
            InnerConeAngle: 0.25f,
            OuterConeAngle: 0.4f));
        return snapshot;
    }
}
