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

        ContactStarted contact = Assert.Single(h.Contacts);
        // Canonical order: the lower entity first. The normal points from A to B.
        Assert.Equal(floor, contact.A);
        Assert.Equal(box, contact.B);
        Assert.Equal(ground, contact.Frame);
        Assert.True(Vector3.Distance(contact.Normal, Vector3.UnitY) < 1e-3f, contact.Normal.ToString());
        Assert.InRange(contact.Point.Y, -0.05, 0.05);
        Assert.True(Math.Abs(contact.Point.X) < 0.6 && Math.Abs(contact.Point.Z) < 0.6);
        // It fell 0.2 m: about 2 m/s, times the reduced mass (2 kg against a static floor).
        Assert.InRange(contact.Impulse, 3f, 4.5f);
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

        ContactStarted contact = Assert.Single(h.Contacts);
        Assert.Equal(ship, contact.Frame);
        Assert.Equal(ship.Entity, contact.A);
        Assert.Equal(crate, contact.B);
        Assert.InRange(contact.Point.Y, -2.05, -1.95);
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

        Assert.Equal(5, h.Contacts.Count);
        Assert.All(h.Contacts, c => Assert.Equal(floor, c.A));
        Assert.Equal(boxes, h.Contacts.Select(c => c.B));
    }
}
