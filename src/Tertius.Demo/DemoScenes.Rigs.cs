using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The radius of each cylinder of the rigs scenes, in meters.</summary>
    private const float RigCylinderRadius = 0.15f;

    /// <summary>The length of each cylinder of the rigs scenes, in meters. Each bends at half of it.</summary>
    private const float RigCylinderLength = 2;

    /// <summary>
    /// The time of one sway of the rigs scenes to each side and back, in seconds: each rig sways to one side 1 s in and to
    /// the other 3 s in, where the script <c>rigs</c> captures them.
    /// </summary>
    private const double RigSwayPeriod = 4;

    /// <summary>The largest bend of a bending cylinder of the rigs scenes, in radians: about 34 degrees.</summary>
    private const float RigBend = 0.6f;

    /// <summary>The largest bend of the twisting cylinder, in radians: about 23 degrees.</summary>
    private const float RigTwistBend = 0.4f;

    /// <summary>The cylinders of the crowd of the rigs scenes stand in a square of this many on a side.</summary>
    private const int RigCrowdSide = 10;

    /// <summary>The distance between two neighbor cylinders of the crowd, in meters.</summary>
    private const double RigCrowdSpacing = 1.2;

    /// <summary>
    /// The phase between two neighbor cylinders of the crowd, in radians: not a simple share of a turn, so that no two of
    /// the 100 sway in step.
    /// </summary>
    private const double RigCrowdPhaseStep = 0.61;

    /// <summary>The base color of the bending cylinders of the rigs scenes, a strong blue.</summary>
    private static readonly Vector4 RigBlue = new(x: 0.15f, y: 0.35f, z: 0.9f, w: 1);

    /// <summary>The half extents of the morphing box, in meters: a cube 0.8 m on a side.</summary>
    private static readonly Vector3 RigBoxHalfExtents = new(0.4f);

    /// <summary>The scenes of skinning and morph targets (docs/plans/skinning-and-morphs.md, "Part 5").</summary>
    private static IEnumerable<Scene> RigScenes => [new("rigs", Rigs), new("rigs-crowd", RigsCrowd)];

    /// <summary>
    /// The review scene of skinning and morph targets (docs/plans/skinning-and-morphs.md, "Part 5"): in the root frame
    /// <c>ground</c>, in daylight, three rigs that <see cref="RigSway"/> sways with a period of 4 s, each to one side 1 s
    /// in and to the other 3 s in.
    /// <list type="bullet">
    /// <item>The <c>floor</c>, a gray slab 12 m square with its top at y = 0.</item>
    /// <item>The <c>bending-cylinder</c>, a blue <see cref="Primitives.BendingCylinder"/> 2 m tall at x = -2, whose top
    /// half bends up to 34 degrees to each side about +Z.</item>
    /// <item>The <c>morphing-box</c>, an orange <see cref="Primitives.MorphingBox"/> 0.8 m on a side at the origin, which
    /// stretches up to twice its height with its Stretch morph and leans its top along ±X with its Lean morph, both at
    /// once: stretched and leaning to +X 1 s in, and leaning to -X with no stretch 3 s in.</item>
    /// <item>The <c>twisting-cylinder</c>, a green <see cref="Primitives.TwistingBendingCylinder"/> 2 m tall at x = 2,
    /// with green and white stripes along it, so that its twist shows. It bends up to 23 degrees about +Z, and it twists
    /// its top by a quarter turn 1 s in and not at all 3 s in.</item>
    /// <item>The <c>lamp</c>, a household bulb that casts shadows, 2.5 m up behind the rigs, and the <c>sun</c>, 1 AU
    /// away up, to +X and a little in front, so that each rig's shadow falls to -X, beside it and away from the next
    /// rig.</item>
    /// <item>The view <c>camera</c> looks down at all three and their shadows from 5.2 m in front and 3.6 m up. The
    /// views <c>box-camera</c> and <c>twist-camera</c> look closely at the box and the twisting cylinder.</item>
    /// </list>
    /// The rigs have no bodies and a bounding radius of 0, so the renderer culls them by their posed bounds.
    /// </summary>
    private static SceneInstance Rigs(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = RigGround(world: world, resources: resources, scene: scene, halfSize: 6);
        AddBendingCylinder(world: world, resources: resources, scene: scene, frame: ground);
        AddMorphingBox(world: world, resources: resources, scene: scene, frame: ground);
        AddTwistingCylinder(world: world, resources: resources, scene: scene, frame: ground);

        scene.Add("lamp", world.Spawn(GlowLamp(surface: ground, resources: resources, position: new(-1, 2.5, -1.5))));
        Star(world: world, scene: scene, frame: ground, center: Double3.Zero, direction: new(45, 40, 15));
        AddCamera(world: world, scene: scene, frame: ground, pose: LookingAt(new(0, 3.6, 5.2), new(-0.4, 0.5, 0)));
        scene.Add("box-camera", world.Spawn(ViewIn(ground, LookingAt(new(0.9, 1.3, 2.6), new(0, 0.6, 0)))));
        scene.Add("twist-camera", world.Spawn(ViewIn(ground, LookingAt(new(2.6, 1.4, 2.8), new(2, 1, 0)))));
        scene.AddView("box-camera");
        scene.AddView("twist-camera");
        return scene;
    }

    /// <summary>The <c>bending-cylinder</c> of <see cref="Rigs"/>.</summary>
    private static void AddBendingCylinder(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle frame
    )
    {
        MeshData cylinder = Primitives.BendingCylinder(radius: RigCylinderRadius, length: RigCylinderLength);
        AddRig(
            world: world,
            scene: scene,
            name: "bending-cylinder",
            frame: frame,
            position: new(-2, 0, 0),
            render: RigDrawn(resources: resources, mesh: cylinder, color: RigBlue),
            sway: BendSway(rest: RestOf(cylinder), bend: RigBend, phase: 0)
        );
    }

    /// <summary>The <c>morphing-box</c> of <see cref="Rigs"/>.</summary>
    private static void AddMorphingBox(World world, IRenderResources resources, SceneInstance scene, FrameHandle frame)
    {
        MeshData box = Primitives.MorphingBox(RigBoxHalfExtents);
        RigSway sway = new()
        {
            Rest = RestOf(box),
            Period = RigSwayPeriod,
            Phase = 0,
            Joints = [],
            // Stretch never goes below 0, which would squash the box through its own floor.
            Morphs = [new(Morph: 0, Center: 0.5f, Amplitude: 0.5f), new(Morph: 1, Center: 0, Amplitude: 1)],
        };
        AddRig(
            world: world,
            scene: scene,
            name: "morphing-box",
            frame: frame,
            position: new(0, RigBoxHalfExtents.Y, 0),
            render: RigDrawn(resources: resources, mesh: box, color: new(x: 1, y: 0.45f, z: 0.1f, w: 1)),
            sway: sway
        );
    }

    /// <summary>The striped <c>twisting-cylinder</c> of <see cref="Rigs"/>.</summary>
    private static void AddTwistingCylinder(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle frame
    )
    {
        MeshData twisting = Primitives.TwistingBendingCylinder(radius: RigCylinderRadius, length: RigCylinderLength);
        RigSway sway = new()
        {
            Rest = RestOf(twisting),
            Period = RigSwayPeriod,
            Phase = 0,
            Joints = [new(Joint: 1, Axis: Vector3.UnitZ, Amplitude: RigTwistBend)],
            // A twist's deltas are linear, so a weight below 0 widens the rings rather than turning them back.
            Morphs = [new(Morph: 0, Center: 0.5f, Amplitude: 0.5f)],
        };
        MaterialId striped = resources.CreateMaterial(
            new(BaseColor: Vector4.One, Roughness: UsualRoughness, BaseColorMap: resources.CreateTexture(RigStripes()))
        );
        AddRig(
            world: world,
            scene: scene,
            name: "twisting-cylinder",
            frame: frame,
            position: new(2, 0, 0),
            render: RigDrawn(resources: resources, mesh: twisting, color: Vector4.One) with
            {
                Material = striped,
            },
            sway: sway
        );
    }

    /// <summary>
    /// The crowd of the rigs scenes, which measures the cost of the deformation (docs/plans/skinning-and-morphs.md,
    /// "Memory and cost"): in the root frame <c>ground</c>, in daylight, 100 blue bending cylinders, <c>cylinder-0</c> to
    /// <c>cylinder-99</c>, of 20,000 vertices each, 1.2 m apart on a square grid round the origin. Each bends as
    /// <c>bending-cylinder</c> of <see cref="Rigs"/> does, with a phase 0.61 rad past its neighbor's. A gray
    /// <c>floor</c> 18 m square, the <c>sun</c> and the <c>lamp</c> light them, and the view <c>camera</c> looks down
    /// across them from a corner.
    /// </summary>
    private static SceneInstance RigsCrowd(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = RigGround(world: world, resources: resources, scene: scene, halfSize: 9);

        // 200 rings of 100 vertices: 20,000 vertices, the size of a human (docs/plans/skinning-and-morphs.md, "Memory and cost").
        MeshData cylinder = Primitives.BendingCylinder(
            radius: RigCylinderRadius,
            length: RigCylinderLength,
            segments: 99,
            rings: 199
        );
        RenderData drawn = RigDrawn(resources: resources, mesh: cylinder, color: RigBlue);
        RigPose rest = RestOf(cylinder);
        double half = (RigCrowdSide - 1) * RigCrowdSpacing / 2;
        for (int i = 0; i < RigCrowdSide * RigCrowdSide; i++)
            AddRig(
                world: world,
                scene: scene,
                name: $"cylinder-{i}",
                frame: ground,
                position: new(i % RigCrowdSide * RigCrowdSpacing - half, 0, i / RigCrowdSide * RigCrowdSpacing - half),
                render: drawn,
                sway: BendSway(rest: rest, bend: RigBend, phase: i * RigCrowdPhaseStep)
            );

        scene.Add("lamp", world.Spawn(GlowLamp(surface: ground, resources: resources, position: new(0, 2.6, 0))));
        Star(world: world, scene: scene, frame: ground, center: Double3.Zero, direction: new(25, 35, 35));
        AddCamera(world: world, scene: scene, frame: ground, pose: LookingAt(new(8, 5, 10), new(0, 0.6, 0)));
        return scene;
    }

    /// <summary>
    /// The root frame <c>ground</c> of a rigs scene, in daylight, with a gray <c>floor</c> of
    /// <paramref name="halfSize"/> meters on each side of the origin.
    /// </summary>
    private static FrameHandle RigGround(World world, IRenderResources resources, SceneInstance scene, float halfSize)
    {
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("ground", ground.Entity);
        world.Ambient = Daylight;
        ShadowFloor(world: world, resources: resources, scene: scene, frame: ground, halfSize: halfSize);
        return ground;
    }

    /// <summary>
    /// A sway of a rig of <see cref="Primitives.BendingCylinder"/>'s joints, whose joint Tip bends up to
    /// <paramref name="bend"/> radians to each side about +Z, from <paramref name="phase"/>.
    /// </summary>
    private static RigSway BendSway(RigPose rest, float bend, double phase) =>
        new()
        {
            Rest = rest,
            Period = RigSwayPeriod,
            Phase = phase,
            Joints = [new(Joint: 1, Axis: Vector3.UnitZ, Amplitude: bend)],
            Morphs = [],
        };

    /// <summary>
    /// The texture of the twisting cylinder: eight green and eight white stripes round the tube, along its length, so that
    /// a twist turns them into a spiral.
    /// </summary>
    private static TextureData RigStripes()
    {
        const int width = 64,
            height = 4;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool green = x / 4 % 2 == 0;
            int i = (y * width + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = green
                ? ((byte)40, (byte)170, (byte)70, (byte)255)
                : ((byte)235, (byte)235, (byte)225, (byte)255);
        }

        return new(Width: width, Height: height, Rgba: rgba);
    }

    /// <summary>The rest pose of the rig of <paramref name="mesh"/>, one of the rigged meshes of <see cref="Primitives"/>.</summary>
    private static RigPose RestOf(MeshData mesh) =>
        mesh.Rigging?.Rig.Rest ?? throw new InvalidOperationException("The mesh of a rig has rigging.");

    /// <summary>
    /// The render row of a rig of <paramref name="mesh"/> in <paramref name="color"/>, which casts shadows. Its bounding
    /// radius of 0 lets the renderer cull it by its posed bound (docs/plans/skinning-and-morphs.md, decision 24).
    /// </summary>
    private static RenderData RigDrawn(IRenderResources resources, MeshData mesh, Vector4 color) =>
        new()
        {
            Mesh = resources.CreateMesh(mesh),
            Material = resources.CreateMaterial(MaterialDesc.Color(color, UsualRoughness)),
            Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
        };

    /// <summary>
    /// Adds the rig <paramref name="name"/> to <paramref name="scene"/>, in <paramref name="frame"/> with its model's origin
    /// at <paramref name="position"/>, drawn by <paramref name="render"/> and swayed by <paramref name="sway"/>, from its
    /// pose at the world's time.
    /// </summary>
    private static void AddRig(
        World world,
        SceneInstance scene,
        string name,
        FrameHandle frame,
        Double3 position,
        RenderData render,
        RigSway sway
    ) =>
        scene.Add(
            name,
            world.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = new(position, DQuaternion.Identity),
                    Render = render,
                    RigPose = sway.PoseAt(world.Time),
                    Components = [sway],
                }
            )
        );
}
