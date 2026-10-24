using System.Diagnostics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A following frame inside a turning frame, with numeric frames on and off, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "Numeric frames inside turning frames", and docs/plans/numeric-frames.md, "Following
/// frames"). A kinematic hull, the owner of a frame with a bubble of 16 m, moves fast or far in a frame that turns in place
/// in a root frame without a field, so with numeric frames it anchors a following frame below the turning frame, and
/// without them it lives in the turning frame's own system. The hull stops, starts or skips one step while a body crosses
/// its bubble, or while a dynamic ship floats beside it.
/// <para>
/// Each body then moves as it does without numeric frames. With numeric frames, physics measures a body's velocity at the
/// end of a step against the following frame's instant velocity in Jolt's stepping, which differs from its plan's when the
/// plan changes the frame's velocity at the start of the step (<c>JoltPhysicsBackend.HomeSteppedVelocityOverPlan</c>, from
/// ticket ter-zsku), and in a turning frame it adds the turn's part of the instant velocity on the body's semantic place and
/// chord (<c>FrameTurn.InstantAtEnd</c>, from ticket ter-1bnq). The two compose in
/// <c>JoltPhysicsBackend.InstantVelocity</c>,
/// <c>HandedOffVelocities</c> and <c>TryHullRecord</c>, which these tests run together.
/// </para>
/// </summary>
public sealed class TurningFollowingFrameTests
{
    // ---- A body that enters the bubble of a hull in the step in which the hull stops ----

    /// <summary>The tick from which <see cref="ChangingDrift"/> writes no motion intent in the scenes below.</summary>
    private const long StopTick = 20;

    /// <summary>
    /// A fast hull: 2 km/s along the frame's +X, 100 km from the axis of a frame that turns at 1e-3 rad/s. There the
    /// centrifugal term is 0.1 m/s², and the Coriolis term on the hull's drift is 4 m/s². The hull anchors a following frame
    /// from 1,000 m/s, and keeps it down to 400 m/s. A float velocity at 2 km/s has steps of 1.2e-4 m/s, and Jolt rounds the
    /// two worlds' velocities differently, in different systems and about different origins: the worst differences measured
    /// are 7.4e-5 m and 4.9e-4 m/s.
    /// </summary>
    private static readonly HullScene Fast = new(
        Rate: 1e-3,
        Start: new(0, 0, 1e5),
        Drift: new(2_000, 0, 0),
        PlaceTolerance: 3e-4,
        VelocityTolerance: 1e-3
    );

    /// <summary>
    /// A far hull: 10 m/s along the frame's +X, 10,000 km from the axis of a frame that turns at 3e-4 rad/s, past the 2^23 m
    /// from which a kinematic body anchors a following frame at any speed. There the centrifugal term is 0.9 m/s², and the
    /// frame's axes move at 3 km/s in inertial space. A float velocity at 10 to 20 m/s has steps of 1e-6 to 2e-6 m/s: the
    /// worst differences measured are 1.3e-6 m and 2.9e-6 m/s. The turn's Coriolis half step on a change of 10 m/s is
    /// ω v dt / 2 = 2.5e-5 m/s, so the tolerance sees an error in it.
    /// </summary>
    private static readonly HullScene Far = new(
        Rate: 3e-4,
        Start: new(0, 0, 1e7),
        Drift: new(10, 0, 0),
        PlaceTolerance: 1e-5,
        VelocityTolerance: 1e-5
    );

