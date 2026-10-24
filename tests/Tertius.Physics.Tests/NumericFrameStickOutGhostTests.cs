using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.NumericFrameTesting;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body that sticks out of the bubble of its frame, a semantic child of another frame, has a ghost in the frame's tree
/// parent, the home of the frame's hull, so that the bodies there collide with it. A ghost is a kinematic copy of a body in
/// the physics system of another frame. Numeric frames are an engine detail with no bubbles, so they must not change which
/// bodies meet it: a body of the semantic parent that may touch the ghost meets it, as it does in the semantic parent's own
/// system without numeric frames. No body of the semantic parent can move the ghost, so the merge test counts the ghost's
/// place as infinitely heavy, as it counts an anchor (ticket ter-4psc): a body that may touch it joins the home of the
/// frame's hull, and a frame without a hull holds the ghost fixed in the semantic parent's own system, where the body then
/// stays. So no other numeric frame needs a ghost of the body, and the semantic parent's own system needs none beside the
/// one in the tree parent. <see cref="GhostPlaceTests"/> pins the places themselves. These tests run the World on the real
/// backend, with the default numeric frame options, and with no numeric frames. Under the defaults, a group of free bodies
/// falls in a numeric frame from 1,000 m/s relative to its semantic frame, and a group with a body that thrusts falls from
/// 50 m/s. So the rocks and ships here that fly at 60 m/s and must fall, or would fall alone, thrust gently (see
/// <see cref="Thruster"/>), from 0.5 s before their falling frames matter. A slow group rests in a numeric frame from 2^23 m
/// (8,389 km) from its semantic frame's origin, so the scenes whose bodies would rest alone lie 9,000 km out (see
/// <see cref="FarOut"/>).
/// </summary>
public partial class NumericFrameStickOutGhostTests
{
    /// <summary>
    /// How many ticks <see cref="Build"/> runs a scene before its test starts: the 30 ticks of 1/60 s, 0.5 s, of thrust
    /// after which a body with a <see cref="Thruster"/> thrusts (see <see cref="NumericFrameOptions.ThrustOnset"/>). From the
    /// sync point of the last of them on, a group with that body falls in a numeric frame from 50 m/s.
    /// </summary>
    private const int OnsetTicks = 30;

    /// <summary>
    /// How far along +X from the origin of space the scenes put the bodies that must rest in numeric frames: 9,000 km, past
    /// the 2^23 m (8,389 km) from which a slow group gets a resting frame (see
    /// <see cref="NumericFrameOptions.CreateDistance"/>).
    /// </summary>
    private const double FarOut = 9_000_000;

    // ---- Frames on rails, sensors and hulls ---------------------------------------------------------------------------

    /// <summary>The G·M of the earth, in m³/s².</summary>
    private const double EarthMu = 3.986004418e14;

