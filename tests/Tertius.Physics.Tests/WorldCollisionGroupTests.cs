using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Collision groups on the real World and Jolt. A body is in a set of collision groups and has a collision mask, and two
/// bodies collide only when each body's mask matches the other body's groups. Every Jolt body of an entity carries the
/// entity's groups and mask: its own body, the inner body and the movement of a character, the interior of a frame that
/// it owns, and its ghosts. A frame's hull is the exception: it meets static bodies and other hulls, whatever their groups.
/// </summary>
public class WorldCollisionGroupTests
{
    private static readonly CollisionMask InAnySolid = CollisionMask.Empty.InAny(CollisionGroup.Solid);
    private readonly CollisionGroupRegistry registry = new();

    private static Entity Spawn(World world, FrameHandle frame, DPose pose, BodyDesc body,
        params IComponent[] components) =>
        world.Spawn(new() { Frame = frame, Pose = pose, Body = body, Components = components });

    private static double Height(World world, Entity entity, FrameHandle frame) =>
        world.PoseIn(entity, frame).Position.Y;

    private static BodyDesc StaticBox(Vector3 halfExtents) =>
        new(Shape: new BoxShapeDesc(halfExtents), Motion: BodyMotion.Static);

    [Fact]
    public void TwoGroupsThatDoNotNameEachOtherPassThroughEachOtherWhileAThirdGroupHitsBoth()
    {
        CollisionGroup debris = registry.Register("Debris"),
            people = registry.Register("People"),
            wall = registry.Register("Wall");
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Spawn(world: world, frame: ground, pose: At(0, -0.5), body: StaticBox(new(50, 0.5f, 50)) with
        {
            CollisionGroups = CollisionGroups.Of(wall),
            CollisionMask = CollisionMask.Empty.InAny(debris).InAny(people)
        });
        var personLog = new CollisionLog();
        Entity person = Spawn(world: world, frame: ground, pose: At(0, 0.5), body: Cube() with
        {
            CollisionGroups = CollisionGroups.Of(people),
            CollisionMask = CollisionMask.Empty.InAny(wall)
        }, personLog);
        // The debris falls from right above the person.
        var debrisLog = new CollisionLog();
        Entity rock = Spawn(world: world, frame: ground, pose: At(0, 3), body: Ball(0.4f) with
        {
            CollisionGroups = CollisionGroups.Of(debris),
            CollisionMask = CollisionMask.Empty.InAny(wall)
        }, debrisLog);

        Tick(world, 120);

        Height(world, rock, ground).Should()
            .BeApproximately(0.4, 0.05, "the rock fell through the person onto the floor");
        Height(world, person, ground).Should().BeApproximately(0.5, 0.05);
        debrisLog.Seen.Select(seen => seen.Other).Should().OnlyContain(other => other == floor).And.NotBeEmpty();
        personLog.Seen.Select(seen => seen.Other).Should().OnlyContain(other => other == floor).And.NotBeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABodyPassesThroughABodyThatOnlyOneOfTheTwoMasksNames(bool theBallNamesTheFloor)
    {
        CollisionGroup debris = registry.Register("Debris"), wall = registry.Register("Wall");
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Spawn(world: world, frame: ground, pose: At(0, -0.5), body: StaticBox(new(50, 0.5f, 50)) with
        {
            CollisionGroups = CollisionGroups.Of(wall),
            CollisionMask = theBallNamesTheFloor ? CollisionMask.Empty.InAny(wall) : CollisionMask.Empty.InAny(debris)
        });
        var log = new CollisionLog();
        Entity ball = Spawn(world: world, frame: ground, pose: At(0, 1), body: Ball() with
        {
            CollisionGroups = CollisionGroups.Of(debris),
            CollisionMask = theBallNamesTheFloor ? CollisionMask.Empty.InAny(wall) : CollisionMask.Empty.InAny(debris)
        }, log);

        Tick(world, 60);

        Height(world, ball, ground).Should().BeLessThan(-2, "each body's mask must match the other body's groups");
        log.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ABodyThatChangesItsGroupsByAnIntentFallsThroughWhatItRestedOn()
    {
        CollisionGroup debris = registry.Register("Debris"), glass = registry.Register("Glass");
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        // The floor meets debris and glass, and the shelf only debris. The ball's mask names neither of its groups, so
        // no body near it in its own group can wake it: only the change itself can.
        Spawn(world: world, frame: ground, pose: At(0, -0.5),
            body: StaticBox(new(50, 0.5f, 50)) with { CollisionMask = InAnySolid.InAny(debris).InAny(glass) });
        Spawn(world: world, frame: ground, pose: At(0, 2),
            body: StaticBox(new(1, 0.25f, 1)) with { CollisionMask = InAnySolid.InAny(debris) });
        var pilot = new Pilot();
        Entity ball = Spawn(world: world, frame: ground, pose: At(0, 2.75),
            body: Ball() with { CollisionGroups = CollisionGroups.Of(debris) },
            pilot);
        Tick(world, 120);
        Height(world, ball, ground).Should().BeApproximately(2.75, 0.02, "the ball rests on the shelf");
        IsAwake(backend, ground, ball).Should().BeFalse("the ball fell asleep on the shelf");

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(glass), InAnySolid);
        Tick(world, 120);

        Height(world, ball, ground).Should()
            .BeApproximately(0.5, 0.02, "the ball fell through the shelf onto the floor");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WhatRestedOnAStaticBodyFallsWhenTheStaticBodyChangesItsGroups()
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Spawn(world: world, frame: ground, pose: At(0, 2), body: StaticBox(new(1, 0.25f, 1)), pilot);
        Entity ball = Spawn(world: world, frame: ground, pose: At(0, 2.75), body: Ball());
        Tick(world, 120);
        IsAwake(backend, ground, ball).Should().BeFalse("the ball fell asleep on the shelf");

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(glass), CollisionMask.Empty.InAny(glass));
        Tick(world, 120);

        Height(world, ball, ground).Should()
            .BeApproximately(0.5, 0.02, "the ball fell through the shelf onto the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A block of glass overlaps a sleeping ball on the floor and does not meet it. When the glass joins Solid, it meets
    /// the ball, which wakes and is pushed out of it.
    /// </summary>
    [Fact]
    public void ASleepingBodyWakesWhenAStaticBodyThatItOverlapsStartsToMeetIt()
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Spawn(world: world, frame: ground, pose: At(0.6, 0.5), body: StaticBox(new(0.5f)) with
        {
            CollisionGroups = CollisionGroups.Of(glass),
            CollisionMask = CollisionMask.Empty.InAny(glass)
        }, pilot);
        Entity ball = Spawn(world: world, frame: ground, pose: At(0, 0.5), body: Ball());
        Tick(world, 120);
        IsAwake(backend, ground, ball).Should().BeFalse("the ball fell asleep on the floor, inside the glass");

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(CollisionGroup.Solid), InAnySolid);
        Tick(world, 120);

        world.PoseIn(ball, ground).Position.X.Should().BeLessThan(-0.3, "the glass pushed the ball out of it");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A kinematic block in a small bubble sticks 1 m out of it, so the parent frame holds a ghost of the block. A ball in
    /// the parent flies at the part that sticks out. It hits the ghost only when the ghost has the block's groups.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGhostCarriesTheGroupsOfItsBody(bool theBlockIsSolid)
    {
        CollisionGroup phantom = registry.Register("Phantom");
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity owner = world.Spawn(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(10)) });
        FrameHandle bubble = world.GetOwnedFrame(owner);
        var block = new BodyDesc(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic);
        Entity blockEntity = Spawn(world: world, frame: bubble, pose: At(10), body: theBlockIsSolid
            ? block
            : block with
            {
                CollisionGroups = CollisionGroups.Of(phantom), CollisionMask = CollisionMask.Empty.InAny(phantom)
            });
        Entity ball = world.Spawn(new()
            { Frame = space, Pose = At(16), LinearVelocity = new(-5, 0, 0), Body = Ball(0.25f) });
        world.Tick();
        backend.HasGhostIn(blockEntity, space).Should().BeTrue();

