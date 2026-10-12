using System.Numerics;
using Silk.NET.Input;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Crate and Carry: a person takes a crate that it uses, carries a drawn copy of it in front of its eye, and throws it
/// with a charged throw of the left mouse button. The person has a PersonControl and a Carry, and its view is the world's
/// camera. It stands on a floor in a room with 9.81 m/s² of gravity along -Y, or it floats in a room with no gravity,
/// where a thrown crate and the person keep the velocities that the throw gives them.
/// </summary>
public sealed class CarryTests : IDisposable
{
    private static readonly Vector3 Eye = new(0, 0.75f, 0);
    private static readonly CapsuleShapeDesc Capsule = new(0.6f, 0.3f);
    private const float PersonMass = 70;

    /// <summary>
    /// A crate 0.4 m on a side, of 5 kg. The world has no renderer, so the mesh and the material are only numbers, which
    /// the drawn copy and a thrown crate must keep.
    /// </summary>
    private static readonly CrateSpec Spec = new(new Vector3(0.2f), 5, new MeshId(7), new MaterialId(3));

    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    private Entity SpawnPerson(FrameHandle frame, DPose pose, Vector3 velocity)
    {
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            LinearVelocity = velocity,
            Body = new BodyDesc(Capsule, BodyMotion.Character, Mass: PersonMass),
            Components = [new PersonControl(Eye), new Carry(Capsule)],
            Subscriptions = [Topics.Input],
        });
        world.Camera = person;
        return person;
    }

    /// <summary>
    /// A room with 9.81 m/s² of gravity along -Y and a floor whose top is at y = 0, with a person that stands on the floor
    /// at the origin and faces -Z.
    /// </summary>
    private (FrameHandle Room, Entity Person) StandInARoom()
    {
        FrameHandle room = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        world.Spawn(new SpawnDesc
        {
            Frame = room,
            Pose = new DPose(new Double3(0, -0.5, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
        });
        Entity person = SpawnPerson(room, new DPose(new Double3(0, 0.95, 0), DQuaternion.Identity), Vector3.Zero);
        // The person lands on the floor.
        Ticks(30);
        return (room, person);
    }

    /// <summary>A room with no gravity, with a person at rest at the origin that faces -Z.</summary>
    private (FrameHandle Space, Entity Person) FloatInSpace() => FloatInSpace(DQuaternion.Identity, Vector3.Zero);

    /// <summary>A room with no gravity, with a person at the origin, turned by <paramref name="rotation"/>, that moves at <paramref name="velocity"/>.</summary>
    private (FrameHandle Space, Entity Person) FloatInSpace(DQuaternion rotation, Vector3 velocity)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static));
        Entity person = SpawnPerson(space, new DPose(Double3.Zero, rotation), velocity);
        Ticks(3);
        return (space, person);
    }

    private Entity SpawnCrate(FrameHandle frame, Double3 at, Vector3 velocity = default) =>
        world.Spawn(Spec.Spawn(frame, new DPose(at, DQuaternion.Identity), velocity));

    private void Post(InputKind kind, int code = 0, Vector2 delta = default) =>
        world.Post(Event.Create(Topics.Input, Entity.Null, new InputEvent { Kind = kind, Code = code, Delta = delta }));

    private void PressE()
    {
        Post(InputKind.KeyDown, (int)Key.E);
        Post(InputKind.KeyUp, (int)Key.E);
    }

    private void ButtonDown() => Post(InputKind.MouseDown, (int)MouseButton.Left);

    private void ButtonUp() => Post(InputKind.MouseUp, (int)MouseButton.Left);

    private void MoveMouse(float right, float down) => Post(InputKind.MouseMove, delta: new Vector2(right, down));

    /// <summary>Every live entity that has a component of type <typeparamref name="T"/>, in slot order.</summary>
    private List<Entity> With<T>() where T : class, IComponent
    {
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        return live.Where(entity => world.GetComponent<T>(entity) != null).ToList();
    }

    /// <summary>
    /// Puts a crate 1 m in front of the person, which moves with it at <paramref name="velocity"/>, and presses E. A
    /// post acts in the second tick after it: the person sends Use, the crate offers itself in the next tick, the person
    /// takes the offer in the tick after that, and the crate goes in the next. Returns the drawn copy.
    /// </summary>
    private Entity TakeACrate(FrameHandle frame, Entity person, Vector3 velocity = default)
    {
        SpawnCrate(frame, world.Snapshot.PoseOf(person).Position + new Double3(0, 0, -1), velocity);
        Ticks(1);
        PressE();
        Ticks(8);
        return With<HeldInView>().Should().ContainSingle().Which;
    }

    /// <summary>
    /// Holds the left button for <paramref name="ticksHeld"/> ticks, lets go, and returns the crate that the throw
    /// spawned. With no ticks, the press and the release reach the person in one tick: a click.
    /// </summary>
    private Entity Throw(int ticksHeld)
    {
        ButtonDown();
        Ticks(ticksHeld);
        ButtonUp();
        Ticks(4);
        return With<Crate>().Should().ContainSingle("the throw spawned one crate").Which;
    }

    private static void ExpectNear(Vector3 actual, Vector3 expected, float tolerance) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    /// <summary>The distance of <paramref name="point"/>, in the person's axes, from the axis of the person's capsule.</summary>
    private static float DistanceFromTheCapsulesAxis(Vector3 point) =>
        Vector3.Distance(point, new Vector3(0, Math.Clamp(point.Y, -Capsule.HalfHeight, Capsule.HalfHeight), 0));

    [Fact]
    public void AUsedCrateOffersItselfToTheUserWithItsSizeMassMeshAndMaterial()
    {
        (FrameHandle room, _) = StandInARoom();
        Entity crate = SpawnCrate(room, new Double3(3, 0.2, 0));
        Probe user = new(EventMask.Of<CrateOffer>());
        Entity userEntity = world.Spawn(new SpawnDesc { Frame = room, Pose = new DPose(new Double3(-3, 1, 0), DQuaternion.Identity), Components = [user] });
        world.Post(Event.Create(crate, Entity.Null, new Use { User = userEntity }));
        Ticks(3);

        user.Seen.Should().ContainSingle();
        user.Seen[0].Event.Sender.Should().Be(crate);
        user.Seen[0].Event.As<CrateOffer>().Crate.Should().Be(Spec);
        world.IsAlive(crate).Should().BeTrue("an offer alone does not move the crate");
    }

    [Fact]
    public void ACrateThatIsTakenGoesAndOffersItselfToNoOneInThatTick()
    {
        (FrameHandle room, _) = StandInARoom();
        Entity crate = SpawnCrate(room, new Double3(3, 0.2, 0));
        Probe user = new(EventMask.Of<CrateOffer>());
        Entity userEntity = world.Spawn(new SpawnDesc { Frame = room, Pose = new DPose(new Double3(-3, 1, 0), DQuaternion.Identity), Components = [user] });
        world.Post(Event.Create(crate, Entity.Null, new Use { User = userEntity }));
        world.Post(Event.Create(crate, Entity.Null, new CrateTaken()));
        Ticks(3);
        // A later order reaches no one.
        world.Post(Event.Create(crate, Entity.Null, new CrateTaken()));
        world.Post(Event.Create(crate, Entity.Null, new Use { User = userEntity }));
        Ticks(3);

        world.IsAlive(crate).Should().BeFalse();
        user.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void PressingENearACrateTakesItAndADrawnCopyRidesInFrontOfTheEyeLowAndToTheRight()
    {
        (FrameHandle room, Entity person) = StandInARoom();
        Entity crate = SpawnCrate(room, new Double3(0, 0.2, -1));
        Ticks(1);
        PressE();
        Ticks(8);

        world.IsAlive(crate).Should().BeFalse("the person took the crate, so it went");
        Entity copy = With<HeldInView>().Should().ContainSingle().Which;
        world.GetBody(copy).Should().BeNull("the drawn copy is only drawn");
        world.GetRender(copy).Mesh.Should().Be(Spec.Mesh);
        world.GetRender(copy).Material.Should().Be(Spec.Material);
        world.GetFrame(copy).Should().Be(room);
        // The person looks along -Z from its eye, and its body is not turned. Low and to the right is -Y and +X.
        Double3 held = world.GetPose(copy).Position - world.GetPose(person).Position;
        Double3.Distance(held, (Double3)(Eye + Carry.Hold)).Should().BeLessThan(1e-4);
        Carry.Hold.X.Should().BePositive();
        Carry.Hold.Y.Should().BeNegative();
        Carry.Hold.Z.Should().BeNegative();
    }

    [Fact]
    public void TheDrawnCopyRidesAtItsPlaceFromItsFirstTick()
    {
        (FrameHandle room, Entity person) = StandInARoom();
        SpawnCrate(room, new Double3(0, 0.2, -1));
        Ticks(1);
        PressE();
        int ticks = 0;
        while (With<HeldInView>().Count == 0 && ticks++ < 10)
            Ticks(1);

        // The copy reads the view in its own ticks, which start after the tick that makes it live. The take places it.
        Entity copy = With<HeldInView>().Should().ContainSingle().Which;
        Double3 held = world.GetPose(copy).Position - world.GetPose(person).Position;
        Double3.Distance(held, (Double3)(Eye + Carry.Hold)).Should().BeLessThan(1e-4);
    }

    [Fact]
    public void OfTwoCratesInReachThePersonTakesOneAndTheOtherStays()
    {
        (FrameHandle room, _) = StandInARoom();
        Entity left = SpawnCrate(room, new Double3(-0.6, 0.2, -1)), right = SpawnCrate(room, new Double3(0.6, 0.2, -1));
        Ticks(1);
        PressE();
        Ticks(8);

        new[] { left, right }.Count(world.IsAlive).Should().Be(1, "both offered in one tick, and the hands were full after the first");
        With<HeldInView>().Should().ContainSingle();
    }

    [Fact]
    public void WithFullHandsThePersonIgnoresAnOfferAndTheCrateStays()
    {
        (FrameHandle room, Entity person) = StandInARoom();
        TakeACrate(room, person);
        Entity second = SpawnCrate(room, new Double3(0, 0.2, -1));
        Ticks(1);
        PressE();
        Ticks(8);

        world.IsAlive(second).Should().BeTrue();
        With<HeldInView>().Should().ContainSingle();
    }

    [Fact]
    public void TheDrawnCopyFollowsTheViewsTurnAndTilt()
    {
        (FrameHandle room, Entity person) = StandInARoom();
        Entity copy = TakeACrate(room, person);
        MoveMouse(0.7f / PersonControl.TurnPerPixel, -0.4f / PersonControl.TurnPerPixel);
        // The copy reads the view from the snapshot, so it takes a tick longer than the view.
        Ticks(4);

        Pose view = world.GetRender(person).ViewOffset;
        Vector3.Transform(-Vector3.UnitZ, view.Rotation).Y.Should().BeApproximately(MathF.Sin(0.4f), 1e-4f, "the view looks 0.4 rad up");
        DPose personPose = world.GetPose(person), held = world.GetPose(copy);
        Double3.Distance(held.Position - personPose.Position, (Double3)(view.Position + Vector3.Transform(Carry.Hold, view.Rotation))).Should().BeLessThan(1e-4);
        Quaternion.Dot(held.Rotation.ToQuaternion(), view.Rotation).Should().BeApproximately(1, 1e-5f, "the copy turns with the view");
    }

    [Theory]
    [InlineData(15, 3f)]
    [InlineData(30, 6f)]
    [InlineData(60, 12f)]
    [InlineData(120, 12f)]
    public void HoldingTheLeftButtonChargesTheThrowFromZeroToTwelveMetersPerSecondInOneSecond(int ticksHeld, float speed)
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);

        Entity thrown = Throw(ticksHeld);

        // The person looks along -Z. A tick of charge adds 12 m/s / 60 = 0.2 m/s.
        ExpectNear(world.Snapshot.VelocityOf(thrown), new Vector3(0, 0, -speed), 1e-3f);
    }

    [Fact]
    public void TheCrateFliesAlongTheViewAtTheChargedSpeedOnTopOfThePersonsVelocity()
    {
        // The person is turned a quarter turn about +Z, which takes its +X to +Y. It drifts at (1, 0.5, -2) m/s.
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2);
        (FrameHandle space, Entity person) = FloatInSpace(turn, new Vector3(1, 0.5f, -2));
        TakeACrate(space, person, new Vector3(1, 0.5f, -2));
        MoveMouse(0.6f / PersonControl.TurnPerPixel, -0.3f / PersonControl.TurnPerPixel);
        Ticks(3);
        Vector3 drift = world.Snapshot.VelocityOf(person);

        Entity thrown = Throw(60);

        // The view turns 0.6 rad right and tilts 0.3 rad up, in the person's axes. The body's turn takes that into the room's.
        var view = new Vector3(MathF.Cos(0.3f) * MathF.Sin(0.6f), MathF.Sin(0.3f), -MathF.Cos(0.3f) * MathF.Cos(0.6f));
        ExpectNear(world.Snapshot.VelocityOf(thrown), drift + Vector3.Transform(view, turn.ToQuaternion()) * Carry.MaxThrowSpeed, 1e-3f);
    }

    [Fact]
    public void AThrownCrateStartsTurnedAsTheCarriedCopyWas()
    {
        (FrameHandle space, Entity person) = FloatInSpace(DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2), Vector3.Zero);
        Entity copy = TakeACrate(space, person);
        MoveMouse(0.6f / PersonControl.TurnPerPixel, -0.3f / PersonControl.TurnPerPixel);
        Ticks(4);
        Quaternion held = world.GetPose(copy).Rotation.ToQuaternion();

        // A click throws at no speed, and the crate does not turn in flight.
        Entity thrown = Throw(0);

        MathF.Abs(Quaternion.Dot(world.GetPose(thrown).Rotation.ToQuaternion(), held)).Should().BeApproximately(1, 1e-5f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    [InlineData(-1.4f)]
    [InlineData(1f)]
    public void AThrownCrateStartsOnTheViewWhereItsBoundingSphereClearsThePersonsCapsuleByTheGap(float pitch)
    {
        // The eye is in the capsule's rounded top. A level view leaves the capsule through that top, a view 0.5 rad down
        // through its round side, a view 1.4 rad down through its rounded bottom, below the feet, and a view 1 rad up
        // through its rounded top again.
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        MoveMouse(0, -pitch / PersonControl.TurnPerPixel);
        Ticks(3);

        // A click throws at no speed, so the crate stays where it starts.
        Entity thrown = Throw(0);

        Vector3 at = (world.Snapshot.PoseOf(thrown).Position - world.Snapshot.PoseOf(person).Position).ToVector3();
        var aim = new Vector3(0, MathF.Sin(pitch), -MathF.Cos(pitch));
        Vector3.Cross(at - Eye, aim).Length().Should().BeLessThan(1e-4f, "the crate starts on the view");
        Vector3.Dot(at - Eye, aim).Should().BePositive("the crate starts in front of the eye");
        DistanceFromTheCapsulesAxis(at).Should().BeApproximately(Capsule.Radius + Spec.BoundingRadius + Carry.ThrowGap, 1e-4f);
    }

    [Fact]
    public void ADriftingPersonThrowsFromInFrontOfItsEyeNotFromWhereItWasATickBefore()
    {
        // At 30 m/s the person moves 0.5 m in a tick. Physics moves it in the tick of the throw, and the crate becomes live
        // at the end of that tick.
        (FrameHandle space, Entity person) = FloatInSpace(DQuaternion.Identity, new Vector3(30, 0, 0));
        TakeACrate(space, person, new Vector3(30, 0, 0));

        Entity thrown = Throw(0);

        Double3 at = world.Snapshot.PoseOf(thrown).Position - world.Snapshot.PoseOf(person).Position;
        at.X.Should().BeApproximately(0, 1e-3, "the crate starts straight in front of the eye");
        at.Z.Should().BeNegative();
    }

    [Fact]
    public void AThrowPushesThePersonBackWithTheCratesMassTimesTheThrowVelocity()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        Vector3 before = world.Snapshot.VelocityOf(person);

        Throw(60);

        // 5 kg thrown at 12 m/s along -Z pushes 70 kg back along +Z at 5 · 12 / 70 = 0.857 m/s.
        ExpectNear(world.Snapshot.VelocityOf(person) - before, new Vector3(0, 0, Spec.Mass * Carry.MaxThrowSpeed / PersonMass), 1e-4f);
    }

    [Fact]
    public void AThrownCrateIsANewCrateOfTheSameSizeMassAndLookThatThePersonCanTakeAgain()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);

        Entity thrown = Throw(0);

        With<HeldInView>().Should().BeEmpty("the throw let go of the drawn copy");
        world.GetBody(thrown).Should().Be(new BodyDesc(new BoxShapeDesc(Spec.HalfExtents), Mass: Spec.Mass, LinearDamping: 0, AngularDamping: 0));
        world.GetRender(thrown).Mesh.Should().Be(Spec.Mesh);
        world.GetRender(thrown).Material.Should().Be(Spec.Material);
        PressE();
        Ticks(8);
        world.IsAlive(thrown).Should().BeFalse("the person took the thrown crate again");
        With<HeldInView>().Should().ContainSingle();
    }

    [Fact]
    public void WithEmptyHandsTheLeftButtonThrowsNothing()
    {
        (_, Entity person) = FloatInSpace();
        ButtonDown();
        Ticks(60);
        ButtonUp();
        Ticks(4);

        With<Crate>().Should().BeEmpty();
        world.Snapshot.VelocityOf(person).Should().Be(Vector3.Zero);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WhenItsViewGoesOffThePersonDropsTheChargeAndKeepsTheCrate()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        ButtonDown();
        Ticks(30);
        world.Camera = world.Spawn(new SpawnDesc { Frame = space, Render = new RenderData() });
        Ticks(5);
        world.Camera = person;
        ButtonUp();
        Ticks(5);

        With<Crate>().Should().BeEmpty("the release came after the charge was dropped");
        With<HeldInView>().Should().ContainSingle("the person keeps the crate");
        // A new press charges from zero: 15 ticks throw at 3 m/s.
        ExpectNear(world.Snapshot.VelocityOf(Throw(15)), new Vector3(0, 0, -3), 1e-3f);
    }

    [Fact]
    public void WhileAnotherViewIsOnTheLeftButtonChargesNothing()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        world.Camera = world.Spawn(new SpawnDesc { Frame = space, Render = new RenderData() });
        ButtonDown();
        // The person handles the press in the second tick, the last one before its view comes back on.
        Ticks(2);
        world.Camera = person;
        Ticks(30);
        ButtonUp();
        Ticks(5);

        With<Crate>().Should().BeEmpty("the press came while another view was on");
        With<HeldInView>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(0f, 5f)]
    [InlineData(-0.2f, 5f)]
    [InlineData(float.NaN, 5f)]
    [InlineData(float.PositiveInfinity, 5f)]
    [InlineData(0.2f, 0f)]
    [InlineData(0.2f, -1f)]
    [InlineData(0.2f, float.NaN)]
    [InlineData(0.2f, float.PositiveInfinity)]
    public void ACrateRefusesASizeOrAMassThatIsNotPositiveAndFinite(float halfHeight, float mass)
    {
        Action create = () => _ = new Crate(Spec with { HalfExtents = new Vector3(0.2f, halfHeight, 0.2f), Mass = mass });

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("spec");
    }

    [Fact]
    public void CarryNeedsThePersonsCapsule()
    {
        Action create = () => _ = new Carry(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("body");
    }
}
