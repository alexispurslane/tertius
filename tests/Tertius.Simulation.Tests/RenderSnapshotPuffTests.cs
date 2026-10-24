using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.PoseChecks;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The puffs of a render snapshot (ter-4zt6, task 4). The builder places each range of a puff view through the pose of
/// its frame relative to the camera's physics home, less the render origin, as it places an instance. A puff's previous
/// position is its place one chord back, placed through the frame's pose of the previous build, and only when that build
/// was of the tick before. The tests make a builder over an entity table and a frame tree, as
/// <see cref="RenderSnapshotParallelBuildTests"/> does, and hand it views of rows that the test writes.
/// </summary>
public sealed class RenderSnapshotPuffTests
{
    /// <summary>The length of the step that the rows describe, in s.</summary>
    private const double Step = 1.0 / 60;

    private static readonly PuffLook HotLook = new(Life: 2, StartRadius: 0.5f, EndRadius: 2, TemperatureRatio: 3,
        Shading: PuffShading.Glow(new(4, 2, 1)));

    private static readonly PuffLook ColdLook = new(Life: 5, StartRadius: 1, EndRadius: 4, TemperatureRatio: 0.5f,
        Shading: PuffShading.Lit(new(0.5f)));

    /// <summary>
    /// An entity table and a frame tree with a root frame, and a builder over them with no workers. Every frame is a plain
    /// semantic frame with an owner entity. A frame that stands in for a numeric home is one with the identity rotation.
    /// </summary>
    private sealed class Harness
    {
        private readonly EntityTable entities;

        public Harness()
        {
            EngineEvents.Register();
            entities = new(chunkSize: 64, initialCapacity: 16);
            Frames = new();
            Root = AddFrame(FrameHandle.Null, DPose.Identity);
            Builder = new(entities, Frames);
        }

        public FrameTree Frames { get; }
        public FrameHandle Root { get; }
        public RenderSnapshotBuilder Builder { get; }

        public FrameHandle AddFrame(FrameHandle parent, DPose local)
        {
            Entity owner = entities.Reserve();
            FrameHandle frame = Frames.Create(frameOwner: owner, parent: parent, localPose: local,
                frameBounds: FrameBounds.Sphere(1e9f));
            entities.Activate(entity: owner, row: new() { Frame = parent, PhysicsHome = parent, Pose = local },
                render: new(), components: [], wants: [], body: null,
                bodyCenterOfMass: Vector3.Zero, ownedFrame: frame);
            Frames.Update();
            return frame;
        }

        /// <summary>A camera at <paramref name="pose"/> in <paramref name="frame"/>, which is also its physics home.</summary>
        public Entity AddCamera(FrameHandle frame, DPose pose) =>
            AddCamera(frame: frame, pose: pose, home: frame, homePosition: pose.Position);

        /// <summary>
        /// A camera at <paramref name="pose"/> in <paramref name="frame"/>, whose physics home is <paramref name="home"/>, a
        /// frame below it with the identity rotation, at <paramref name="homePosition"/> in that home.
        /// </summary>
        public Entity AddCamera(FrameHandle frame, DPose pose, FrameHandle home, Double3 homePosition)
        {
            Entity camera = entities.Reserve();
            entities.Activate(entity: camera,
                row: new() { Frame = frame, PhysicsHome = home, Pose = pose, HomePosition = homePosition },
                render: new(), components: [], wants: [], body: null, bodyCenterOfMass: Vector3.Zero,
                ownedFrame: FrameHandle.Null);
            return camera;
        }

        public void Move(FrameHandle frame, DPose local)
        {
            Frames.SetLocalPose(frame, local);
            Frames.Update();
        }

        /// <summary>Builds a snapshot of tick <paramref name="tick"/> from <paramref name="camera"/>, with the puffs of
        /// <paramref name="puffs"/> in it.</summary>
        public RenderSnapshot Build(long tick, Entity camera, in PuffView puffs)
        {
            var snapshot = new RenderSnapshot();
            Build(snapshot: snapshot, tick: tick, camera: camera, puffs: puffs);
            return snapshot;
        }

