using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.NumericFrameTesting;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.WorldTestingGrowth;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics follows the frame tree. At the start of each step, and in ApplyChanges, the backend reconciles with the tree:
/// it drops the hand-offs of the last step and puts each frame hull in the physics system of its frame's parent in the
/// tree. These tests pin down what the reconcile guarantees, also when the system that a hull moves into is full. They run
/// the World on the real backend. The game moves a frame through <see cref="World.Frames"/> between two ticks, and the
/// reconcile runs at the start of the step of the next tick. A test that reads the backend "right after the reconcile"
/// either reads it after that tick, or, when the step would change the state under test, calls the reconcile alone (an
/// empty change batch, see <see cref="Reconcile"/>) and reads the backend between the reconcile and the step. The comment
/// of each such test says which it does and why.
/// </summary>
public sealed class ReconcileTests
{
    /// <summary>A hull of 4 m by 4 m by 4 m with a mass of 1000 kg, and no damping or bounce.</summary>
    private static BodyDesc ShipHull() =>
        new(Shape: new BoxShapeDesc(new(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The hand-offs that Jolt reported so far, in the order of the steps.</summary>
    private static List<HandOff> HandOffs(FaultInjectingBackend faults) =>
        [.. faults.Outputs.SelectMany(output => output.HandOffs)];

    /// <summary>
    /// Runs the reconcile of the backend alone, as the start of a step does it, and nothing else: it applies an empty change
    /// batch to the frame tree of the World. The old test stand-in of the sync point had a method for this. Call it between two ticks.
    /// </summary>
    private static void Reconcile(FaultInjectingBackend faults, World world) =>
        faults.Jolt.ApplyChanges(new(), world.Frames);

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
    /// Probe P1 of the review, with physics systems of at most 8 bodies. A hangar bubble of radius 30 m, 1 km along +X,
    /// and a ship 70 m beyond the hangar's center that flies into it at 60 m/s, with a gentle thruster along -X. Far along
    /// -X, a storage frame whose system is full with 8 static crates, and a static skiff. The backend reconciles frame
    /// owners from the last one created, so it reaches the skiff's hull before the ship's. With
    /// <c>numericFrames</c> false, the World makes no numeric frames. With it true, the ship's hull thrusts from
    /// the sync point of its thirtieth tick on, 40 m beyond the hangar's center, and a hull that thrusts at 60 m/s falls in
    /// a numeric frame of space: so it enters the hangar from that frame.
    /// </summary>
    private sealed class FullStorage : IDisposable
    {
        private readonly bool numericFrames;

        public FullStorage(bool numericFrames)
        {
            this.numericFrames = numericFrames;
            World = NewWorldWithFaultsAndSmallSystems(
                maxBodiesPerFrame: 8,
                faults: out FaultInjectingBackend faults,
                configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames
            );
            Faults = faults;
            Space = World.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
            Hangar = EmptyFrame(world: World, parent: Space, pose: At(1000), radius: 30);
            BodyDesc hull = ShipHull();
            Ship = World.GetOwnedFrame(
                World.Spawn(
                    new()
                    {
                        Frame = Space,
                        Pose = At(1070),
                        LinearVelocity = new Vector3(-60, 0, 0),
                        Body = hull,
                        AsFrame = new(FrameBounds.Sphere(10)),
                        Components = [new Thruster(hull.Mass) { Direction = -Vector3.UnitX }],
                    }
                )
            );
            Storage = EmptyFrame(world: World, parent: Space, pose: At(-5000), radius: 50);
            for (int i = 0; i < 8; i++)
                World.Spawn(
                    new()
                    {
                        Frame = Storage,
                        Pose = At(3 * i - 12),
                        Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static),
                    }
                );
            Skiff = World.GetOwnedFrame(
                World.Spawn(
                    new()
                    {
                        Frame = Space,
                        Pose = At(-3000),
                        Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Static),
                        AsFrame = new(FrameBounds.Sphere(5)),
                    }
                )
            );
        }

        public World World { get; }
        public FaultInjectingBackend Faults { get; }
        public FrameHandle Space { get; }
        public FrameHandle Hangar { get; }
        public FrameHandle Ship { get; }
        public FrameHandle Storage { get; }
        public FrameHandle Skiff { get; }

        /// <summary>
        /// Ticks until physics hands the ship into the hangar, and checks that the hull enters from a falling numeric frame
        /// with numeric frames, or from the system of space without. Then, between ticks and with no change batch, the game
        /// moves the skiff into storage, the ship back into space and the hangar under the ship. Every move is legal.
        /// </summary>
        public void MoveFramesAfterTheShipEntersTheHangar()
        {
            // The ship's tree parent, and how it moves, before the step of the hand-off.
            FrameHandle treeParent = FrameHandle.Null;
            NumericMode treeParentMode = NumericMode.None;
            TickUntil(
                World,
                () =>
                {
                    if (HandOffs(Faults).Count > 0)
                        return true;
                    treeParent = World.Frames.TreeParent(Ship);
                    treeParentMode = World.Frames.Numeric(treeParent);
                    return false;
                },
                60
            );
            HandOff entry = HandOffs(Faults).Should().ContainSingle().Which;
            entry.To.Should().Be(Hangar);
            entry.From.Should().Be(treeParent, "the hull enters the hangar from the system of the ship's tree parent");
            treeParentMode
                .Should()
                .Be(
                    numericFrames ? NumericMode.Falling : NumericMode.None,
                    numericFrames
                        ? "the ship's hull thrusts at 60 m/s, so it falls in a numeric frame of space"
                        : "the World makes no numeric frames"
                );
            World.Frames.Reparent(Skiff, Storage);
            World.Frames.Reparent(Ship, Space);
            World.Frames.Reparent(Hangar, Ship);
            World.Frames.Update();
        }

        public void Dispose() => World.Dispose();
    }

    [Fact]
    public void EveryHullFollowsTheTreeWhenTheGameMovesAFrameIntoAFullSystem()
    {
        // Numeric frames off: the test checks that the system of the ship's semantic parent holds its hull.
        using var scene = new FullStorage(numericFrames: false);
        scene.MoveFramesAfterTheShipEntersTheHangar();

        // At 60 m/s the ship's hull needs about 70 steps to cross the hangar's bubble. A hull that the reconcile left in
        // the hangar's system would then leave it for the hangar's parent, which is the ship's own frame.
        for (int i = 0; i < 90; i++)
        {
            scene.World.Tick();
            FrameHandle hullFrame = scene.Faults.Jolt.BodyFrameOf(scene.Ship.Entity)!.Value;
            hullFrame
                .Should()
                .Be(scene.World.Frames.Parent(scene.Ship), "the ship's hull follows the tree after step {0}", i);
            FrameHandle skiffFrame = scene.Faults.Jolt.BodyFrameOf(scene.Skiff.Entity)!.Value;
            skiffFrame.Should().Be(scene.Storage, "the storage system grew to take the skiff's hull at step {0}", i);
        }

        HandOffs(scene.Faults).Should().ContainSingle("the ship has no other frame to enter or leave");
        scene.Faults.Jolt.CapacityOf(scene.Storage).Should().Be(16);
        scene.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Step 4 of the plan: this test read the backend between the stand-in's reconcile and the step. Now it asserts
    /// the same two facts after one tick. Numeric frames are on, and the ship's hull enters the hangar from a falling frame
    /// (see <see cref="FullStorage"/>). The tick reconciles first, with the same code, and nothing in its step moves a
    /// hull to another system again or takes one out, so the step cannot repair a reconcile that left a hull in the wrong
    /// system. The reconcile also checks its own invariants at its end in a Debug build.
    /// </summary>
    [Fact]
    public void TheInvariantsHoldRightAfterTheReconcileThatMovesAHullIntoAFullSystem()
    {
        using var scene = new FullStorage(numericFrames: true);
        scene.MoveFramesAfterTheShipEntersTheHangar();

        scene.World.Tick();

        scene.Faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        FrameHandle skiffFrame = scene.Faults.Jolt.BodyFrameOf(scene.Skiff.Entity)!.Value;
        skiffFrame.Should().Be(scene.Storage);
        scene.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The backend hands the fighter into the carrier. The test gives the report a wrong origin, as a bug would, so the
    /// World refuses it and the tree keeps the fighter in space. A Debug build breaks a contract at the broken output. A
    /// Release build skips the fighter's whole update and records one error.
    /// <para>
    /// The World hands physics a batch at every sync point, also an empty one, and the batch reconciles (see
    /// <c>World.UpdateNumericFrames</c>). So the reconcile runs at the sync point of the refused report, and the broken state
    /// never shows between two ticks. The test reads the backend in the one place where it still shows: right before Jolt
    /// applies the first batch after the step that made the report. After the tick it reads the backend again, with no
    /// reconcile of its own. Then it ticks once more, to show that the World and the backend agree in the next step too.
    /// </para>
    /// </summary>
    [Fact]
    public void AfterARefusedHandOffTheReconcileRestoresTheInvariants()
    {
        // Numeric frames off: the Release branch checks that the system of space holds the fighter's hull.
        using World world = NewWorldWithFaults(
            faults: out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = WithoutNumericFrames
        );
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle carrier = EmptyFrame(world: world, parent: space, pose: At(1000), radius: 200);
        FrameHandle fighter = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(1260),
                    LinearVelocity = new Vector3(-60, 0, 0),
                    Body = ShipHull(),
                    AsFrame = new(FrameBounds.Sphere(10)),
                }
            )
        );
        // The backend hands the fighter into the carrier. The test gives the report a wrong origin, as a bug would. In a
        // Release build, the first batch after that step reads which system holds the fighter's hull before Jolt applies it.
        FrameHandle hullFrameBeforeTheBatch = FrameHandle.Null;
        faults.EditOutput = output =>
        {
            for (int i = 0; i < output.HandOffs.Count; i++)
                if (output.HandOffs[i].Entity == fighter.Entity)
                {
                    output.HandOffs[i] = output.HandOffs[i] with { From = carrier };
                    faults.EditNextChanges = _ =>
                        hullFrameBeforeTheBatch = faults.Jolt.BodyFrameOf(fighter.Entity) ?? FrameHandle.Null;
                }
        };

#if TERTIUS_CONTRACTS
        Action tickUntilTheHandOff = () => TickUntil(world, () => false, 120);

        tickUntilTheHandOff.Should().Throw<ContractException>().WithMessage($"*{fighter.Entity}*");
#else
        TickUntil(world, () => world.Errors.Count > 0, 120);
        faults.EditOutput = null;
        world.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {fighter.Entity}");
        HandOffs(faults).Should().ContainSingle().Which.To.Should().Be(carrier);
        world
            .Frames.Parent(fighter)
            .Should()
            .Be(space, "the World refused the hand-off, so the tree keeps the fighter in space");

        hullFrameBeforeTheBatch
            .Should()
            .Be(carrier, "the step left the fighter's hull in the carrier's system, and the tree refused to follow");
        // The batch of the same sync point reconciled: it put the hull back where the tree has the fighter's frame.
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        FrameHandle hullFrame = faults.Jolt.BodyFrameOf(fighter.Entity)!.Value;
        hullFrame.Should().Be(space);

        // The fighter is still inside the carrier's bubble, so the next step hands it in, with a correct report this time.
        world.Tick();

        HandOffs(faults)
            .Should()
            .HaveCount(2)
            .And.Contain(handOff => handOff.Entity == fighter.Entity && handOff.From == space && handOff.To == carrier);
        world.Frames.Parent(fighter).Should().Be(carrier);
        // The batch of that sync point reconciled too, and found the hull where the tree has the fighter's frame.
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        hullFrame = faults.Jolt.BodyFrameOf(fighter.Entity)!.Value;
        hullFrame.Should().Be(carrier);
        world.Errors.Should().ContainSingle("only the first, refused report is an error");
#endif
    }

