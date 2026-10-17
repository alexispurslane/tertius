using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The contacts that Jolt reports when two bodies start to touch, and the Collision events that the World makes of them,
/// on the World and the real backend. A test reads the contacts that Jolt really sent from <see cref="FaultInjectingBackend.Outputs"/>.
/// </summary>
public class ContactTests
{
    /// <summary>A body of a box with sides of 2 · <paramref name="half"/> m that nothing slows down.</summary>
    private static BodyDesc BoxBody(float half, float mass = 1f) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: mass, LinearDamping: 0, AngularDamping: 0);

    private static List<ContactStarted> ContactsSent(FaultInjectingBackend faults) => faults.Outputs.SelectMany(output => output.Contacts).ToList();

    [Fact]
    public void ContactIsReportedOnceWhenItStartsWithEntitiesMapped()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 0.5f, 10)), BodyMotion.Static),
            Components = [floorLog],
        });
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.7), Body = BoxBody(0.5f, mass: 2), Components = [boxLog] });

        Tick(world, 120);

        ContactStarted contact = ContactsSent(faults).Should().ContainSingle().Which;
        // Canonical order: the lower entity first. The normal points from A to B.
        contact.A.Should().Be(floor);
        contact.B.Should().Be(box);
        contact.Frame.Should().Be(ground);
        Vector3.Distance(contact.Normal, Vector3.UnitY).Should().BeLessThan(1e-3f);
        contact.Point.Y.Should().BeInRange(-0.05, 0.05);
        Math.Abs(contact.Point.X).Should().BeLessThan(0.6);
        Math.Abs(contact.Point.Z).Should().BeLessThan(0.6);
        // It fell 0.2 m: about 2 m/s, times the reduced mass (2 kg against a static floor).
        contact.Impulse.Should().BeInRange(3f, 4.5f);

        // The World made one Collision of the contact for each of the two entities, with the entity of the other body.
        var heardByBox = boxLog.Seen.Should().ContainSingle().Which;
        heardByBox.Other.Should().Be(floor);
        heardByBox.Impulse.Should().Be(contact.Impulse);
        var heardByFloor = floorLog.Seen.Should().ContainSingle().Which;
        heardByFloor.Other.Should().Be(box);
        heardByFloor.Impulse.Should().Be(contact.Impulse);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ContactWithFrameInteriorNamesTheFrameOwner()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 0.25f, 5)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        var shipLog = new CollisionLog();
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = DPose.Identity,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 5)), InteriorShape: deck),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
            Components = [shipLog],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, 0, 0), Body = BoxBody(0.5f), Components = [crateLog] });

        Tick(world, 90);

        ContactStarted contact = ContactsSent(faults).Should().ContainSingle().Which;
        contact.Frame.Should().Be(ship);
        contact.A.Should().Be(shipOwner);
        contact.B.Should().Be(crate);
        contact.Point.Y.Should().BeInRange(-2.05, -1.95);
        // The World made one Collision of the contact for each of the two entities, with the entity of the other body.
        crateLog.Seen.Should().ContainSingle().Which.Other.Should().Be(shipOwner);
        shipLog.Seen.Should().ContainSingle().Which.Other.Should().Be(crate);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SeparateContactsAreEachReportedAndInDeterministicOrder()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 0.5f, 10)), BodyMotion.Static),
        });
        var boxes = new List<Entity>();
        for (int i = 0; i < 5; i++)
            boxes.Add(world.Spawn(new SpawnDesc { Frame = ground, Pose = At(i * 2 - 4, 0.5), Body = BoxBody(0.25f) }));

        Tick(world, 60);

        List<ContactStarted> contacts = ContactsSent(faults);
        contacts.Should().HaveCount(5).And.OnlyContain(c => c.A == floor);
        contacts.Select(c => c.B).Should().Equal(boxes);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The body of a frame's owner is destroyed while a crate rests on the owner's interior, and nothing else changes. The
    /// interior goes with the body, which ends its contact with the crate.
    /// </summary>
    [Fact]
    public void DestroyingTheBodyOfAFrameOwnerEndsTheContactsOfItsInterior()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 0.25f, 5)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = DPose.Identity,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 5)), InteriorShape: deck),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
        });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = world.GetOwnedFrame(shipOwner), Pose = At(0, -1.5), Body = BoxBody(0.5f), Components = [crateLog] });
        Tick(world, 30);
        ContactsSent(faults).Should().ContainSingle();
        faults.Outputs.SelectMany(output => output.ContactsEnded).Should().BeEmpty("nothing has changed yet");

        world.Destroy(shipOwner);
        world.Tick();

        faults.Outputs.SelectMany(output => output.ContactsEnded).Should().Equal(new ContactEnded(shipOwner, crate));
        Tick(world, 2);
        crateLog.Ended.Should().ContainSingle().Which.Other.Should().Be(shipOwner);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The box's entity comes before the floor's, but its body comes after the floor's body, so Jolt names the floor first.
    /// The end of their contact still names the box first.
    /// </summary>
    [Fact]
    public void AnEndedContactNamesItsEntitiesInEntityOrder()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        // The World gives a new entity the slot that was freed first. Freeing the higher slot first makes the floor, which
        // is spawned first, take the higher slot, and the box, which is spawned next, take the lower one.
        Entity lowerSlot = world.Spawn(new SpawnDesc { Frame = ground });
        Entity higherSlot = world.Spawn(new SpawnDesc { Frame = ground });
        world.Destroy(higherSlot);
        world.Destroy(lowerSlot);
        var boxLog = new CollisionLog();
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 0.5f, 10)), BodyMotion.Static),
        });
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = BoxBody(0.5f), Components = [boxLog] });
        box.Should().BeLessThan(floor, "the box has the lower entity");
        Tick(world, 10);
        faults.AppliedChanges.SelectMany(changes => changes.BodiesCreated).Select(create => create.Entity).Should().Equal([floor, box], "the floor's body is the first one created");
        ContactsSent(faults).Should().ContainSingle();

        world.Destroy(floor);
        world.Tick();

        faults.Outputs.SelectMany(output => output.ContactsEnded).Should().Equal(new ContactEnded(box, floor));
        world.Tick();
        boxLog.Ended.Should().ContainSingle().Which.Other.Should().Be(floor);
        world.Errors.Should().BeEmpty();
    }
}
