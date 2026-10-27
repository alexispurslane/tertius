using System.Numerics;
using Tertius.Testing;

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
            Plane = renderer.CreateMesh(Primitives.Plane(1f, 10f)),
        };
        TextureId groundChecker = renderer.CreateTexture(
            TestTextures.Checker(size: 256, tiles: 8, a: (200, 200, 200), b: (90, 90, 95))
        );
        TextureId boxChecker = renderer.CreateTexture(
            TestTextures.Checker(size: 256, tiles: 4, a: (230, 180, 60), b: (60, 90, 170))
        );
        TextureId tiles = renderer.CreateTexture(TestTextures.BeveledTiles(size: 256, tiles: 8));
        TextureId boxTiles = renderer.CreateTexture(TestTextures.BeveledTiles(size: 256, tiles: 4));
        scene.Ground = renderer.CreateMaterial(
            new(
                BaseColor: Vector4.One,
                Reflectance: 0.5f,
                Roughness: 0.42f,
                BaseColorMap: groundChecker,
                NormalMap: tiles,
                MetallicRoughnessMap: TextureId.None
            )
        );
        scene.Textured = renderer.CreateMaterial(
            new(
                BaseColor: Vector4.One,
                Reflectance: 0.6f,
                Roughness: 0.32f,
                BaseColorMap: boxChecker,
                NormalMap: boxTiles,
                MetallicRoughnessMap: TextureId.None
            )
        );
        scene.Red = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.75f, y: 0.06f, z: 0.05f, w: 1), roughness: 0.2f) with
            {
                Reflectance = 1f,
            }
        );
        scene.Blue = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.1f, y: 0.25f, z: 0.8f, w: 1), roughness: 0.65f)
        );
        scene.White = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.38f)
        );
        scene.Orange = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.9f, y: 0.4f, z: 0.08f, w: 1), roughness: 0.26f)
        );
        scene.Glass = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.2f, y: 0.9f, z: 0.3f, w: 0.45f), roughness: 0.18f) with
            {
                Reflectance = 1f,
            }
        );
        scene.Station = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.7f, y: 0.7f, z: 0.75f, w: 1), roughness: 0.55f)
        );
        scene.PlanetMaterial = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.25f, y: 0.4f, z: 0.6f, w: 1), roughness: 0.75f) with
            {
                Reflectance = 0.2f,
            }
        );
        scene.StarMaterial = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.38f) with
            {
                Emissive = new(60f, 55f, 45f),
            }
        );
        return scene;
    }

    public RenderSnapshot Snapshot(bool shadows = true)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(SunDirection);
        List<RenderInstance> instances = snapshot.Instances;
        RenderFlags caster = shadows ? RenderFlags.CastsShadow : RenderFlags.None;

        // Near range.
        instances.Add(
            Gpu.Instance(
                mesh: Plane,
                material: Ground,
                position: new(0, -1.5f, -40f),
                scale: new(40f, 1f, 40f),
                flags: caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Box,
                material: Textured,
                position: TexturedBox,
                scale: new(1.8f),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f),
                flags: caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Box,
                material: White,
                position: TallBox,
                scale: new(1.2f, 2f, 1.2f),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.3f),
                flags: caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Box,
                material: Orange,
                position: new(3.8f, -1.0f, -9f),
                scale: new(1f),
                rotation: Quaternion.CreateFromYawPitchRoll(0.4f, 0f, 0f),
                flags: caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Sphere,
                material: Red,
                position: RedSphere,
                scale: new(0.8f),
                flags: caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Sphere,
                material: Blue,
                position: BlueSphere,
                scale: new(0.6f),
                flags: caster,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Sphere,
                material: Glass,
                position: GreenGlass,
                scale: new(0.5f),
                flags: RenderFlags.Transparent,
                chainDepth: TestChains.CamerasFrame
            )
        );

        // Middle range: a 1 km station turned so one face catches the sun.
        instances.Add(
            Gpu.Instance(
                mesh: Box,
                material: Station,
                position: StationCenter,
                scale: new(1_000f),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.35f),
                chainDepth: TestChains.CamerasFrame
            )
        );

        // Far range.
        // The planet casts a far shadow: an eclipse of the sun for whatever lies behind it.
        instances.Add(
            Gpu.Instance(
                mesh: Sphere,
                material: PlanetMaterial,
                position: PlanetCenter,
                scale: new(PlanetRadius),
                flags: RenderFlags.Celestial | RenderFlags.CastsShadow,
                chainDepth: TestChains.CamerasFrame
            )
        );
        instances.Add(
            Gpu.Instance(
                mesh: Sphere,
                material: StarMaterial,
                position: StarCenter,
                scale: new(StarRadius),
                flags: RenderFlags.Celestial,
                chainDepth: TestChains.CamerasFrame
            )
        );

        snapshot.Lights.Add(
            Gpu.Lamp(
                position: new(-0.4f, 0.4f, -4.4f),
                color: new(1f, 0.7f, 0.4f),
                intensity: 6f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        Vector3 spotPosition = new(2.5f, 3.5f, -4.2f);
        snapshot.Lights.Add(
            Gpu.Spot(
                position: spotPosition,
                direction: Vector3.Normalize(BlueSphere - spotPosition),
                color: new(0.6f, 0.8f, 1f),
                intensity: 40f,
                innerConeAngle: 0.25f,
                outerConeAngle: 0.4f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }
}