        public void Build(RenderSnapshot snapshot, long tick, Entity camera, in PuffView puffs)
        {
            Builder.Build(snapshot, new(Tick: tick, Time: tick * Step, Camera: camera, VerticalFov: 1,
                SunFrame: FrameHandle.Null, SunColor: Vector3.One,
                SunIntensity: 0,
                Ambient: new(), ShowFrameOutlines: false), puffs);
        }
    }

    private static Puff Row(Double3 place, Double3 chord = default, float age = 0, int look = 0) =>
        new() { Place = place, Chord = chord, Age = age, Look = look };

    private static PuffView View(Puff[] rows, PuffRange[] ranges, PuffLook[]? looks = null) =>
        new(rows: rows, ranges: ranges, looks: looks ?? [HotLook], step: Step);

    /// <summary>One range in <paramref name="frame"/> that holds every row.</summary>
    private static PuffView ViewIn(FrameHandle frame, params Puff[] rows) => View(rows, [new(frame, 0, rows.Length)]);

    [Fact]
    public void APuffIsPlacedThroughItsFramesPoseRelativeToTheCamerasHome()
    {
        var h = new Harness();
        var shipPose = new DPose(new(100, 20, -30), QuarterTurnAboutY);
        var hutPose = new DPose(new(-40, 0, 10), DQuaternion.FromAxisAngle(Double3.UnitX, 0.4));
        FrameHandle ship = h.AddFrame(h.Root, shipPose);
        FrameHandle hut = h.AddFrame(h.Root, hutPose);
        // The camera is at (1, 2, 3) in the ship, and its home is a cell of the ship at (50, 0, 0), as a numeric frame is.
        FrameHandle cell = h.AddFrame(ship, new(new(50, 0, 0), DQuaternion.Identity));
        Entity camera = h.AddCamera(frame: ship, pose: new(new(1, 2, 3), DQuaternion.Identity), home: cell,
            homePosition: new(-49, 2, 3));
        var place = new Double3(5, 6, 7);

        RenderSnapshot snapshot = h.Build(1, camera, ViewIn(hut, Row(place: place)));

        // The place in the root, then in the ship's axes, less the camera's place in the ship.
        Double3 expected = shipPose.InverseTransformPoint(hutPose.TransformPoint(place)) - new Double3(1, 2, 3);
        snapshot.Puffs.Should().ContainSingle();
        AssertNear(snapshot.Puffs[0].Position, expected.ToVector3(), 1e-4f);
    }

    [Fact]
    public void APuffsPreviousPositionIsItsPlaceOneChordBackThroughThePreviousFramePose()
    {
        var h = new Harness();
        var before = new DPose(new(10, 0, 0), DQuaternion.Identity);
        var after = new DPose(new(10.5, 0.25, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.01));
        FrameHandle ship = h.AddFrame(h.Root, before);
        Entity camera = h.AddCamera(h.Root, new(new(0, 0, 5), DQuaternion.Identity));
        var place = new Double3(1, 2, 3);
        var chord = new Double3(30, -6, 12);
        PuffView puffs = ViewIn(ship, Row(place: place, chord: chord));
        h.Build(1, camera, puffs);
        h.Move(ship, after);

        RenderSnapshot snapshot = h.Build(2, camera, puffs);

        var eye = new Double3(0, 0, 5);
        RenderPuff puff = snapshot.Puffs.Should().ContainSingle().Subject;
        AssertNear(puff.Position, (after.TransformPoint(place) - eye).ToVector3(), 1e-5f);
        AssertNear(puff.PreviousPosition, (before.TransformPoint(place - chord * Step) - eye).ToVector3(), 1e-5f);
        snapshot.PuffAgeStep.Should().Be((float)Step);
    }

    [Theory]
    [InlineData("a rebuild at the same tick")]
    [InlineData("a camera cut")]
    public void APuffHasNoMotionAndNoAgeStepInARebuildAtTheSameTickOrAfterACameraCut(string build)
    {
        var h = new Harness();
        FrameHandle ship = h.AddFrame(h.Root, new(new(10, 0, 0), DQuaternion.Identity));
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        Entity other = h.AddCamera(h.Root, DPose.Identity);
        PuffView puffs = ViewIn(ship, Row(place: new(1, 2, 3), chord: new(30, -6, 12), age: 0.5f));
        h.Build(1, camera, puffs);
        h.Build(2, camera, puffs);
        h.Move(ship, new(new(11, 0, 0), DQuaternion.Identity));

        RenderSnapshot snapshot = build == "a camera cut" ? h.Build(3, other, puffs) : h.Build(2, camera, puffs);

        RenderPuff puff = snapshot.Puffs.Should().ContainSingle().Subject;
        puff.PreviousPosition.Should().Be(puff.Position);
        AssertNear(puff.Position, new(12, 2, 3));
        snapshot.PuffAgeStep.Should().Be(0);
    }

