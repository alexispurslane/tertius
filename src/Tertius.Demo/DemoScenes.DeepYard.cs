using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The scene <c>deep-yard</c>: a small cargo yard one light-year from the origin of its frame, where a double steps by
    /// 2 m. Its bodies are slow and far from the origin, so the engine holds them in resting numeric frames, and those
    /// frames lie farther than 10^9 m from the origin, so a nesting frame on the grid of 2^20 m holds them. Only through
    /// that nesting frame do the reads between the yard's bodies, and the moves of a body from one numeric frame to
    /// another, keep millimeter steps. See docs/plans/numeric-frames.md, "Nesting for vast distances".
    /// <list type="bullet">
    /// <item>The root frame <c>space</c> has no gravity. Its origin is where the light comes from.</item>
    /// <item>The yard's center, <see cref="Center"/>, lies one light-year from space's origin along -Z.</item>
    /// <item><c>crate-a</c>, orange, and <c>crate-b</c>, green, are crates of 1 m and 50 kg, 3 m apart center to center
    /// along X, 1.5 m on either side of the yard's center. crate-a moves toward crate-b at 0.1 m/s, and crate-b rests.
    /// Their faces are 2 m apart, so they touch 20 s after the start.</item>
    /// <item>The <c>person</c>, a capsule character 1.8 m tall of 70 kg, floats at rest 24 m from the yard's center along
    /// +Z, and faces the crates, along -Z. Space has no gravity, so W thrusts it toward them (see
    /// <see cref="PersonControl"/>). It is the camera, and its view sees the crates with the light behind it.</item>
    /// </list>
    /// The yard lies a light-year out along Z, and the crates are apart along X. A double steps by 2 m along Z there, so
    /// the scene could not place two crates 3 m apart along Z: its spawn positions are in space's coordinates. Along X the
    /// coordinates are small, so the crates sit exactly 3 m apart, and the person exactly 24 m from the center.
    /// </summary>
    public static class DeepYard
    {
        /// <summary>One light-year, in meters. A double steps by 2 m there.</summary>
        public const double LightYear = 9.4607e15;

        /// <summary>The distance between the crates' centers along X, in meters.</summary>
        public const double CrateSpacing = 3;

        /// <summary>Half the size of each crate along each of its axes, in meters.</summary>
        public const float CrateHalfSize = 0.5f;

        /// <summary>The mass of each crate, in kilograms.</summary>
        public const float CrateMass = 50;

        /// <summary>The speed of crate-a toward crate-b at the start, along +X, in m/s.</summary>
        public const double CrateSpeed = 0.1;

        /// <summary>
        /// How far the person floats from the yard's center along +Z at the start, in meters. It is even, so that the
        /// person's Z, a light-year out, is exact in a double.
        /// </summary>
        public const double PersonDistance = 24;

        /// <summary>The mass of the person, in kilograms.</summary>
        public const float PersonMass = 70;

        /// <summary>The yard's center in space, in meters: one light-year from the origin along -Z. It is exact in a double.</summary>
        public static Double3 Center { get; } = new(0, 0, -LightYear);

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere((float)(2 * LightYear)), MotionDriver.Static, GravityField.None));
            scene.Add("space", space.Entity);
            // The light comes from space's origin, behind the person as it faces the crates. A face that the light does
            // not reach gets the same dim light from every direction.
            world.SunFrame = space;
            world.Ambient = new AmbientLight { SkyColor = new Vector3(0.03f), GroundColor = new Vector3(0.03f), Intensity = 1 };

            MeshId crateMesh = resources.CreateMesh(Primitives.Box(new Vector3(CrateHalfSize)));
            var orange = new CrateSpec(new Vector3(CrateHalfSize), CrateMass, crateMesh, resources.CreateMaterial(MaterialDesc.Color(new Vector4(1, 0.45f, 0.1f, 1))));
            var green = orange with { Material = resources.CreateMaterial(MaterialDesc.Color(new Vector4(0.3f, 0.6f, 0.25f, 1))) };
            scene.Add("crate-a", world.Spawn(orange.Spawn(space, InYard(-CrateSpacing / 2, 0), new Double3(CrateSpeed, 0, 0))));
            scene.Add("crate-b", world.Spawn(green.Spawn(space, InYard(CrateSpacing / 2, 0), Double3.Zero)));

            // The capsule is 1.8 m tall, and the eye is 0.75 m above its center.
            scene.Add("person", world.Spawn(new SpawnDesc
            {
                Frame = space,
                Pose = InYard(0, PersonDistance),
                Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: PersonMass),
                Render = Drawn(resources, Primitives.Box(new Vector3(0.3f, 0.9f, 0.3f)), new Vector4(0.2f, 0.45f, 0.8f, 1), boundingRadius: 1),
                Components = [new PersonControl(new Vector3(0, 0.75f, 0))],
                Subscriptions = [Topics.Input],
            }));
            scene.AddView("person");
            world.Camera = scene["person"];
            return scene;
        }

        /// <summary>A pose at <paramref name="x"/> and <paramref name="z"/> from the yard's center, in meters, facing -Z.</summary>
        private static DPose InYard(double x, double z) => new(Center + new Double3(x, 0, z), DQuaternion.Identity);
    }
}
