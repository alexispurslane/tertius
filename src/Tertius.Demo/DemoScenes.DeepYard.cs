using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The scene <c>deep-yard</c>: a small cargo yard one light-year from the origin of its frame, where a double steps by
    /// 2 m, that drifts through space at <see cref="DriftSpeed"/>. Its bodies are fast relative to space and slow relative
    /// to each other, so the engine holds them in falling numeric frames that move with the yard, and those frames lie
    /// farther than 10^9 m from space's origin, so the engine nests them under a nesting frame on the grid of 2^20 m. See
    /// docs/plans/numeric-frames.md, "Nesting for vast distances".
    /// <list type="bullet">
    /// <item>The root frame <c>space</c> has no gravity. Its origin is where the light comes from.</item>
    /// <item>The yard's center, <see cref="Center"/> at the start, lies one light-year from space's origin along -Z. The
    /// whole yard moves at <see cref="Drift"/>, 2,000 m/s along -Z, away from the light. All positions and velocities below
    /// are relative to the yard.</item>
    /// <item><c>crate-a</c>, orange, and <c>crate-b</c>, green, are crates of 1 m and 50 kg, 3 m apart center to center
    /// along X, 1.5 m on either side of the yard's center. crate-a moves toward crate-b at 0.1 m/s, and crate-b rests.
    /// Their faces are 2 m apart, so they touch 20 s after the start.</item>
    /// <item>The <c>person</c>, a capsule character 1.8 m tall of 70 kg, floats at rest 24 m from the yard's center along
    /// +Z, and faces the crates, along -Z. Space has no gravity, so W thrusts it toward them (see
    /// <see cref="PersonControl"/>). It is the camera, and its view sees the crates with the light behind it.</item>
    /// </list>
    /// What the nest does here: each falling frame moves 2,000 m/s × 1/60 s = 33.33 m along -Z in each tick. A frame whose
    /// place were a double in space, a light-year out, would round each such step to a multiple of 2 m, 34 m, so it would
    /// move at 2,040 m/s while its members' velocities say 2,000 m/s. Under the nest, each frame's place is its offset from a grid point,
    /// less than 2^20 m, where a double steps by about 1e-10 m, so every step adds without a visible rounding. The merge test between the person's frame and the crates' frame also
    /// reads their places through the nest, in small numbers.
    /// <para>
    /// The yard lies a light-year out along Z, and the crates are apart along X. Spawn positions are in space's
    /// coordinates, and a double steps by 2 m along Z there, so no spawn can place two crates 3 m apart along Z: the engine
    /// has no way to spawn a body more precisely than a double of its semantic frame's coordinates. Along X the
    /// coordinates are small, so the crates sit exactly 3 m apart, and the person's 24 m along Z is even, so it is exact
    /// too.
    /// </para>
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

        /// <summary>The speed of crate-a toward crate-b at the start, along +X, relative to the yard, in m/s.</summary>
        public const double CrateSpeed = 0.1;

        /// <summary>
        /// How fast the whole yard drifts through space along -Z, in m/s. It is twice the 1,000 m/s at which the engine gives
        /// a group of free bodies a falling numeric frame (<c>NumericFrameOptions.CreateSpeed</c>), so the crates' frame
        /// falls with the yard. The person is a character, which always thrusts, so its frame falls from 50 m/s
        /// (<c>NumericFrameOptions.ThrustCreateSpeed</c>).
        /// </summary>
        public const double DriftSpeed = 2_000;

        /// <summary>
        /// How far the person floats from the yard's center along +Z at the start, in meters. It is even, so that the
        /// person's Z, a light-year out, is exact in a double.
        /// </summary>
        public const double PersonDistance = 24;

        /// <summary>The mass of the person, in kilograms.</summary>
        public const float PersonMass = 70;

        /// <summary>The person's capsule: half the length of its straight part, and its radius, in meters. It is 1.8 m tall.</summary>
        public static CapsuleShapeDesc PersonShape { get; } = new(0.6f, 0.3f);

        /// <summary>The yard's center in space at the start, in meters: one light-year from the origin along -Z. It is exact in a double.</summary>
        public static Double3 Center { get; } = new(0, 0, -LightYear);

        /// <summary>The velocity of the whole yard in space, in m/s: <see cref="DriftSpeed"/> along -Z, away from the light.</summary>
        public static Double3 Drift { get; } = new(0, 0, -DriftSpeed);

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle space =
                world.CreateRootFrame(new(FrameBounds.Sphere((float)(2 * LightYear)), GravityField.None));
            scene.Add("space", space.Entity);
            // The light comes from space's origin, behind the person as it faces the crates. A face that the light does
            // not reach gets the same dim light from every direction.
            world.SunFrame = space;
            world.Ambient = new() { SkyColor = new(0.03f), GroundColor = new(0.03f), Intensity = 1 };

            MeshId crateMesh = resources.CreateMesh(Primitives.Box(new(CrateHalfSize)));
            var orange = new CrateSpec(new(CrateHalfSize), CrateMass, crateMesh,
                resources.CreateMaterial(MaterialDesc.Color(new(1, 0.45f, 0.1f, 1))));
            CrateSpec green = orange with
            {
                Material = resources.CreateMaterial(MaterialDesc.Color(new(0.3f, 0.6f, 0.25f, 1)))
            };
            scene.Add("crate-a",
                world.Spawn(orange.Spawn(space, InYard(-CrateSpacing / 2, 0), Drift + new Double3(CrateSpeed, 0, 0))));
            scene.Add("crate-b", world.Spawn(green.Spawn(space, InYard(CrateSpacing / 2, 0), Drift)));

            // The capsule is 1.8 m tall, and the eye is 0.75 m above its center.
            scene.Add("person", world.Spawn(new()
            {
                Frame = space,
                Pose = InYard(0, PersonDistance),
                LinearVelocity = Drift,
                Body = new(PersonShape, BodyMotion.Character, Mass: PersonMass),
                Render = Drawn(resources, Primitives.Box(new(0.3f, 0.9f, 0.3f)), new(0.2f, 0.45f, 0.8f, 1),
                    boundingRadius: 1),
                Components = [new PersonControl(new(0, 0.75f, 0))],
                Subscriptions = [Topics.Input]
            }));
            scene.AddView("person");
            world.Camera = scene["person"];
            return scene;
        }

        /// <summary>A pose at <paramref name="x"/> and <paramref name="z"/> from the yard's center, in meters, facing -Z.</summary>
        private static DPose InYard(double x, double z) => new(Center + new Double3(x, 0, z), DQuaternion.Identity);
    }
}