    [Theory]
    [InlineData("the first build")]
    [InlineData("a build two ticks after the previous one")]
    public void APuffHasNoMotionWhenThePreviousBuildWasNotOfTheTickBefore(string build)
    {
        // The place one chord back is the place at the tick before, so it is the previous position only after a build of
        // that tick.
        var h = new Harness();
        FrameHandle ship = h.AddFrame(h.Root, DPose.Identity);
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        PuffView puffs = ViewIn(ship, Row(place: new(1, 2, 3), chord: new(30, -6, 12)));
        if (build != "the first build")
            h.Build(1, camera, puffs);

        RenderSnapshot snapshot = h.Build(3, camera, puffs);

        RenderPuff puff = snapshot.Puffs.Should().ContainSingle().Subject;
        puff.PreviousPosition.Should().Be(puff.Position);
        snapshot.PuffAgeStep.Should().Be(0);
    }

    [Theory]
    [InlineData("a cell of the station")]
    [InlineData("earth")]
    public void APuffFarFromItsFramesOriginIsExactNearTheCamera(string home)
    {
        // A station 6.8e6 m out in earth, turned, and a camera at (0, 2, 60) in it, or at the same place in earth. A float
        // step at 6.8e6 m is 0.5 m, so any part of the placement in floats would miss by far more than the tolerance. The
        // snapshot holds positions in the axes of the camera's home: the station's for its cell, and earth's for earth.
        var h = new Harness();
        var stationPose = new DPose(new(6.8e6, 1.25e6, -3.3e6), QuarterTurnAboutY);
        FrameHandle station = h.AddFrame(h.Root, stationPose);
        var eyeInStation = new Double3(0, 2, 60);
        var fromEye = new Double3(0.3, 0.45, -0.7);
        Double3 place = stationPose.TransformPoint(eyeInStation + fromEye);
        Entity camera;
        Double3 expected;
        if (home == "earth")
        {
            camera = h.AddCamera(h.Root, new(stationPose.TransformPoint(eyeInStation), stationPose.Rotation));
            expected = stationPose.Rotation.Rotate(fromEye);
        }
        else
        {
            FrameHandle cell = h.AddFrame(station, new(new(0, 0, 40), DQuaternion.Identity));
            camera = h.AddCamera(frame: station, pose: new(eyeInStation, DQuaternion.Identity), home: cell,
                homePosition: new(0, 2, 20));
            expected = fromEye;
        }

        RenderSnapshot snapshot = h.Build(1, camera, ViewIn(h.Root, Row(place: place)));

        AssertNear(snapshot.Puffs.Should().ContainSingle().Subject.Position, expected.ToVector3(), 1e-6f);
    }

    [Fact]
    public void TheSnapshotCarriesTheLooksThatThePuffsIndex()
    {
        var h = new Harness();
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        // Entry 1 is free: no live puff uses it, and the table keeps it in place.
        PuffLook[] looks = [HotLook, default, ColdLook];
        Puff[] rows = [Row(place: new(1, 0, 0), age: 0.25f, look: 2), Row(place: new(2, 0, 0), age: 1.75f, look: 0)];

        RenderSnapshot snapshot = h.Build(1, camera, View(rows, [new(h.Root, 0, 2)], looks));

        snapshot.PuffLooks.Should().Equal(looks);
        snapshot.Puffs.Select(p => (p.Age, p.Look)).Should().Equal((0.25f, 2), (1.75f, 0));
    }

