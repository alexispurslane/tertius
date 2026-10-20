using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The one rule for characters (User): physics stands a character only on walkable ground under gravity. A character
/// that does not stand keeps its momentum in any gravity, turns its up against the gravity whenever it is under gravity,
/// touching or not, and loses its motion into what it hits. Physics reports whether it stood each character in each step. These tests run
/// the World on the real Jolt backend and read the report through <see cref="Snapshot.IsStanding"/>. A component writes a
/// command through <see cref="BodyHandle"/> during a tick, and physics applies it in the step of the next tick. So a test
/// sends a command with <see cref="Send"/>, which ticks once, and ticks on to see what the command did.
/// </summary>
public class CharacterStandingTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    // The capsule center rests 0.9 m above the floor, plus Jolt's character padding.
    private const double StandingHeight = 0.9;

    private static BodyDesc StaticBox(Vector3 halfExtents) => new(new BoxShapeDesc(halfExtents), BodyMotion.Static);

    /// <summary>A world with a root frame of 9.81 m/s² of gravity along -Y and a floor whose top is at y = 0.</summary>
    private static (World World, FrameHandle Ground) WorldWithFloor()
    {
        World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        return (world, ground);
    }

    /// <summary>
    /// Gives <paramref name="command"/> to the pilot and ticks once. The tick writes the command into the body's intent, so
    /// the step of that tick does not know it yet. The step of the next tick applies it.
    /// </summary>
    private static void Send(World world, Pilot pilot, BodyCommand command)
    {
        pilot.Once = command;
        world.Tick();
    }

    private static Entity SpawnPerson(World world, FrameHandle frame, DPose pose, Pilot? pilot = null) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = pose, Body = Person, Components = pilot is null ? [] : [pilot] });

    private static Vector3 UpOf(World world, Entity entity) => world.PoseInOwnFrame(entity).Rotation.Rotate(Vector3.UnitY);

    [Fact]
    public void ACharacterOnAFloorUnderGravityStands()
    {
        var (world, ground) = WorldWithFloor();
        using (world)
        {
            Entity person = SpawnPerson(world, ground, At(0, StandingHeight + 0.02, 0));
            Tick(world, 30);

            world.Snapshot.IsStanding(person).Should().BeTrue();
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterThatJumpsDoesNotStandInTheAirAndStandsAgainOnceItLands()
    {
        var (world, ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(world, ground, At(0, StandingHeight + 0.02, 0), pilot);
            Tick(world, 20);
            Send(world, pilot, body => body.Jump(5));
            Tick(world, 10);

            world.PoseInOwnFrame(person).Position.Y.Should().BeGreaterThan(StandingHeight + 0.3, "the character is in the air");
            world.Snapshot.IsStanding(person).Should().BeFalse();

            // v / g = 0.51 s up and as long down.
            Tick(world, 70);
            world.Snapshot.IsStanding(person).Should().BeTrue("it landed on the floor");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterThatAForcePressesOntoAFloorInZeroGravityDoesNotStand()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Floor(world, space);
        // 70 N on 70 kg presses the character onto the floor at 1 m/s², so Jolt finds the floor under it in every
        // update, as it does under gravity. The force of tick i acts in the step of tick i + 1, so 31 ticks hold 30 steps
        // with a force.
        var pilot = new Pilot { EveryTick = body => body.AddForce(new Vector3(0, -70, 0)) };
        Entity person = SpawnPerson(world, space, At(0, StandingHeight + 0.01, 0), pilot);

        Tick(world, 31);

        world.PoseInOwnFrame(person).Position.Y.Should().BeApproximately(StandingHeight, 0.03, "the character rests on the floor");
        world.Snapshot.IsStanding(person).Should().BeFalse();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterOnASlopeTooSteepToStandOnDoesNotStandAndSlidesDown()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A slab tilted 60 degrees about +Z, steeper than the 50 degrees that a character can stand on. Its upper face
        // passes through the origin and faces up and toward -X.
        var tilt = DQuaternion.FromAxisAngle(Double3.UnitZ, 60 * Math.PI / 180);
        world.Spawn(new SpawnDesc { Frame = ground, Pose = new DPose(tilt.Rotate(new Double3(0, -0.5, 0)), tilt), Body = StaticBox(new Vector3(20, 0.5f, 20)) });
        // The lower end of the capsule's axis is 0.3 m nearer the face than its center, so the capsule's side clears the
        // face by 2 cm.
        Vector3 normal = tilt.Rotate(Vector3.UnitY);
        Entity person = SpawnPerson(world, ground, new DPose((Double3)(normal * 0.62f), DQuaternion.Identity));
        Tick(world, 45);

        world.Snapshot.IsStanding(person).Should().BeFalse();
        // g · sin 60° = 8.5 m/s² along the slope carries it 2.4 m in 0.75 s, 2.1 m down.
        world.PoseInOwnFrame(person).Position.Y.Should().BeLessThan(-1, "the character slid down the slope");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Changed on purpose by ter-1b21 (docs/plans/spin-gravity.md, "Characters", fork F5): up follows the gravity in the
    /// air too. Before, a character in the air kept its up until it touched something, so this test was
    /// UnderGravityACharacterInTheAirKeepsItsUpAndTurnsItToTheGravityOnceItLands and expected the old up in the air.
    /// </summary>
    [Fact]
    public void UnderGravityACharacterInTheAirTurnsItsUpAgainstTheGravityAndLandsUpright()
    {
        var (world, ground) = WorldWithFloor();
        using (world)
        {
            Entity person = SpawnPerson(world, ground, At(0, 5, 0));
            world.Tick();
            // The field turns 20 degrees about +Z while the character falls. The floor stays, so it is a slope of 20 degrees
            // under the new field, gentle enough to stand on.
            double angle = 20 * Math.PI / 180;
            var tilted = new Vector3((float)Math.Sin(angle), -(float)Math.Cos(angle), 0);
            world.SetFrameGravity(ground, GravityField.Uniform(9.81f * tilted));
            Tick(world, 10);

            world.PoseInOwnFrame(person).Position.Y.Should().BeGreaterThan(3, "the character is still in the air");
            Vector3.Distance(UpOf(world, person), -tilted).Should().BeLessThan(1e-3f, "under gravity, up turns against it in the air too");

            Tick(world, 90);
            world.Snapshot.IsStanding(person).Should().BeTrue("it landed on the floor");
            Vector3.Distance(UpOf(world, person), -tilted).Should().BeLessThan(1e-3f, "it keeps its up against the gravity");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A character lands on a floor tilted by <paramref name="tiltDegrees"/> about +Z, as a person who tumbled in the air
    /// does. At 60 degrees it lands on its side, and Jolt finds steep ground under it. At 120 and 180 degrees it lands
    /// on its head, and Jolt finds a contact that does not support it at all. Under gravity its up turns against the
    /// gravity, in the air too (fork F5 of docs/plans/spin-gravity.md), so it turns upright and stands.
    /// </summary>
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(180)]
    public void UnderGravityACharacterThatLandsTiltedTurnsUprightAndStands(double tiltDegrees)
    {
        using World world = NewWorld(out _);
        // The frame has no gravity yet, so physics creates the character with the tilt of its pose.
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Floor(world, ground);
        double tilt = tiltDegrees * Math.PI / 180;
        var rotation = DQuaternion.FromAxisAngle(Double3.UnitZ, tilt);
        // The lowest point of the tilted capsule is 0.6 m · |cos tilt| + 0.3 m below its center. It starts 5 cm above
        // the floor, so it falls for about 0.1 s, or 6 steps.
        double lowest = 0.6 * Math.Abs(Math.Cos(tilt)) + 0.3;
        Entity person = SpawnPerson(world, ground, new DPose(new Double3(0, lowest + 0.05, 0), rotation));
        world.Tick();
        Vector3 tilted = rotation.Rotate(Vector3.UnitY);
        Vector3.Distance(UpOf(world, person), tilted).Should().BeLessThan(1e-5f, "the character starts tilted");
        world.SetFrameGravity(ground, GravityField.Uniform(new Vector3(0, -9.81f, 0)));

        int steps = TickUntil(world, () => world.Snapshot.IsStanding(person), 60);

        steps.Should().BeLessThan(15, "the character lands, turns upright and stands within a few steps of the fall");
        Vector3.Distance(UpOf(world, person), Vector3.UnitY).Should().BeLessThan(1e-3f);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UnderGravityACharacterThatJumpsIntoALowCeilingLosesItsUpwardMotionThere()
    {
        var (world, ground) = WorldWithFloor();
        using (world)
        {
            // The ceiling's lower face is at y = 2.3, 0.5 m above the head of the person who stands on the floor.
            world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2.8, 0), Body = StaticBox(new Vector3(50, 0.5f, 50)) });
            var pilot = new Pilot();
            Entity person = SpawnPerson(world, ground, At(0, StandingHeight + 0.02, 0), pilot);
            Tick(world, 20);
            Send(world, pilot, body => body.Jump(5));
            world.Tick();
            world.VelocityInOwnFrame(person).Y.Should().BeGreaterThan(4, "the character jumped");

            // The head meets the ceiling after about 0.11 s, or 7 steps, at about 3.9 m/s. Pressing on into the ceiling
            // until gravity ends the rise would take 0.5 s, or 30 steps.
            int rising = TickUntil(world, () => world.VelocityInOwnFrame(person).Y <= 0, 60);

            rising.Should().BeLessThan(10);
            world.PoseInOwnFrame(person).Position.Y.Should().BeLessThan(2.3 - StandingHeight, "the head stays below the ceiling");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A root frame <c>ground</c> whose gravity leans <paramref name="leanDegrees"/> about +Z from -Y, with a floor, and a
    /// child frame <c>building</c> at its origin with a bubble of 10 m, gravity along its own -Y, and a floor that reaches
    /// past the bubble. A walker in the building walks along +X at 4 m/s until physics hands it to the ground. The
    /// returned <see cref="FaultInjectingBackend"/> holds the output of every step, which is how a test reads what Jolt
    /// reported about the walker.
    /// </summary>
    private static (World World, FaultInjectingBackend Faults, FrameHandle Ground, Entity Walker) WalkOutOfABuilding(double leanDegrees)
    {
        World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        double lean = leanDegrees * Math.PI / 180;
        var down = new Vector3((float)Math.Sin(lean), -(float)Math.Cos(lean), 0);
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(9.81f * down)));
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.5, 0), Body = StaticBox(new Vector3(100, 0.5f, 100)) });
        Entity buildingOwner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), GravityField.Uniform(new Vector3(0, -9.81f, 0))),
        });
        FrameHandle building = world.GetOwnedFrame(buildingOwner);
        world.Spawn(new SpawnDesc { Frame = building, Pose = At(0, -0.5, 0), Body = StaticBox(new Vector3(15, 0.5f, 15)) });
        var pilot = new Pilot { Once = body => body.MoveCharacter(new Vector3(4, 0, 0)) };
        Entity walker = SpawnPerson(world, building, At(0, StandingHeight + 0.05, 0), pilot);
        bool HandedOff() => faults.Outputs.Count > 0 && faults.Outputs[^1].HandOffs.Count > 0;
        try
        {
            TickUntil(world, HandedOff, 300).Should().BeLessThan(300, "the walker leaves the building");
        }
        catch
        {
            world.Dispose();
            throw;
        }
        return (world, faults, ground, walker);
    }

    [Fact]
    public void AStandingCharacterThatWalksIntoAnotherFrameTakesItsUpFromThatFramesGravity()
    {
        var (world, _, ground, walker) = WalkOutOfABuilding(leanDegrees: 10);
        using (world)
        {
            world.GetFrame(walker).Should().Be(ground);
            double lean = 10 * Math.PI / 180;
            var up = new Vector3(-(float)Math.Sin(lean), (float)Math.Cos(lean), 0);
            Vector3.Distance(UpOf(world, walker), up).Should().BeLessThan(1e-3f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void AHandOffCarriesTheRotationThatPhysicsCreatedTheCharacterWithInItsNewFrame()
    {
        var (world, faults, ground, walker) = WalkOutOfABuilding(leanDegrees: 10);
        using (world)
        {
            // The sync point applies the state of a hand-off after the body states of the step, so the transform table
            // holds the hand-off's pose. That pose must have the rotation that physics turned to the gravity of the
            // ground, as the body state of the same step has it.
            HandOff handOff = faults.Outputs.SelectMany(output => output.HandOffs).Should().ContainSingle().Which;
            handOff.To.Should().Be(ground);
            BodyState created = faults.Outputs[^1].Bodies.Single(state => state.Entity == walker);
            created.Frame.Should().Be(ground);
            handOff.Pose.Rotation.Should().Be(created.Pose.Rotation);
            Vector3.Distance(created.Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitY).Should().BeGreaterThan(0.1f, "physics turned the character away from the building's up");
            world.PoseInOwnFrame(walker).Should().Be(handOff.Pose, "the World holds the pose that the hand-off carried");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A walker crosses from the building into the ground on a floor that goes on across the boundary, with the same
    /// gravity on both sides. Once it has settled onto the floor of the building, physics stands it in every step but
    /// one: the step of the hand-off reports it not standing, because a character that a hand-off moved has not stood in
    /// its new frame yet (see <see cref="CharacterState"/>). From the next step on it stands again, because the new
    /// character knows the ground under it at once.
    /// </summary>
    [Fact]
    public void ACharacterThatWalksIntoAnotherFrameOnAContinuousFloorStandsInEveryStepButTheStepOfTheHandOff()
    {
        var (world, faults, ground, walker) = WalkOutOfABuilding(leanDegrees: 0);
        using (world)
        {
            // About 0.5 m, or 8 steps, into the ground.
            Tick(world, 8);

            world.GetFrame(walker).Should().Be(ground);
            List<bool> standing = faults.Outputs.Select(output => output.Characters.Single().Standing).ToList();
            int handOffStep = faults.Outputs.FindIndex(output => output.HandOffs.Count > 0);
            int settled = standing.IndexOf(true);
            settled.Should().BeInRange(0, 10, "the walker starts 5 cm above the floor of the building");
            handOffStep.Should().BeGreaterThan(settled);
            IEnumerable<bool> expected = Enumerable.Range(settled, standing.Count - settled).Select(step => step != handOffStep);
            standing.Skip(settled).Should().Equal(expected);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterDoesNotStandInTheStepThatHandsItToAnotherFrame()
    {
        var (world, faults, ground, walker) = WalkOutOfABuilding(leanDegrees: 0);
        using (world)
        {
            world.GetFrame(walker).Should().Be(ground);
            faults.Outputs[^1].Characters.Single().Standing.Should().BeFalse("Jolt reports the character not standing in the step of its hand-off");
            world.Snapshot.IsStanding(walker).Should().BeFalse("in its new frame, nothing has stood the character yet");
            world.Errors.Should().BeEmpty();
        }
    }
}
