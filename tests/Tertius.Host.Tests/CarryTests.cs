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

    /// <summary>
    /// A turn of 1.1 rad about the axis (1, 2, 2) / 3, which lines up with none of the frame's axes, so a vector that
    /// is turned wrongly, or not at all, lands far from where the right turn puts it.
    /// </summary>
    private static readonly DQuaternion Tumbled = DQuaternion.FromAxisAngle(new Double3(1.0 / 3, 2.0 / 3, 2.0 / 3), 1.1);

    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            world.Tick();
    }

    /// <summary>
    /// A person with a PersonControl and a Carry, whose eye is at <paramref name="eye"/> and whose capsule is
    /// <paramref name="capsule"/>, or <see cref="Eye"/> and <see cref="Capsule"/> if the test gives none. Its view is the
    /// world's camera.
    /// </summary>
    private Entity SpawnPerson(FrameHandle frame, DPose pose, Vector3 velocity, Vector3? eye = null, CapsuleShapeDesc? capsule = null)
    {
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            LinearVelocity = velocity,
            Body = new BodyDesc(capsule ?? Capsule, BodyMotion.Character, Mass: PersonMass),
            Components = [new PersonControl(eye ?? Eye), new Carry(capsule ?? Capsule)],
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

    /// <summary>
    /// A room with no gravity, with a person at rest at the origin that faces -Z, whose eye is at <paramref name="eye"/> and
    /// whose capsule is <paramref name="capsule"/>.
    /// </summary>
    private (FrameHandle Space, Entity Person) FloatInSpace(Vector3 eye, CapsuleShapeDesc capsule)
    {
        FrameHandle space = NewSpace();
        Entity person = SpawnPerson(space, new DPose(Double3.Zero, DQuaternion.Identity), Vector3.Zero, eye, capsule);
        Ticks(3);
        return (space, person);
    }

    /// <summary>
    /// A room with no gravity, with a person at rest at the origin that has a Carry and no PersonControl, so that nothing
    /// turns its view: it looks along <paramref name="look"/>, from <see cref="Eye"/>, for good.
    /// </summary>
    private (FrameHandle Space, Entity Person) FloatLooking(Quaternion look)
    {
        FrameHandle space = NewSpace();
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(Double3.Zero, DQuaternion.Identity),
            Body = new BodyDesc(Capsule, BodyMotion.Character, Mass: PersonMass),
            Render = new RenderData { ViewOffset = new Pose(Eye, look) },
            Components = [new Carry(Capsule)],
            Subscriptions = [Topics.Input],
        });
        world.Camera = person;
        Ticks(3);
        return (space, person);
    }

    /// <summary>A room with no gravity, with a person at the origin, turned by <paramref name="rotation"/>, that moves at <paramref name="velocity"/>.</summary>
    private (FrameHandle Space, Entity Person) FloatInSpace(DQuaternion rotation, Vector3 velocity)
    {
        FrameHandle space = NewSpace();
        Entity person = SpawnPerson(space, new DPose(Double3.Zero, rotation), velocity);
        Ticks(3);
        return (space, person);
    }

    /// <summary>
    /// A room with no gravity, with two persons at rest, 2 m apart, that face -Z. Both have a PersonControl and a Carry,
    /// but only the person that the world's camera names reads the keys, and that is the second one.
    /// </summary>
    private (FrameHandle Space, Entity First, Entity Second) FloatTwoInSpace()
    {
        FrameHandle space = NewSpace();
        Entity first = SpawnPerson(space, At(-1), Vector3.Zero), second = SpawnPerson(space, At(1), Vector3.Zero);
        Ticks(3);
        return (space, first, second);
    }

    private FrameHandle NewSpace() => world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), MotionDriver.Static));

    private Entity SpawnCrate(FrameHandle frame, Double3 at, Vector3 velocity = default) =>
        world.Spawn(Spec.Spawn(frame, new DPose(at, DQuaternion.Identity), velocity));

    private void Post(InputKind kind, int code = 0, Vector2 delta = default) =>
        world.Post(Event.Create(Topics.Input, Entity.Null, new InputEvent { Kind = kind, Code = code, Delta = delta }));

    private void PressE()
    {
        Post(InputKind.KeyDown, (int)Key.E);
        Post(InputKind.KeyUp, (int)Key.E);
    }

    /// <summary>
    /// The Use that a press of E by <paramref name="user"/> sends to <paramref name="crate"/>, from the user. Only the
    /// person whose view is on reads a key, so a test of two users posts the Use that their two presses send.
    /// </summary>
    private void UseCrate(Entity crate, Entity user) =>
        world.Post(Event.Create(crate, user, new Use { User = user }));

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
    /// True if <paramref name="copy"/> sits where the drawn copy of a crate sits for <paramref name="person"/>: at the
    /// person's eye plus <see cref="Carry.Hold"/>, in the person's axes. The persons of a test stand 2 m apart.
    /// </summary>
    private bool HeldBy(Entity copy, Entity person) =>
        Double3.Distance(world.PoseInOwnFrame(copy).Position - world.PoseInOwnFrame(person).Position, (Double3)(Eye + Carry.Hold)) < 1e-3;

    /// <summary>
    /// Puts a crate 1 m in front of the person, which moves with it at <paramref name="velocity"/>, and presses E. A
    /// post acts in the second tick after it: the person sends Use, the crate offers itself in the next tick, the person
    /// takes the offer in the tick after that, and the crate goes in the next. Returns the drawn copy.
    /// </summary>
    private Entity TakeACrate(FrameHandle frame, Entity person, Vector3 velocity = default)
    {
        SpawnCrate(frame, world.Snapshot.PoseInOwnFrame(person).Position + new Double3(0, 0, -1), velocity);
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

    /// <summary>
    /// The direction of a view that has turned <paramref name="yaw"/> radians to the right and tilted
    /// <paramref name="pitch"/> radians up, in the person's axes. The person faces -Z before it turns.
    /// </summary>
    private static Vector3 LookDirection(float yaw, float pitch) =>
        new(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), -MathF.Cos(pitch) * MathF.Cos(yaw));

    private static void ExpectNear(Vector3 actual, Vector3 expected, float tolerance) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    /// <summary>
    /// Expects that a crate which a click threw starts at <paramref name="at"/>, in the person's axes, on the view from
    /// <paramref name="eye"/> along the unit vector <paramref name="aim"/>, in front of the eye, with its bounding sphere
    /// clear of <paramref name="capsule"/> by <see cref="Carry.ThrowGap"/>.
    /// </summary>
    private static void ExpectStartsOnTheViewClearOfTheCapsule(Vector3 at, Vector3 eye, Vector3 aim, CapsuleShapeDesc capsule)
    {
        Vector3.Cross(at - eye, aim).Length().Should().BeLessThan(1e-4f, "the crate starts on the view");
        Vector3.Dot(at - eye, aim).Should().BePositive("the crate starts in front of the eye");
        // The distance of the crate's center, in the person's axes, from the axis of the person's capsule.
        float distance = Vector3.Distance(at, new Vector3(0, Math.Clamp(at.Y, -capsule.HalfHeight, capsule.HalfHeight), 0));
        distance.Should().BeApproximately(capsule.Radius + Spec.BoundingRadius + Carry.ThrowGap, 1e-4f);
    }

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
        Double3 held = world.PoseInOwnFrame(copy).Position - world.PoseInOwnFrame(person).Position;
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
        Double3 held = world.PoseInOwnFrame(copy).Position - world.PoseInOwnFrame(person).Position;
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
    public void ACrateThatGetsTwoUsesInOneTickOffersItselfToTheFirstUserOnly()
    {
        FrameHandle space = NewSpace();
        Entity crate = SpawnCrate(space, new Double3(3, 0, 0));
        Probe first = new(EventMask.Of<CrateOffer>()), second = new(EventMask.Of<CrateOffer>());
        Entity firstUser = world.Spawn(new SpawnDesc { Frame = space, Components = [first] });
        Entity secondUser = world.Spawn(new SpawnDesc { Frame = space, Components = [second] });
        UseCrate(crate, firstUser);
        UseCrate(crate, secondUser);
        Ticks(6);

        first.Seen.Should().ContainSingle("the crate holds one offer at a time, and the first use got it");
        second.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ACrateIgnoresUsesWhileItsOfferIsOnTheWayAndOffersAgainWhenTheRoundTripHasPassedWithNoAnswer()
    {
        FrameHandle space = NewSpace();
        Entity crate = SpawnCrate(space, new Double3(3, 0, 0));
        // This user never answers, as a person with full hands does not.
        Probe user = new(EventMask.Of<CrateOffer>());
        Entity userEntity = world.Spawn(new SpawnDesc { Frame = space, Components = [user] });
        // Three uses, in three ticks in a row. The crate gets them on the ticks x, x + 1 and x + 2. It offers on x, and
        // the offer reaches the user on x + 1. The answer would reach the crate on x + 2.
        UseCrate(crate, userEntity);
        Ticks(1);
        UseCrate(crate, userEntity);
        Ticks(1);
        UseCrate(crate, userEntity);
        Ticks(8);

        user.Seen.Should().HaveCount(2, "the use on x + 1 came before the answer could, and the use on x + 2 came after the silence");
        (user.Seen[1].Tick - user.Seen[0].Tick).Should().Be(2);
    }

    [Fact]
    public void ACrateThatGetsAUseAndADestroyInOneTickIsNotCarried()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        Entity crate = SpawnCrate(space, new Double3(0, 0, -1));
        Ticks(1);
        UseCrate(crate, person);
        world.Post(Event.Create(crate, Entity.Null, new Destroy()));
        Ticks(8);

        // The crate offers itself on the tick that it dies, so the offer reaches a person after the crate is gone.
        world.IsAlive(crate).Should().BeFalse();
        With<HeldInView>().Should().BeEmpty("the sender of the offer was gone when the offer arrived");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OfTwoPersonsWhoUseOneCrateTicksApartTheFirstCarriesItAndTheCrateIsTakenOnce(int ticksApart)
    {
        // The crate offers on x, the first person takes the offer on x + 1, and the crate hears it on x + 2. A second use
        // that reaches the crate on x or x + 1 comes while the offer is out. One that reaches it on x + 2 meets the news
        // that the crate is taken, and one on x + 3 reaches no one.
        (FrameHandle space, Entity first, Entity second) = FloatTwoInSpace();
        Entity crate = SpawnCrate(space, new Double3(0, 0, -1));
        Probe news = new(EventMask.Of<CrateTaken>());
        world.AddComponent(crate, news);
        Ticks(1);
        UseCrate(crate, first);
        Ticks(ticksApart);
        UseCrate(crate, second);
        Ticks(10);

        world.IsAlive(crate).Should().BeFalse();
        news.Seen.Where(seen => seen.Event.Is<CrateTaken>()).Should().ContainSingle("the crate was taken once");
        Entity copy = With<HeldInView>().Should().ContainSingle("one crate is carried by one person").Which;
        HeldBy(copy, first).Should().BeTrue("the first person to use the crate took it");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateThatThePersonWithFullHandsDeclinedGoesToAnotherPersonAfterTheRoundTrip()
    {
        (FrameHandle space, Entity first, Entity second) = FloatTwoInSpace();
        Entity carried = SpawnCrate(space, new Double3(-1, 0, -1));
        Ticks(1);
        UseCrate(carried, first);
        Ticks(8);
        Entity crate = SpawnCrate(space, new Double3(0, 0, -1));
        Ticks(1);
        // The first person has full hands, so it declines. The second person uses the crate some ticks later.
        UseCrate(crate, first);
        Ticks(3);
        UseCrate(crate, second);
        Ticks(10);

        world.IsAlive(crate).Should().BeFalse("the second person took the crate that the first one declined");
        List<Entity> copies = With<HeldInView>();
        copies.Should().HaveCount(2);
        copies.Count(copy => HeldBy(copy, first)).Should().Be(1, "the first person still carries the crate that it took before");
        copies.Count(copy => HeldBy(copy, second)).Should().Be(1);
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
        DPose personPose = world.PoseInOwnFrame(person), held = world.PoseInOwnFrame(copy);
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
        ExpectNear(world.Snapshot.VelocityInOwnFrame(thrown), new Vector3(0, 0, -speed), 1e-3f);
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
        Vector3 drift = world.Snapshot.VelocityInOwnFrame(person);

        Entity thrown = Throw(60);

        // The view turns 0.6 rad right and tilts 0.3 rad up, in the person's axes. The body's turn takes that into the room's.
        var view = new Vector3(MathF.Cos(0.3f) * MathF.Sin(0.6f), MathF.Sin(0.3f), -MathF.Cos(0.3f) * MathF.Cos(0.6f));
        ExpectNear(world.Snapshot.VelocityInOwnFrame(thrown), drift + Vector3.Transform(view, turn.ToQuaternion()) * Carry.MaxThrowSpeed, 1e-3f);
    }

    [Fact]
    public void AThrownCrateStartsTurnedAsTheCarriedCopyWas()
    {
        (FrameHandle space, Entity person) = FloatInSpace(DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2), Vector3.Zero);
        Entity copy = TakeACrate(space, person);
        MoveMouse(0.6f / PersonControl.TurnPerPixel, -0.3f / PersonControl.TurnPerPixel);
        Ticks(4);
        Quaternion held = world.PoseInOwnFrame(copy).Rotation.ToQuaternion();

        // A click throws at no speed, and the crate does not turn in flight.
        Entity thrown = Throw(0);

        MathF.Abs(Quaternion.Dot(world.PoseInOwnFrame(thrown).Rotation.ToQuaternion(), held)).Should().BeApproximately(1, 1e-5f);
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

        Vector3 at = (world.Snapshot.PoseInOwnFrame(thrown).Position - world.Snapshot.PoseInOwnFrame(person).Position).ToVector3();
        ExpectStartsOnTheViewClearOfTheCapsule(at, Eye, new Vector3(0, MathF.Sin(pitch), -MathF.Cos(pitch)), Capsule);
    }

    [Fact]
    public void ADriftingPersonThrowsFromInFrontOfItsEyeNotFromWhereItWasATickBefore()
    {
        // At 30 m/s the person moves 0.5 m in a tick. Physics moves it in the tick of the throw, and the crate becomes live
        // at the end of that tick.
        (FrameHandle space, Entity person) = FloatInSpace(DQuaternion.Identity, new Vector3(30, 0, 0));
        TakeACrate(space, person, new Vector3(30, 0, 0));

        Entity thrown = Throw(0);

        Double3 at = world.Snapshot.PoseInOwnFrame(thrown).Position - world.Snapshot.PoseInOwnFrame(person).Position;
        at.X.Should().BeApproximately(0, 1e-3, "the crate starts straight in front of the eye");
        at.Z.Should().BeNegative();
    }

    [Fact]
    public void AThrowPushesThePersonBackWithTheCratesMassTimesTheThrowVelocity()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        Vector3 before = world.Snapshot.VelocityInOwnFrame(person);

        Throw(60);

        // 5 kg thrown at 12 m/s along -Z pushes 70 kg back along +Z at 5 · 12 / 70 = 0.857 m/s.
        ExpectNear(world.Snapshot.VelocityInOwnFrame(person) - before, new Vector3(0, 0, Spec.Mass * Carry.MaxThrowSpeed / PersonMass), 1e-4f);
    }

    [Fact]
    public void AThrowPushesATurnedPersonBackAlongTheFramesAxesAgainstTheThrow()
    {
        (FrameHandle space, Entity person) = FloatInSpace(Tumbled, Vector3.Zero);
        TakeACrate(space, person);
        MoveMouse(0.6f / PersonControl.TurnPerPixel, -0.3f / PersonControl.TurnPerPixel);
        Ticks(3);

        Entity thrown = Throw(60);

        // The throw goes along the view, which the body's turn takes from the person's axes into the frame's.
        Vector3 along = Vector3.Transform(LookDirection(0.6f, 0.3f), Tumbled.ToQuaternion());
        ExpectNear(world.Snapshot.VelocityInOwnFrame(thrown), along * Carry.MaxThrowSpeed, 1e-3f);
        // The person was at rest, so its velocity is the recoil: 5 kg · 12 m/s / 70 kg = 0.857 m/s against the throw.
        ExpectNear(world.Snapshot.VelocityInOwnFrame(person), -along * (Spec.Mass * Carry.MaxThrowSpeed / PersonMass), 1e-4f);
    }

    [Fact]
    public void ATurnedPersonsThrownCrateStartsOnTheViewWhereItsBoundingSphereClearsTheCapsuleByTheGap()
    {
        // The view looks 0.6 rad to the right and 0.5 rad down, so the aim leaves the capsule through its round side.
        (FrameHandle space, Entity person) = FloatInSpace(Tumbled, Vector3.Zero);
        TakeACrate(space, person);
        MoveMouse(0.6f / PersonControl.TurnPerPixel, 0.5f / PersonControl.TurnPerPixel);
        Ticks(3);

        // A click throws at no speed, so the crate stays where it starts.
        Entity thrown = Throw(0);

        // Where the crate is, in the person's axes: its offset from the person in the frame's axes, turned back by the
        // body's turn.
        Double3 offset = world.Snapshot.PoseInOwnFrame(thrown).Position - world.Snapshot.PoseInOwnFrame(person).Position;
        Vector3 at = Vector3.Transform(offset.ToVector3(), Quaternion.Inverse(Tumbled.ToQuaternion()));
        ExpectStartsOnTheViewClearOfTheCapsule(at, Eye, LookDirection(0.6f, -0.5f), Capsule);
    }

    [Theory]
    // The eye is in the capsule's rounded top. The view leaves the capsule through that top.
    [InlineData(0.15f, 0.75f, -0.1f, 0.6f, 0.6f, 0.5f)]
    // The same eye, with the view 0.5 rad down: it leaves through the round side.
    [InlineData(0.15f, 0.75f, -0.1f, 0.6f, -0.9f, -0.5f)]
    // The eye is in the straight part of the capsule, 0.2 m under its center. A level view leaves through the round side.
    [InlineData(0.15f, -0.2f, -0.1f, 0.6f, 0.6f, 0f)]
    // The same eye, with the view turned past a right angle and 0.8 rad up: it leaves through the round side too.
    [InlineData(0.15f, -0.2f, -0.1f, 0.6f, 2.2f, 0.8f)]
    // The same eye, with the view 1.2 rad down: it leaves through the rounded bottom.
    [InlineData(0.15f, -0.2f, -0.1f, 0.6f, -0.7f, -1.2f)]
    // The same eye, with the view 1.3 rad up: it leaves through the rounded top.
    [InlineData(0.15f, -0.2f, -0.1f, 0.6f, 1f, 1.3f)]
    // A taller capsule, with the eye in its straight part at the center, and two views that leave through its round side.
    [InlineData(0.15f, 0f, -0.1f, 1f, 0.6f, 0.3f)]
    [InlineData(0.15f, 0f, -0.1f, 1f, -2f, -0.4f)]
    public void AThrownCrateFromAnEyeOffTheCapsulesAxisStartsOnTheViewWhereItsBoundingSphereClearsTheCapsuleByTheGap(
        float eyeX, float eyeY, float eyeZ, float halfHeight, float yaw, float pitch)
    {
        var eye = new Vector3(eyeX, eyeY, eyeZ);
        var capsule = new CapsuleShapeDesc(halfHeight, 0.3f);
        (FrameHandle space, Entity person) = FloatInSpace(eye, capsule);
        TakeACrate(space, person);
        MoveMouse(yaw / PersonControl.TurnPerPixel, -pitch / PersonControl.TurnPerPixel);
        Ticks(3);

        // A click throws at no speed, so the crate stays where it starts.
        Entity thrown = Throw(0);

        Vector3 at = (world.Snapshot.PoseInOwnFrame(thrown).Position - world.Snapshot.PoseInOwnFrame(person).Position).ToVector3();
        ExpectStartsOnTheViewClearOfTheCapsule(at, eye, LookDirection(yaw, pitch), capsule);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AThrownCrateFromAViewStraightUpOrDownStartsOnTheViewWhereItsBoundingSphereClearsTheCapsuleByTheGap(bool up)
    {
        // PersonControl never lets the view reach straight up or down. A person that has only a Carry can have such a
        // view, and the aim then has no part across the capsule's axis, so it leaves through the top or the bottom. The
        // quaternions turn the view a quarter turn about +X, which takes -Z to +Y or -Y, and then a quarter turn about +Y,
        // which leaves that direction where it is. Their components are exact in a float, so the aim is exact too.
        (FrameHandle space, Entity person) = FloatLooking(up ? new Quaternion(0.5f, 0.5f, -0.5f, 0.5f) : new Quaternion(-0.5f, 0.5f, 0.5f, 0.5f));
        Entity crate = SpawnCrate(space, new Double3(0, 0, -1));
        Ticks(1);
        UseCrate(crate, person);
        Ticks(8);
        With<HeldInView>().Should().ContainSingle("the person took the crate");

        // A click throws at no speed, so the crate stays where it starts.
        Entity thrown = Throw(0);

        world.Errors.Should().BeEmpty();
        Vector3 at = (world.Snapshot.PoseInOwnFrame(thrown).Position - world.Snapshot.PoseInOwnFrame(person).Position).ToVector3();
        ExpectStartsOnTheViewClearOfTheCapsule(at, Eye, up ? Vector3.UnitY : -Vector3.UnitY, Capsule);
    }

    [Fact]
    public void AMouseMoveThatArrivesInTheTickOfTheReleaseTurnsTheThrowAlongTheNewView()
    {
        // Carry runs after PersonControl, which sets the view of the tick. So a move and a release in one tick throw along
        // the view that the move made. A throw along the view of the tick before would go along -Z.
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        ButtonDown();
        Ticks(60);
        MoveMouse(0.6f / PersonControl.TurnPerPixel, -0.3f / PersonControl.TurnPerPixel);
        ButtonUp();
        Ticks(4);

        Entity thrown = With<Crate>().Should().ContainSingle().Which;
        ExpectNear(world.Snapshot.VelocityInOwnFrame(thrown), LookDirection(0.6f, 0.3f) * Carry.MaxThrowSpeed, 1e-3f);
    }

    [Theory]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void OnlyTheLeftButtonCharges_APressOfAnotherButtonChargesNothing(MouseButton other)
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        Post(InputKind.MouseDown, (int)other);
        Ticks(30);
        // The release of the left button throws only a throw that a press of the left button charged.
        ButtonUp();
        Ticks(4);

        With<Crate>().Should().BeEmpty("the press of another button charged nothing");
        With<HeldInView>().Should().ContainSingle("the person still carries the crate");
        world.Snapshot.VelocityInOwnFrame(person).Should().Be(Vector3.Zero);
    }

    [Theory]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void OnlyTheLeftButtonThrows_AReleaseOfAnotherButtonWhileTheLeftIsHeldThrowsNothingAndKeepsTheCharge(MouseButton other)
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        ButtonDown();
        Ticks(15);
        Post(InputKind.MouseUp, (int)other);
        Ticks(15);

        With<Crate>().Should().BeEmpty("the release of another button throws nothing");
        ButtonUp();
        Ticks(4);

        // The charge ran from the press of the left button to its release: 30 ticks of 0.2 m/s.
        Entity thrown = With<Crate>().Should().ContainSingle().Which;
        ExpectNear(world.Snapshot.VelocityInOwnFrame(thrown), new Vector3(0, 0, -6), 1e-3f);
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
    public void AfterAThrowAnotherReleaseThrowsNothingAndTheNextPressChargesFromZero()
    {
        (FrameHandle space, Entity person) = FloatInSpace();
        TakeACrate(space, person);
        Entity first = Throw(60);
        Vector3 afterTheThrow = world.Snapshot.VelocityInOwnFrame(person);

        // A release with no press before it, with the hands free. The charge of the first throw ended with that throw.
        ButtonUp();
        Ticks(4);

        With<Crate>().Should().ContainSingle().Which.Should().Be(first, "the second release threw nothing");
        world.Snapshot.VelocityInOwnFrame(person).Should().Be(afterTheThrow, "a throw that did not happen gives the person no recoil");
        world.Errors.Should().BeEmpty();
        // The first crate flies on at 12 m/s, out of the person's reach, so that the person takes the next one. A press
        // charges the new throw from zero: 15 ticks throw at 3 m/s, relative to the person.
        Ticks(30);
        TakeACrate(space, person);
        Vector3 drift = world.Snapshot.VelocityInOwnFrame(person);
        ButtonDown();
        Ticks(15);
        ButtonUp();
        Ticks(4);
        Entity second = With<Crate>().Should().HaveCount(2).And.Subject.Single(crate => crate != first);
        ExpectNear(world.Snapshot.VelocityInOwnFrame(second), drift + new Vector3(0, 0, -3), 1e-3f);
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
        world.Snapshot.VelocityInOwnFrame(person).Should().Be(Vector3.Zero);
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
        ExpectNear(world.Snapshot.VelocityInOwnFrame(Throw(15)), new Vector3(0, 0, -3), 1e-3f);
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

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("spec").WithMessage("*size and mass*positive and finite*");
    }

    [Fact]
    public void CarryNeedsThePersonsCapsule()
    {
        Action create = () => _ = new Carry(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("body");
    }
}
