using System.Numerics;

namespace Tertius.Physics.Tests;

public class ContactTests
{
    [Fact]
    public void ContactIsReportedOnceWhenItStartsWithEntitiesMapped()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity floor = h.AddBody(ground, Shapes.Floor(10), Shapes.At(0, -0.5, 0));
        Entity box = h.AddBody(ground, Shapes.Box(0.5f, mass: 2), Shapes.At(0, 0.7, 0));

        h.Step(120);

        ContactStarted contact = h.Contacts.Should().ContainSingle().Which;
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
    }

    [Fact]
    public void ContactWithFrameInteriorNamesTheFrameOwner()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 0.25f, 5)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        FrameHandle ship = h.AddFrameWithBody(space, DPose.Identity, FrameBounds.Sphere(30), MotionDriver.Static,
            new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 5)), InteriorShape: deck), GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity crate = h.AddBody(ship, Shapes.Box(0.5f), Shapes.At(0, 0, 0));

        h.Step(90);

        ContactStarted contact = h.Contacts.Should().ContainSingle().Which;
        contact.Frame.Should().Be(ship);
        contact.A.Should().Be(ship.Entity);
        contact.B.Should().Be(crate);
        contact.Point.Y.Should().BeInRange(-2.05, -1.95);
    }

    [Fact]
    public void SeparateContactsAreEachReportedAndInDeterministicOrder()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity floor = h.AddBody(ground, Shapes.Floor(10), Shapes.At(0, -0.5, 0));
        var boxes = new List<Entity>();
        for (int i = 0; i < 5; i++)
            boxes.Add(h.AddBody(ground, Shapes.Box(0.25f), Shapes.At(i * 2 - 4, 0.5, 0)));

        h.Step(60);

        h.Contacts.Should().HaveCount(5).And.OnlyContain(c => c.A == floor);
        h.Contacts.Select(c => c.B).Should().Equal(boxes);
    }

    /// <summary>
    /// The body of a frame's owner is destroyed while a crate rests on the owner's interior, and nothing else changes. The
    /// interior goes with the body, which ends its contact with the crate.
    /// </summary>
    [Fact]
    public void DestroyingTheBodyOfAFrameOwnerEndsTheContactsOfItsInterior()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 0.25f, 5)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        FrameHandle ship = h.AddFrameWithBody(space, DPose.Identity, FrameBounds.Sphere(30), MotionDriver.Static,
            new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 5)), InteriorShape: deck), GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity crate = h.AddBody(ship, Shapes.Box(0.5f), Shapes.At(0, -1.5, 0));
        h.Step(30);
        h.Contacts.Should().ContainSingle();

        h.Destroy(ship.Entity);
        h.Step();

        h.ContactsEnded.Should().Equal(new ContactEnded(ship.Entity, crate));
    }

    /// <summary>
    /// The box's entity comes before the floor's, but its body comes after the floor's body, so Jolt names the floor first.
    /// The end of their contact still names the box first.
    /// </summary>
    [Fact]
    public void AnEndedContactNamesItsEntitiesInEntityOrder()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity box = h.NewEntity();
        Entity floor = h.AddBody(ground, Shapes.Floor(10), Shapes.At(0, -0.5, 0));
        h.AddBody(ground, Shapes.Box(0.5f), Shapes.At(0, 0.5, 0), entity: box);
        h.Step(10);
        h.Contacts.Should().ContainSingle();

        h.Destroy(floor);
        h.Step();

        h.ContactsEnded.Should().Equal(new ContactEnded(box, floor));
    }
}