    /// <summary>
    /// A ship whose bubble of radius 10 m sits at the origin of space, and a hangar bubble of radius 15 m, 20 m along +X.
    /// The ship's bubble does not fit in the hangar's, so it never enters, but it reaches into the hangar's bubble, so
    /// the hangar's system holds a ghost of the ship's hull.
    /// </summary>
    private static (FrameHandle Space, FrameHandle Hangar, FrameHandle Ship) ShipBesideHangar(
        World world,
        FaultInjectingBackend faults
    )
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle hangar = EmptyFrame(world: world, parent: space, pose: At(20), radius: 15);
        FrameHandle ship = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Body = ShipHull(),
                    AsFrame = new(FrameBounds.Sphere(10)),
                }
            )
        );
        world.Tick();
        faults.Jolt.HasGhostIn(ship.Entity, hangar).Should().BeTrue();
        return (space, hangar, ship);
    }

    /// <summary>
    /// Step 4 of the plan: a tick would change the state under test. The ghost pass at the end of a step adds the ghosts
    /// that the tree wants and takes away the ghosts that nothing wants, so after a tick the ghost would be gone even if the
    /// reconcile had left it in place. So the test calls the reconcile alone (see <see cref="Reconcile"/>) and reads the
    /// backend before the next step. It then ticks and reads again, to show that the step does not make the ghost again.
    /// </summary>
    [Fact]
    public void AHullHasNoGhostInAFrameThatTheGameMovedInsideItsOwnFrame()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        (_, FrameHandle hangar, FrameHandle ship) = ShipBesideHangar(world, faults);

        // Between ticks, with no change batch, the game moves the hangar into the ship.
        world.Frames.Reparent(hangar, ship);
        world.Frames.Update();
        Reconcile(faults, world);

        faults
            .Jolt.HasGhostIn(ship.Entity, hangar)
            .Should()
            .BeFalse("the hangar's contents are inside the ship and never meet its hull");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        faults.Jolt.HasGhostIn(ship.Entity, hangar).Should().BeFalse("the next step does not make the ghost again");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Step 4 of the plan: a tick would change the state under test, for the reason given at
    /// <see cref="AHullHasNoGhostInAFrameThatTheGameMovedInsideItsOwnFrame"/>. So the test calls the reconcile alone and
    /// reads the backend before the next step, and then ticks and reads again.
    /// </summary>
    [Fact]
    public void ABodyHasNoGhostInAFrameThatTheGameMovedAwayFromItsFrame()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle bubble = EmptyFrame(world: world, parent: space, pose: At(50), radius: 20);
        FrameHandle station = EmptyFrame(world: world, parent: space, pose: At(-500), radius: 200);
        // The ball sticks out of the bubble, so space holds a ghost of it.
        Entity ball = world.Spawn(
            new()
            {
                Frame = bubble,
                Pose = At(19.8),
                Body = new(
                    Shape: new SphereShapeDesc(0.5f),
                    Mass: 1,
                    Restitution: 0,
                    LinearDamping: 0,
                    AngularDamping: 0
                ),
            }
        );
        world.Tick();
        faults.Jolt.HasGhostIn(ball, space).Should().BeTrue();

        // Between ticks the game moves the bubble into the station. Space is now the bubble's grandparent.
        world.Frames.Reparent(bubble, station);
        world.Frames.Update();
        Reconcile(faults, world);

        faults.Jolt.HasGhostIn(ball, space).Should().BeFalse();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        faults.Jolt.HasGhostIn(ball, space).Should().BeFalse("the next step does not make the ghost again");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Step 4 of the plan: a tick would change the state under test. The ghost pass at the end of a step makes a missing
    /// ghost again, so after a tick the ghost would be there even if the reconcile had dropped it, and the step would
    /// have run without it. So the test calls the reconcile alone (see <see cref="Reconcile"/>) and reads the backend
    /// before the next step. It then ticks and reads again.
    /// </summary>
    [Fact]
    public void AGhostThatTheTreeStillAllowsSurvivesTheReconcile()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        (FrameHandle space, FrameHandle hangar, FrameHandle ship) = ShipBesideHangar(world, faults);
        FrameHandle buoy = EmptyFrame(world: world, parent: space, pose: At(-5000), radius: 5);
        FrameHandle depot = EmptyFrame(world: world, parent: space, pose: At(5000), radius: 50);
        world.Tick();

        // A change far away makes the backend reconcile, but the hangar is still the ship's frame's sibling.
        world.Frames.Reparent(buoy, depot);
        world.Frames.Update();
        Reconcile(faults, world);

        faults
            .Jolt.HasGhostIn(ship.Entity, hangar)
            .Should()
            .BeTrue("without the ghost, the hangar's contents would pass through the hull for a step");
        world.Tick();
        faults.Jolt.HasGhostIn(ship.Entity, hangar).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }
}