    private static readonly BodyDesc Crate = new(
        Shape: new BoxShapeDesc(new(0.25f)),
        Mass: 2,
        Restitution: 0,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>The scenes of a hull in a turning frame that the tests run.</summary>
    public enum SceneKind
    {
        /// <summary>The hull of <see cref="Fast"/>.</summary>
        Fast,

        /// <summary>The hull of <see cref="Far"/>.</summary>
        Far,
    }

    /// <summary>
    /// A scene of a hull in a turning frame: the frame's rate about its +Y in rad/s, the hull's place in the frame when the
    /// scene is built, the velocity of the hull's drift in the frame's coordinates, in m/s, and how far a body in the scene
    /// may be from where it is without numeric frames, a few times the worst difference measured, near the float step at the
    /// scene's speeds.
    /// </summary>
    /// <param name="Rate">The turning frame's angular velocity about its +Y, in rad/s.</param>
    /// <param name="Start">The hull's place in the turning frame at the spawn, in m.</param>
    /// <param name="Drift">The hull's velocity in the turning frame's coordinates while it drifts, in m/s.</param>
    /// <param name="PlaceTolerance">The largest distance in the turning frame from the body's place without numeric frames, in m.</param>
    /// <param name="VelocityTolerance">The largest difference from the body's velocity without numeric frames, in m/s.</param>
    public sealed record HullScene(
        double Rate,
        Double3 Start,
        Double3 Drift,
        double PlaceTolerance,
        double VelocityTolerance
    );

    private static HullScene SceneOf(SceneKind kind) =>
        kind switch
        {
            SceneKind.Fast => Fast,
            SceneKind.Far => Far,
            var other => throw new UnreachableException($"The scene {other} has no hull."),
        };

    /// <summary>How the hull's drift changes at the tick of the change.</summary>
    public enum DriftChange
    {
        /// <summary>The hull drifts until the change and holds still from it on.</summary>
        Stops,

        /// <summary>The hull holds still until the change and drifts from it on.</summary>
        Starts,

        /// <summary>The hull drifts, and holds still for the one step of the change.</summary>
        Skips,
    }

    /// <summary>
    /// Drives its kinematic body along a straight line in its frame's coordinates at the scene's drift with end poses, in
    /// the ticks that <paramref name="change"/> names at <paramref name="changeTick"/>. In the other ticks it writes no motion
    /// intent, so the body holds still in the step that applies that tick's intents.
    /// </summary>
    private sealed class ChangingDrift(Double3 drift, DriftChange change, long changeTick) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            bool drifts = change switch
            {
                DriftChange.Stops => self.Tick < changeTick,
                DriftChange.Starts => self.Tick >= changeTick,
                _ => self.Tick != changeTick,
            };
            if (!drifts)
                return;
            DPose start = self.PoseAtNextStepStartIn(self.Frame);
            self.MoveKinematic(new(start.Position + drift * self.DeltaTime, start.Rotation));
        }
    }

    /// <summary>A world with the default numeric frames, or with none, on the real backend wrapped to record its steps.</summary>
    private static World NewTurningWorld(bool numericFrames, out FaultInjectingBackend backend) =>
        NewWorldWithFaults(
            faults: out backend,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames
        );

    /// <summary>
    /// A root frame without a field, and in it a frame with a bubble of 100,000 km that turns in place about its +Y at the
    /// scene's rate from its first step. Its owner has no body.
    /// </summary>
    private static FrameHandle TurningFrame(World world, HullScene scene)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f)));
        Entity owner = world.Spawn(
            new SpawnDesc { Frame = space, AsFrame = new(FrameBounds.Sphere(1e8f)) }.Spinning(
                world,
                new(Tilt: DQuaternion.Identity, Rate: scene.Rate),
                firstMove: true
            )
        );
        return world.GetOwnedFrame(owner);
    }

    /// <summary>
    /// The owner of a frame with a bubble of 16 m at the scene's start in <paramref name="turning"/>, whose kinematic hull, a
    /// box of 4 m, <see cref="ChangingDrift"/> drives. A hull that drifts from its spawn has the drift as its spawn velocity
    /// and its first step's end pose as its first motion intent.
    /// </summary>
    private static Entity SpawnHull(
        World world,
        FrameHandle turning,
        HullScene scene,
        DriftChange change,
        long changeTick
    )
    {
        bool driftsAtSpawn = change != DriftChange.Starts || changeTick <= world.TickNumber;
        return world.Spawn(
            new()
            {
                Frame = turning,
                Pose = new(scene.Start, DQuaternion.Identity),
                Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Kinematic, Mass: 1e5f),
                AsFrame = new(FrameBounds.Sphere(16)),
                LinearVelocity = driftsAtSpawn ? scene.Drift : Double3.Zero,
                FirstMove = driftsAtSpawn
                    ? new MotionIntent(
                        new EndPose(new(scene.Start + scene.Drift * world.DeltaTime, DQuaternion.Identity))
                    )
                    : default,
                Components = [new ChangingDrift(scene.Drift, change, changeTick)],
            }
        );
    }

    // ---- A body that leaves the bubble of a hull that stops, starts or skips a step ----

    /// <summary>
    /// The hull of <see cref="SpawnHull"/>, and a crate in its frame 15 m along +Y from the hull that drifts on along +Y at
    /// 2 m/s, so it leaves the bubble into the frame's tree parent within a second: the hull's following frame with numeric
    /// frames, the turning frame without them. With <paramref name="ship"/> the crate is the dynamic hull of a ship, a frame
    /// with a bubble of 10 cm.
    /// </summary>
    private static (World World, FrameHandle Turning, Entity Owner, Entity Crate) LeavingCrate(
        bool numericFrames,
        HullScene scene,
        DriftChange change,
        long changeTick,
        bool ship
    ) =>
        LeavingCrate(
            numericFrames: numericFrames,
            scene: scene,
            change: change,
            changeTick: changeTick,
            ship: ship,
            faults: out _
        );

    /// <summary>The scene of <see cref="LeavingCrate(bool, HullScene, DriftChange, long, bool)"/>, with its backend.</summary>
    private static (World World, FrameHandle Turning, Entity Owner, Entity Crate) LeavingCrate(
        bool numericFrames,
        HullScene scene,
        DriftChange change,
        long changeTick,
        bool ship,
        out FaultInjectingBackend faults
    )
    {
        World world = NewTurningWorld(numericFrames, out faults);
        FrameHandle turning = TurningFrame(world, scene);
        Entity owner = SpawnHull(world: world, turning: turning, scene: scene, change: change, changeTick: changeTick);
        Entity crate = world.Spawn(
            new()
            {
                Frame = world.GetOwnedFrame(owner),
                Pose = At(0, 15),
                LinearVelocity = new(0, 2, 0),
                Body = Crate,
                AsFrame = ship ? new FrameSpec(FrameBounds.Sphere(0.1f)) : null,
            }
        );
        return (world, turning, owner, crate);
    }

    /// <summary>
    /// The hull changes its drift in the step in which the crate leaves its bubble, or in one of the three steps before. The
    /// crate then moves on in the turning frame as it does without numeric frames, whichever step the hull changed in. A crate
    /// that leaves in the step of the change hands off into the following frame while the frame's instant velocity in Jolt's
    /// stepping differs from its plan's by half the change of its velocity: 1 km/s for the fast hull, 5 m/s for the far one.
    /// The hand-off takes that difference and the turn's part of the crate's instant velocity in the turning frame, and a
    /// ship's step record after the hand-off takes both too.
    /// <para>
    /// Each scene has its own tolerance near the float step at its speed (see <see cref="HullScene"/>). At least one of the
    /// four change steps hands the crate off into the hull's following frame in a step whose plan differs from the plan of
    /// the step before. A fast hull that stops loses its following frame at the update after the stop, below 400 m/s, so a
    /// crate that leaves later leaves into the turning frame's own system.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(SceneKind.Fast, DriftChange.Stops, false)]
    [InlineData(SceneKind.Fast, DriftChange.Stops, true)]
    [InlineData(SceneKind.Fast, DriftChange.Skips, false)]
    [InlineData(SceneKind.Fast, DriftChange.Skips, true)]
    [InlineData(SceneKind.Far, DriftChange.Stops, false)]
    [InlineData(SceneKind.Far, DriftChange.Starts, false)]
    [InlineData(SceneKind.Far, DriftChange.Starts, true)]
    public void ACrateThatLeavesTheBubbleOfAHullThatChangesItsDriftInATurningFrameMovesOnAsWithoutNumericFrames(
        SceneKind sceneKind,
        DriftChange change,
        bool ship
    )
    {
        HullScene scene = SceneOf(sceneKind);
        long exitTick;
        (World probe, FrameHandle _, Entity probeOwner, Entity probeCrate) = LeavingCrate(
            numericFrames: true,
            scene: scene,
            change: change == DriftChange.Starts ? DriftChange.Starts : DriftChange.Stops,
            changeTick: long.MaxValue,
            ship: ship
        );
        using (probe)
        {
            TickUntil(probe, () => probe.GetFrame(probeCrate) != probe.GetOwnedFrame(probeOwner), 120);
            probe
                .GetFrame(probeCrate)
                .Should()
                .NotBe(probe.GetOwnedFrame(probeOwner), "the crate leaves the bubble within two seconds");
            probe
                .Frames.Anchors(probe.PhysicsHomeOf(probeOwner), probeOwner)
                .Should()
                .BeTrue("the hull anchors a following frame");
            probe
                .Frames.IsNumeric(probe.PhysicsHomeOf(probeCrate))
                .Should()
                .BeTrue("the crate leaves into the hull's following frame");
            exitTick = probe.TickNumber;
        }

        double worstPlace = 0,
            worstVelocity = 0;
        var changedPlanExits = new List<long>();
        for (long changeTick = exitTick - 3; changeTick <= exitTick; changeTick++)
        {
            (World world, FrameHandle turning, Entity owner, Entity crate) = LeavingCrate(
                numericFrames: true,
                scene: scene,
                change: change,
                changeTick: changeTick,
                ship: ship,
                faults: out FaultInjectingBackend faults
            );
            (World plain, FrameHandle plainTurning, Entity plainOwner, Entity plainCrate) = LeavingCrate(
                numericFrames: false,
                scene: scene,
                change: change,
                changeTick: changeTick,
                ship: ship
            );
            // The crate's hand-off out of the hull's frame, read in the step that makes it, while the tree is as the step has
            // it: whether it goes into a frame that the hull anchors, and whether the hull had a motion intent in that step and
            // in the step before, which give its frame's plan.
            faults.EditOutput = output =>
            {
                foreach (HandOff handOff in output.HandOffs)
                {
                    if (handOff.Entity != crate || !world.Frames.Anchors(handOff.To, owner))
                        continue;
                    long step = faults.Steps[^1].Tick;
                    bool moves = faults.Moves.Any(move => move.Tick == step && move.Entity == owner);
                    bool movedBefore = faults.Moves.Any(move => move.Tick == step - 1 && move.Entity == owner);
                    if (moves != movedBefore)
                        changedPlanExits.Add(changeTick);
                }
            };
            using (world)
            using (plain)
            {
                for (long tick = 0; tick < exitTick + 30; tick++)
                {
                    world.Tick();
                    plain.Tick();
                    (plain.GetFrame(plainCrate) == plainTurning)
                        .Should()
                        .Be(
                            world.GetFrame(crate) == turning,
                            "the crate leaves the bubble at the same sync point, change tick {0}, sync point {1}",
                            changeTick,
                            world.TickNumber
                        );
                    double place = (
                        world.PoseIn(crate, turning).Position - plain.PoseIn(plainCrate, plainTurning).Position
                    ).Length;
                    double velocity = (
                        world.VelocityIn(crate, turning) - plain.VelocityIn(plainCrate, plainTurning)
                    ).Length;
                    place
                        .Should()
                        .BeLessThan(
                            scene.PlaceTolerance,
                            "the crate is where it is without numeric frames, change tick {0}, sync point {1}",
                            changeTick,
                            world.TickNumber
                        );
                    velocity
                        .Should()
                        .BeLessThan(
                            scene.VelocityTolerance,
                            "the crate moves as it does without numeric frames, change tick {0}, sync point {1}",
                            changeTick,
                            world.TickNumber
                        );
                    worstPlace = Math.Max(worstPlace, place);
                    worstVelocity = Math.Max(worstVelocity, velocity);
                }

                world.GetFrame(crate).Should().Be(turning, "the crate left the hull's bubble");
                (world.PoseIn(owner, turning).Position - plain.PoseIn(plainOwner, plainTurning).Position)
                    .Length.Should()
                    .BeLessThan(1e-6, "the hull is where it is without numeric frames, change tick {0}", changeTick);
                world.Errors.Should().BeEmpty();
                plain.Errors.Should().BeEmpty();
            }
        }

        changedPlanExits
            .Should()
            .NotBeEmpty(
                "the crate leaves into the hull's following frame in the step in which its plan changes, for one of the change steps"
            );
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{sceneKind} {change} ship={ship}: worst place {worstPlace:E2} m, worst velocity {worstVelocity:E2} m/s, "
                + $"exits into a changed plan with the change ticks {string.Join(", ", changedPlanExits)} of {exitTick - 3} to {exitTick}"
        );
    }

    /// <summary>
    /// The fast hull of <see cref="SpawnHull"/>, which stops at <see cref="StopTick"/>, and a body in the turning frame 24 m
    /// behind the hull along -X and 5 m beside it along +Y, outside its bubble of 16 m, with the hull's velocity and 10 m/s
    /// more toward the hull: a crate, a box of 0.5 m and 2 kg, or a character. With numeric frames the merge test links the
    /// body to the hull, so it moves in the hull's following frame. It is about 20.6 m behind the hull when the hull stops,
    /// and in that step it moves on by 33.5 m relative to the hull, and ends the step about 13 m from it, inside the bubble.
    /// </summary>
    private static (World World, FrameHandle Turning, Entity Owner, Entity Body) EnteringBody(
        bool numericFrames,
        bool character,
        out FaultInjectingBackend backend
    ) =>
        EnteringBody(
            numericFrames: numericFrames,
            character: character,
            scene: Fast,
            stopTick: StopTick,
            backend: out backend
        );

    /// <summary>
    /// The scene of <see cref="EnteringBody(bool, bool, out FaultInjectingBackend)"/> with the hull of
    /// <paramref name="scene"/>,
    /// which stops at <paramref name="stopTick"/>, or drifts on with <see cref="long.MaxValue"/>.
    /// </summary>
    private static (World World, FrameHandle Turning, Entity Owner, Entity Body) EnteringBody(
        bool numericFrames,
        bool character,
        HullScene scene,
        long stopTick,
        out FaultInjectingBackend backend
    )
    {
        World world = NewTurningWorld(numericFrames, out backend);
        FrameHandle turning = TurningFrame(world, scene);
        Entity owner = SpawnHull(
            world: world,
            turning: turning,
            scene: scene,
            change: DriftChange.Stops,
            changeTick: stopTick
        );
        Entity body = world.Spawn(
            new()
            {
                Frame = turning,
                Pose = new(scene.Start + new Double3(-24, 5, 0), DQuaternion.Identity),
                LinearVelocity = scene.Drift + new Double3(10, 0, 0),
                Body = character
                    ? new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70)
                    : Crate,
            }
        );
        return (world, turning, owner, body);
    }

    /// <summary>
    /// The body enters the hull's bubble in the step in which the hull stops, and moves on in the turning frame as it does
    /// without numeric frames: it enters the hull's frame at 2 km/s and leaves it again in the next step. Jolt's chord of the
    /// body in the following frame, plus half a step of the gravity that physics applied to it, is relative to the frame's
    /// instant velocity in Jolt's stepping, 1 km/s past the instant velocity of the frame's still plan, and in the turning
    /// frame a dynamic body's instant velocity also takes the turn's part on its semantic place and chord.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABodyThatEntersTheBubbleOfAHullInTheStepInWhichTheHullStopsInATurningFrameMovesOnAsWithoutNumericFrames(
        bool character
    )
    {
        (World world, FrameHandle turning, Entity owner, Entity body) = EnteringBody(
            numericFrames: true,
            character,
            out _
        );
        (World plain, FrameHandle plainTurning, Entity plainOwner, Entity plainBody) = EnteringBody(
            numericFrames: false,
            character,
            out _
        );
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world
                .PhysicsHomeOf(body)
                .Should()
                .Be(world.PhysicsHomeOf(owner), "the body moves in the hull's following frame");
            world.Frames.IsNumeric(world.PhysicsHomeOf(owner)).Should().BeTrue();
            int inside = 0;
            double worstPlace = 0,
                worstVelocity = 0;
            for (int tick = 0; tick < 60; tick++)
            {
                world.Tick();
                plain.Tick();
                bool entered = world.GetFrame(body) == world.GetOwnedFrame(owner);
                entered
                    .Should()
                    .Be(
                        plain.GetFrame(plainBody) == plain.GetOwnedFrame(plainOwner),
                        "the body is in the hull's frame at the same sync points"
                    );
                if (entered)
                    inside++;
                double place = (
                    world.PoseIn(body, turning).Position - plain.PoseIn(plainBody, plainTurning).Position
                ).Length;
                double velocity = (world.VelocityIn(body, turning) - plain.VelocityIn(plainBody, plainTurning)).Length;
                place
                    .Should()
                    .BeLessThan(
                        Fast.PlaceTolerance,
                        "the body is where it is without numeric frames at sync point {0}",
                        world.TickNumber
                    );
                velocity
                    .Should()
                    .BeLessThan(
                        Fast.VelocityTolerance,
                        "the body moves as it does without numeric frames at sync point {0}",
                        world.TickNumber
                    );
                worstPlace = Math.Max(worstPlace, place);
                worstVelocity = Math.Max(worstVelocity, velocity);
            }

            inside.Should().BeGreaterThan(0, "the body enters the hull's bubble");
            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"entering character={character}: worst place {worstPlace:E2} m, worst velocity {worstVelocity:E2} m/s"
            );
        }
    }

    /// <summary>
    /// A crate enters the bubble of a hull that drifts on, at 10 m/s relative to it, and moves on in the turning frame as it
    /// does without numeric frames. The hand-off out of the following frame takes the turn's part of the crate's instant
    /// velocity on its semantic place and chord, which hold the frame's place and velocity in the turning frame: 2 km/s for
    /// the fast hull, whose turn moves the instant velocity by ω dt times that, 3.3 cm/s, and 10,000 km for the far hull,
    /// whose centrifugal half step moves it by ω² r dt / 2, 7.5 mm/s.
    /// </summary>
    [Theory]
    [InlineData(SceneKind.Fast)]
    [InlineData(SceneKind.Far)]
    public void ACrateThatEntersTheBubbleOfAHullThatDriftsOnInATurningFrameMovesAsWithoutNumericFrames(
        SceneKind sceneKind
    )
    {
        HullScene scene = SceneOf(sceneKind);
        (World world, FrameHandle turning, Entity owner, Entity crate) = EnteringBody(
            numericFrames: true,
            character: false,
            scene: scene,
            stopTick: long.MaxValue,
            backend: out _
        );
        (World plain, FrameHandle plainTurning, Entity plainOwner, Entity plainCrate) = EnteringBody(
            numericFrames: false,
            character: false,
            scene: scene,
            stopTick: long.MaxValue,
            backend: out _
        );
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world
                .PhysicsHomeOf(crate)
                .Should()
                .Be(world.PhysicsHomeOf(owner), "the crate moves in the hull's following frame");
            world.Frames.IsNumeric(world.PhysicsHomeOf(owner)).Should().BeTrue();
            int inside = 0;
            double worstPlace = 0,
                worstVelocity = 0;
            for (int tick = 0; tick < 90; tick++)
            {
                world.Tick();
                plain.Tick();
                bool entered = world.GetFrame(crate) == world.GetOwnedFrame(owner);
                entered
                    .Should()
                    .Be(
                        plain.GetFrame(plainCrate) == plain.GetOwnedFrame(plainOwner),
                        "the crate is in the hull's frame at the same sync points"
                    );
                if (entered)
                    inside++;
                double place = (
                    world.PoseIn(crate, turning).Position - plain.PoseIn(plainCrate, plainTurning).Position
                ).Length;
                double velocity = (
                    world.VelocityIn(crate, turning) - plain.VelocityIn(plainCrate, plainTurning)
                ).Length;
                place
                    .Should()
                    .BeLessThan(
                        scene.PlaceTolerance,
                        "the crate is where it is without numeric frames at sync point {0}",
                        world.TickNumber
                    );
                velocity
                    .Should()
                    .BeLessThan(
                        scene.VelocityTolerance,
                        "the crate moves as it does without numeric frames at sync point {0}",
                        world.TickNumber
                    );
                worstPlace = Math.Max(worstPlace, place);
                worstVelocity = Math.Max(worstVelocity, velocity);
            }

            inside.Should().BeGreaterThan(0, "the crate enters the hull's bubble");
            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"entering {sceneKind} hull that drifts on: worst place {worstPlace:E2} m, worst velocity {worstVelocity:E2} m/s"
            );
        }
    }

    // ---- A dynamic ship beside a hull that stops ----

    /// <summary>
    /// The fast hull of <see cref="SpawnHull"/>, which stops at <see cref="StopTick"/>, and the owner of a ship, a frame with a
    /// bubble of 3 m whose dynamic hull is a box of 2 m and 1 t, in the turning frame 22 m from the hull along +Y and 10 m
    /// behind it, outside its bubble, with the hull's velocity and 10 m/s more along +X. With numeric frames the ship's hull
    /// lives in the hull's following frame, and its frame hangs there.
    /// </summary>
    private static (World World, FrameHandle Turning, Entity Owner, Entity Ship) ShipBesideAStoppingHull(
        bool numericFrames
    )
    {
        World world = NewTurningWorld(numericFrames, out _);
        FrameHandle turning = TurningFrame(world, Fast);
        Entity owner = SpawnHull(
            world: world,
            turning: turning,
            scene: Fast,
            change: DriftChange.Stops,
            changeTick: StopTick
        );
        Entity ship = world.Spawn(
            new()
            {
                Frame = turning,
                Pose = new(Fast.Start + new Double3(-10, 22, 0), DQuaternion.Identity),
                LinearVelocity = Fast.Drift + new Double3(10, 0, 0),
                AsFrame = new(FrameBounds.Sphere(3)),
                Body = new(
                    Shape: new BoxShapeDesc(new(1)),
                    Mass: 1000,
                    Restitution: 0,
                    LinearDamping: 0,
                    AngularDamping: 0
                ),
            }
        );
        return (world, turning, owner, ship);
    }

    /// <summary>
    /// The ship moves on in the turning frame when the hull beside it stops, and its frame reads its place and velocity in the
    /// turning frame at every sync point, the one after the stop step included, as without numeric frames. The step record
    /// that physics releases for the ship's frame is relative to the instant velocity of the following frame's plan, and in
    /// the turning frame it holds the turn's part of the record's instant velocity.
    /// </summary>
    [Fact]
    public void ADynamicShipBesideAHullThatStopsInATurningFrameMovesAndReadsItsVelocityAsWithoutNumericFrames()
    {
        (World world, FrameHandle turning, Entity owner, Entity ship) = ShipBesideAStoppingHull(numericFrames: true);
        (World plain, FrameHandle plainTurning, Entity _, Entity plainShip) = ShipBesideAStoppingHull(
            numericFrames: false
        );
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world
                .PhysicsHomeOf(ship)
                .Should()
                .Be(world.PhysicsHomeOf(owner), "the ship's hull lives in the hull's following frame");
            double worstPlace = 0,
                worstVelocity = 0;
            for (int tick = 0; tick < 60; tick++)
            {
                world.Tick();
                plain.Tick();
                double place = (
                    world.PoseIn(ship, turning).Position - plain.PoseIn(plainShip, plainTurning).Position
                ).Length;
                double velocity = (world.VelocityIn(ship, turning) - plain.VelocityIn(plainShip, plainTurning)).Length;
                place
                    .Should()
                    .BeLessThan(
                        Fast.PlaceTolerance,
                        "the ship is where it is without numeric frames at sync point {0}",
                        world.TickNumber
                    );
                velocity
                    .Should()
                    .BeLessThan(
                        Fast.VelocityTolerance,
                        "the ship moves as it does without numeric frames at sync point {0}",
                        world.TickNumber
                    );
                worstPlace = Math.Max(worstPlace, place);
                worstVelocity = Math.Max(worstVelocity, velocity);
            }

            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"ship: worst place {worstPlace:E2} m, worst velocity {worstVelocity:E2} m/s"
            );
        }
    }
}