    /// <summary>A hull of 4 m by 4 m by 4 m and 1000 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc ShipHull = new(
        Shape: new BoxShapeDesc(new(2)),
        Mass: 1000,
        Restitution: 0,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>A crate of 2 m by 2 m by 2 m and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc CrateBody = new(
        Shape: new BoxShapeDesc(new(1)),
        Mass: 2,
        Restitution: 0,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>
    /// A rock: a ball of radius 0.5 m and 1 kg that nothing slows down and that does not bounce. A ghost is kinematic, and
    /// Jolt makes no contact between a kinematic body and a static one, so the rock is dynamic.
    /// </summary>
    private static readonly BodyDesc RockBody = new(
        Shape: new SphereShapeDesc(0.5f),
        Mass: 1,
        Restitution: 0,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.</summary>
    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(
            faults: out faults,
            configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames
        );

    /// <summary>
    /// The scenes of these tests. <see cref="Build"/> runs each for <see cref="OnsetTicks"/> ticks, so the places here are
    /// those at the spawn, and each layout says where its bodies are when the test starts.
    /// </summary>
    public enum Layout
    {
        /// <summary>
        /// Space, and at (1000, 0, 0) in it a parked station: a frame that physics does not drive, with a bubble of radius
        /// 40 m and no hull. The station hangs directly in space, with numeric frames or without, so the ghost of a body that
        /// sticks out of it is fixed in the system of space. A crate floats at (0, 40.5, 0) in the station and sticks 1.5 m
        /// out of its bubble. A rock flies at 60 m/s along -X from (1130, 41, 0) in space, with a gentle thrust, through the
        /// part of the crate that sticks out. When the test starts, the rock thrusts and is at (1100, 41, 0), but it lies
        /// within the merge test's reach of the crate's place, their bounding spheres plus 2 s of their relative speed, about
        /// 122 m. So it stays in the system of space, where the ghost of the crate lives, and falls in no numeric frame while
        /// it may touch that ghost. The rock's center stays at least 1 m outside the station's bubble, so it never touches the
        /// bubble and never meets the real crate.
        /// </summary>
        StationAndFastRock,

        /// <summary>
        /// Space, and at (<see cref="FarOut"/> + 130, 0, 0) in it a ship that flies at 60 m/s along -X: a hull of 4 m by 4 m
        /// by 4 m and 1000 kg that physics drives, with a gentle thrust, and a bubble of radius 200 m. When the test starts,
        /// the hull thrusts and is 100 m from the rock along X, so it falls in a numeric frame of its own, under which the
        /// ship's frame hangs. A crate floats at (0, -200, 0) in the ship, on the edge of its bubble, and so sticks out of
        /// it. A rock rests at (<see cref="FarOut"/>, -200.6, 0) in space, in the path of the part of the crate that sticks
        /// out, 0.1 m outside the ship's bubble. It is slow and 9,000 km from the origin of space, so alone it would rest in
        /// a numeric frame of its own. The merge test reaches the bounding spheres of the hull and the rock plus 2 s of their
        /// relative speed, about 124 m, and the rock stays at least 200 m from the hull, so it never links them directly. But
        /// at the spawn the crate's place is 130 m from the rock, within the merge test's reach of it, about 132 m in one
        /// system with the split margin. So the rock joins the hull's group and falls with it in the hull's falling frame
        /// from the onset, where the ghost of the crate lives.
        /// </summary>
        ShipAndFarRock,

        /// <summary>
        /// Space, and at (1100, 0, 0) in it the ship of <see cref="ShipAndFarRock"/>, which flies at 60 m/s along -X with a
        /// gentle thrust and so falls in a numeric frame of its own when the test starts. The crate at (0, -200, 0) in the
        /// ship drifts out of its bubble along -Y at 2 m/s. A rock flies beside the ship with the ship's velocity and the
        /// same gentle thrust, from (1100, -202.6, 0) in space, 1.1 m below the crate's face. When the test starts, the rock
        /// thrusts, and the crate has drifted 1 m, to 0.1 m above the rock. The hull and the rock move together, so the
        /// merge test reaches only their bounding spheres, about 4 m, and never links them directly. But the rock lies within
        /// its reach of the crate's place from the spawn, so the rock joins the hull's group and falls with it in the hull's
        /// falling frame from the onset, where the ghost of the crate lives.
        /// </summary>
        ShipAndRockBeside,
    }

    /// <summary>
    /// A scene of <see cref="Layout"/>: <see cref="Bubble"/> is the frame that the crate sticks out of and <see cref="Owner"/>
    /// its owner, and <see cref="CrateLog"/> and <see cref="RockLog"/> hear the collisions of the crate and of the rock.
    /// </summary>
    private sealed record Scene(
        Layout Layout,
        FrameHandle Space,
        Entity Owner,
        FrameHandle Bubble,
        Entity Crate,
        Entity Rock,
        CollisionLog CrateLog,
        CollisionLog RockLog
    );

    /// <summary>
    /// Builds the scene of <paramref name="layout"/> and runs it for <see cref="OnsetTicks"/> ticks, so that its bodies with
    /// a thruster thrust when the test starts. The crate floats at <paramref name="crateInBubble"/> in its frame at the
    /// spawn instead, when it is given. The rock is <paramref name="rockOffset"/> farther along +Y from where the layout puts
    /// it.
    /// </summary>
    private static Scene Build(World world, Layout layout, Double3? crateInBubble = null, double rockOffset = 0)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        (DPose ownerPose, Double3 ownerVelocity, BodyDesc? hull, FrameSpec bubble) = layout switch
        {
            Layout.StationAndFastRock => (At(1000), Double3.Zero, null, new(FrameBounds.Sphere(40))),
            Layout.ShipAndFarRock => (At(FarOut + 130), new(-60, 0, 0), ShipHull, new(FrameBounds.Sphere(200))),
            _ => (At(1100), new Double3(-60, 0, 0), ShipHull, new FrameSpec(FrameBounds.Sphere(200))),
        };
        Entity owner = world.Spawn(
            new()
            {
                Frame = space,
                Pose = ownerPose,
                LinearVelocity = ownerVelocity,
                Body = hull,
                AsFrame = bubble,
                Components = hull is null ? [] : [new Thruster(hull.Mass)],
            }
        );
        FrameHandle frame = world.GetOwnedFrame(owner);
        (Double3 cratePlace, Double3 crateVelocity, Double3 rockPlace, Double3 rockVelocity) = layout switch
        {
            Layout.StationAndFastRock => (new(0, 40.5, 0), Double3.Zero, new(1130, 41, 0), new(-60, 0, 0)),
            Layout.ShipAndFarRock => (new(0, -200, 0), Double3.Zero, new(FarOut, -200.6, 0), Double3.Zero),
            _ => (new Double3(0, -200, 0), new Double3(0, -2, 0), new Double3(1100, -202.6, 0), new Double3(-60, 0, 0)),
        };
        var crateLog = new CollisionLog();
        var rockLog = new CollisionLog();
        Entity crate = world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(crateInBubble ?? cratePlace, DQuaternion.Identity),
                LinearVelocity = crateVelocity,
                Body = CrateBody,
                Components = [crateLog],
            }
        );
        Entity rock = world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(rockPlace + new Double3(0, rockOffset, 0), DQuaternion.Identity),
                LinearVelocity = rockVelocity,
                Body = RockBody,
                Components = layout == Layout.ShipAndFarRock ? [rockLog] : [rockLog, new Thruster(RockBody.Mass)],
            }
        );
        Tick(world, OnsetTicks);
        return new(
            Layout: layout,
            Space: space,
            Owner: owner,
            Bubble: frame,
            Crate: crate,
            Rock: rock,
            CrateLog: crateLog,
            RockLog: rockLog
        );
    }

    /// <summary>
    /// Checks the rock's home, and with numeric frames that the ship's hull falls in a numeric frame of its own. Without
    /// numeric frames, the rock is in the system of space. With them, a rock that <paramref name="mayTouchGhost"/>, the ghost
    /// of the crate that sticks out of the bubble, lives in the home of that ghost, the bubble's tree parent: the system of
    /// space for the station, which has no hull, and the falling frame of the ship's hull. Any other rock is in a numeric
    /// frame of space of its own, apart from the ship's hull.
    /// </summary>
    private static void RockHomeShouldBe(World world, Scene scene, bool numericFrames, bool mayTouchGhost)
    {
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        if (!numericFrames)
        {
            home.Should().Be(scene.Space);
            return;
        }

        if (scene.Layout != Layout.StationAndFastRock)
            world
                .Frames.Numeric(world.PhysicsHomeOf(scene.Owner))
                .Should()
                .Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");
        if (mayTouchGhost)
        {
            home.Should()
                .Be(
                    world.Frames.TreeParent(scene.Bubble),
                    "the rock may touch the ghost of the crate, so it lives in the ghost's home"
                );
            home.Should()
                .Be(
                    scene.Layout == Layout.StationAndFastRock ? scene.Space : world.PhysicsHomeOf(scene.Owner),
                    "the station has no hull, so it holds the ghost fixed in the system of space, and the ship's hull lives in its falling frame"
                );
            return;
        }

        world
            .Frames.Numeric(home)
            .Should()
            .Be(
                scene.Layout == Layout.ShipAndFarRock ? NumericMode.Resting : NumericMode.Falling,
                "the rock thrusts at 60 m/s, or rests 9,000 km out"
            );
        world.Frames.SemanticOf(home).Should().Be(scene.Space);
        world.PhysicsHomeOf(scene.Owner).Should().NotBe(home, "the merge test does not link the rock to the hull");
        world.Frames.TreeParent(scene.Bubble).Should().NotBe(home);
    }

    /// <summary>
    /// The homes of <c>body</c> after the last two ticks, each with the home that the scene expects for it,
    /// which <c>expectedHome</c> gives, and its mode. A contact in the step of a tick becomes a Collision event
    /// at that tick's sync point, and a component hears it in the next tick. That step ran in the home that the sync point
    /// of the tick before it gave the body. So when a component first hears a contact at the end of a tick, before
    /// <see cref="Push"/> records that tick, <see cref="OfContactStep"/> holds the home in which the contact happened.
    /// </summary>
    private sealed class ContactHome
    {
        private readonly World world;
        private readonly Entity body;
        private readonly Func<FrameHandle> expectedHome;
        private (FrameHandle Home, FrameHandle Expected, NumericMode Mode) last;

        public ContactHome(World world, Entity body, Func<FrameHandle> expectedHome)
        {
            this.world = world;
            this.body = body;
            this.expectedHome = expectedHome;
            last = Now();
            OfContactStep = last;
        }

        /// <summary>The body's home, the expected home and the home's mode after the tick before the last one, or at the start.</summary>
        public (FrameHandle Home, FrameHandle Expected, NumericMode Mode) OfContactStep { get; private set; }

        /// <summary>True when the body was in the expected home in the step of the contact.</summary>
        public bool InExpectedHome => OfContactStep.Home == OfContactStep.Expected;

        /// <summary>Records the homes after a tick.</summary>
        public void Push()
        {
            OfContactStep = last;
            last = Now();
        }

        private (FrameHandle Home, FrameHandle Expected, NumericMode Mode) Now()
        {
            FrameHandle home = world.PhysicsHomeOf(body);
            return (home, expectedHome(), world.Frames.Numeric(home));
        }
    }

    /// <summary>
    /// The outcome of a run: the tick of the rock's first Collision with the crate, whether the rock was in the home of the
    /// crate's ghost, the bubble's tree parent, in the step of the contact, the mode of the rock's home in that step, and the
    /// rock's state relative to space at the end.
    /// </summary>
    private sealed record Outcome(
        long HitTick,
        bool HitInGhostHome,
        NumericMode HitMode,
        Double3 Position,
        Double3 Velocity,
        List<ContactNews> RockNews,
        List<ContactNews> CrateNews
    );

    /// <summary>
    /// Builds the scene of <paramref name="layout"/>, runs it for <paramref name="ticks"/> more ticks and returns the
    /// outcome. The world checks its invariants at the end of every tick.
    /// </summary>
    private static Outcome Run(Layout layout, bool numericFrames, int ticks)
    {
        using World world = NewWorld(numericFrames, out _);
        Scene scene = Build(world: world, layout: layout);
        RockHomeShouldBe(world: world, scene: scene, numericFrames: numericFrames, mayTouchGhost: true);

        long hitTick = -1;
        bool hitInGhostHome = false;
        NumericMode hitMode = default;
        var homes = new ContactHome(world, scene.Rock, () => world.Frames.TreeParent(scene.Bubble));
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            if (hitTick < 0 && scene.RockLog.CountWith(scene.Crate) > 0)
            {
                hitTick = world.TickNumber;
                hitInGhostHome = homes.InExpectedHome;
                hitMode = homes.OfContactStep.Mode;
            }

            homes.Push();
        }

        world.Errors.Should().BeEmpty();
        world.GetFrame(scene.Rock).Should().Be(scene.Space, "the rock never touched the bubble");
        world.GetFrame(scene.Crate).Should().Be(scene.Bubble, "the crate never left the bubble");
        return new(
            HitTick: hitTick,
            HitInGhostHome: hitInGhostHome,
            HitMode: hitMode,
            Position: world.PoseIn(scene.Rock, scene.Space).Position,
            Velocity: world.VelocityIn(scene.Rock, scene.Space),
            RockNews: scene.RockLog.NewsOf(scene.Crate),
            CrateNews: scene.CrateLog.NewsOf(scene.Rock)
        );
    }

    /// <summary>
    /// The rock thrusts at 60 m/s, so alone it would fall in a numeric frame of its own. The station has no hull, so the
    /// place of the crate's ghost is fixed in the system of space, and the rock, which may touch it, stays there.
    /// </summary>
    [Fact]
    public void AFastRockStaysInTheSystemOfSpaceAndHitsACrateThatSticksOutOfAParkedStationWithoutAHullAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.StationAndFastRock, numericFrames: true, ticks: 150);
        Outcome withoutNumericFrames = Run(Layout.StationAndFastRock, numericFrames: false, ticks: 150);

        // The front of the rock reaches the crate's face at x = 1001 in space after 128.5 m from its spawn, or 2.14 s.
        withoutNumericFrames
            .HitTick.Should()
            .BeInRange(127, 133, "the rock hits the ghost of the crate in space after about 2.14 s");
        withoutNumericFrames.Velocity.Length.Should().BeLessThan(5, "the ghost of the crate stopped the rock");
        withoutNumericFrames.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
        withNumericFrames
            .HitInGhostHome.Should()
            .BeTrue(
                "the rock may touch the ghost of the crate, which is fixed in the system of space, so it stays there"
            );
        withNumericFrames.HitMode.Should().Be(NumericMode.None);
        withNumericFrames
            .HitTick.Should()
            .BeInRange(
                withoutNumericFrames.HitTick - 1,
                withoutNumericFrames.HitTick + 1,
                "the rock meets the ghost of the crate in the system of space, as without numeric frames"
            );
        withNumericFrames.Velocity.Length.Should().BeLessThan(5, "the ghost stopped the rock");
        withNumericFrames.Position.X.Should().BeGreaterThan(1001, "the rock did not pass through the crate");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.CrateNews.Should().Equal(withoutNumericFrames.CrateNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The rock rests 9,000 km out, so alone it would rest in a numeric frame of its own. It may touch the ghost of the crate,
    /// which lives in the home of the ship's hull, so it joins the hull's group and falls with it in the hull's falling frame.
    /// Without numeric frames, the rock rests in the system of space and the kinematic ghost of the crate runs into it at
    /// 60 m/s, which Jolt does not sweep. In the falling frame the ghost holds nearly still and the rock flies into it at
    /// 60 m/s, and Jolt sweeps every dynamic body along its path with a linear cast. So gameplay may hear the contact one
    /// tick earlier there (a measured run: tick 130 against tick 131), and the rock's place at the tick of the contact
    /// differs. From then on the ghost pushes the rock in both runs, so the test compares where the rock ends up.
    /// </summary>
    [Fact]
    public void ACrateThatSticksOutOfAFallingShipPushesARockThatRestsFarOutInTheShipsFallingFrameAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.ShipAndFarRock, numericFrames: true, ticks: 130);
        Outcome withoutNumericFrames = Run(Layout.ShipAndFarRock, numericFrames: false, ticks: 130);

        // The front of the crate reaches the back of the rock, at x = FarOut + 0.5 in space, after 128.5 m from its spawn, or 2.14 s.
        withoutNumericFrames
            .HitTick.Should()
            .BeInRange(127, 133, "the ghost of the crate in space reaches the rock after about 2.14 s");
        withoutNumericFrames.Velocity.X.Should().BeLessThan(-10, "the ghost pushed the rock along -X");
        withNumericFrames
            .HitInGhostHome.Should()
            .BeTrue(
                "the rock may touch the ghost of the crate, so it joined the falling frame of the ship's hull, where the ghost lives"
            );
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling);
        withNumericFrames
            .HitTick.Should()
            .BeInRange(
                withoutNumericFrames.HitTick - 1,
                withoutNumericFrames.HitTick,
                "in the falling frame Jolt sweeps the rock into the ghost, so it may find the contact one step earlier"
            );
        (withNumericFrames.Position - withoutNumericFrames.Position)
            .Length.Should()
            .BeLessThan(1e-2, "the ghost pushed the rock to the same place");
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity)
            .Length.Should()
            .BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The rock flies beside the ship, so it may touch the ghost of the crate from the spawn and falls with the ship's hull
    /// in the hull's falling frame. Without numeric frames, the rock flies at 60 m/s in the system of space, so Jolt sweeps
    /// it along its path with a linear cast. At its first contact with the ghost of the crate, which slides beside it at the
    /// same speed, the cast stops it for most of a step, about 1.1 m along X. In the falling frame the rock is slow and has
    /// no cast. So the test compares the place across the path of the ship, not along it.
    /// </summary>
    [Fact]
    public void ACrateThatDriftsOutOfAFallingShipPushesARockThatFliesBesideItInTheShipsFallingFrameAsItDoesWithoutNumericFrames()
    {
        Outcome withNumericFrames = Run(Layout.ShipAndRockBeside, numericFrames: true, ticks: 30);
        Outcome withoutNumericFrames = Run(Layout.ShipAndRockBeside, numericFrames: false, ticks: 30);

        // The crate's face closes the gap of 1.1 m from the spawn at 2 m/s in 0.55 s, or 33 ticks: 3 ticks after the start.
        withoutNumericFrames
            .HitTick.Should()
            .BeInRange(32, 37, "the ghost of the crate in space reaches the rock after about 0.55 s");
        withoutNumericFrames
            .Velocity.Y.Should()
            .BeInRange(-2.1, -1.9, "the ghost pushes the rock out along -Y at the crate's speed");
        withNumericFrames
            .HitInGhostHome.Should()
            .BeTrue(
                "the rock may touch the ghost of the crate, so it lives in the falling frame of the ship's hull, where the ghost lives"
            );
        withNumericFrames.HitMode.Should().Be(NumericMode.Falling);
        withNumericFrames
            .HitTick.Should()
            .Be(
                withoutNumericFrames.HitTick,
                "the ghost of the crate meets the rock in the ship's falling frame as it does in space"
            );
        withNumericFrames
            .Position.Y.Should()
            .BeApproximately(withoutNumericFrames.Position.Y, 0.05, "the ghost pushed the rock out as far");
        withNumericFrames.Position.Z.Should().BeApproximately(withoutNumericFrames.Position.Z, 1e-6);
        (withNumericFrames.Velocity - withoutNumericFrames.Velocity)
            .Length.Should()
            .BeLessThan(1e-2, "the ghost moves at the same velocity");
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>A frame with no body, in <paramref name="parent"/>, at <paramref name="pose"/>, with a bubble of
    /// <paramref name="radius"/> m.</summary>
    private static FrameHandle EmptyFrame(World world, FrameHandle parent, DPose pose, float radius) =>
        world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = parent,
                    Pose = pose,
                    AsFrame = new(FrameBounds.Sphere(radius)),
                }
            )
        );

    /// <summary>
    /// The reconcile at the start of a step drops each ghost that the frame tree does not allow. The rock may touch the
    /// ghost of the crate, so it lives in the ghost's home, the bubble's tree parent, which the tree allows: the system of
    /// space for the station, and the falling frame of the ship's hull, a numeric frame. The test runs the reconcile alone,
    /// by applying an empty change batch after a change of the tree far away, and reads the backend before the next step.
    /// </summary>
    [Theory]
    [InlineData(Layout.StationAndFastRock)]
    [InlineData(Layout.ShipAndFarRock)]
    [InlineData(Layout.ShipAndRockBeside)]
    public void TheGhostOfACrateThatSticksOutInTheHomeOfARockThatMayTouchItSurvivesAReconcile(Layout layout)
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        Scene scene = Build(world: world, layout: layout);
        FrameHandle buoy = EmptyFrame(world: world, parent: scene.Space, pose: At(-50_000), radius: 5);
        FrameHandle depot = EmptyFrame(world: world, parent: scene.Space, pose: At(50_000), radius: 50);
        Tick(world, 2);
        RockHomeShouldBe(world: world, scene: scene, numericFrames: true, mayTouchGhost: true);
        FrameHandle home = world.PhysicsHomeOf(scene.Rock);
        faults
            .Jolt.HasGhostIn(scene.Crate, home)
            .Should()
            .BeTrue("the crate sticks out of the bubble into its tree parent");
        faults.Jolt.GhostCount(scene.Crate).Should().Be(1, "no other system holds a ghost of the crate");

        // A change far away makes the backend reconcile. The bubble is still a semantic child of space.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        faults.Jolt.ApplyChanges(new(), world.Frames);

        faults
            .Jolt.HasGhostIn(scene.Crate, home)
            .Should()
            .BeTrue("the tree allows a ghost of the crate in the bubble's tree parent, also when it is numeric");
        faults.Jolt.GhostCount(scene.Crate).Should().Be(1);
        world.Tick();
        faults.Jolt.HasGhostIn(scene.Crate, world.PhysicsHomeOf(scene.Rock)).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// No system gets a second ghost of a body, or a ghost beside its real body, while the rock lives in the home of the
    /// crate's ghost, the bubble's tree parent: with numeric frames, the system of space for the station, which has no hull,
    /// and the falling frame of the ship's hull, which also holds the hull's real body; without them, the system of space.
    /// </summary>
    [Theory]
    [InlineData(Layout.StationAndFastRock, true)]
    [InlineData(Layout.StationAndFastRock, false)]
    [InlineData(Layout.ShipAndFarRock, true)]
    [InlineData(Layout.ShipAndFarRock, false)]
    [InlineData(Layout.ShipAndRockBeside, true)]
    [InlineData(Layout.ShipAndRockBeside, false)]
    public void NoSystemHoldsTwoBodiesOfOneEntityWhileACrateThatSticksOutMeetsARock(Layout layout, bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Scene scene = Build(world: world, layout: layout);
        RockHomeShouldBe(world: world, scene: scene, numericFrames: numericFrames, mayTouchGhost: true);
        Entity[] entities = [scene.Owner, scene.Crate, scene.Rock];
        var systems = new List<FrameHandle>();
        bool sawGhostBesideRock = false;
        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            FrameHandle rockHome = world.PhysicsHomeOf(scene.Rock);
            sawGhostBesideRock |=
                rockHome == world.Frames.TreeParent(scene.Bubble) && faults.Jolt.HasGhostIn(scene.Crate, rockHome);
            systems.Clear();
            world.Frames.GetAllInTree(systems);
            foreach (FrameHandle frame in systems)
            {
                List<JoltPhysicsBackend.BodyFacts> bodies = faults.Jolt.BodiesIn(frame);
                foreach (Entity entity in entities)
                    bodies
                        .Count(facts => BodyUserData.Names(facts.UserData, entity))
                        .Should()
                        .BeLessThanOrEqualTo(
                            1,
                            $"after tick {tick} the system of {frame} holds the real body of {entity} or one ghost of it, or neither"
                        );
            }
        }

        sawGhostBesideRock
            .Should()
            .BeTrue("the rock lived in the bubble's tree parent, which held a ghost of the crate");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A crate deep inside its frame's bubble, 20 m inside the edge of the station's or 100 m inside the edge of the ship's,
    /// touches no bubble boundary, so it has no ghost, and the merge test has no place of a ghost of it. So the rock near
    /// the bubble keeps a numeric frame of its own, apart from the ship's hull.
    /// </summary>
    [Theory]
    [InlineData(Layout.StationAndFastRock, true)]
    [InlineData(Layout.StationAndFastRock, false)]
    [InlineData(Layout.ShipAndFarRock, true)]
    [InlineData(Layout.ShipAndFarRock, false)]
    [InlineData(Layout.ShipAndRockBeside, true)]
    [InlineData(Layout.ShipAndRockBeside, false)]
    public void ACrateFarFromEveryBubbleBoundaryHasNoGhostInTheNumericFrameOfARockNearItsBubble(
        Layout layout,
        bool numericFrames
    )
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Double3 deepInside = layout == Layout.StationAndFastRock ? new(0, 20, 0) : new Double3(0, -100, 0);
        Scene scene = Build(world: world, layout: layout, crateInBubble: deepInside);
        world.Tick();
        RockHomeShouldBe(world: world, scene: scene, numericFrames: numericFrames, mayTouchGhost: false);

        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            faults
                .Jolt.GhostCount(scene.Crate)
                .Should()
                .Be(0, $"after tick {tick} the crate is far inside its frame's bubble");
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A rock 1 km below the path of the ship of <see cref="Layout.ShipAndFarRock"/> rests in a numeric frame of its own,
    /// beyond the reach of the place of the crate's ghost, so nothing links it to the ship's hull. The crate has its one
    /// ghost in the ship's tree parent, the hull's falling frame, and none in the rock's frame or in the system of space,
    /// the ship's semantic parent, which no body that may touch the ghost uses. Without numeric frames, that tree parent is
    /// space.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatSticksOutOfAShipHasNoGhostInTheNumericFrameOfARockFarFromTheShip(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        Scene scene = Build(world: world, layout: Layout.ShipAndFarRock, rockOffset: -800);
        world.Tick();
        RockHomeShouldBe(world: world, scene: scene, numericFrames: numericFrames, mayTouchGhost: false);

        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            faults
                .Jolt.GhostCount(scene.Crate)
                .Should()
                .Be(1, $"after tick {tick} the crate has its one ghost in the ship's tree parent");
            faults.Jolt.HasGhostIn(scene.Crate, world.Frames.TreeParent(scene.Bubble)).Should().BeTrue();
            faults
                .Jolt.HasGhostIn(scene.Crate, scene.Space)
                .Should()
                .Be(!numericFrames, "with numeric frames, the ship's tree parent is the hull's falling frame");
            if (numericFrames)
                faults
                    .Jolt.HasGhostIn(scene.Crate, world.PhysicsHomeOf(scene.Rock))
                    .Should()
                    .BeFalse("the rock is 800 m beyond the reach of the crate's ghost");
        }

        world.VelocityIn(scene.Rock, scene.Space).Length.Should().BeLessThan(1e-6, "nothing touched the rock");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The outcome of a run of a station on an orbit: the tick of the rock's first Collision with the crate, whether the rock
    /// was in the home of the station's hull in the step of the contact, the mode of the rock's home in that step, and the
    /// rock's place in the station at the end.
    /// </summary>
    private sealed record OrbitOutcome(
        long HitTick,
        bool HitInHullHome,
        NumericMode HitMode,
        Double3 RockInStation,
        List<ContactNews> RockNews
    );

    /// <summary>
    /// A planet whose point gravity has the G·M <paramref name="mu"/>, and a station on a circular orbit 7,000 km from its
    /// center, which its Kepler orbit moves: a kinematic hull of 4 m by 4 m by 4 m and a bubble of radius 40 m, so it hangs directly
    /// in the planet. A crate floats at (0, 40.5, 0) in the station and sticks 1.5 m out of its bubble. A rock co-orbits
    /// beside the station, from (3, 41, 0) in the station's place, and drifts toward the crate at 1 m/s relative to the
    /// station. The rock never touches the bubble, so only the ghost of the crate can stop it. The ghost lives in the
    /// station's tree parent, the home of its hull, and the rock lies within the merge test's reach of the ghost's place
    /// from the spawn: their bounding spheres, 2.2 m, plus 2 s at 1 m/s. So with numeric frames the rock joins the group of
    /// the station's hull and meets the ghost in the hull's home. At the earth's G·M the station orbits at 7,546 m/s, so in
    /// one tick of 1/60 s it moves 126 m, far more than that reach. When <paramref name="rockThrusts"/> is true, the rock
    /// thrusts gently (see <see cref="Thruster"/>), so that alone it would fall in a numeric frame from 50 m/s after the
    /// first <see cref="OnsetTicks"/> ticks, 1 m before the crate.
    /// </summary>
    private static OrbitOutcome RunStationOnAnOrbit(bool numericFrames, double mu, bool rockThrusts = false)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(mu)));
        Entity owner = world.Spawn(
            new SpawnDesc
            {
                Frame = planet,
                Body = ShipHull with { Motion = BodyMotion.Kinematic },
                AsFrame = new(FrameBounds.Sphere(40)),
            }.OnKeplerOrbit(world, KeplerOrbit.Circular(7_000_000))
        );
        FrameHandle station = world.GetOwnedFrame(owner);
        // The station holds still for its first step, so the planet's gravity pulls on its contents then, and stands on its
        // rails from the second sync point on. The crate and the rock come once it falls freely on its orbit, as they did
        // before the station could have a first motion intent: the hull's new following frame starts with the hull's instant
        // velocity, and its first step plan lands it on the orbit with an acceleration that is not the orbit's (docs/plans/
        // rails-components.md, "The step plan of a following frame").
        Tick(world, 2);
        long start = world.TickNumber;
        Double3 stationPlace = world.Frames.LocalPose(station).Position;
        Double3 stationVelocity = world.Frames.LinearVelocity(station);
        Entity crate = world.Spawn(
            new()
            {
                Frame = station,
                Pose = At(0, 40.5),
                Body = CrateBody,
            }
        );
        var rockLog = new CollisionLog();
        Entity rock = world.Spawn(
            new()
            {
                Frame = planet,
                Pose = new(stationPlace + new Double3(3, 41, 0), DQuaternion.Identity),
                LinearVelocity = stationVelocity + new Double3(-1, 0, 0),
                Body = RockBody,
                Components = rockThrusts ? [rockLog, new Thruster(RockBody.Mass)] : [rockLog],
            }
        );

        long hitTick = -1;
        bool hitInHullHome = false;
        NumericMode hitMode = default;
        var homes = new ContactHome(world, rock, () => world.PhysicsHomeOf(owner));
        for (int tick = 0; tick < 150; tick++)
        {
            world.Tick();
            if (hitTick < 0 && rockLog.CountWith(crate) > 0)
            {
                hitTick = world.TickNumber - start;
                hitInHullHome = homes.InExpectedHome;
                hitMode = homes.OfContactStep.Mode;
            }

            homes.Push();
        }

        world.Errors.Should().BeEmpty();
        world.GetFrame(rock).Should().Be(planet, "the rock never touched the bubble");
        return new(
            HitTick: hitTick,
            HitInHullHome: hitInHullHome,
            HitMode: hitMode,
            RockInStation: world.PoseIn(rock, station).Position,
            RockNews: rockLog.NewsOf(crate)
        );
    }

    /// <summary>
    /// The station orbits at 7,546 m/s, so its hull lives in a following frame of its own, and the rock, which may touch the
    /// ghost of the crate there, joins it.
    /// </summary>
    [Fact]
    public void ARockThatCoOrbitsWithAStationOnRailsHitsACrateThatSticksOutOfItAsItDoesWithoutNumericFrames()
    {
        OrbitOutcome withNumericFrames = RunStationOnAnOrbit(numericFrames: true, EarthMu);
        OrbitOutcome withoutNumericFrames = RunStationOnAnOrbit(numericFrames: false, EarthMu);

        // The rock's face closes the gap of 1.5 m to the crate's face at 1 m/s in 1.5 s, or 90 ticks.
        withoutNumericFrames
            .HitTick.Should()
            .BeInRange(87, 94, "the ghost of the crate in the planet's system stops the rock");
        withoutNumericFrames.RockInStation.X.Should().BeGreaterThan(1.4, "the rock did not pass through the crate");
        withNumericFrames
            .HitInHullHome.Should()
            .BeTrue(
                "the rock may touch the ghost of the crate, so it joined the following frame of the station's hull, where the ghost lives"
            );
        withNumericFrames.HitMode.Should().Be(NumericMode.Following);
        withNumericFrames
            .HitTick.Should()
            .BeInRange(
                withoutNumericFrames.HitTick - 1,
                withoutNumericFrames.HitTick + 1,
                "the rock meets the ghost of the crate in the hull's following frame as it does in the planet's system"
            );
        withNumericFrames.RockInStation.X.Should().BeApproximately(withoutNumericFrames.RockInStation.X, 0.05);
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
        withNumericFrames.RockNews.Should().StartWith(ContactNews.Started);
    }

    /// <summary>
    /// The same scene with a G·M at which the station orbits at 60 m/s, so it moves only 1 m in a tick. It shows that the
    /// scene works when the place of the bubble hardly changes over a tick. A free rock at 60 m/s falls in no numeric
    /// frame, so the rock thrusts gently, with numeric frames and without, and alone it would fall in a numeric frame of
    /// its own. The station's hull is a slow Kepler hull, an anchor at rest in the planet's own system, so the rock, which
    /// may touch the ghost of the crate there, joins the hull's group and stays in that system.
    /// </summary>
    [Fact]
    public void ARockThatCoOrbitsWithASlowStationOnRailsHitsACrateThatSticksOutOfItAsItDoesWithoutNumericFrames()
    {
        const double slowMu = 2.52e10;
        OrbitOutcome withNumericFrames = RunStationOnAnOrbit(numericFrames: true, slowMu, rockThrusts: true);
        OrbitOutcome withoutNumericFrames = RunStationOnAnOrbit(numericFrames: false, slowMu, rockThrusts: true);

        withoutNumericFrames.HitTick.Should().BeInRange(87, 94);
        withNumericFrames
            .HitInHullHome.Should()
            .BeTrue(
                "the rock may touch the ghost of the crate, so it joined the home of the station's hull, the planet's own system"
            );
        withNumericFrames.HitMode.Should().Be(NumericMode.None);
        withNumericFrames
            .HitTick.Should()
            .BeInRange(withoutNumericFrames.HitTick - 1, withoutNumericFrames.HitTick + 1);
        withNumericFrames.RockInStation.X.Should().BeApproximately(withoutNumericFrames.RockInStation.X, 0.05);
        withNumericFrames.RockNews.Should().Equal(withoutNumericFrames.RockNews);
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks and returns, for the first contact of <paramref name="body"/> with
    /// <paramref name="other"/>, which <paramref name="log"/> hears, whether the body was in the home that
    /// <paramref name="expectedHome"/> gives in the step of the contact, and the mode of its home then (see
    /// <see cref="ContactHome"/>): false and None when the body never hears the other.
    /// </summary>
    private static (bool InExpectedHome, NumericMode Mode) WatchFirstContact(
        World world,
        Entity body,
        CollisionLog log,
        Entity other,
        Func<FrameHandle> expectedHome,
        int ticks
    )
    {
        var homes = new ContactHome(world, body, expectedHome);
        (bool InExpectedHome, NumericMode Mode)? contact = null;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            if (contact is null && log.CountWith(other) > 0)
                contact = (homes.InExpectedHome, homes.OfContactStep.Mode);
            homes.Push();
        }

        return contact ?? (false, NumericMode.None);
    }

    /// <summary>
    /// A parked station <see cref="FarOut"/>, 9,000 km, out in space, a static hull with a bubble of radius 40 m. A crate at
    /// (0, 39, 0) in it drifts out along +Y at 1 m/s, so it sticks out of the bubble. A static sensor ball of radius 1 m
    /// stands in space, 3.5 m outside the bubble and 43.5 m from the hull, beyond the merge test's reach of the hull. A
    /// static sensor meets a kinematic body, such as the ghost of the crate, so without numeric frames it detects the crate
    /// in the system of space. 9,000 km out the sensor alone would rest in a numeric frame of its own. But it may touch the
    /// ghost of the crate, which lives in the home of the station's static hull, so the merge test links it to the hull, and
    /// it stays in the system of space. The outcome says whether the sensor was in the home of the station's hull in the
    /// step of its first contact with the crate, and gives the mode of its home then and its home at the end.
    /// </summary>
    private static (
        List<ContactNews> SensorNews,
        bool SensorInHullHome,
        NumericMode SensorMode,
        FrameHandle SensorHome,
        FrameHandle Space
    ) RunStaticSensorBesideAStation(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity station = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(FarOut),
                Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Static),
                AsFrame = new(FrameBounds.Sphere(40)),
            }
        );
        FrameHandle bubble = world.GetOwnedFrame(station);
        Entity crate = world.Spawn(
            new()
            {
                Frame = bubble,
                Pose = At(0, 39),
                LinearVelocity = new(0, 1, 0),
                Body = CrateBody,
            }
        );
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(FarOut, 43.5),
                Body = new(Shape: new SphereShapeDesc(1f), Motion: BodyMotion.Static) { IsSensor = true },
                Components = [sensorLog],
            }
        );
        (bool InExpectedHome, NumericMode Mode) contact = WatchFirstContact(
            world: world,
            body: sensor,
            log: sensorLog,
            other: crate,
            expectedHome: () => world.PhysicsHomeOf(station),
            ticks: 200
        );
        world.Errors.Should().BeEmpty();
        world.GetFrame(crate).Should().Be(bubble, "the crate still sticks out of the station's bubble");
        return (sensorLog.NewsOf(crate), contact.InExpectedHome, contact.Mode, world.PhysicsHomeOf(sensor), space);
    }

    [Fact]
    public void AStaticSensorFarOutDetectsACrateThatSticksOutOfAParkedStationAsItDoesWithoutNumericFrames()
    {
        (
            List<ContactNews> SensorNews,
            bool SensorInHullHome,
            NumericMode SensorMode,
            FrameHandle SensorHome,
            FrameHandle Space
        ) with = RunStaticSensorBesideAStation(numericFrames: true);
        (
            List<ContactNews> SensorNews,
            bool SensorInHullHome,
            NumericMode SensorMode,
            FrameHandle SensorHome,
            FrameHandle Space
        ) without = RunStaticSensorBesideAStation(numericFrames: false);

        without.SensorHome.Should().Be(without.Space);
        without.SensorNews.Should().StartWith(ContactNews.Started);
        with.SensorInHullHome.Should()
            .BeTrue(
                "the sensor may touch the ghost of the crate, so it joined the home of the station's static hull, the system of space"
            );
        with.SensorMode.Should().Be(NumericMode.None);
        with.SensorNews.Should().Equal(without.SensorNews, "numeric frames do not change which events gameplay hears");
    }

    /// <summary>
    /// A parked station <see cref="FarOut"/>, 9,000 km, out in space, a static hull with a bubble of radius 40 m, holds a
    /// radar frame that a <see cref="SpinMotion"/> turns, at (0, 39, 0). The radar's hull is a kinematic ball of radius 5 m.
    /// So it reaches
    /// 44 m from the station's origin and sticks out of the bubble, and its kinematic ghost in space stops a rock that
    /// drifts at it at 0.3 m/s from 0.05 m above it, outside the bubble. 9,000 km out the rock alone would rest in a
    /// numeric frame of its own. But it may touch the ghost of the radar's hull, which lives in the home of the station's
    /// static hull, so the merge test links it to the station's hull, and it stays in the system of space. The outcome says
    /// whether the rock was in the home of the station's hull in the step of its first contact with the radar's hull, and
    /// gives the mode of its home then and its home at the end.
    /// </summary>
    private static (
        List<ContactNews> RockNews,
        Double3 RockPosition,
        Double3 RockVelocity,
        bool RockInHullHome,
        NumericMode RockMode,
        FrameHandle RockHome,
        FrameHandle Space
    ) RunRadarAndRock(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity station = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(FarOut),
                Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Static),
                AsFrame = new(FrameBounds.Sphere(40)),
            }
        );
        Entity radar = world.Spawn(
            new SpawnDesc
            {
                Frame = world.GetOwnedFrame(station),
                Pose = At(0, 39),
                Body = new(Shape: new SphereShapeDesc(5f), Motion: BodyMotion.Kinematic),
                AsFrame = new(FrameBounds.Sphere(6)),
            }.Spinning(world, new(Tilt: DQuaternion.Identity, Rate: 0.1))
        );
        var rockLog = new CollisionLog();
        Entity rock = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(FarOut, 44.55),
                LinearVelocity = new(0, -0.3, 0),
                Body = RockBody,
                Components = [rockLog],
            }
        );
        (bool InExpectedHome, NumericMode Mode) contact = WatchFirstContact(
            world: world,
            body: rock,
            log: rockLog,
            other: radar,
            expectedHome: () => world.PhysicsHomeOf(station),
            ticks: 120
        );
        world.Errors.Should().BeEmpty();
        world.GetFrame(rock).Should().Be(space, "the rock never touched the station's bubble");
        return (
            rockLog.NewsOf(radar),
            world.PoseIn(rock, space).Position,
            world.VelocityIn(rock, space),
            contact.InExpectedHome,
            contact.Mode,
            world.PhysicsHomeOf(rock),
            space
        );
    }

    [Fact]
    public void ARockFarOutHitsTheHullOfASpinningRadarThatSticksOutOfAParkedStationAsItDoesWithoutNumericFrames()
    {
        (
            List<ContactNews> RockNews,
            Double3 RockPosition,
            Double3 RockVelocity,
            bool RockInHullHome,
            NumericMode RockMode,
            FrameHandle RockHome,
            FrameHandle Space
        ) with = RunRadarAndRock(numericFrames: true);
        (
            List<ContactNews> RockNews,
            Double3 RockPosition,
            Double3 RockVelocity,
            bool RockInHullHome,
            NumericMode RockMode,
            FrameHandle RockHome,
            FrameHandle Space
        ) without = RunRadarAndRock(numericFrames: false);

        without.RockHome.Should().Be(without.Space);
        without.RockNews.Should().StartWith(ContactNews.Started);
        without.RockPosition.Y.Should().BeGreaterThan(44.4, "the ghost of the radar's hull stopped the rock");
        with.RockInHullHome.Should()
            .BeTrue(
                "the rock may touch the ghost of the radar's hull, so it joined the home of the station's static hull, the system of space"
            );
        with.RockMode.Should().Be(NumericMode.None);
        with.RockNews.Should().Equal(without.RockNews, "numeric frames do not change which events gameplay hears");
        (with.RockPosition - without.RockPosition).Length.Should().BeLessThan(1e-2);
        (with.RockVelocity - without.RockVelocity).Length.Should().BeLessThan(1e-2);
    }
}
