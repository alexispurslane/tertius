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
public sealed class CharacterStandingTests
{
    // The capsule center rests 0.9 m above the floor, plus Jolt's character padding.
    private const double StandingHeight = 0.9;

    private static readonly BodyDesc Person = new(
        Shape: new CapsuleShapeDesc(0.6f, 0.3f),
        Motion: BodyMotion.Character,
        Mass: 70
    );

    /// <summary>The largest angle, in radians, by which up turns in one step of 1/60 s in the air, at the default air rate.</summary>
    private static float AirUpTurnPerStep => new JoltBackendOptions().CharacterAirUpTurnRate / 60f;

    private static BodyDesc StaticBox(Vector3 halfExtents) =>
        new(Shape: new BoxShapeDesc(halfExtents), Motion: BodyMotion.Static);

    /// <summary>A world with a root frame of 9.81 m/s² of gravity along -Y and a floor whose top is at y = 0.</summary>
    private static (World World, FrameHandle Ground) WorldWithFloor()
    {
        World world = NewWorld(backend: out _);
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
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = pose,
                Body = Person,
                Components = pilot is null ? [] : [pilot],
            }
        );

    private static Vector3 UpOf(World world, Entity entity) =>
        world.PoseInOwnFrame(entity).Rotation.Rotate(Vector3.UnitY);

    [Fact]
    public void ACharacterOnAFloorUnderGravityStands()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, StandingHeight + 0.02, 0));
            Tick(world, 30);

            world.Snapshot.IsStanding(person).Should().BeTrue();
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterThatJumpsDoesNotStandInTheAirAndStandsAgainOnceItLands()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(
                world: world,
                frame: ground,
                pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot
            );
            Tick(world, 20);
            Send(world, pilot, body => body.Jump(5));
            Tick(world, 10);

            world
                .PoseInOwnFrame(person)
                .Position.Y.Should()
                .BeGreaterThan(StandingHeight + 0.3, "the character is in the air");
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
        using World world = NewWorld(backend: out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Floor(world, space);
        // 70 N on 70 kg presses the character onto the floor at 1 m/s², so Jolt finds the floor under it in every
        // update, as it does under gravity. The force of tick i acts in the step of tick i + 1, so 31 ticks hold 30 steps
        // with a force.
        var pilot = new Pilot { EveryTick = body => body.AddForce(new(0, -70, 0)) };
        Entity person = SpawnPerson(world: world, frame: space, pose: At(0, StandingHeight + 0.01, 0), pilot: pilot);

        Tick(world, 31);

        world
            .PoseInOwnFrame(person)
            .Position.Y.Should()
            .BeApproximately(StandingHeight, 0.03, "the character rests on the floor");
        world.Snapshot.IsStanding(person).Should().BeFalse();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACharacterOnASlopeTooSteepToStandOnDoesNotStandAndSlidesDown()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A slab tilted 60 degrees about +Z, steeper than the 50 degrees that a character can stand on. Its upper face
        // passes through the origin and faces up and toward -X.
        var tilt = DQuaternion.FromAxisAngle(Double3.UnitZ, 60 * Math.PI / 180);
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = new(tilt.Rotate(new Double3(0, -0.5, 0)), tilt),
                Body = StaticBox(new(20, 0.5f, 20)),
            }
        );
        // The lower end of the capsule's axis is 0.3 m nearer the face than its center, so the capsule's side clears the
        // face by 2 cm.
        Vector3 normal = tilt.Rotate(Vector3.UnitY);
        Entity person = SpawnPerson(world: world, frame: ground, pose: new(normal * 0.62f, DQuaternion.Identity));
        Tick(world, 45);

        world.Snapshot.IsStanding(person).Should().BeFalse();
        // g · sin 60° = 8.5 m/s² along the slope carries it 2.4 m in 0.75 s, 2.1 m down.
        world.PoseInOwnFrame(person).Position.Y.Should().BeLessThan(-1, "the character slid down the slope");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character stands on a walkable slab tilted 20 degrees about +Z, under gravity along −Y, and walks at 3 m/s along
    /// +X, uphill (<paramref name="direction"/> 1) or downhill (−1). Physics turns the part of the walk across up into the
    /// ground plane at the same speed (docs/plans/spin-gravity.md, "Characters", fork F6), so the character walks along
    /// the face at 3 m/s, stays at its height above the face, and stands in every step: the test of standing measures the
    /// motion away from the ground along the ground's normal, not along up.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void AStandingCharacterWalksAlongAWalkableSlopeInItsGroundPlaneAtTheSpeedThatItAsksFor(int direction)
    {
        World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        using (world)
        {
            // The upper face passes through the origin, faces up and toward −X, and rises toward +X.
            var tilt = DQuaternion.FromAxisAngle(Double3.UnitZ, 20 * Math.PI / 180);
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(tilt.Rotate(new Double3(0, -0.5, 0)), tilt),
                    Body = StaticBox(new(40, 0.5f, 20)),
                }
            );
            Double3 normal = tilt.Rotate(Double3.UnitY);
            Double3 along = tilt.Rotate(Double3.UnitX) * direction;
            // The upright capsule's lower sphere, 0.6 m below its center, clears the face by 2 cm.
            var pilot = new Pilot();
            Entity person = SpawnPerson(
                world: world,
                frame: ground,
                pose: new(normal * (0.32 + 0.6 * Math.Cos(20 * Math.PI / 180)), DQuaternion.Identity),
                pilot: pilot
            );
            Tick(world, 30);
            world.Snapshot.IsStanding(person).Should().BeTrue("the slope is gentle enough to stand on");
            double height = Double3.Dot(world.PoseInOwnFrame(person).Position, normal);

            pilot.EveryTick = body => body.MoveCharacter(new(3 * direction, 0, 0));
            // The tick writes the walk, and the step of the next tick applies it.
            Tick(world, 2);
            Double3 last = world.PoseInOwnFrame(person).Position;
            var speeds = new List<double>();
            var offPlane = new List<double>();
            var heights = new List<double>();
            var standing = new List<bool>();
            for (int tick = 0; tick < 40; tick++)
            {
                world.Tick();
                Double3 now = world.PoseInOwnFrame(person).Position;
                Double3 velocity = (now - last) * world.Options.TickRate;
                speeds.Add(Double3.Dot(velocity, along));
                offPlane.Add((velocity - Double3.Dot(velocity, along) * along).Length);
                heights.Add(Double3.Dot(now, normal) - height);
                standing.Add(world.Snapshot.IsStanding(person));
                last = now;
            }

            standing.Should().OnlyContain(s => s, "the character stands in every step of its walk");
            speeds
                .Should()
                .OnlyContain(
                    s => Math.Abs(s - 3) < 0.03,
                    "the walk moves the character along the face at the speed that it asks for"
                );
            offPlane.Max().Should().BeLessThan(0.03, "the walk stays in the ground plane, along the line of the walk");
            heights.Max(Math.Abs).Should().BeLessThan(5e-3, "the character keeps its height above the face");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A character stands on a plate of 700 kg that slides without friction down a static slab tilted 40 degrees about +Z,
    /// under gravity along −Y. Jolt presses a standing character's weight onto a dynamic ground. For a standing character
    /// the weight's part along up presses along the ground's normal (docs/plans/spin-gravity.md, "Characters"), and the
    /// rest is dropped, because the ground holds it. So the press has no part along the slab, and the plate slides down it
    /// at g sin 40° = 6.31 m/s², as it would without the person. A press along the gravity instead, as Jolt makes it from
    /// the gravity itself, would push the plate down the slab with m g cos 40° sin 40° = 338 N more, 0.48 m/s².
    /// </summary>
    [Fact]
    public void AStandingCharacterPressesADynamicGroundAlongTheGroundsNormal()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        double slope = 40 * Math.PI / 180;
        var tilt = DQuaternion.FromAxisAngle(Double3.UnitZ, slope);
        Double3 normal = tilt.Rotate(Double3.UnitY);
        Double3 downhill = -tilt.Rotate(Double3.UnitX);
        // The slab's upper face passes through the origin, and the plate, 0.2 m thick, lies on it.
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = new(tilt.Rotate(new Double3(0, -0.5, 0)), tilt),
                Body = new(Shape: new BoxShapeDesc(new(40, 0.5f, 10)), Motion: BodyMotion.Static, Friction: 0),
            }
        );
        Entity plate = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = new(normal * 0.1, tilt),
                Body = new(
                    Shape: new BoxShapeDesc(new(2, 0.1f, 2)),
                    Mass: 700,
                    Friction: 0,
                    LinearDamping: 0,
                    AngularDamping: 0
                ),
            }
        );
        // The upright capsule's lower sphere, 0.6 m below its center, starts 1 cm above the plate.
        Entity person = SpawnPerson(
            world: world,
            frame: ground,
            pose: new(normal * (0.2 + 0.31 + 0.6 * Math.Cos(slope)), DQuaternion.Identity)
        );
        TickUntil(world, () => world.Snapshot.IsStanding(person), 30)
            .Should()
            .BeLessThan(30, "the person lands on the plate and stands on it");

        var places = new List<double> { Double3.Dot(world.PoseInOwnFrame(plate).Position, downhill) };
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            world.Snapshot.IsStanding(person).Should().BeTrue($"the person stands on the sliding plate in tick {tick}");
            places.Add(Double3.Dot(world.PoseInOwnFrame(plate).Position, downhill));
        }

        double rate = world.Options.TickRate;
        for (int n = 2; n < places.Count; n++)
            ((places[n] - 2 * places[n - 1] + places[n - 2]) * rate * rate)
                .Should()
                .BeApproximately(
                    9.81 * Math.Sin(slope),
                    0.05,
                    $"the person presses the plate along its normal, so the plate slides as it would alone, in step {n}"
                );
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character walks at 3 m/s into a stair 0.3 m high, as the person of <c>ChaseCameraTests</c> does. At the stair's
    /// edge the face that the character touches leans 68 degrees toward the riser, steeper than a character can stand on,
    /// so the walk keeps to the plane across up there, and Jolt's climb of stairs lifts the character by the stair's height
    /// (see <c>GroundFaceNormal</c> in the backend). A walk in the plane of that face threw the character up the riser at
    /// 2.8 m/s.
    /// </summary>
    [Fact]
    public void AStandingCharacterThatWalksIntoAStairClimbsByTheStairsHeightOnly()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(8, 0.15),
                    Body = StaticBox(new(5, 0.15f, 5)),
                }
            );
            var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new(3, 0, 0)) };
            Entity person = SpawnPerson(
                world: world,
                frame: ground,
                pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot
            );
            double highest = 0,
                last = 0;
            for (int tick = 0; tick < 150; tick++)
            {
                world.Tick();
                double y = world.PoseInOwnFrame(person).Position.Y;
                highest = Math.Max(highest, y);
                last = y;
            }

            world.PoseInOwnFrame(person).Position.X.Should().BeGreaterThan(5, "the character walked onto the stair");
            last.Should().BeApproximately(0.3 + StandingHeight, 0.03, "the character stands on the stair");
            highest
                .Should()
                .BeLessThan(0.3 + StandingHeight + 0.03, "the climb lifts the character by the stair's height only");
            world.Errors.Should().BeEmpty();
        }
    }

    private static float AngleBetween(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));

    /// <summary>
    /// Changed on purpose by ter-1b21 (docs/plans/spin-gravity.md, "Characters", fork F5): up follows the gravity in the
    /// air too, at a limited rate (the user's choice of 2026-10-20). Before, a character in the air kept its up until it
    /// touched something, so this test was UnderGravityACharacterInTheAirKeepsItsUpAndTurnsItToTheGravityOnceItLands and
    /// expected the old up in the air. Then for one commit up turned at once in the air, and this test expected the new up
    /// after 10 steps.
    /// </summary>
    [Fact]
    public void UnderASteadyGravityACharacterInTheAirTurnsItsUpAtTheAirRateUntilItIsAgainstTheGravityAndLandsUpright()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 5, 0));
            world.Tick();
            // The field turns 20 degrees about +Z while the character falls. The floor stays, so it is a slope of 20 degrees
            // under the new field, gentle enough to stand on.
            double angle = 20 * Math.PI / 180;
            var tilted = new Vector3((float)Math.Sin(angle), -(float)Math.Cos(angle), 0);
            world.SetFrameGravity(ground, GravityField.Uniform(9.81f * tilted));

            var gaps = new List<float> { AngleBetween(UpOf(world, person), -tilted) };
            for (int tick = 0; tick < 40; tick++)
            {
                world.Tick();
                gaps.Add(AngleBetween(UpOf(world, person), -tilted));
            }

            world.PoseInOwnFrame(person).Position.Y.Should().BeGreaterThan(2, "the character is still in the air");
            for (int n = 1; n < gaps.Count; n++)
            {
                float expected = Math.Max(0, gaps[n - 1] - AirUpTurnPerStep);
                gaps[n]
                    .Should()
                    .BeApproximately(
                        expected,
                        2e-4f,
                        $"in step {n} up turns by the air rate times the step, until it is aligned"
                    );
            }

            gaps.Count(g => g > AirUpTurnPerStep).Should().BeGreaterThan(5, "a turn of 20 degrees takes several steps");
            gaps[^1].Should().BeLessThan(1e-3f, "under gravity, up turns against it in the air too");

            Tick(world, 60);
            world.Snapshot.IsStanding(person).Should().BeTrue("it landed on the floor");
            Vector3
                .Distance(UpOf(world, person), -tilted)
                .Should()
                .BeLessThan(1e-3f, "it keeps its up against the gravity");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A character falls high above the floor in a field along −Y, with its up along +Y, and the game teleports it to a
    /// rotation tilted 60 degrees about +Z. Up is the body's +Y, so a teleport sets the up together with the rotation, as a
    /// spawn does, and in the air the up then turns back against the gravity at most at the air rate, with the rotation
    /// along. Before the review of ticket ter-1b21, the up kept its old value and the next step turned the rotation onto it
    /// at once, which skipped the air rate.
    /// </summary>
    [Fact]
    public void ACharacterInTheAirThatTheGameTeleportsToATiltedRotationTurnsUprightAtTheAirRate()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 200, 0), pilot: pilot);
            Tick(world, 5);
            double tilt = 60 * Math.PI / 180;
            var tilted = new DPose(new(0, 190, 0), DQuaternion.FromAxisAngle(new(0, 0, 1), tilt));
            Send(world, pilot, body => body.Teleport(tilted));
            // The step of this tick teleports the character, and its update turns it once.
            world.Tick();

            var gaps = new List<float> { AngleBetween(UpOf(world, person), Vector3.UnitY) };
            for (int tick = 0; tick < 70; tick++)
            {
                world.Tick();
                gaps.Add(AngleBetween(UpOf(world, person), Vector3.UnitY));
            }

            gaps[0]
                .Should()
                .BeGreaterThan(
                    (float)tilt - AirUpTurnPerStep - 1e-3f,
                    "the teleport's step turns the body by the air rate at most"
                );
            for (int n = 1; n < gaps.Count; n++)
            {
                float expected = Math.Max(0, gaps[n - 1] - AirUpTurnPerStep);
                gaps[n]
                    .Should()
                    .BeApproximately(
                        expected,
                        2e-4f,
                        $"in step {n} up turns by the air rate times the step, until it is aligned"
                    );
            }

            gaps[^1].Should().BeLessThan(1e-3f, "under gravity, up turns against it in the air");
            world.PoseInOwnFrame(person).Position.Y.Should().BeGreaterThan(100, "the character is still in the air");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The same teleport for a character in the rigid form: the field is off, so the character is weightless, and the game
    /// teleports it to a rotation tilted 60 degrees about +Z. The teleport sets the up of the rigid form too, so when the
    /// field comes back and the character walks again in the air, its up and its rotation turn upright together at the air
    /// rate, not in one step.
    /// </summary>
    [Fact]
    public void AWeightlessCharacterThatTheGameTeleportsToATiltedRotationTurnsUprightAtTheAirRateOnceGravityReturns()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 200, 0), pilot: pilot);
            world.SetFrameGravity(ground, GravityField.Uniform(Vector3.Zero));
            Tick(world, 5);
            double tilt = 60 * Math.PI / 180;
            Send(
                world,
                pilot,
                body => body.Teleport(new(new(0, 190, 0), DQuaternion.FromAxisAngle(new(0, 0, 1), tilt)))
            );
            Tick(world, 3);
            AngleBetween(UpOf(world, person), Vector3.UnitY)
                .Should()
                .BeApproximately((float)tilt, 1e-3f, "a weightless character keeps the teleport's rotation");

            world.SetFrameGravity(ground, GravityField.Uniform(new(0, -9.81f, 0)));
            var gaps = new List<float> { AngleBetween(UpOf(world, person), Vector3.UnitY) };
            for (int tick = 0; tick < 70; tick++)
            {
                world.Tick();
                gaps.Add(AngleBetween(UpOf(world, person), Vector3.UnitY));
            }

            for (int n = 1; n < gaps.Count; n++)
                gaps[n]
                    .Should()
                    .BeGreaterThan(
                        gaps[n - 1] - AirUpTurnPerStep - 2e-4f,
                        $"in step {n} up turns by the air rate at most"
                    );
            gaps[^1].Should().BeLessThan(1e-3f, "under gravity, up turns against it in the air");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A character floats high above the floor in a field along −Y, with its up along +Y. The frame's field then turns by
    /// <paramref name="flipDegrees"/> for one step, as the felt gravity in a cabin flips for a step when its engine starts or
    /// its hull jolts, and turns back. Up turns toward the flipped gravity by at most the air rate times the step, and then
    /// back.
    /// </summary>
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    public void AFloatingCharacterWhoseGravityFlipsForOneStepTurnsItsUpByAtMostTheAirRateTimesTheStep(
        double flipDegrees
    )
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 200, 0));
            Tick(world, 5);
            Vector3.Distance(UpOf(world, person), Vector3.UnitY).Should().BeLessThan(1e-5f);
            double flip = flipDegrees * Math.PI / 180;
            var flipped = new Vector3((float)Math.Sin(flip), -(float)Math.Cos(flip), 0);
            world.SetFrameGravity(ground, GravityField.Uniform(9.81f * flipped));
            world.Tick();
            var turns = new List<float> { AngleBetween(UpOf(world, person), Vector3.UnitY) };
            world.SetFrameGravity(ground, GravityField.Uniform(new(0, -9.81f, 0)));
            for (int tick = 0; tick < 5; tick++)
            {
                world.Tick();
                turns.Add(AngleBetween(UpOf(world, person), Vector3.UnitY));
            }

            turns
                .Max()
                .Should()
                .BeLessThanOrEqualTo(
                    AirUpTurnPerStep * 1.001f,
                    "one step turns up by at most the air rate times the step"
                );
            turns
                .Max()
                .Should()
                .BeGreaterThan(AirUpTurnPerStep * 0.99f, "up starts to turn toward the flipped gravity");
            turns[^1].Should().BeLessThan(1e-4f, "up turns back once the gravity is back");
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
        using World world = NewWorld(backend: out _);
        // The frame has no gravity yet, so physics creates the character with the tilt of its pose.
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Floor(world, ground);
        double tilt = tiltDegrees * Math.PI / 180;
        var rotation = DQuaternion.FromAxisAngle(Double3.UnitZ, tilt);
        // The lowest point of the tilted capsule is 0.6 m · |cos tilt| + 0.3 m below its center. It starts 5 cm above
        // the floor, so it falls for about 0.1 s, or 6 steps.
        double lowest = 0.6 * Math.Abs(Math.Cos(tilt)) + 0.3;
        Entity person = SpawnPerson(world: world, frame: ground, pose: new(new(0, lowest + 0.05, 0), rotation));
        world.Tick();
        Vector3 tilted = rotation.Rotate(Vector3.UnitY);
        Vector3.Distance(UpOf(world, person), tilted).Should().BeLessThan(1e-5f, "the character starts tilted");
        world.SetFrameGravity(ground, GravityField.Uniform(new(0, -9.81f, 0)));

        int steps = TickUntil(world, () => world.Snapshot.IsStanding(person), 60);

        steps.Should().BeLessThan(15, "the character lands, turns upright and stands within a few steps of the fall");
        Vector3.Distance(UpOf(world, person), Vector3.UnitY).Should().BeLessThan(1e-3f);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UnderGravityACharacterThatJumpsIntoALowCeilingLosesItsUpwardMotionThere()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            // The ceiling's lower face is at y = 2.3, 0.5 m above the head of the person who stands on the floor.
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(0, 2.8, 0),
                    Body = StaticBox(new(50, 0.5f, 50)),
                }
            );
            var pilot = new Pilot();
            Entity person = SpawnPerson(
                world: world,
                frame: ground,
                pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot
            );
            Tick(world, 20);
            Send(world, pilot, body => body.Jump(5));
            world.Tick();
            world.VelocityInOwnFrame(person).Y.Should().BeGreaterThan(4, "the character jumped");

            // The head meets the ceiling after about 0.11 s, or 7 steps, at about 3.9 m/s. Pressing on into the ceiling
            // until gravity ends the rise would take 0.5 s, or 30 steps.
            int rising = TickUntil(world, () => world.VelocityInOwnFrame(person).Y <= 0, 60);

            rising.Should().BeLessThan(10);
            world
                .PoseInOwnFrame(person)
                .Position.Y.Should()
                .BeLessThan(2.3 - StandingHeight, "the head stays below the ceiling");
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
    private static (World World, FaultInjectingBackend Faults, FrameHandle Ground, Entity Walker) WalkOutOfABuilding(
        double leanDegrees
    )
    {
        World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        double lean = leanDegrees * Math.PI / 180;
        var down = new Vector3((float)Math.Sin(lean), -(float)Math.Cos(lean), 0);
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), GravityField.Uniform(9.81f * down)));
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, -0.5, 0),
                Body = StaticBox(new(100, 0.5f, 100)),
            }
        );
        Entity buildingOwner = world.Spawn(
            new() { Frame = ground, AsFrame = new(FrameBounds.Sphere(10), GravityField.Uniform(new(0, -9.81f, 0))) }
        );
        FrameHandle building = world.GetOwnedFrame(buildingOwner);
        world.Spawn(
            new()
            {
                Frame = building,
                Pose = At(0, -0.5, 0),
                Body = StaticBox(new(15, 0.5f, 15)),
            }
        );
        var pilot = new Pilot { Once = body => body.MoveCharacter(new(4, 0, 0)) };
        Entity walker = SpawnPerson(world: world, frame: building, pose: At(0, StandingHeight + 0.05, 0), pilot: pilot);
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
        (World world, _, FrameHandle ground, Entity walker) = WalkOutOfABuilding(leanDegrees: 10);
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
        (World world, FaultInjectingBackend faults, FrameHandle ground, Entity walker) = WalkOutOfABuilding(
            leanDegrees: 10
        );
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
            Vector3
                .Distance(created.Pose.Rotation.Rotate(Vector3.UnitY), Vector3.UnitY)
                .Should()
                .BeGreaterThan(0.1f, "physics turned the character away from the building's up");
            world
                .PoseInOwnFrame(walker)
                .Should()
                .Be(handOff.Pose, "the World holds the pose that the hand-off carried");
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
        (World world, FaultInjectingBackend faults, FrameHandle ground, Entity walker) = WalkOutOfABuilding(
            leanDegrees: 0
        );
        using (world)
        {
            // About 0.5 m, or 8 steps, into the ground.
            Tick(world, 8);

            world.GetFrame(walker).Should().Be(ground);
            List<bool> standing = [.. faults.Outputs.Select(output => output.Characters.Single().Standing)];
            int handOffStep = faults.Outputs.FindIndex(output => output.HandOffs.Count > 0);
            int settled = standing.IndexOf(true);
            settled.Should().BeInRange(0, 10, "the walker starts 5 cm above the floor of the building");
            handOffStep.Should().BeGreaterThan(settled);
            IEnumerable<bool> expected = Enumerable
                .Range(settled, standing.Count - settled)
                .Select(step => step != handOffStep);
            standing.Skip(settled).Should().Equal(expected);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterDoesNotStandInTheStepThatHandsItToAnotherFrame()
    {
        (World world, FaultInjectingBackend faults, FrameHandle ground, Entity walker) = WalkOutOfABuilding(
            leanDegrees: 0
        );
        using (world)
        {
            world.GetFrame(walker).Should().Be(ground);
            faults
                .Outputs[^1]
                .Characters.Single()
                .Standing.Should()
                .BeFalse("Jolt reports the character not standing in the step of its hand-off");
            world.Snapshot.IsStanding(walker).Should().BeFalse("in its new frame, nothing has stood the character yet");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void AStandingCharacterTeleportedIntoTheAirDoesNotStandInTheStepOfTheTeleportAndFallsFromRestThere()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(
                world: world,
                frame: ground,
                pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot
            );
            Tick(world, 30);
            world.Snapshot.IsStanding(person).Should().BeTrue("the character stands on the floor before the teleport");
            var top = new Double3(0, 10, 0);
            Send(world, pilot, body => body.Teleport(new(top, DQuaternion.Identity)));

            // The step of this tick teleports the character.
            world.Tick();

            world.Snapshot.IsStanding(person).Should().BeFalse("nothing is under the character at its new place");
            // A teleport leaves the character at rest, so in one step of 1/60 s it falls g t² / 2, as a character that
            // spawned there at rest falls (InstantVelocityTests).
            double dt = world.DeltaTime;
            world
                .PoseInOwnFrame(person)
                .Position.Y.Should()
                .BeApproximately(top.Y - 9.81f * dt * dt / 2, 1e-4, "the character falls in the step of the teleport");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ACharacterInTheAirTeleportedOntoGroundStandsInTheStepOfTheTeleport()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            var pilot = new Pilot();
            Entity person = SpawnPerson(world: world, frame: ground, pose: At(0, 200, 0), pilot: pilot);
            Tick(world, 30);
            world.Snapshot.IsStanding(person).Should().BeFalse("the character falls before the teleport");
            Send(world, pilot, body => body.Teleport(At(0, StandingHeight, 0)));

            // The step of this tick teleports the character.
            world.Tick();

            world.Snapshot.IsStanding(person).Should().BeTrue("the floor is under the character at its new place");
            world
                .PoseInOwnFrame(person)
                .Position.Y.Should()
                .BeApproximately(StandingHeight, 1e-3, "the character stands on the floor at its new place");
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void AStandingCharacterTeleportedOntoOtherGroundStandsThereInTheStepOfTheTeleport()
    {
        (World world, FrameHandle ground) = WorldWithFloor();
        using (world)
        {
            // A platform whose top is 5 m above the floor, 30 m away from the character.
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(30, 4.5, 0),
                    Body = StaticBox(new(2, 0.5f, 2)),
                }
            );
            var pilot = new Pilot();
            Entity person = SpawnPerson(
                world: world,
                frame: ground,
                pose: At(0, StandingHeight + 0.02, 0),
                pilot: pilot
            );
            Tick(world, 30);
            world.Snapshot.IsStanding(person).Should().BeTrue("the character stands on the floor before the teleport");
            Send(world, pilot, body => body.Teleport(At(30, 5 + StandingHeight, 0)));

            // The step of this tick teleports the character.
            world.Tick();

            world.Snapshot.IsStanding(person).Should().BeTrue("the platform is under the character at its new place");
            world
                .PoseInOwnFrame(person)
                .Position.Y.Should()
                .BeApproximately(5 + StandingHeight, 1e-3, "the character stands on the platform at its new place");
            world.Errors.Should().BeEmpty();
        }
    }
}
