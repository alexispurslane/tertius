using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A weightless character that pushes a free crate (ticket ter-1w06), on the World and the real Jolt backend. The frame
/// has no gravity and no numeric frames. The character is a capsule of half height 0.6 m and radius 0.3 m with 70 kg, and
/// the crate is a dynamic cube with sides of 1 m and 20 kg. Neither body has damping. The character starts behind the crate
/// along +x, with a gap between the capsule and the crate's face. A gap of 0.02 m is the padding that a Jolt character keeps
/// from what it touches, so the two touch from the start.
/// </summary>
/// <remarks>
/// The pair has no outside force but the thrust on the character, so its momentum must follow the thrust, and the two must
/// move together as one body of 90 kg. A force that a component writes during a tick acts in the physics step of the next
/// tick, so a test counts the thrust steps as the calls of the pilot before each tick.
/// </remarks>
public class WeightlessCharacterPushTests
{
    private const float PersonMass = 70;
    private const float CrateMass = 20;
    private const float PairMass = PersonMass + CrateMass;

    /// <summary>The thrust on the character along +x, in newtons. It is above the character's strength of 100 N.</summary>
    private const float Thrust = 105;

    /// <summary>The distance from the character's center to its front, and from the crate's center to its rear face, in m.</summary>
    private const double PersonRadius = 0.3, CrateHalf = 0.5;