    [Fact]
    public void PuffsKeepTheOrderOfTheirRanges()
    {
        var h = new Harness();
        FrameHandle east = h.AddFrame(h.Root, new(new(100, 0, 0), DQuaternion.Identity));
        FrameHandle west = h.AddFrame(h.Root, new(new(-100, 0, 0), DQuaternion.Identity));
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        // The west range comes first in the list, though its frame was made after the east one.
        Puff[] rows =
        [
            Row(place: new(1, 0, 0)), Row(place: new(2, 0, 0)), Row(place: new(3, 0, 0)), Row(place: new(4, 0, 0)),
            Row(place: new(5, 0, 0))
        ];
        PuffRange[] ranges = [new(west, 0, 2), new(east, 2, 3)];

        RenderSnapshot snapshot = h.Build(1, camera, View(rows, ranges));

        snapshot.Puffs.Select(p => p.Position.X).Should().Equal(-99, -98, 103, 104, 105);
    }

    [Theory]
    [InlineData("in the first build")]
    [InlineData("after a build with a camera at the tick before")]
    public void ASnapshotWithoutACameraHasNoPuffs(string build)
    {
        var h = new Harness();
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        PuffView puffs = ViewIn(h.Root, Row(place: new(1, 2, 3), chord: new(1, 0, 0)));
        if (build != "in the first build")
            h.Build(1, camera, puffs);

        RenderSnapshot snapshot = h.Build(2, Entity.Null, puffs);

        snapshot.Puffs.Should().BeEmpty();
        snapshot.PuffLooks.Should().BeEmpty();
        snapshot.PuffAgeStep.Should().Be(0);
    }

    [Fact]
    public void ARangeInAFrameThatTheBuildDidNotPlaceAddsNoPuffs()
    {
        // After a sync point every range is in a live frame, so this only guards against a broken table, as the builder
        // guards an entity whose frame it did not place.
        var h = new Harness();
        FrameHandle gone = h.AddFrame(h.Root, new(new(100, 0, 0), DQuaternion.Identity));
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        h.Frames.Destroy(gone);
        h.Frames.Update();
        Puff[] rows = [Row(place: new(1, 0, 0)), Row(place: new(2, 0, 0))];

        RenderSnapshot snapshot = h.Build(1, camera, View(rows, [new(gone, 0, 1), new(h.Root, 1, 1)]));

        snapshot.Puffs.Select(p => p.Position.X).Should().Equal(2);
    }

    [Fact]
    public void PlacingTheSameViewTwiceInOneBuildGivesTheSamePuffs()
    {
        var h = new Harness();
        FrameHandle ship = h.AddFrame(h.Root, new(new(10, 0, 0), DQuaternion.Identity));
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        PuffView puffs = ViewIn(ship, Row(place: new(1, 0, 0), chord: new(3, 0, 0)), Row(place: new(2, 0, 0)));
        h.Build(1, camera, puffs);
        var snapshot = new RenderSnapshot();
        h.Build(snapshot: snapshot, tick: 2, camera: camera, puffs: puffs);
        RenderPuff[] first = [.. snapshot.Puffs];

        h.Builder.BuildPuffs(snapshot, puffs);

        snapshot.Puffs.Should().Equal(first);
        snapshot.PuffLooks.Should().Equal(HotLook);
        snapshot.PuffAgeStep.Should().Be((float)Step);
    }

    [Fact]
    public void PlacingPuffsAllocatesNothingAfterWarmUp()
    {
        var h = new Harness();
        FrameHandle ship = h.AddFrame(h.Root, new(new(10, 0, 0), DQuaternion.Identity));
        Entity camera = h.AddCamera(h.Root, DPose.Identity);
        Puff[] rows =
            [.. Enumerable.Range(0, 500).Select(i => Row(place: new(i, 0, 0), chord: new(1, 0, 0), look: i % 2))];
        PuffRange[] ranges = [new(ship, 0, 300), new(h.Root, 300, 200)];
        PuffLook[] looks = [HotLook, ColdLook];
        var snapshot = new RenderSnapshot();
        h.Build(snapshot: snapshot, tick: 1, camera: camera, puffs: View(rows, ranges, looks));
        h.Build(snapshot: snapshot, tick: 2, camera: camera, puffs: View(rows, ranges, looks));

        long allocated = Allocations.Fewest(() => h.Builder.BuildPuffs(snapshot, View(rows, ranges, looks)));

        allocated.Should().Be(0);
        snapshot.Puffs.Should().HaveCount(500);
    }
}
