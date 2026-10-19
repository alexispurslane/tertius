using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An entity hears one <see cref="FrameChanged"/> for each change of its frame, the semantic frame that
/// <c>Self.Frame</c> shows, whatever changed it: a hand-off of physics into a frame or out of it, the death of the frame
/// that the entity was in, which moves it to the nearest ancestor that survives, and for the owner of a frame, a move of
/// that frame under another parent that the game makes itself. A new entity hears none for the frame that it spawns
/// into. Each rule holds for a plain body, whose row physics writes, and for the owner of a frame, whose body is the
/// frame's hull and whose row the sync point fills from the frame tree. The world runs on real Jolt.
/// </summary>
public class FrameChangedTests
{
    /// <summary>The two kinds of entity whose frame can change.</summary>
    public enum Mover
    {
        /// <summary>An entity with a ball of radius 0.5 m as its body, whose row physics writes.</summary>
        PlainBody,

        /// <summary>
        /// The owner of a frame whose bubble is a sphere of 10 m. Its body is the frame's hull, a dynamic sphere of 5 m in the
        /// frame's parent, and its row is filled from the frame tree.
        /// </summary>
        FrameOwner,
    }

    /// <summary>Spawns a <paramref name="mover"/> in <paramref name="frame"/> at <paramref name="pose"/>, at rest, with <paramref name="log"/>.</summary>
    private static Entity Spawn(TestWorld test, Mover mover, FrameHandle frame, DPose pose, Recorder log) => test.World.Spawn(mover switch
    {
        Mover.PlainBody => new SpawnDesc { Frame = frame, Pose = pose, Body = WorldTesting.Ball(), Components = [log] },
        _ => new SpawnDesc
        {
            Frame = frame,
            Pose = pose,
            Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = [log],
        },
    });

    private static Recorder NewLog() => new(EventMask.Of<FrameChanged>());

    // The mover starts in the root, 100 m inside the bubble of a carrier of 500 m, so Jolt hands it into the carrier in
    // the step of tick 0. The sync point of tick 0 routes the FrameChanged, and the mover handles it in tick 1.
    [Theory]
    [InlineData(Mover.PlainBody)]
    [InlineData(Mover.FrameOwner)]
    public void AnEntityThatPhysicsHandsIntoAFrameHearsOneFrameChangedFromItsOldFrameToTheNewOne(Mover mover)
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Recorder log = NewLog();
        Entity entity = Spawn(test, mover, test.Root, TestWorld.At(1100), log);

        test.Run(4);

        test.World.GetFrame(entity).Should().Be(carrier, "Jolt handed the mover into the carrier");
        (long tick, Event heard) = log.Seen.Should().ContainSingle().Subject;
        tick.Should().Be(1);
        heard.As<FrameChanged>().From.Should().Be(test.Root);
        heard.As<FrameChanged>().To.Should().Be(carrier);
    }

    // The mover starts in a carrier of 50 m, 200 m from its center: fully outside its bubble plus the margin, which is
    // the larger of 2% of the bubble's radius and the mover's radius. So Jolt hands it out into the root in the step of
    // tick 0.
    [Theory]
    [InlineData(Mover.PlainBody)]
    [InlineData(Mover.FrameOwner)]
    public void AnEntityThatPhysicsHandsOutOfAFrameHearsOneFrameChangedFromThatFrameToItsParent(Mover mover)
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 50);
        Recorder log = NewLog();
        Entity entity = Spawn(test, mover, carrier, TestWorld.At(200), log);

        test.Run(4);

        test.World.GetFrame(entity).Should().Be(test.Root, "Jolt handed the mover out of the carrier");
        (long tick, Event heard) = log.Seen.Should().ContainSingle().Subject;
        tick.Should().Be(1);
        heard.As<FrameChanged>().From.Should().Be(carrier);
        heard.As<FrameChanged>().To.Should().Be(test.Root);
    }

    // A Destroy that goes out in tick 0 reaches the carrier's owner in tick 1, and the destroy batch of that sync point
    // moves the mover into the root. The mover handles the FrameChanged in tick 2.
    [Theory]
    [InlineData(Mover.PlainBody)]
    [InlineData(Mover.FrameOwner)]
    public void AnEntityWhoseFrameDiesHearsOneFrameChangedFromThatFrameToTheAncestorThatSurvives(Mover mover)
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Recorder log = NewLog();
        Entity entity = Spawn(test, mover, carrier, TestWorld.At(100), log);
        test.SpawnWith(SendOnce.Of(carrier.Entity, new Destroy()));

        test.Run(4);

        test.World.Frames.IsAlive(carrier).Should().BeFalse();
        test.World.GetFrame(entity).Should().Be(test.Root);
        (long tick, Event heard) = log.Seen.Should().ContainSingle().Subject;
        tick.Should().Be(2);
        heard.As<FrameChanged>().From.Should().Be(carrier);
        heard.As<FrameChanged>().To.Should().Be(test.Root);
    }

    // A docking: after tick 0, the game moves the frame of the owner under a station whose bubble of 15 m is too small for
    // the frame's bubble of 10 m to enter on its own. The sync point of tick 1 fills the owner's row from the moved frame
    // and routes the FrameChanged, and the owner handles it in tick 2.
    [Fact]
    public void TheOwnerOfAFrameThatTheGameMovesUnderAnotherFrameHearsOneFrameChangedFromTheOldParentToTheNewOne()
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 15);
        Recorder log = NewLog();
        Entity owner = Spawn(test, Mover.FrameOwner, test.Root, TestWorld.At(505), log);
        test.Run(1);
        test.World.GetFrame(owner).Should().Be(test.Root, "the frame does not fit in the station, so physics hands nothing off");

        test.World.Frames.Reparent(test.World.GetOwnedFrame(owner), station);
        test.Run(3);

        test.World.GetFrame(owner).Should().Be(station);
        (long tick, Event heard) = log.Seen.Should().ContainSingle().Subject;
        tick.Should().Be(2);
        heard.As<FrameChanged>().From.Should().Be(test.Root);
        heard.As<FrameChanged>().To.Should().Be(station);
    }

    // The mover spawns 100 m inside the bubble of a carrier of 500 m, so no hand-off moves it.
    [Theory]
    [InlineData(Mover.PlainBody)]
    [InlineData(Mover.FrameOwner)]
    public void ANewEntityHearsNoFrameChangedForTheFrameThatItSpawnsInto(Mover mover)
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Recorder log = NewLog();
        Entity entity = Spawn(test, mover, carrier, TestWorld.At(100), log);

        test.Run(4);

        test.World.GetFrame(entity).Should().Be(carrier);
        log.Seen.Should().BeEmpty("joining the frame that it spawns into is no change of frame");
    }
}
