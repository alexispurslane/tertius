using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Parts (docs/plans/parts.md): an attached entity pushes the body of its chain's root with the additive intents, a force,
/// a torque, an impulse and an angular impulse. At the sync point the engine carries each part's push to the root's body,
/// at the part's place: a force there gives the body the same force and the torque of its lever about the body's center
/// of mass. The exclusive intents stay with the root. Every test runs the World on the real Jolt backend.
/// <para>
/// A test that compares a part's push with the root's own push runs the same scene twice. In the first run a
/// <see cref="Jet"/> on the part pushes. In the second the part is there without a component, and an <see cref="OwnJet"/>
/// on the root writes what the engine must make of the part's push: it computes the lever from the root's pose in the front
/// snapshot, in doubles, and narrows once. "Bit for bit" means that the poses and velocities of every row, read in each
/// entity's own frame, are equal as doubles after every tick.
/// </para>
/// </summary>
public class PartPushTests
{
    private const float CrateMass = 50;

    /// <summary>A dynamic box of 2 m by 1 m by 4 m and 50 kg that nothing slows down, centered on its origin.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new(1, 0.5f, 2)), Mass: CrateMass, LinearDamping: 0,
        AngularDamping: 0);

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static readonly DQuaternion Tilted = DQuaternion.FromAxisAngle(new Double3(0.3, 1, 0.2).Normalized(), 0.6);

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    private static Pose Offset(double x, double y, double z) =>
        new(new((float)x, (float)y, (float)z), Quaternion.Identity);

    private static Entity Attach(World world, Entity parent, Pose offset, params IComponent[] components) =>
        world.Spawn(new()
            { Frame = world.GetFrame(parent), AttachTo = parent, AttachOffset = offset, Components = components });

    // ---- Components ----

    /// <summary>
    /// Writes a fixed push on every tick from <see cref="From"/> to <see cref="Until"/>: each field that is not zero, in the
    /// axes of the entity's frame. With <see cref="DestroyAt"/>, it destroys its entity in that tick.
    /// </summary>
    private sealed class Jet : IComponent
    {
        public Vector3 Force { get; init; }
        public Vector3 Torque { get; init; }
        public Vector3 Impulse { get; init; }
        public Vector3 AngularImpulse { get; init; }
        public long From { get; init; }
        public long Until { get; init; } = long.MaxValue;
        public long DestroyAt { get; init; } = -1;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick >= From && self.Tick <= Until)
                Write(self.Body, Force, Torque, Impulse, AngularImpulse);
            if (self.Tick == DestroyAt)
                self.Destroy();
        }

        public static void Write(BodyHandle body, Vector3 force, Vector3 torque, Vector3 impulse,
            Vector3 angularImpulse)
        {
            if (force != Vector3.Zero)
                body.AddForce(force);
            if (torque != Vector3.Zero)
                body.AddTorque(torque);
            if (impulse != Vector3.Zero)
                body.AddImpulse(impulse);
            if (angularImpulse != Vector3.Zero)
                body.AddAngularImpulse(angularImpulse);
        }
    }

    /// <summary>
    /// On a chain root: the push that a part at <see cref="PlaceAt"/> (the place in the body, in the root body's axes) gives
    /// the root, plus the root's own force and torque, by the rule of docs/plans/parts.md. The lever is the place less the
    /// center of mass, turned by the root's rotation in the front snapshot. The sum starts from the root's own values, adds
    /// the part's, and narrows once.
    /// </summary>
    private sealed class OwnJet(Vector3 centerOfMass) : IComponent
    {
        public Func<long, Double3> PlaceAt { get; init; } = _ => Double3.Zero;
        public Vector3 Force { get; init; }
        public Vector3 Torque { get; init; }
        public Vector3 Impulse { get; init; }
        public Vector3 AngularImpulse { get; init; }
        public Vector3 OwnForce { get; init; }
        public long From { get; init; }
        public long Until { get; init; } = long.MaxValue;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick < From || self.Tick > Until)
            {
                Jet.Write(self.Body, OwnForce, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                return;
            }

            DQuaternion q = self.PoseIn(self.Frame).Rotation;
            Double3 lever = q.Rotate(PlaceAt(self.Tick) - centerOfMass);
            var force = (OwnForce + (Double3)Force).ToVector3();
            var torque = (Double3.Zero + (Torque + Double3.Cross(lever, Force))).ToVector3();
            var angular = (Double3.Zero + (AngularImpulse + Double3.Cross(lever, Impulse))).ToVector3();
            Jet.Write(self.Body, force, torque, Impulse, angular);
        }
    }

    /// <summary>Writes the attachment offset of its entity in tick <see cref="At"/>.</summary>
    private sealed class OffsetChange(long at, Pose offset) : IComponent
    {
        public int Priority => -1;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == at)
                self.AttachOffset = offset;
        }
    }

    /// <summary>Destroys its entity in tick <paramref name="at"/>.</summary>
    private sealed class DestroyAt(long at) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == at)
                self.Destroy();
        }
    }

    // ---- Comparing two runs ----

    /// <summary>The state of one live entity after a tick, in its own frame, in doubles.</summary>
    private readonly record struct Row(
        long Tick,
        Entity Entity,
        FrameHandle Frame,
        DPose Pose,
        Double3 Velocity,
        Vector3 AngularVelocity);

    /// <summary>A scene: it spawns into a new world, and the flag says whether a part pushes (true) or the root does (false).</summary>
    private delegate void Scene(World world, bool partPushes);

    /// <summary>
    /// Builds <paramref name="scene"/> in a new world and ticks it <paramref name="ticks"/> times, with the rows after every
    /// tick. The world runs on the fault wrapper of the real backend, which only records here, and
    /// <paramref name="inspect"/> reads its records after the last tick.
    /// </summary>
    private static List<Row> Run(Scene scene, bool partPushes, int ticks, Action<WorldOptions>? configure = null,
        Action<FaultInjectingBackend>? inspect = null)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, configure: configure);
        scene(world, partPushes);
        var rows = new List<Row>();
        var live = new List<Entity>();
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            live.Clear();
            world.CopyLiveEntities(live);
            foreach (Entity entity in live)
            {
                FrameHandle frame = world.GetFrame(entity);
                if (frame.IsNull)
                    continue;
                rows.Add(new(tick, entity, frame, world.PoseIn(entity, frame), world.VelocityIn(entity, frame),
                    world.AngularVelocityIn(entity, frame)));
            }
        }

        world.Errors.Should().BeEmpty();
        inspect?.Invoke(faults);
        return rows;
    }

    /// <summary>
    /// Runs <paramref name="scene"/> with the part's push and with the root's own, and checks that the rows are equal bit for
    /// bit. <paramref name="inspect"/> reads the records of the physics steps of the part's run.
    /// </summary>
    private static List<Row> ShouldMatchBitForBit(Scene scene, int ticks, Action<WorldOptions>? configure = null,
        Action<FaultInjectingBackend>? inspect = null)
    {
        List<Row> withPart = Run(scene, partPushes: true, ticks, configure, inspect);
        List<Row> withOwn = Run(scene, partPushes: false, ticks, configure);
        withPart.Count.Should().Be(withOwn.Count);
        int differs = Enumerable.Range(0, withPart.Count).FirstOrDefault(i => withPart[i] != withOwn[i], -1);
        differs.Should().Be(-1, "the runs differ first at {0}:\n{1}\n{2}", differs,
            differs < 0 ? "" : withPart[differs], differs < 0 ? "" : withOwn[differs]);
        return withPart;
    }

    private static Row Last(List<Row> rows, Entity entity) => rows.Last(row => row.Entity == entity);

    /// <summary>
    /// A crate at the origin of a new space, turned by <paramref name="rotation"/>, and an attached entity at
    /// <paramref name="place"/>, which pushes with <paramref name="jet"/> when <paramref name="partPushes"/>. Otherwise the
    /// crate's <see cref="OwnJet"/> pushes the same. Returns the crate.
    /// </summary>
    private static Entity CrateWithPart(World world, bool partPushes, DQuaternion rotation, Double3 place, Jet jet,
        Vector3 ownForce = default,
        BodyDesc? body = null, FrameHandle frame = default, Double3 position = default, Double3 velocity = default)
    {
        FrameHandle into = frame.IsNull ? Space(world) : frame;
        BodyDesc crateBody = body ?? Crate;
        IComponent root = partPushes
            ? new Jet { Force = ownForce }
            : new OwnJet(Vector3.Zero)
            {
                // The part's offset is a float pose, so its place in the body is the place narrowed and widened again.
                PlaceAt = _ => Offset(place.X, place.Y, place.Z).Position, Force = jet.Force, Torque = jet.Torque,
                Impulse = jet.Impulse, AngularImpulse = jet.AngularImpulse,
                OwnForce = ownForce, From = jet.From, Until = jet.Until
            };
        Entity crate = world.Spawn(new()
        {
            Frame = into,
            Pose = new(position, rotation),
            LinearVelocity = velocity,
            Body = crateBody,
            Components = [root]
        });
        Attach(world, crate, Offset(place.X, place.Y, place.Z), partPushes ? [jet] : []);
        return crate;
    }

    // ---- What a part's push does ----

    [Fact]
    public void APartOnTheLineThroughTheCenterOfMassPushesItsRootAsTheRootsOwnForceDoesBitForBit()
    {
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
                crate = CrateWithPart(world, part, DQuaternion.Identity, new(0, 0, -2),
                    new() { Force = new(0, 0, 40) }),
            30);

        Row last = Last(rows, crate);
        last.Velocity.Z.Should().BeGreaterThan(0.3, "40 N on 50 kg for half a second");
        last.AngularVelocity.Should().Be(Vector3.Zero, "a force on a line through the center of mass turns nothing");
    }

    [Fact]
    public void APartOffTheCenterOfMassPushesItsRootAsTheRootsOwnForceAndTheTorqueOfTheLeverDoBitForBit()
    {
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
            crate = CrateWithPart(world, part, Tilted, new(1, 0.5, -2), new() { Force = new(5, 0, 40) }), 30);

        Last(rows, crate).AngularVelocity.Length().Should().BeGreaterThan(0.05f, "the force misses the center of mass");
    }

    [Fact]
    public void TheLeverOfAPartIsMeasuredFromTheCenterOfMassOfAHullShapeAndNotFromTheBodysOrigin()
    {
        World world = NewWorld(out JoltPhysicsBackend backend);
        using (world)
        {
            FrameHandle space = Space(world);
            var offsetBox = new CompoundShapeDesc([
                new(new BoxShapeDesc(new(1, 0.5f, 2)), new(new(1.5f, -1, 2), Quaternion.Identity))
            ]);
            Vector3 centerOfMass = backend.ShapeCenterOfMass(offsetBox);
            centerOfMass.Length().Should().BeGreaterThan(2, "the box sits well off the body's origin");
            Entity crate = world.Spawn(new()
            {
                Frame = space,
                Body = new(offsetBox, Mass: CrateMass, LinearDamping: 0, AngularDamping: 0)
            });
            Attach(world, crate, new(centerOfMass, Quaternion.Identity), new Jet { Force = new(0, 30, 40) });

            Tick(world, 30);

            world.AngularVelocityIn(crate, space).Should()
                .Be(Vector3.Zero, "the part sits at the center of mass, so its lever is zero");
            world.VelocityIn(crate, space).Z.Should().BeGreaterThan(0.3);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void TwoOppositeJetsAtOppositePlacesTurnTheirRootAndLeaveItsCenterOfMassAtRest()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
        Attach(world, crate, Offset(1, 0, 0), new Jet { Force = new(0, 0, 10) });
        Attach(world, crate, Offset(-1, 0, 0), new Jet { Force = new(0, 0, -10) });

        Tick(world, 30);

        world.PoseIn(crate, space).Position.Should().Be(Double3.Zero);
        world.VelocityIn(crate, space).Should().Be(Double3.Zero);
        // Each jet gives a torque of 10 N·m about -Y.
        world.AngularVelocityIn(crate, space).Y.Should().BeLessThan(-0.05f);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APartsPushAddsToTheForceThatTheRootWritesItself()
    {
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
            crate = CrateWithPart(world, part, Tilted, new(0, 0, 1), new() { Force = new(0, 5, 0) },
                ownForce: new(10, 0, 0)), 30);

        Last(rows, crate).Velocity.X.Should().BeGreaterThan(0.08, "the root's own 10 N acts too");
    }

    [Fact]
    public void ATorqueOfAPartTurnsItsRootAsTheRootsOwnTorqueDoes()
    {
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
            crate = CrateWithPart(world, part, Tilted, new(2, 0, 0), new() { Torque = new(0, 3, 0) }), 30);

        Last(rows, crate).AngularVelocity.Length().Should().BeGreaterThan(0.01f);
        Last(rows, crate).Velocity.Should()
            .Be(Double3.Zero, "a torque is a free vector, so the part's place adds no force");
    }

    [Fact]
    public void AnImpulseOfAPartGivesItsRootTheAngularImpulseOfItsLever()
    {
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
            crate = CrateWithPart(world, part, Tilted, new(0, 1, 0), new() { Impulse = new(2, 0, 0), Until = 0 }), 10);

        Last(rows, crate).AngularVelocity.Length().Should()
            .BeGreaterThan(0.01f, "the impulse misses the center of mass");
    }

    [Fact]
    public void AGrandchildPushesTheChainRootAtTheGrandchildsPlace()
    {
        var first = new Pose(new(1, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        Pose second = Offset(0, 0, 2);
        Double3 place = DPose.Compose(first.ToDPose(), second.ToDPose()).Position;
        // The quarter turn takes the grandchild's 2 m along +Z to 2 m along +X.
        (place - new Double3(3, 0, 0)).Length.Should().BeLessThan(1e-6);
        var jet = new Jet { Force = new(0, 10, 0) };
        Entity crate = default;

        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            crate = world.Spawn(new()
            {
                Frame = Space(world),
                Pose = new(Double3.Zero, Tilted),
                Body = Crate,
                Components = part ? [] : [new OwnJet(Vector3.Zero) { PlaceAt = _ => place, Force = jet.Force }]
            });
            Entity child = Attach(world, crate, first);
            Attach(world, child, second, part ? [jet] : []);
        }, 30);

        Last(rows, crate).AngularVelocity.Length().Should().BeGreaterThan(0.05f);
    }

    [Fact]
    public void APartPushesFromTheOffsetThatItWroteInTheSameTick()
    {
        Pose before = Offset(0, 0, -1), after = Offset(0, 0, 3);
        var jet = new Jet { Force = new(0, 10, 0) };
        Entity crate = default;

        ShouldMatchBitForBit((world, part) =>
        {
            crate = world.Spawn(new()
            {
                Frame = Space(world),
                Pose = new(Double3.Zero, Tilted),
                Body = Crate,
                Components = part
                    ? []
                    :
                    [
                        new OwnJet(Vector3.Zero)
                            { PlaceAt = tick => tick < 5 ? new(0, 0, -1) : new Double3(0, 0, 3), Force = jet.Force }
                    ]
            });
            Attach(world, crate, before, part ? [new OffsetChange(5, after), jet] : [new OffsetChange(5, after)]);
        }, 20);
    }

    [Fact]
    public void APartsPushActsInTheStepAfterTheTickThatWritesIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
        Attach(world, crate, Offset(0, 0, -2), new Jet { Impulse = new(0, 0, 5), Until = 0 });

        world.Tick();
        world.VelocityIn(crate, space).Should().Be(Double3.Zero, "the push of tick 0 acts in the step of tick 1");
        world.Tick();

        world.VelocityIn(crate, space).Z.Should().BeApproximately(0.1, 1e-6, "5 N·s on 50 kg");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Static)]
    public void ThePushOfAPartOnARootThatIsNotDynamicMovesNothing(BodyMotion motion)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new()
            { Frame = space, Pose = new(new(1, 2, 3), Tilted), Body = Crate with { Motion = motion } });
        Attach(world, crate, Offset(1, 0, -2), new Jet { Force = new(0, 0, 40), Impulse = new(3, 0, 0) });
        // Physics keeps a rotation in floats, so the crate's first step rounds its rotation once.
        world.Tick();
        DPose first = world.PoseIn(crate, space);

        Tick(world, 20);

        world.PoseIn(crate, space).Should().Be(first);
        (first.Position - new Double3(1, 2, 3)).Length.Should().BeLessThan(1e-6);
        world.VelocityIn(crate, space).Should().Be(Double3.Zero);
        world.AngularVelocityIn(crate, space).Should().Be(Vector3.Zero);
        faults.Intents.Should().Contain(
            record => record.Entity == crate && record.Intent.Force == new Vector3(0, 0, 40),
            "the sum stays in the root's intent, and physics ignores it");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Changes the motion of its hull in tick <paramref name="at"/>.</summary>
    private sealed class MotionAt(long at, BodyMotion motion) : IComponent
    {
        public int Priority => 1;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == at)
                self.Body.SetMotion(motion);
        }
    }

    [Fact]
    public void APartsPushInTheTickThatItsKinematicRootTurnsDynamicMovesTheRootInTheNextStepAsTheRootsOwnForceDoes()
    {
        // Step 4 of the sync point of tick 3 plans the hull's change of motion, and step 5 adds the part's push to the
        // hull's intent after it. The push still acts in step 4, in which the hull is dynamic.
        var jet = new Jet { Force = new(40, 0, 300), From = 3 };
        Entity hull = default;
        FrameHandle space = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            space = Space(world);
            IComponent own = part
                ? new Jet()
                : new OwnJet(Vector3.Zero) { PlaceAt = _ => new(1, 0, -3), Force = jet.Force, From = 3 };
            hull = world.Spawn(new()
            {
                Frame = space,
                Pose = new(Double3.Zero, Tilted),
                Body = new(new BoxShapeDesc(new(2, 1, 3)), BodyMotion.Kinematic, Mass: 1000, LinearDamping: 0,
                    AngularDamping: 0),
                AsFrame = new(FrameBounds.Sphere(10)),
                Components = [own, new MotionAt(3, BodyMotion.Dynamic)]
            });
            Attach(world, hull, Offset(1, 0, -3), part ? [jet] : []);
        }, 8);

        rows.Where(row => row.Entity == hull && row.Tick <= 3).Should().OnlyContain(row => row.Velocity == Double3.Zero,
            "the hull is kinematic until step 4");
        Last(rows, hull).Velocity.Length.Should()
            .BeGreaterThan(0.01, "a push of about 300 N on 1,000 kg moves the hull from step 4 on");
        Last(rows, hull).AngularVelocity.Length().Should().BeGreaterThan(1e-3f, "the part's lever turns the hull");
    }

    [Theory]
    [InlineData("walking")]
    [InlineData("rigid")]
    public void ThePushOfAPartOnACharacterChangesItsVelocityAsTheCharactersOwnForceDoesInBothForms(string form)
    {
        // A character reads no torque, so the root's own run writes the force alone, and the part's lever and torque are lost.
        var force = new Vector3(30, 0, 10);
        Entity person = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            FrameHandle frame = form == "walking" ? Ground(world, 9.81f) : Space(world);
            person = world.Spawn(new()
            {
                Frame = frame,
                Pose = At(0, 20, 0),
                Body = Person,
                Components = part ? [] : [new Jet { Force = force }]
            });
            Attach(world, person, Offset(0.2, 0.5, -0.3),
                part ? [new Jet { Force = force, Torque = new(0, 7, 0) }] : []);
        }, 30);

        Last(rows, person).Velocity.X.Should().BeGreaterThan(0.1, "30 N on 70 kg for half a second");
    }

    // ---- What a part cannot do ----

    /// <summary>Tries every write of <c>Writes</c> in its entity's first tick, and counts the ones that throw
    /// <see cref="InvalidOperationException"/>.</summary>
    private sealed class Attempts(params string[] writes) : IComponent
    {
        public List<string> Refused { get; } = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (Refused.Count > 0 || self.Tick > 0)
                return;
            DPose here = self.PoseIn(self.Frame);
            foreach (string write in writes)
                try
                {
                    switch (write)
                    {
                        case "AddForce": self.Body.AddForce(Vector3.UnitX); break;
                        case "AddTorque": self.Body.AddTorque(Vector3.UnitX); break;
                        case "AddImpulse": self.Body.AddImpulse(Vector3.UnitX); break;
                        case "AddAngularImpulse": self.Body.AddAngularImpulse(Vector3.UnitX); break;
                        case "SetVelocity": self.Body.SetVelocity(Double3.UnitX); break;
                        case "SetAngularVelocity": self.Body.SetAngularVelocity(Vector3.UnitX); break;
                        case "Teleport": self.Body.Teleport(here); break;
                        case "MoveCharacter": self.Body.MoveCharacter(Vector3.UnitX); break;
                        case "Jump": self.Body.Jump(1); break;
                        case "SetCollision":
                            self.Body.SetCollision(CollisionGroups.Of(CollisionGroup.Solid),
                                CollisionMask.Empty.InAny(CollisionGroup.Solid)); break;
                        case "SetMotion": self.Body.SetMotion(BodyMotion.Static); break;
                        case "SteerTo": self.Body.SteerTo(here); break;
                        case "MoveKinematic": self.MoveKinematic(here); break;
                        case "MoveOnRails":
                            self.MoveOnRails(new(here, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero,
                                Double3.Zero)); break;
                        default: throw new ArgumentOutOfRangeException(nameof(writes), write, null);
                    }
                }
                catch (InvalidOperationException)
                {
                    Refused.Add(write);
                }

            if (Refused.Count == 0)
                Refused.Add("none");
        }
    }

    [Fact]
    public void APartWhoseRootHasNoBodyCannotPush()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity marker = world.Spawn(new() { Frame = space });
        string[] pushes = ["AddForce", "AddTorque", "AddImpulse", "AddAngularImpulse"];
        var onPart = new Attempts(pushes);
        var onLoose = new Attempts(pushes);
        Attach(world, marker, Offset(1, 0, 0), onPart);
        world.Spawn(new() { Frame = space, Pose = At(5), Components = [onLoose] });

        world.Tick();

        onPart.Refused.Should().Equal(pushes);
        onLoose.Refused.Should().Equal(pushes, "an entity without a body that is no part cannot push either");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APartCannotSetAVelocityTeleportOrWriteAMotionIntent()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
        string[] exclusive =
        [
            "SetVelocity", "SetAngularVelocity", "Teleport", "MoveCharacter", "Jump", "SetCollision", "SetMotion",
            "SteerTo", "MoveKinematic", "MoveOnRails"
        ];
        var attempts = new Attempts([.. exclusive, "AddForce"]);
        Attach(world, crate, Offset(1, 0, 0), attempts);

        world.Tick();

        attempts.Refused.Should().Equal(exclusive, "only the additive intents flow up, and the part takes them");
        world.Errors.Should().BeEmpty();
    }

    // ---- Deaths ----

    [Fact]
    public void APartThatDestroysItselfGivesNoPushInTheNextStep()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
        Entity part = Attach(world, crate, Offset(0, 0, -2),
            new Jet { Impulse = new(0, 0, 5), From = 3, Until = 3, DestroyAt = 3 });

        Tick(world, 10);

        world.Entities.IsAlive(part).Should().BeFalse();
        world.VelocityIn(crate, space).Should().Be(Double3.Zero);
        faults.Intents.Should().NotContain(record => record.Entity == crate);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APartWhoseParentDiesGivesNoPushInTheNextStep()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
        Entity bracket = Attach(world, crate, Offset(0, 0, -2), new DestroyAt(3));
        Entity part = Attach(world, bracket, Offset(0, 1, 0), new Jet { Impulse = new(0, 0, 5), From = 3, Until = 3 });

        Tick(world, 10);

        world.Entities.IsAlive(part).Should().BeFalse();
        world.VelocityIn(crate, space).Should().Be(Double3.Zero);
        faults.Intents.Should().NotContain(record => record.Entity == crate);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ThePushOfADyingPartIsSkippedBecauseTheDestroyBatchIsKnownFirst()
    {
        // The bracket asks to die, and its part joins the destroy batch only when the batch grows. The other part's push
        // still acts, so the root moves as if only that part had pushed.
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            crate = world.Spawn(new() { Frame = Space(world), Pose = new(Double3.Zero, Tilted), Body = Crate });
            Entity bracket = Attach(world, crate, Offset(1, 0, 0), new DestroyAt(3));
            Attach(world, bracket, Offset(0, 1, 0),
                part ? [new Jet { Impulse = new(0, 0, 5), From = 3, Until = 3 }] : []);
            Attach(world, crate, Offset(0, 1, 0), new Jet { Impulse = new(2, 0, 0), From = 3, Until = 3 });
        }, 10);

        Last(rows, crate).Velocity.X.Should()
            .BeApproximately(0.04, 1e-6, "2 N·s on 50 kg, from the part that survives");
        Last(rows, crate).Velocity.Z.Should().Be(0);
    }

    [Fact]
    public void APartThatTheGameDestroysOutsideATickAfterItsPushFlowedStillPushesInTheNextStep()
    {
        static Double3 VelocityAfter(bool destroy)
        {
            using World world = NewWorld(out _);
            FrameHandle space = Space(world);
            Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
            Entity part = Attach(world, crate, Offset(0, 0, -2),
                new Jet { Impulse = new(0, 0, 5), From = 3, Until = 3 });
            Tick(world, 4);
            if (destroy)
                world.Destroy(part).Should().BeTrue();
            Tick(world, 3);
            world.Errors.Should().BeEmpty();
            return world.VelocityIn(crate, space);
        }

        Double3 destroyed = VelocityAfter(destroy: true);

        destroyed.Should().Be(VelocityAfter(destroy: false));
        destroyed.Z.Should().BeApproximately(0.1, 1e-6);
    }

    // ---- Changes of frame ----

    /// <summary>A frame without a body in <paramref name="space"/> at (100, 0, 0), turned by <see cref="Tilted"/>, with a bubble of 10 m.</summary>
    private static Entity Bay(World world, FrameHandle space, params IComponent[] components) => world.Spawn(new()
    {
        Frame = space,
        Pose = new(new(100, 0, 0), Tilted),
        AsFrame = new(FrameBounds.Sphere(10)),
        Components = components
    });

    [Fact]
    public void APartsPushInTheTickOfItsRootsHandOffConvertsWithTheRootsOwnForce()
    {
        var jet = new Jet { Force = new(0, 3, 0), Impulse = new(0, 0, 0.01f) };
        Entity ball = default;
        FrameHandle bay = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            FrameHandle space = Space(world);
            bay = world.GetOwnedFrame(Bay(world, space));
            Scenes.Approach approach = Scenes.ApproachToBubble(world, bay, tick: 5);
            ball = CrateWithPart(world, part, DQuaternion.Identity, new(0.3, 0, 0), jet, body: Ball(), frame: space,
                position: approach.Pose.Position, velocity: approach.Velocity);
        }, 12);

        rows.First(row => row.Entity == ball && row.Frame == bay).Tick.Should()
            .Be(5, "Jolt hands the ball off in the step of tick 5");
    }

    [Fact]
    public void APartsPushInTheTickThatItsRootsFrameDiesConvertsIntoTheSurvivingAncestor()
    {
        var jet = new Jet { Force = new(0, 3, 1) };
        Entity crate = default;
        FrameHandle space = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            space = Space(world);
            FrameHandle bay = world.GetOwnedFrame(Bay(world, space, new DestroyAt(4)));
            crate = CrateWithPart(world, part, Tilted, new(1, 0, -2), jet, frame: bay, position: new(2, 0, 0));
        }, 10);

        Last(rows, crate).Frame.Should().Be(space, "the crate moved out of the bay when the bay died");
    }

    [Fact]
    public void APartsPushNamesTheFrontFrameOfItsChainAtTheFlow()
    {
        // In one tick, Jolt hands the ball into the bay and the bay dies, so step 2 moves the root into the bay and step 5
        // rehomes it into space. The part's push still names the front frame of the chain at the flow, so it sums with
        // the root's own push in that frame and converts with it.
        var jet = new Jet { Force = new(0, 3, 0), Torque = new(0.1f, 0, 0) };
        Entity ball = default, bayOwner = default;
        FrameHandle space = default, bay = default;
        List<HandOff> handOffsInTick5 = [];
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            space = Space(world);
            bayOwner = Bay(world, space, new DestroyAt(5));
            bay = world.GetOwnedFrame(bayOwner);
            Scenes.Approach approach = Scenes.ApproachToBubble(world, bay, tick: 5);
            ball = CrateWithPart(world, part, DQuaternion.Identity, new(0.3, 0, 0), jet, body: Ball(), frame: space,
                position: approach.Pose.Position, velocity: approach.Velocity);
        }, 10, inspect: faults =>
        {
            int step = faults.Steps.FindIndex(record => record.Tick == 5);
            handOffsInTick5.AddRange(faults.Outputs[step].HandOffs);
        });

        // The premise: the output of the step of tick 5, which the sync point of tick 5 applies, hands the ball into the bay,
        // and the bay dies at that same sync point.
        handOffsInTick5.Should().ContainSingle(
            handOff => handOff.Entity == ball && handOff.From == space && handOff.To == bay,
            "Jolt hands the ball into the bay in the step of tick 5");
        rows.Last(row => row.Entity == bayOwner).Tick.Should().Be(4, "the bay destroys itself in tick 5");
        rows.Where(row => row.Entity == ball).Should().OnlyContain(row => row.Frame == space);
    }

    [Fact]
    public void APartOfAHullInANumericFramePushesItsHullAsTheHullsOwnForceDoes()
    {
        // A part takes its root's physics home, so a part in the intent list would break the assertion of the frameless
        // movers in every Debug sync point after a push. The world runs its debug checks in every tick.
        var jet = new Jet { Force = new(50, 0, 400) };
        Entity hull = default;
        FrameHandle space = default;

        void Scene(World world, bool part)
        {
            space = Space(world);
            hull = world.Spawn(new()
            {
                Frame = space,
                Pose = new(Double3.Zero, Tilted),
                LinearVelocity = new(1500, 0, 0),
                Body = new(new BoxShapeDesc(new(2, 1, 3)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
                AsFrame = new(FrameBounds.Sphere(10)),
                Components = part ? [] : [new OwnJet(Vector3.Zero) { PlaceAt = _ => new(1, 0, -3), Force = jet.Force }]
            });
            Attach(world, hull, Offset(1, 0, -3), part ? [jet] : []);
        }

        ShouldMatchBitForBit(Scene, 30);

        using World world = NewWorld(out _);
        Scene(world, part: true);
        Tick(world, 30);
        world.PhysicsHomeOf(hull).Should().NotBe(space, "a hull at 1,500 m/s falls in a numeric frame");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APartsPushInASpinningStationTakesTheTurnOfTheStepAsTheRootsOwnForceDoes()
    {
        var jet = new Jet { Force = new(0, 2, 3) };
        Entity crate = default;
        List<Row> rows = ShouldMatchBitForBit((world, part) =>
        {
            FrameHandle space = Space(world);
            Entity station = world.Spawn(new SpawnDesc
            {
                Frame = space,
                Body = new(new BoxShapeDesc(new(5, 3, 15)), BodyMotion.Kinematic,
                    InteriorShape: new SphereShapeDesc(1)),
                AsFrame = new(FrameBounds.Sphere(50))
            }.Spinning(world, new(DQuaternion.Identity, 0.5)));
            crate = CrateWithPart(world, part, Tilted, new(1, 0, -2), jet, frame: world.GetOwnedFrame(station),
                position: new(10, 0, 0));
        }, 30);

        Last(rows, crate).AngularVelocity.Length().Should().BeGreaterThan(0.01f);
    }

    // ---- The intents of the step ----

    [Fact]
    public void APartIsNoIntentOfTheStepAndItsRootIsOne()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity quiet = world.Spawn(new() { Frame = space, Body = Crate });
        Entity pushing = world.Spawn(new()
            { Frame = space, Pose = At(10), Body = Crate, Components = [new Jet { Force = new(1, 0, 0) }] });
        Entity quietPart = Attach(world, quiet, Offset(0, 0, -2), new Jet { Force = new(0, 0, 4) });
        Entity pushingPart = Attach(world, pushing, Offset(0, 0, -2), new Jet { Force = new(0, 0, 4) });

        Tick(world, 5);

        faults.Intents.Should().NotContain(record => record.Entity == quietPart || record.Entity == pushingPart);
        foreach (IGrouping<long, IntentRecord> step in faults.Intents.GroupBy(record => record.Tick))
            step.Select(record => record.Entity).Should().Equal([pushing, quiet],
                "the root that wrote nothing joins the list after the writers");
        faults.Intents.Where(record => record.Entity == quiet).Should().OnlyContain(record =>
            record.Intent.Frame == space && record.Intent.Force == new Vector3(0, 0, 4));
        faults.Intents.Where(record => record.Entity == pushing).Should()
            .OnlyContain(record => record.Intent.Force == new Vector3(1, 0, 4));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WithoutABackendAPartsPushIsClearedAndNoRootGetsAnIntent()
    {
        using var world = new World(new() { MaxDegreeOfParallelism = 1, DebugChecks = true });
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new() { Frame = space, Body = Crate });
        Entity part = Attach(world, crate, Offset(0, 0, -2), new Jet { Force = new(0, 0, 4) });

        Tick(world, 3);

        world.Entities.IntentEntities.ToArray().Should().BeEmpty();
        world.Entities.FrontIntents.Span[(int)crate.Index].Should().Be(default(PhysicsIntent));
        world.Entities.FrontIntents.Span[(int)part.Index].Should().Be(default(PhysicsIntent));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheThrustClockCountsThePushOfAPartAsTheRootsOwnForce()
    {
        static List<NumericMode> Modes(bool part)
        {
            using World world = NewWorld(out _);
            FrameHandle space = Space(world);
            var thruster = new Thruster(CrateMass);
            Entity crate = world.Spawn(new()
            {
                Frame = space, LinearVelocity = new(60, 0, 0), Body = Crate, Components = part ? [] : [thruster]
            });
            Attach(world, crate, Offset(-1, 0, 0), part ? [thruster] : []);
            var modes = new List<NumericMode>();
            for (int tick = 0; tick < 60; tick++)
            {
                world.Tick();
                modes.Add(world.Frames.Numeric(world.PhysicsHomeOf(crate)));
            }

            world.Errors.Should().BeEmpty();
            return modes;
        }

        List<NumericMode> withPart = Modes(part: true);

        withPart.Should().Equal(Modes(part: false));
        withPart.IndexOf(NumericMode.Falling).Should()
            .Be(29, "the crate thrusts from the sync point of its thirtieth tick of thrust");
    }

    [Fact]
    public void TheProperAccelerationOfAHullThatItsPartsPushIsTheirThrustOverItsMass()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity ship = world.Spawn(new()
        {
            Frame = space,
            Pose = new(Double3.Zero, Tilted),
            Body = new(new BoxShapeDesc(new(2, 0.5f, 2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(10))
        });
        // Two engines on the line through the center of mass, pushing along the hull's +Z with 300 N and 200 N.
        var forward = Tilted.Rotate(Double3.UnitZ).ToVector3();
        Attach(world, ship, Offset(0, 0, -2), new Jet { Force = forward * 300 });
        Attach(world, ship, Offset(0, 0, 2), new Jet { Force = forward * 200 });

        Tick(world, 5);

        Vector3 felt = world.Frames.ProperAcceleration(world.GetOwnedFrame(ship));
        Vector3.Distance(felt, new(0, 0, 0.5f)).Should().BeLessThan(1e-4f,
            "500 N on 1,000 kg along the hull's +Z, in the frame's axes: {0}", felt);
        world.Errors.Should().BeEmpty();
    }

    // ---- Order and threads ----

    [Fact]
    public void ThePushesOfManyPartsSumToTheSameIntentsOnOneThreadAndOnTheWorkers()
    {
        (List<IntentRecord> Intents, ulong Hash) Run(int workers)
        {
            using World world = NewWorldWithFaults(out FaultInjectingBackend faults, configure: options =>
            {
                options.MaxDegreeOfParallelism = workers;
                options.ChunkSize = 4;
            });
            FrameHandle space = Space(world);
            for (int i = 0; i < 24; i++)
            {
                Entity crate = world.Spawn(new()
                    { Frame = space, Pose = new(new(i * 10, 0, 0), Tilted), Body = Crate });
                for (int j = 0; j < 3; j++)
                {
                    // Forces whose sizes differ, so that a sum in another order would round otherwise.
                    var force = new Vector3(0.1f * (j + 1) + i, 1e3f / (j + 1), 7e-3f * j);
                    Attach(world, crate, Offset(j - 1, 0.3 * j, -1), new Jet { Force = force, Torque = force * 0.01f });
                }
            }

            Tick(world, 20);
            world.Errors.Should().BeEmpty();
            return ([.. faults.Intents], WorldHash.Of(world));
        }

        (List<IntentRecord> one, ulong oneHash) = Run(1);
        (List<IntentRecord> many, ulong manyHash) = Run(4);

        many.Should().Equal(one);
        manyHash.Should().Be(oneHash);
    }

    [Fact]
    public void TheComponentsOfTheRootDoNotSeeThePushesOfItsParts()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        // The spare slot goes to the part, so the part runs before its root in every tick.
        Entity spare = world.Spawn(new() { Frame = space });
        var seen = new List<Vector3>();
        Entity crate = world.Spawn(new()
        {
            Frame = space,
            Body = Crate,
            Components = [new Jet { Force = new(1, 0, 0) }, new PendingReader(seen)]
        });
        world.Destroy(spare);
        Entity part = Attach(world, crate, Offset(0, 0, -2), new Jet { Force = new(0, 0, 4) });
        part.Index.Should().BeLessThan(crate.Index);

        Tick(world, 5);

        seen.Should().HaveCount(5).And.OnlyContain(force => force == new Vector3(1, 0, 0));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Records the force of its entity's pending intent at the end of each run.</summary>
    private sealed class PendingReader(List<Vector3> seen) : IComponent
    {
        public int Priority => 10;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            seen.Add(self.Body.Pending.Force);
    }

    [Fact]
    public void CarryingThePushesAllocatesNothingAfterWarmUp()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle space = Space(world);
        for (int i = 0; i < 100; i++)
        {
            Entity crate = world.Spawn(new() { Frame = space, Pose = At(i * 10), Body = Crate });
            for (int j = 0; j < 3; j++)
                Attach(world, crate, Offset(j - 1, 0, -1),
                    new Jet { Force = new(0, 0.1f, 1), Impulse = new(1e-3f, 0, 0) });
        }

        Tick(world, 60);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Tick(world, 100);
        long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        perTick.Should().Be(0, "a sync point with parts that push allocates nothing");
        world.Errors.Should().BeEmpty();
    }

    // ---- Decisions ----

    /// <summary>Records the rotation of its entity in the front snapshot on every tick.</summary>
    private sealed class RotationReader(List<DQuaternion> seen) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            seen.Add(self.PoseIn(self.Frame).Rotation);
    }

    /// <summary>Pushes along its own -Z with <paramref name="thrust"/> newtons, and records each force that it writes.</summary>
    private sealed class Nozzle(float thrust, List<Vector3> written) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            var force = (self.PoseIn(self.Frame).Rotation.Rotate(-Double3.UnitZ) * thrust).ToVector3();
            self.Body.AddForce(force);
            written.Add(force);
        }
    }

    [Fact]
    public void TheLeverTurnsWithTheRotationThatTheWriterRead()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var rotations = new List<DQuaternion>();
        var forces = new List<Vector3>();
        Entity crate = world.Spawn(new()
        {
            Frame = space, Body = Crate, AngularVelocity = new(0, 3, 0), Components = [new RotationReader(rotations)]
        });
        var place = new Double3(1, 0, 0);
        Attach(world, crate, Offset(place.X, place.Y, place.Z), new Nozzle(20, forces));

        Tick(world, 10);

        List<IntentRecord> applied = [.. faults.Intents.Where(record => record.Entity == crate)];
        applied.Should().HaveCount(9, "the pushes of ticks 0 to 8 act in the steps of ticks 1 to 9");
        for (int tick = 0; tick < applied.Count; tick++)
        {
            Vector3 force = forces[tick];
            applied[tick].Intent.Force.Should().Be(force);
            var read = Double3.Cross(rotations[tick].Rotate(place), force).ToVector3();
            var atStart = Double3.Cross(rotations[tick + 1].Rotate(place), force).ToVector3();
            applied[tick].Intent.Torque.Should()
                .Be(read, "the lever turns by the rotation that the jet read in tick {0}", tick);
            Vector3.Distance(read, atStart).Should()
                .BeGreaterThan(1e-3f, "at 3 rad/s the root turns by 0.05 rad in a step");
        }

        world.Errors.Should().BeEmpty();
    }
}