    private static readonly BodyDesc Person =
        new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: PersonMass, LinearDamping: 0, AngularDamping: 0);

    private static readonly BodyDesc Crate =
        new(new BoxShapeDesc(new Vector3(0.5f)), Mass: CrateMass, LinearDamping: 0, AngularDamping: 0);

    /// <summary>Membership rules under which no group of bodies ever gets a numeric frame.</summary>
    private static readonly NumericFrameOptions WithoutNumericFrames = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        CreateDistance = double.PositiveInfinity,
        LeaveDistance = double.PositiveInfinity,
    };

    /// <summary>A character behind a crate in a frame without gravity, and the pilot that steers the character.</summary>
    private sealed class Pair : IDisposable
    {
        public required World World { get; init; }
        public required FrameHandle Space { get; init; }
        public required Entity Person { get; init; }
        public required Entity Crate { get; init; }
        public required Pilot Pilot { get; init; }

        /// <summary>How many physics steps the thrust has acted in, after the last tick.</summary>
        public int ThrustSteps { get; private set; }

        private int pilotCalls;

        /// <summary>From the next tick on, the pilot adds <see cref="Thrust"/> along +x to the character in every tick.</summary>
        public void StartThrust() => Pilot.EveryTick = body =>
        {
            body.AddForce(new Vector3(Thrust, 0, 0));
            pilotCalls++;
        };

        /// <summary>Ticks once. Every call of the pilot before this tick acts in a step up to this tick's step.</summary>
        public void Tick()
        {
            ThrustSteps = pilotCalls;
            World.Tick();
        }

        public double PersonX => World.PoseIn(Person, Space).Position.X;
        public double CrateX => World.PoseIn(Crate, Space).Position.X;
        public Double3 PersonVelocity => World.VelocityIn(Person, Space);
        public Double3 CrateVelocity => World.VelocityIn(Crate, Space);

        public void Dispose() => World.Dispose();
    }

    /// <summary>
    /// A character at the origin and a crate ahead of it along +x, <paramref name="startGap"/> m apart, both with
    /// <paramref name="commonVelocity"/>.
    /// </summary>
    private static Pair CharacterBehindCrate(double startGap, Double3 commonVelocity = default)
    {
        World world = NewWorld(out _, configure: options => options.NumericFrames = WithoutNumericFrames);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        var pilot = new Pilot();
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(0), LinearVelocity = commonVelocity, Body = Person, Components = [pilot],
        });
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(PersonRadius + startGap + CrateHalf), LinearVelocity = commonVelocity, Body = Crate,
        });
        return new Pair { World = world, Space = space, Person = person, Crate = crate, Pilot = pilot };
    }

    /// <summary>
    /// The velocity along x of a pair of 90 kg after <paramref name="thrustSteps"/> steps of <see cref="Thrust"/>, in m/s.
    /// </summary>
    private static double PairVelocity(int thrustSteps, double dt) => Thrust / PairMass * thrustSteps * dt;

    /// <summary>
    /// The character presses into the crate with a thrust of 105 N for 2 s, from a start in touch. A pair of 90 kg moves
    /// about 2.3 m in that time, and the crate moves with it. Today the crate never moves: Jolt cancels the character's
    /// velocity into the crate's face before it moves the character, so Jolt never pushes.
    /// </summary>
    [Fact]
    public void AWeightlessCharacterThatPressesIntoAFreeCrateMovesIt()
    {
        using Pair pair = CharacterBehindCrate(startGap: 0.02);
        pair.Tick();
        double dt = pair.World.DeltaTime;
        double crateStart = pair.CrateX;
        pair.StartThrust();

        double pairPath = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            pair.Tick();
            pairPath += PairVelocity(pair.ThrustSteps, dt) * dt;
        }

        (pair.CrateX - crateStart).Should().BeGreaterThan(0.8 * pairPath,
            $"a pair of 90 kg under {Thrust} N moves {pairPath:F2} m in 2 s, and the crate is pushed along with the character");
    }

    /// <summary>
    /// The character flies 0.18 m into the crate under a thrust of 105 N and goes on pressing. The thrust is the only outside
    /// force, so the momentum of the pair, from the velocity reads, is the thrust's impulse. Today Jolt gives the crate an
    /// impulse while the engine keeps the character's whole velocity, so the pair gains about 20 % more momentum than the
    /// thrust gave it.
    /// </summary>
    [Fact]
    public void ThePairsMomentumFollowsTheThrust()
    {
        using Pair pair = CharacterBehindCrate(startGap: 0.2);
        pair.Tick();
        double dt = pair.World.DeltaTime;
        pair.StartThrust();

        Tick(pair, 120);

        double momentum = PersonMass * pair.PersonVelocity.X + CrateMass * pair.CrateVelocity.X;
        double impulse = Thrust * pair.ThrustSteps * dt;
        momentum.Should().BeApproximately(impulse, 0.02 * impulse,
            $"the thrust gave the pair {impulse:F1} N·s, and the contact between the character and the crate adds none " +
            $"(character {pair.PersonVelocity.X:F3} m/s, crate {pair.CrateVelocity.X:F3} m/s)");
    }

    /// <summary>
    /// The character presses into the crate with a thrust of 105 N for 2 s, from a start in touch. The velocity that the World
    /// reads for it is the velocity that it moves with: its path in the last tick over the tick's length. Today the read
    /// grows as if the character were free, 3 m/s after 2 s, while the character does not move.
    /// </summary>
    [Fact]
    public void TheVelocityReadOfAWeightlessCharacterThatPressesIntoACrateAgreesWithItsMotion()
    {
        using Pair pair = CharacterBehindCrate(startGap: 0.02);
        pair.Tick();
        double dt = pair.World.DeltaTime;
        pair.StartThrust();
        Tick(pair, 119);
        double before = pair.PersonX;

        pair.Tick();

        double moved = (pair.PersonX - before) / dt;
        double read = pair.PersonVelocity.X;
        read.Should().BeApproximately(moved, 0.02 * PairVelocity(pair.ThrustSteps, dt),
            $"the character moved {moved:F3} m/s in the last tick, and its velocity read must say how it moves");
    }

    /// <summary>
    /// The character and the crate drift together at 1 m/s along the space diagonal, in touch, with no thrust. Physics does
    /// not depend on a common velocity, so the crate stays where it was relative to the character, as it does at rest. Today
    /// the character loses its whole motion in its first step, because Jolt sweeps it against the crate where the crate was
    /// at the start of the step, and its motion along x in the second step, because Jolt cancels its velocity into the
    /// crate's face. The crate ends up one step of the cross velocity aside and two steps of the velocity along x ahead.
    /// </summary>
    [Fact]
    public void AWeightlessCharacterAndACrateThatDriftTogetherKeepTheirPlaces()
    {
        Double3 drift = new Double3(1, 1, 1).Normalized();
        using Pair pair = CharacterBehindCrate(startGap: 0.02, commonVelocity: drift);
        Double3 start = pair.World.PoseIn(pair.Crate, pair.Space).Position - pair.World.PoseIn(pair.Person, pair.Space).Position;

        Tick(pair, 60);

        Double3 now = pair.World.PoseIn(pair.Crate, pair.Space).Position - pair.World.PoseIn(pair.Person, pair.Space).Position;
        Double3 change = now - start;
        change.Length.Should().BeLessThan(0.001,
            $"both bodies drift with the same velocity, but the crate moved ({change.X:F4}, {change.Y:F4}, {change.Z:F4}) m relative to the character");
    }

    private static void Tick(Pair pair, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            pair.Tick();
    }
}
