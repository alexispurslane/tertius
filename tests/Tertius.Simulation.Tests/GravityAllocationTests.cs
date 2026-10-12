using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The two hot paths that read a gravity field must not allocate: the effective gravity of a body, which physics asks for
/// once per awake body per step, and the render snapshot build, which reads the up of every instance. A gravity field and
/// a hemisphere up are unions, and a union that boxed its case on a pattern match would allocate on both paths. Each test
/// warms up first and then counts the bytes that the calling thread allocates.
/// </summary>
public class GravityAllocationTests
{
    private static readonly Vector3 Down = new(0, -9.81f, 0);
    private static readonly BoundingBox Cabin = new(new Vector3(-2), new Vector3(2));

    /// <summary>The fields to read, by the names that the tests use: one frame of each case and one without gravity.</summary>
    private static readonly (string Name, GravityField Field)[] Fields =
    [
        ("no gravity", GravityField.None),
        ("a uniform field", GravityField.Uniform(Down)),
        ("a uniform field in a region", GravityField.Uniform(Down, Cabin)),
        ("a point field", GravityField.Point(4e14)),
    ];

    private static FrameHandle FrameWith(TestWorld test, GravityField field, Double3 at) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(at, DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1000), MotionDriver.Static, field),
        }));

    [Fact]
    public void TheEffectiveGravityOfABodyAllocatesNothingWhateverTheFieldOfItsFrame()
    {
        using var test = new TestWorld(debugChecks: false);
        FrameHandle[] frames = Fields.Select((f, i) => FrameWith(test, f.Field, new Double3(i * 3000, 0, 0))).ToArray();
        var inside = new Double3(1, 1, 1);
        var outside = new Double3(500, 20, 0);
        for (int warm = 0; warm < 1000; warm++)
            foreach (FrameHandle frame in frames)
                test.World.Frames.EffectiveGravity(frame, warm % 2 == 0 ? inside : outside);

        Vector3 total = Vector3.Zero;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            foreach (FrameHandle frame in frames)
                total += test.World.Frames.EffectiveGravity(frame, i % 2 == 0 ? inside : outside);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0, "physics reads the effective gravity of every awake body at every step");
        total.Should().NotBe(Vector3.Zero, "the fields did pull, so the loop did read them");
    }

    [Fact]
    public void ASteadyStateTickWithEveryKindOfUpAllocatesNothing()
    {
        using var test = new TestWorld(debugChecks: false);
        for (int kind = 0; kind < Fields.Length; kind++)
        {
            FrameHandle frame = FrameWith(test, Fields[kind].Field, new Double3(kind * 3000, 0, 0));
            for (int i = 0; i < 50; i++)
                test.Show(frame, new DPose(new Double3(i % 5 - 2, i / 5 % 5 - 2, i / 25 - 1), DQuaternion.Identity));
        }
        test.World.Camera = test.SpawnCamera(test.Root, new DPose(new Double3(0, 0, 50), DQuaternion.Identity));
        test.Run(30);

        long before = GC.GetAllocatedBytesForCurrentThread();
        test.Run(100);
        long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        perTick.Should().Be(0, "the snapshot build reads the up of every instance");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheSnapshotOfTheAllocationWorldReallyCarriesEveryKindOfUp()
    {
        // Guards the test above against passing for the wrong reason: if no instance had an up, it would measure nothing.
        using var test = new TestWorld(debugChecks: false);
        var entities = new List<Entity>();
        foreach ((string _, GravityField field) in Fields)
            entities.Add(test.Show(FrameWith(test, field, new Double3(entities.Count * 3000, 0, 0)), new DPose(new Double3(1, 1, 1), DQuaternion.Identity)));
        test.World.Camera = test.SpawnCamera(test.Root, new DPose(new Double3(0, 0, 50), DQuaternion.Identity));
        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();

        snapshot.InstanceOf(entities[0]).Hemisphere.HasValue.Should().BeFalse("no gravity");
        snapshot.InstanceOf(entities[1]).Hemisphere.TryGetValue(out UpAlong _).Should().BeTrue("a uniform field");
        snapshot.InstanceOf(entities[2]).Hemisphere.TryGetValue(out UpAlong _).Should().BeTrue("a uniform field in a region, at a place inside it");
        snapshot.InstanceOf(entities[3]).Hemisphere.TryGetValue(out UpAwayFrom _).Should().BeTrue("a point field");
    }
}