        Tick(world, 90);

        if (theBlockIsSolid)
        {
            world.PoseIn(ball, space).Position.X.Should()
                .BeGreaterThan(11.25 - 0.05, "the ball must not go through the ghost");
            world.VelocityIn(ball, space).X.Should().BeGreaterThan(-0.5);
        }
        else
        {
            world.PoseIn(ball, space).Position.X.Should().BeLessThan(10, "the ball passed through the ghost");
            world.VelocityIn(ball, space).X.Should().BeApproximately(-5, 0.01);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>The scene of <see cref="AGhostCarriesTheGroupsOfItsBody"/>, but the block leaves Solid by an intent after its ghost exists.</summary>
    [Fact]
    public void AGhostTakesTheGroupsThatItsBodyChangesTo()
    {
        CollisionGroup phantom = registry.Register("Phantom");
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity owner = world.Spawn(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(10)) });
        FrameHandle bubble = world.GetOwnedFrame(owner);
        var pilot = new Pilot();
        Entity block = Spawn(world: world, frame: bubble, pose: At(10),
            body: new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic), pilot);
        Entity ball = world.Spawn(new()
            { Frame = space, Pose = At(16), LinearVelocity = new(-5, 0, 0), Body = Ball(0.25f) });
        world.Tick();
        backend.HasGhostIn(block, space).Should().BeTrue();

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(phantom), CollisionMask.Empty.InAny(phantom));
        Tick(world, 90);

        world.PoseIn(ball, space).Position.X.Should().BeLessThan(10, "the ball passed through the ghost");
        world.VelocityIn(ball, space).X.Should().BeApproximately(-5, 0.01);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ball hangs in a bubble without a field of its own, 8 m above a floor in the ground frame that meets only Solid. The
    /// bubble holds still in the ground's gravity, so the ball starts to fall in it (see "The proper acceleration of a
    /// frame" in docs/plans/rails-components.md). An intent moves the ball from Glass into Solid, or from Solid into Glass,
    /// each with a mask that names only that group. Then the bubble dies, so the ball moves into the ground frame, where
    /// physics creates its body again, and falls on. It meets
    /// the floor by the groups and mask of the intent, not by those of its spawn.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABodyKeepsTheGroupsThatAnIntentGaveItWhenItsFrameDies(bool theBallJoinsSolid)
    {
        CollisionGroup glass = registry.Register("Glass");
        (CollisionGroups Groups, CollisionMask Mask) solid = (CollisionGroups.Of(CollisionGroup.Solid), InAnySolid);
        (CollisionGroups Groups, CollisionMask Mask) glassy = (CollisionGroups.Of(glass),
            CollisionMask.Empty.InAny(glass));
        ((CollisionGroups Groups, CollisionMask Mask) spawned, (CollisionGroups Groups, CollisionMask Mask) changed) =
            theBallJoinsSolid ? (glassy, solid) : (solid, glassy);
        using World world = NewWorld(backend: out _);
        var pilot = new Pilot();
        (FrameHandle ground, Entity owner, Entity ball) =
            BallInABubble(world: world, groups: spawned.Groups, mask: spawned.Mask, pilot: pilot);

        pilot.Once = body => body.SetCollision(changed.Groups, changed.Mask);
        Tick(world, 3);
        FactsOf(world, ball).CollisionGroups.Should().Be(changed.Groups, "the step after the intent applied it");
        Height(world, ball, ground).Should()
            .BeApproximately(8, 0.05, "the ball has only started to fall in the bubble");

        world.Destroy(owner).Should().BeTrue();
        world.Tick();
        world.GetFrame(ball).Should().Be(ground);
        BodyFacts moved = FactsOf(world, ball);
        Tick(world, 150);

        moved.CollisionGroups.Should().Be(changed.Groups);
        moved.CollisionMask.Should().Be(changed.Mask);
        if (theBallJoinsSolid)
            Height(world, ball, ground).Should().BeApproximately(0.5, 0.02, "the ball, now Solid, rests on the floor");
        else
            Height(world, ball, ground).Should().BeLessThan(-2, "the ball, now Glass, fell through the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of <see cref="ABodyKeepsTheGroupsThatAnIntentGaveItWhenItsFrameDies"/>, but the bubble's owner destroys
    /// itself during a tick, so the ball moves at the sync point, while the snapshot does not show the intent yet. With no
    /// tick between the intent and the death, physics creates the ball's body again before its step applies the intent.
    /// With one tick, the step has applied the intent, and its groups land in the sync point where the bubble dies.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ABodyKeepsTheGroupsOfAnIntentThatIsInFlightWhenItsFrameDies(int ticksFromIntentToDeath)
    {
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(backend: out _);
        var pilot = new Pilot();
        var doom = new DestroySelf();
        (FrameHandle ground, _, Entity ball) =
            BallInABubble(world: world, groups: CollisionGroups.Of(CollisionGroup.Solid), mask: InAnySolid,
                pilot: pilot, doom);

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(glass), CollisionMask.Empty.InAny(glass));
        Tick(world, ticksFromIntentToDeath);
        doom.Now = true;
        world.Tick();
        world.GetFrame(ball).Should().Be(ground);
        Tick(world, 150);

        FactsOf(world, ball).CollisionGroups.Should().Be(CollisionGroups.Of(glass));
        Height(world, ball, ground).Should().BeLessThan(-2, "the ball, now Glass, fell through the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship floats in a carrier's frame, and a deck is its interior. An intent gives the ship's owner a mask that names
    /// only Hull, so the deck stays in Solid but no longer meets Solid bodies. Then the carrier dies, so the ship's frame
    /// moves into the ground frame, and physics creates the owner's hull and interior again. A Solid ball dropped onto the
    /// deck afterwards falls through it.
    /// </summary>
    [Fact]
    public void AFrameOwnerKeepsTheMaskThatAnIntentGaveItWhenItsParentFrameDies()
    {
        CollisionGroup hull = registry.Register("Hull");
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Entity carrier = world.Spawn(new()
            { Frame = ground, Pose = At(0, 50), AsFrame = new(FrameBounds.Sphere(200)) });
        var pilot = new Pilot();
        Entity ship = world.Spawn(new()
        {
            Frame = world.GetOwnedFrame(carrier),
            Body = StaticBox(new(5, 0.5f, 5)),
            AsFrame = new(FrameBounds.Sphere(100), GravityField.Uniform(new(0, -9.81f, 0))),
            Components = [pilot]
        });
        FrameHandle shipFrame = world.GetOwnedFrame(ship);
        world.Tick();

        pilot.Once = body =>
            body.SetCollision(CollisionGroups.Of(CollisionGroup.Solid), CollisionMask.Empty.InAny(hull));
        Tick(world, 3);
        world.Destroy(carrier).Should().BeTrue();
        world.Tick();
        world.Frames.Parent(shipFrame).Should().Be(ground);
        FactsOf(world, ship).CollisionMask.Should().Be(CollisionMask.Empty.InAny(hull));

        Entity ball = Spawn(world: world, frame: shipFrame, pose: At(0, 2), body: Ball());
        Tick(world, 90);

        Height(world, ball, shipFrame).Should().BeLessThan(-2, "the ball fell through the deck");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of the tests of a frame's death: a ground frame with gravity and a floor that meets only Solid, a bubble
    /// without gravity whose center floats 8 m above the floor, and a ball at rest at that center. One tick has run.
    /// </summary>
    private static (FrameHandle Ground, Entity Owner, Entity Ball) BallInABubble(World world, CollisionGroups groups,
        CollisionMask mask,
        Pilot pilot, params IComponent[] ownerComponents)
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity owner = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, 8),
            AsFrame = new(FrameBounds.Sphere(4)),
            Components = ownerComponents
        });
        Entity ball = Spawn(world: world, frame: world.GetOwnedFrame(owner), pose: At(0),
            body: Ball() with { CollisionGroups = groups, CollisionMask = mask }, pilot);
        world.Tick();
        return (ground, owner, ball);
    }

    private static BodyFacts FactsOf(World world, Entity entity)
    {
        world.Snapshot.TryGetBody(entity, out BodyFacts facts).Should().BeTrue($"{entity} has a body");
        return facts;
    }

    /// <summary>Destroys its own entity on every tick after the test sets <see cref="Now"/>.</summary>
    private sealed class DestroySelf : IComponent
    {
        public bool Now { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (Now)
                self.Destroy();
        }
    }

    /// <summary>The owner of a root frame has no hull. Its body is the frame's static interior: here a deck whose top is at 0.5 m.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFramesInteriorCarriesTheGroupsOfItsOwner(bool theOwnerIsSolid)
    {
        CollisionGroup hull = registry.Register("Hull");
        using World world = NewWorld(backend: out _);
        BodyDesc deck = StaticBox(new(5, 0.5f, 5));
        Entity owner = world.Spawn(new()
        {
            Body = theOwnerIsSolid
                ? deck
                : deck with
                {
                    CollisionGroups = CollisionGroups.Of(hull), CollisionMask = CollisionMask.Empty.InAny(hull)
                },
            AsFrame = new(FrameBounds.Sphere(100), GravityField.Uniform(new(0, -9.81f, 0)))
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Entity ball = Spawn(world: world, frame: ship, pose: At(0, 2), body: Ball());

        Tick(world, 90);

        if (theOwnerIsSolid)
            Height(world, ball, ship).Should().BeApproximately(1, 0.02, "the ball rests on the deck");
        else
            Height(world, ball, ship).Should().BeLessThan(-2, "the ball fell through the deck");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheOwnerOfARootFrameChangesTheGroupsOfItsInteriorByAnIntent()
    {
        CollisionGroup hull = registry.Register("Hull");
        using World world = NewWorld(backend: out _);
        var pilot = new Pilot();
        Entity owner = world.Spawn(new()
        {
            Body = StaticBox(new(5, 0.5f, 5)),
            AsFrame = new(FrameBounds.Sphere(100), GravityField.Uniform(new(0, -9.81f, 0))),
            Components = [pilot]
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Entity ball = Spawn(world: world, frame: ship, pose: At(0, 1), body: Ball());
        Tick(world, 30);
        Height(world, ball, ship).Should().BeApproximately(1, 0.02, "the ball rests on the deck");

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(hull), CollisionMask.Empty.InAny(hull));
        Tick(world, 90);

        Height(world, ball, ship).Should().BeLessThan(-2, "the ball fell through the deck");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACharacterMovesWithItsOwnGroups(bool theCharacterIsSolid)
    {
        CollisionGroup phantom = registry.Register("Phantom");
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var walker = new BodyDesc(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70);
        Entity character = Spawn(world: world, frame: ground, pose: At(0, 1), body: theCharacterIsSolid
            ? walker
            : walker with
            {
                CollisionGroups = CollisionGroups.Of(phantom), CollisionMask = CollisionMask.Empty.InAny(phantom)
            });

        Tick(world, 60);

        if (theCharacterIsSolid)
            Height(world, character, ground).Should().BeApproximately(0.9, 0.05, "the character stands on the floor");
        else
            Height(world, character, ground).Should().BeLessThan(-2, "the character fell through the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character stands on a floor that meets Solid and Phantom. It then moves from Solid to Phantom by an intent and
    /// keeps standing, because its mask still names Solid. A Solid ball that falls onto it afterwards passes through its
    /// inner body, because the ball's mask does not name Phantom.
    /// </summary>
    [Fact]
    public void ACharacterThatChangesItsGroupsByAnIntentLetsABodyThrough()
    {
        CollisionGroup phantom = registry.Register("Phantom");
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Spawn(world: world, frame: ground, pose: At(0, -0.5),
            body: StaticBox(new(50, 0.5f, 50)) with { CollisionMask = InAnySolid.InAny(phantom) });
        var pilot = new Pilot();
        Entity character = Spawn(world: world, frame: ground, pose: At(0, 0.9),
            body: new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70), pilot);
        Tick(world, 30);

        pilot.Once = body => body.SetCollision(CollisionGroups.Of(phantom), InAnySolid);
        Tick(world, 2);
        Entity ball = Spawn(world: world, frame: ground, pose: At(0, 3), body: Ball(0.2f));
        Tick(world, 90);

        DPose pose = world.PoseIn(ball, ground);
        pose.Position.Y.Should().BeApproximately(0.2, 0.02, "the ball fell through the character onto the floor");
        pose.Position.X.Should().BeApproximately(0, 0.01);
        Height(world, character, ground).Should().BeApproximately(0.9, 0.05, "the character still stands on the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A character's inner body and its movement always had the Moving layer, whatever its description named.</summary>
    [Theory]
    [InlineData(BodyLayer.Static)]
    [InlineData(BodyLayer.FrameHull)]
    [InlineData(BodyLayer.Ghost)]
    public void ACharacterIsOnTheLayerOfAMovingBodyWhateverLayerItsDescriptionNames(BodyLayer layer)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Entity character = Spawn(world: world, frame: ground, pose: At(0, 5),
            body: new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Layer: layer, Mass: 70));
        Entity ball = Spawn(world: world, frame: ground, pose: At(5, 5), body: Ball());
        world.Tick();

        List<JoltPhysicsBackend.BodyFacts> bodies = backend.BodiesIn(ground);
        JoltPhysicsBackend.BodyFacts inner =
            bodies.Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.CharacterInner);
        JoltPhysicsBackend.BodyFacts moving = bodies.Single(facts => BodyUserData.Names(facts.UserData, ball));

        BodyUserData.Names(inner.UserData, character).Should().BeTrue();
        inner.Layer.Should().Be(moving.Layer);
    }

    [Fact]
    public void ABodyPassesThroughACharacterWhoseGroupsItsMaskDoesNotName()
    {
        CollisionGroup phantom = registry.Register("Phantom");
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        // The floor meets both, and the character stands on it.
        Spawn(world: world, frame: ground, pose: At(0, -0.5),
            body: StaticBox(new(50, 0.5f, 50)) with { CollisionMask = InAnySolid.InAny(phantom) });
        Spawn(world: world, frame: ground, pose: At(0, 0.9),
            body: new BodyDesc(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70) with
            {
                CollisionGroups = CollisionGroups.Of(phantom),
                CollisionMask = InAnySolid
            });
        Entity ball = Spawn(world: world, frame: ground, pose: At(0, 3), body: Ball(0.2f));

        Tick(world, 90);

        DPose pose = world.PoseIn(ball, ground);
        pose.Position.Y.Should().BeApproximately(0.2, 0.02, "the ball fell through the character onto the floor");
        pose.Position.X.Should().BeApproximately(0, 0.01);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship's frame with a bubble of 1 m, so a ball 3 m from its origin stays in the parent frame. The ship's hull, a
    /// slab whose top is at 0.5 m, is in the parent frame too. The ball's mask has no condition, so it matches every
    /// group, but a hull meets only static bodies and other hulls.
    /// </summary>
    [Fact]
    public void AFrameHullPassesThroughABodyWhoseMaskHasNoCondition()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        world.Spawn(new()
        {
            Frame = ground,
            Body = StaticBox(new(5, 0.5f, 5)),
            AsFrame = new(FrameBounds.Sphere(1))
        });
        Entity ball = Spawn(world: world, frame: ground, pose: At(3, 2),
            body: Ball() with { CollisionMask = CollisionMask.Empty });

        Tick(world, 60);

        Height(world, ball, ground).Should().BeLessThan(-2, "the ball fell through the hull");
        world.Errors.Should().BeEmpty();
    }
}
