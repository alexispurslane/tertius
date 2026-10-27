using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The radius of the skin head of the skin-and-hair scene, in meters: the size of a head.</summary>
    private const float SkinHeadRadius = 0.1f;

    /// <summary>The radius of the skin ear of the skin-and-hair scene, in meters: an ear is about 6 cm tall.</summary>
    private const float SkinEarRadius = 0.03f;

    /// <summary>The thickness of the skin ear, in meters.</summary>
    private const float SkinEarThickness = 0.003f;

    /// <summary>
    /// The radiant power of the lamp behind the ear, in W, so that it outshines the sunlit floor around the ear.
    /// </summary>
    private const float EarLampPower = 60;

    /// <summary>The radius of the lamp behind the ear, in meters.</summary>
    private const float EarLampRadius = 0.008f;

    /// <summary>Half the width of each strand card, in meters.</summary>
    private const float StrandCardHalfWidth = 0.0375f;

    /// <summary>The length of each strand card from its root to its tip, along its v, in meters.</summary>
    private const float StrandCardLength = 0.3f;

    /// <summary>The cards of one fan of the skin-and-hair scene.</summary>
    private const int StrandFanCards = 12;

    /// <summary>The cutoff of the fringed and the cutout strand materials.</summary>
    private const float StrandCutoff = 0.5f;

    /// <summary>The strand image's width, across the strands, along u, in texels.</summary>
    private const int StrandImageWidth = 64;

    /// <summary>The strand image's height, along the strands, along v, in texels: the card's aspect, 1 : 4.</summary>
    private const int StrandImageHeight = 256;

    /// <summary>The strands of the strand image.</summary>
    private const int StrandImageStrands = 18;

    /// <summary>
    /// The sun of the skin-and-hair scene: from +X, 14° above the horizon. It lights the head from its side, the hair
    /// fans from the front, and the ear from behind.
    /// </summary>
    private static readonly Double3 SkinAndHairToSun = new(1, 0.25, 0);

    /// <summary>
    /// The skin color of the skin-and-hair scene: sRGB (224, 172, 150), decoded, the color of the skin tests.
    /// </summary>
    private static readonly Vector4 SkinColor = new(x: 0.745f, y: 0.413f, z: 0.305f, w: 1);

    /// <summary>The dark brown base color of the strand cards, linear.</summary>
    private static readonly Vector4 DarkBrownHair = new(x: 0.08f, y: 0.045f, z: 0.025f, w: 1);

    /// <summary>The light brown base color of the strand cards, linear.</summary>
    private static readonly Vector4 LightBrownHair = new(x: 0.45f, y: 0.28f, z: 0.15f, w: 1);

    /// <summary>
    /// The fans of the skin-and-hair scene, each 1.2 m from the next along Z: fringed hair, cutout hair, and glass of
    /// the standard model, the cards' look before the epic.
    /// </summary>
    private static readonly StrandFan[] StrandFans =
    [
        new(
            Name: "hair-fringed",
            Center: new(-3, 1.3, 4.8),
            Model: new HairShading(StrandDirection.PlusV),
            Alpha: new FringedAlpha(StrandCutoff)
        ),
        new(
            Name: "hair-cutout",
            Center: new(-3, 1.3, 6.0),
            Model: new HairShading(StrandDirection.PlusV),
            Alpha: new CutoutAlpha(StrandCutoff)
        ),
        new(Name: "hair-glass", Center: new(-3, 1.3, 7.2), Model: new StandardShading(0), Alpha: new GlassAlpha()),
    ];

    /// <summary>
    /// The review scene of skin and hair (docs/plans/skin-and-hair.md, "Part 6"), on the gray floor of
    /// <see cref="EarthSurface"/> under the sun along <see cref="SkinAndHairToSun"/>, with the sky of the hair card
    /// scenes.
    /// <list type="bullet">
    /// <item>The view <c>head-camera</c> looks along -Z at the head, a skin sphere with a nose
    /// (<see cref="SkinHead"/>), lit from its side by the sun.</item>
    /// <item>The view <c>ear-camera</c> looks along +X, toward the sun, at the ear, a thin skin disk with a lamp behind
    /// it (<see cref="SkinEar"/>).</item>
    /// <item>The views <c>hair-fringed-camera</c>, <c>hair-cutout-camera</c> and <c>hair-glass-camera</c> each look
    /// along -X, with the sun behind them, at one fan of twelve strand cards (<see cref="StrandFanBlock"/>): fringed
    /// hair, cutout hair, and glass of the standard model, the cards' look before the epic. A flat glass pane of the
    /// strand image stands beside the fringed fan. The view <c>hair-camera</c> shows the three fans and the pane
    /// together.</item>
    /// </list>
    /// </summary>
    private static SceneInstance SkinAndHair(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: new(10, 0.5f, 10)
        );
        ReviewSky(world);
        world.VerticalFov = 40 * MathF.PI / 180;
        Star(world: world, scene: scene, frame: surface, center: AboveFloor(0, 0, 0), direction: SkinAndHairToSun);

        MaterialId skin = resources.CreateMaterial(
            new(BaseColor: SkinColor, Roughness: 0.5f, Model: new SkinShading())
        );
        Entity headCamera = SkinHead(world: world, resources: resources, scene: scene, surface: surface, skin: skin);
        SkinEar(world: world, resources: resources, scene: scene, surface: surface, skin: skin);
        SpawnStrandFans(world: world, resources: resources, scene: scene, surface: surface);
        world.Camera = headCamera;
        return scene;
    }

    /// <summary>
    /// The entities <c>head</c>, a skin sphere of <see cref="SkinHeadRadius"/> 1.5 m above the floor, and <c>nose</c>,
    /// a small skin sphere in front of it, and the view <c>head-camera</c> 0.45 m in front along +Z, where the head
    /// fills about a fifth of the frame. The sun's rays are parallel, so the terminator is the plane through the head's
    /// center across them: a spot light at a finite distance moved it by about 9 px in the tests of the scatter.
    /// </summary>
    private static Entity SkinHead(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle surface,
        MaterialId skin
    )
    {
        Double3 center = AboveFloor(0, 1.5, 3);
        scene.Add(
            "head",
            world.Spawn(
                SkinPart(
                    surface: surface,
                    resources: resources,
                    mesh: Primitives.Sphere(SkinHeadRadius),
                    skin: skin,
                    pose: new(center, DQuaternion.Identity)
                )
            )
        );
        scene.Add(
            "nose",
            world.Spawn(
                SkinPart(
                    surface: surface,
                    resources: resources,
                    mesh: Primitives.Sphere(0.015f),
                    skin: skin,
                    pose: new(center + new Double3(0, -0.01, 0.11), DQuaternion.Identity)
                )
            )
        );
        return SpawnView(
            world: world,
            scene: scene,
            surface: surface,
            name: "head-camera",
            eye: center + new Double3(0, 0, 0.45),
            target: center
        );
    }

    /// <summary>
    /// The entities <c>ear</c>, a skin disk of <see cref="SkinEarRadius"/> and <see cref="SkinEarThickness"/> that
    /// faces -X, and <c>ear-lamp</c>, a glowing lamp of <see cref="EarLampPower"/> 5 cm behind it along +X, at its
    /// upper rim, so that the lamp peeks over the rim. The view <c>ear-camera</c> looks at the ear from 0.25 m along
    /// -X, toward the lamp and the sun. No light passes through thin skin (docs/plans/skin-and-hair.md, settled fork
    /// 1), so the ear's front shows only its ambient light and its scatter.
    /// </summary>
    private static void SkinEar(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle surface,
        MaterialId skin
    )
    {
        Double3 center = AboveFloor(3, 1.5, 6);
        // The disk mesh faces +Z: a quarter turn about +Y the other way faces it along -X.
        var facingMinusX = DQuaternion.FromAxisAngle(Double3.UnitY, -Math.PI / 2);
        scene.Add(
            "ear",
            world.Spawn(
                SkinPart(
                    surface: surface,
                    resources: resources,
                    mesh: SkinDisk(SkinEarRadius, SkinEarThickness),
                    skin: skin,
                    pose: new(center, facingMinusX)
                )
            )
        );
        var lamp = new PointLightData(
            Offset: new(Vector3.Zero, Quaternion.Identity),
            Color: Vector3.One,
            Power: EarLampPower,
            Radius: EarLampRadius,
            CastsShadow: true
        );
        scene.Add(
            "ear-lamp",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(center + new Double3(0.05, 0.035, 0), DQuaternion.Identity),
                    Render = new RenderData
                    {
                        Mesh = resources.CreateMesh(Primitives.Sphere(EarLampRadius)),
                        Material = resources.CreateMaterial(
                            new(
                                BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                                Roughness: UsualRoughness,
                                Reflectance: 0,
                                Emissive: lamp.SurfaceRadiance
                            )
                        ),
                        BoundingRadius = EarLampRadius,
                        Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                        Light = lamp,
                    },
                }
            )
        );
        SpawnView(
            world: world,
            scene: scene,
            surface: surface,
            name: "ear-camera",
            eye: center - new Double3(0.25, 0, 0),
            target: center
        );
    }

    /// <summary>
    /// The three fans of <see cref="StrandFans"/>, each with its view <c>{name}-camera</c> 0.8 m in front of it
    /// along +X, 0.1 m up and 0.3 m to its side, the flat glass pane <c>hair-pane</c> beside the fringed fan, and the
    /// view <c>hair-camera</c> on all of them. The cards alternate between <see cref="DarkBrownHair"/> and
    /// <see cref="LightBrownHair"/>, each with the strand image of <see cref="StrandImage"/>.
    /// </summary>
    private static void SpawnStrandFans(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle surface
    )
    {
        TextureId strands = resources.CreateTexture(StrandImage());
        MeshId card = resources.CreateMesh(StrandCard());
        MaterialId StrandMaterial(Vector4 color, ShadingModel model, AlphaMode alpha) =>
            resources.CreateMaterial(
                new(BaseColor: color, Roughness: 0.35f, BaseColorMap: strands, Model: model, Alpha: alpha)
            );

        foreach (StrandFan fan in StrandFans)
        {
            Double3 center = AboveFloor(fan.Center.X, fan.Center.Y, fan.Center.Z);
            StrandFanBlock(
                world: world,
                scene: scene,
                surface: surface,
                name: fan.Name,
                mesh: card,
                materials:
                [
                    StrandMaterial(DarkBrownHair, fan.Model, fan.Alpha),
                    StrandMaterial(LightBrownHair, fan.Model, fan.Alpha),
                ],
                center: center
            );
            SpawnView(
                world: world,
                scene: scene,
                surface: surface,
                name: $"{fan.Name}-camera",
                eye: center + new Double3(0.8, 0.1, 0.3),
                target: center
            );
        }

        SpawnStrandPane(
            world: world,
            scene: scene,
            surface: surface,
            card: card,
            material: StrandMaterial(LightBrownHair, new StandardShading(0), new GlassAlpha())
        );
        SpawnView(
            world: world,
            scene: scene,
            surface: surface,
            name: "hair-camera",
            eye: AboveFloor(-0.2, 1.6, 5.6),
            target: AboveFloor(-3, 1.3, 5.6)
        );
    }

    /// <summary>
    /// The flat glass pane <c>hair-pane</c> beside the fringed fan: one strand <paramref name="card"/>, twice the size, of
    /// <paramref name="material"/>, facing +X.
    /// </summary>
    private static void SpawnStrandPane(
        World world,
        SceneInstance scene,
        FrameHandle surface,
        MeshId card,
        MaterialId material
    )
    {
        Double3 pane = AboveFloor(-3, 1.3, 3.9);
        scene.Add(
            "hair-pane",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(pane, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
                    Render = new RenderData
                    {
                        Mesh = card,
                        Material = material,
                        Scale = new(2),
                        BoundingRadius = 2 * StrandCardBoundingRadius,
                        Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                    },
                }
            )
        );
    }

    /// <summary>
    /// The cards <c>{name}-0</c> to <c>{name}-11</c> of one fan about <paramref name="center"/>, facing +X. In the
    /// fan's own axes, which face +Z before the quarter turn about +Y: card i hangs from its root 0.15 m above the
    /// center, the roots 1.2 cm apart along X, turned in its plane by (i − 5.5) · 6°, so the fan spreads toward its
    /// tips, turned about its strand by ±20°, alternately, so that no two neighbors share a plane, and set back 5 mm
    /// behind the card before it. The cards take <paramref name="materials"/> in turn.
    /// </summary>
    private static void StrandFanBlock(
        World world,
        SceneInstance scene,
        FrameHandle surface,
        string name,
        MeshId mesh,
        MaterialId[] materials,
        Double3 center
    )
    {
        var facingPlusX = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
        for (int i = 0; i < StrandFanCards; i++)
        {
            double spread = (i - (StrandFanCards - 1) / 2.0) * 6 * Math.PI / 180;
            double twist = (i % 2 == 0 ? 20 : -20) * Math.PI / 180;
            DQuaternion turn =
                facingPlusX
                * DQuaternion.FromAxisAngle(Double3.UnitZ, spread)
                * DQuaternion.FromAxisAngle(Double3.UnitY, twist);
            var root = new Double3((i - (StrandFanCards - 1) / 2.0) * 0.012, StrandCardLength / 2, -0.005 * i);
            // The card's mesh is centered on its middle, half a card below its root along its own -Y.
            Double3 cardCenter =
                center + facingPlusX.Rotate(root) + turn.Rotate(new Double3(0, -StrandCardLength / 2, 0));
            scene.Add(
                $"{name}-{i}",
                world.Spawn(
                    new()
                    {
                        Frame = surface,
                        Pose = new(cardCenter, turn),
                        Render = new RenderData
                        {
                            Mesh = mesh,
                            Material = materials[i % materials.Length],
                            BoundingRadius = StrandCardBoundingRadius,
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
        }
    }

    /// <summary>The radius of a strand card's bounding sphere about its middle, in meters.</summary>
    private static float StrandCardBoundingRadius =>
        MathF.Sqrt(StrandCardHalfWidth * StrandCardHalfWidth + StrandCardLength * StrandCardLength / 4);

    /// <summary>
    /// The point <paramref name="up"/> meters above the floor at (<paramref name="x"/>, <paramref name="z"/>), in the
    /// surface frame.
    /// </summary>
    private static Double3 AboveFloor(double x, double up, double z) => new(x, EarthRadius + up, z);

    /// <summary>
    /// The view <paramref name="name"/> in <paramref name="surface"/> at <paramref name="eye"/>, looking at <paramref
    /// name="target"/>.
    /// </summary>
    private static Entity SpawnView(
        World world,
        SceneInstance scene,
        FrameHandle surface,
        string name,
        Double3 eye,
        Double3 target
    )
    {
        Entity view = world.Spawn(ViewIn(surface, LookingAt(eye, target)));
        scene.Add(name, view);
        scene.AddView(name);
        return view;
    }

    /// <summary>
    /// A static skin part in <paramref name="surface"/> at <paramref name="pose"/>, which casts shadows.
    /// </summary>
    private static SpawnDesc SkinPart(
        FrameHandle surface,
        IRenderResources resources,
        MeshData mesh,
        MaterialId skin,
        DPose pose
    ) =>
        new()
        {
            Frame = surface,
            Pose = pose,
            Render = new RenderData
            {
                Mesh = resources.CreateMesh(mesh),
                Material = skin,
                BoundingRadius = mesh.Vertices.Max(vertex => vertex.Position.Length()),
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
            },
        };

    /// <summary>
    /// A closed disk of <paramref name="radius"/> and <paramref name="thickness"/> about +Z, centered on the origin: a
    /// cap that faces +Z, a cap that faces -Z and a rim, each with its own normals, so that its edge stays sharp.
    /// </summary>
    private static MeshData SkinDisk(float radius, float thickness)
    {
        const int segments = 48;
        float half = thickness / 2;
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        Vertex At(Vector3 position, Vector3 normal, Vector3 tangent) =>
            new()
            {
                Position = position,
                Normal = normal,
                Tangent = new(tangent, 1),
                TexCoord = new(0.5f + position.X / (2 * radius), 0.5f - position.Y / (2 * radius)),
            };

        foreach (float side in (ReadOnlySpan<float>)[1, -1])
        {
            uint middle = (uint)vertices.Count;
            vertices.Add(At(new(0, 0, side * half), new(0, 0, side), new(side, 0, 0)));
            for (int k = 0; k < segments; k++)
            {
                float angle = 2 * MathF.PI * k / segments;
                vertices.Add(
                    At(
                        new(radius * MathF.Cos(angle), radius * MathF.Sin(angle), side * half),
                        new(0, 0, side),
                        new(side, 0, 0)
                    )
                );
            }

            for (uint k = 0; k < segments; k++)
            {
                uint next = (k + 1) % segments;
                // Counter-clockwise seen from the side that the cap faces.
                indices.AddRange(
                    side > 0 ? [middle, middle + 1 + k, middle + 1 + next] : [middle, middle + 1 + next, middle + 1 + k]
                );
            }
        }

        uint rim = (uint)vertices.Count;
        for (int k = 0; k < segments; k++)
        {
            float angle = 2 * MathF.PI * k / segments;
            var outward = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
            var along = new Vector3(-outward.Y, outward.X, 0);
            vertices.Add(At(radius * outward + new Vector3(0, 0, -half), outward, along));
            vertices.Add(At(radius * outward + new Vector3(0, 0, half), outward, along));
        }

        for (uint k = 0; k < segments; k++)
        {
            uint back = rim + 2 * k;
            uint nextBack = rim + 2 * ((k + 1) % segments);
            indices.AddRange([back, nextBack, nextBack + 1, back, nextBack + 1, back + 1]);
        }

        return new([.. vertices], [.. indices]);
    }

    /// <summary>
    /// One strand card: a rectangle of twice <see cref="StrandCardHalfWidth"/> by <see cref="StrandCardLength"/> in the
    /// XY plane, facing +Z, centered on the origin, with its root at the top. u grows along +X and v from 0 at the root
    /// to 1 at the tip, along -Y, so its strands run along +v: the plane mesh of <see cref="Primitives.Plane"/> turned
    /// a quarter about +X, as <see cref="HairCardFan"/> stands it up.
    /// </summary>
    private static MeshData StrandCard()
    {
        const float half = StrandCardLength / 2;
        static Vertex V(float x, float y, float u, float v) =>
            new()
            {
                Position = new(x, y, 0),
                Normal = Vector3.UnitZ,
                Tangent = new(x: 1, y: 0, z: 0, w: 1),
                TexCoord = new(u, v),
            };

        return new(
            [
                V(x: -StrandCardHalfWidth, y: -half, u: 0, v: 1),
                V(x: StrandCardHalfWidth, y: -half, u: 1, v: 1),
                V(x: StrandCardHalfWidth, y: half, u: 1, v: 0),
                V(x: -StrandCardHalfWidth, y: half, u: 0, v: 0),
            ],
            [0, 1, 2, 0, 2, 3]
        );
    }

    /// <summary>
    /// The strand image, <see cref="StrandImageWidth"/> by <see cref="StrandImageHeight"/> texels:
    /// <see cref="StrandImageStrands"/> strands along +v, each 4 to 8 texels wide at a place across the image that a
    /// hash of its number gives. A strand's alpha falls from 1 on its axis to 0 at its sides as cos², so it is soft
    /// across, and from 1 to 0 over the last 35 % of the card before its own tip, which lies between 70 % and 100 % of
    /// the card. A texel takes the alpha of the strongest strand over it, and the gray, 0.7 to 1, of that strand, so
    /// the strands differ a little in brightness. The base color tints the gray.
    /// </summary>
    private static TextureData StrandImage()
    {
        byte[] rgba = new byte[StrandImageWidth * StrandImageHeight * 4];
        for (int y = 0; y < StrandImageHeight; y++)
        for (int x = 0; x < StrandImageWidth; x++)
        {
            double v = (y + 0.5) / StrandImageHeight;
            (double alpha, double gray) = (0, 0.85);
            for (int k = 0; k < StrandImageStrands; k++)
            {
                double axis = 3 + StrandHash(k, salt: 1) * (StrandImageWidth - 6);
                double halfWidth = 2 + 2 * StrandHash(k, salt: 2);
                double tip = 0.7 + 0.3 * StrandHash(k, salt: 3);
                double across = Math.Abs(x + 0.5 - axis) / halfWidth;
                double toTip = Math.Clamp((tip - v) / 0.35, 0, 1);
                double strand =
                    across < 1 ? Math.Pow(Math.Cos(Math.PI / 2 * across), 2) * toTip * toTip * (3 - 2 * toTip) : 0;
                if (strand > alpha)
                    (alpha, gray) = (strand, 0.7 + 0.3 * StrandHash(k, salt: 4));
            }

            int i = (y * StrandImageWidth + x) * 4;
            byte shade = (byte)Math.Round(255 * gray);
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (shade, shade, shade, (byte)Math.Round(255 * alpha));
        }

        return new(Width: StrandImageWidth, Height: StrandImageHeight, Rgba: rgba);
    }

    /// <summary>
    /// A number in [0, 1) from <paramref name="strand"/> and <paramref name="salt"/>, the same on every run.
    /// </summary>
    private static double StrandHash(int strand, int salt)
    {
        uint h = (uint)strand * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h / 4294967296.0;
    }

    /// <summary>
    /// One fan of the skin-and-hair scene: its name, its center in meters above the floor, and its cards' model and
    /// alpha mode.
    /// </summary>
    private readonly record struct StrandFan(string Name, Double3 Center, ShadingModel Model, AlphaMode Alpha);
}
