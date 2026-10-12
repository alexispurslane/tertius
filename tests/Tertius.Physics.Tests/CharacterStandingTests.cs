using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// The one rule for characters (User): physics stands a character only on walkable ground under gravity. A character
/// that does not stand keeps its momentum in any gravity, turns its up under gravity only while it touches something,
/// and loses its motion into what it hits. Physics reports whether it stood each character in each step.
/// </summary>
public class CharacterStandingTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    // The capsule center rests 0.9 m above the floor, plus Jolt's character padding.
    private const double StandingHeight = 0.9;

    /// <summary>A root frame with 9.81 m/s² of gravity along -Y and a floor whose top is at y = 0.</summary>
    private static (PhysicsHarness Harness, FrameHandle Ground) World()
    {
        var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        return (h, ground);
    }

    private static Vector3 UpOf(BodyState state) => state.Pose.Rotation.Rotate(Vector3.UnitY);

    [Fact]
    public void ACharacterOnAFloorUnderGravityStands()
    {
        var (h, ground) = World();
        using (h)
        {
            Entity person = h.AddBody(ground, Person, Shapes.At(0, StandingHeight + 0.02, 0));
            h.Step(30);

            h.Standing[person].Should().BeTrue();
        }
    }

    [Fact]
    public void ACharacterThatJumpsDoesNotStandInTheAirAndStandsAgainOnceItLands()
    {
        var (h, ground) = World();
        using (h)
        {
            Entity person = h.AddBody(ground, Person, Shapes.At(0, StandingHeight + 0.02, 0));
            h.Step(20);
            h.Intent(person).Flags = IntentFlags.Jump;
            h.Intent(person).JumpSpeed = 5;
            h.Step(10);

            h[person].Pose.Position.Y.Should().BeGreaterThan(StandingHeight + 0.3, "the character is in the air");
            h.Standing[person].Should().BeFalse();

            // v / g = 0.51 s up and as long down.
            h.Step(70);
            h.Standing[person].Should().BeTrue("it landed on the floor");
        }
    }

    [Fact]
    public void ACharacterThatAForcePressesOntoAFloorInZeroGravityDoesNotStand()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        h.AddBody(space, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        Entity person = h.AddBody(space, Person, Shapes.At(0, StandingHeight + 0.01, 0));
        // 70 N on 70 kg presses the character onto the floor at 1 m/s², so Jolt finds the floor under it in every
        // update, as it does under gravity.
        for (int i = 0; i < 30; i++)
        {
            h.Intent(person).Force = new Vector3(0, -70, 0);
            h.Step();
        }

        h[person].Pose.Position.Y.Should().BeApproximately(StandingHeight, 0.03, "the character rests on the floor");
        h.Standing[person].Should().BeFalse();
    }

    [Fact]
    public void ACharacterOnASlopeTooSteepToStandOnDoesNotStandAndSlidesDown()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        // A slab tilted 60 degrees about +Z, steeper than the 50 degrees that a character can stand on. Its upper face
        // passes through the origin and faces up and toward -X.
        var tilt = DQuaternion.FromAxisAngle(Double3.UnitZ, 60 * Math.PI / 180);
        h.AddBody(ground, Shapes.Floor(20), new DPose(tilt.Rotate(new Double3(0, -0.5, 0)), tilt));
        // The lower end of the capsule's axis is 0.3 m nearer the face than its center, so the capsule's side clears the
        // face by 2 cm.
        Vector3 normal = tilt.Rotate(Vector3.UnitY);
        Entity person = h.AddBody(ground, Person, new DPose((Double3)(normal * 0.62f), DQuaternion.Identity));
        h.Step(45);

        h.Standing[person].Should().BeFalse();
        // g · sin 60° = 8.5 m/s² along the slope carries it 2.4 m in 0.75 s, 2.1 m down.
        h[person].Pose.Position.Y.Should().BeLessThan(-1, "the character slid down the slope");
    }

    [Fact]
    public void UnderGravityACharacterInTheAirKeepsItsUpAndTurnsItToTheGravityOnceItLands()
    {
        var (h, ground) = World();
        using (h)
        {
            Entity person = h.AddBody(ground, Person, Shapes.At(0, 5, 0));
            h.Step();
            // The field turns 20 degrees about +Z while the character falls. The floor stays, so it is a slope of 20 degrees
            // under the new field, gentle enough to stand on.
            double angle = 20 * Math.PI / 180;
            var tilted = new Vector3((float)Math.Sin(angle), -(float)Math.Cos(angle), 0);
            h.Frames.SetGravity(ground, GravityField.Uniform(9.81f * tilted));
            h.Step(10);

            h[person].Pose.Position.Y.Should().BeGreaterThan(3, "the character is still in the air");
            Vector3.Distance(UpOf(h[person]), Vector3.UnitY).Should().BeLessThan(1e-5f, "in the air nothing turns the character");

            h.Step(90);
            h.Standing[person].Should().BeTrue("it landed on the floor");
            Vector3.Distance(UpOf(h[person]), -tilted).Should().BeLessThan(1e-3f, "it touches the floor, so its up turns against the gravity");
        }
    }

    /// <summary>
    /// A character lands on a floor tilted by <paramref name="tiltDegrees"/> about +Z, as a person who tumbled in the air
    /// does. At 60 degrees it lands on its side, and Jolt finds steep ground under it. At 120 and 180 degrees it lands
    /// on its head, and Jolt finds a contact that does not support it at all. Under gravity, any touch turns its up
    /// against the gravity, so it turns upright and stands.
    /// </summary>
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(180)]
    public void UnderGravityACharacterThatLandsTiltedTurnsUprightAndStands(double tiltDegrees)
    {
        using var h = new PhysicsHarness();
        // The frame has no gravity yet, so physics creates the character with the tilt of its pose.
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        double tilt = tiltDegrees * Math.PI / 180;
        var rotation = DQuaternion.FromAxisAngle(Double3.UnitZ, tilt);
        // The lowest point of the tilted capsule is 0.6 m · |cos tilt| + 0.3 m below its center. It starts 5 cm above
        // the floor, so it falls for about 0.1 s, or 6 steps.
        double lowest = 0.6 * Math.Abs(Math.Cos(tilt)) + 0.3;
        Entity person = h.AddBody(ground, Person, new DPose(new Double3(0, lowest + 0.05, 0), rotation));
        h.Step();
        Vector3 tilted = rotation.Rotate(Vector3.UnitY);
        Vector3.Distance(UpOf(h[person]), tilted).Should().BeLessThan(1e-5f, "the character starts tilted");
        h.Frames.SetGravity(ground, GravityField.Uniform(new Vector3(0, -9.81f, 0)));

        int steps = h.StepUntil(() => h.Standing[person], 60);

        steps.Should().BeLessThan(15, "the character lands, turns upright and stands within a few steps of the fall");
        Vector3.Distance(UpOf(h[person]), Vector3.UnitY).Should().BeLessThan(1e-3f);
    }

    [Fact]
    public void UnderGravityACharacterThatJumpsIntoALowCeilingLosesItsUpwardMotionThere()
    {
        var (h, ground) = World();
        using (h)
        {
            // The ceiling's lower face is at y = 2.3, 0.5 m above the head of the person who stands on the floor.
            h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, 2.8, 0));
            Entity person = h.AddBody(ground, Person, Shapes.At(0, StandingHeight + 0.02, 0));
            h.Step(20);
            h.Intent(person).Flags = IntentFlags.Jump;
            h.Intent(person).JumpSpeed = 5;
            h.Step();
            h[person].LinearVelocity.Y.Should().BeGreaterThan(4, "the character jumped");

            // The head meets the ceiling after about 0.11 s, or 7 steps, at about 3.9 m/s. Pressing on into the ceiling
            // until gravity ends the rise would take 0.5 s, or 30 steps.
            int rising = h.StepUntil(() => h[person].LinearVelocity.Y <= 0, 60);

            rising.Should().BeLessThan(10);
            h[person].Pose.Position.Y.Should().BeLessThan(2.3 - StandingHeight, "the head stays below the ceiling");
        }
    }

    /// <summary>
    /// A root frame <c>ground</c> whose gravity leans <paramref name="leanDegrees"/> about +Z from -Y, with a floor, and a
    /// child frame <c>building</c> at its origin with a bubble of 10 m, gravity along its own -Y, and a floor that reaches
    /// past the bubble. A walker in the building walks along +X at 4 m/s until physics hands it to the ground. The
    /// harness runs <paramref name="afterStep"/> after every step, from the first.
    /// </summary>
    private static (PhysicsHarness Harness, FrameHandle Ground, Entity Walker) WalkOutOfABuilding(double leanDegrees, Action<PhysicsStepOutput>? afterStep = null)
    {
        var h = new PhysicsHarness { AfterStep = afterStep };
        double lean = leanDegrees * Math.PI / 180;
        var down = new Vector3((float)Math.Sin(lean), -(float)Math.Cos(lean), 0);
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(9.81f * down));
        h.AddBody(ground, Shapes.Floor(100), Shapes.At(0, -0.5, 0));
        FrameHandle building = h.AddFrame(ground, DPose.Identity, FrameBounds.Sphere(10), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(building, Shapes.Floor(15), Shapes.At(0, -0.5, 0));
        Entity walker = h.AddBody(building, Person, Shapes.At(0, StandingHeight + 0.05, 0));
        h.Intent(walker).Flags = IntentFlags.CharacterMove;
        h.Intent(walker).CharacterVelocity = new Vector3(4, 0, 0);
        h.StepUntil(() => h.HandOffs.Count > 0, 300).Should().BeLessThan(300, "the walker leaves the building");
        return (h, ground, walker);
    }

    [Fact]
    public void AStandingCharacterThatWalksIntoAnotherFrameTakesItsUpFromThatFramesGravity()
    {
        var (h, ground, walker) = WalkOutOfABuilding(leanDegrees: 10);
        using (h)
        {
            h[walker].Frame.Should().Be(ground);
            double lean = 10 * Math.PI / 180;
            var up = new Vector3(-(float)Math.Sin(lean), (float)Math.Cos(lean), 0);
            Vector3.Distance(UpOf(h[walker]), up).Should().BeLessThan(1e-3f);
        }
    }

    [Fact]
    public void AHandOffCarriesTheRotationThatPhysicsCreatedTheCharacterWithInItsNewFrame()
    {
        var (h, ground, walker) = WalkOutOfABuilding(leanDegrees: 10);
        using (h)
        {
            // The sync point applies the state of a hand-off after the body states of the step, so the transform table
            // holds the hand-off's pose. That pose must have the rotation that physics turned to the gravity of the
            // ground, as the body state of the same step has it.
            HandOff handOff = h.HandOffs.Should().ContainSingle().Which;
            handOff.To.Should().Be(ground);
            BodyState created = h.Output.Bodies.Single(state => state.Entity == walker);
            created.Frame.Should().Be(ground);
            handOff.Pose.Rotation.Should().Be(created.Pose.Rotation);
            Vector3.Distance(UpOf(created), Vector3.UnitY).Should().BeGreaterThan(0.1f, "physics turned the character away from the building's up");
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
        var standing = new List<bool>();
        int handOffStep = -1;
        var (h, ground, walker) = WalkOutOfABuilding(leanDegrees: 0, output =>
        {
            standing.Add(output.Characters.Single().Standing);
            if (output.HandOffs.Count > 0)
                handOffStep = standing.Count - 1;
        });
        using (h)
        {
            // About 0.5 m, or 8 steps, into the ground.
            h.Step(8);

            h[walker].Frame.Should().Be(ground);
            int settled = standing.IndexOf(true);
            settled.Should().BeInRange(0, 10, "the walker starts 5 cm above the floor of the building");
            handOffStep.Should().BeGreaterThan(settled);
            IEnumerable<bool> expected = Enumerable.Range(settled, standing.Count - settled).Select(step => step != handOffStep);
            standing.Skip(settled).Should().Equal(expected);
        }
    }

    [Fact]
    public void ACharacterDoesNotStandInTheStepThatHandsItToAnotherFrame()
    {
        var (h, ground, walker) = WalkOutOfABuilding(leanDegrees: 0);
        using (h)
        {
            h[walker].Frame.Should().Be(ground);
            h.Standing[walker].Should().BeFalse("in its new frame, nothing has stood the character yet");
        }
    }
}
