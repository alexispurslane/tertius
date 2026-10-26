using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The camera's chain in a render snapshot, and the chain depth of each object (docs/plans/environment-maps-per-frame.md,
/// "The snapshot: the chain and the chain depths"). The chain runs from the camera's frame through its semantic parents to
/// the root. An object's chain depth is the depth of the nearest chain frame at or above the frame that holds it.
/// </summary>
public sealed class RenderSnapshotChainTests
{
    private static readonly DQuaternion QuarterTurnAboutY = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
    private static readonly DQuaternion TiltAboutX = DQuaternion.FromAxisAngle(Double3.UnitX, 0.4);
    private static readonly DQuaternion TiltAboutZ = DQuaternion.FromAxisAngle(Double3.UnitZ, -0.7);

    /// <summary>
    /// The chain of the cabin camera in demo-surface, in small: the root stands for the sun, then earth, earth-surface and
    /// the shuttle, each turned in its parent. The camera sits in the shuttle.
    /// </summary>
    private sealed record CabinChain(FrameHandle Sun, FrameHandle Earth, FrameHandle Surface, FrameHandle Shuttle)
    {
        /// <summary>The chain's frames, camera first.</summary>
        public FrameHandle[] CameraFirst => [Shuttle, Surface, Earth, Sun];
    }

    private static CabinChain SpawnCabinChain(TestWorld test, FrameHandle? earth = null)
    {
        FrameHandle earthFrame =
            earth ?? test.SpawnStaticFrame(new(new(1e6, 0, 0), QuarterTurnAboutY), radius: 1e5f, parent: test.Root);
        FrameHandle surface = test.SpawnStaticFrame(new(new(0, 6e4, 0), TiltAboutX), radius: 1e4f, parent: earthFrame);
        FrameHandle shuttle = test.SpawnStaticFrame(new(new(30, 2, -5), TiltAboutZ), radius: 50, parent: surface);
        test.World.Camera = test.SpawnCamera(shuttle, new(new(1, 1, 0), QuarterTurnAboutY));
        return new(Sun: test.Root, Earth: earthFrame, Surface: surface, Shuttle: shuttle);
    }

    private static void ExpectSame(Quaternion actual, DQuaternion expected)
    {
        // Two axes pin a rotation down.
        foreach (Double3 axis in (Double3[])[Double3.UnitX, Double3.UnitY])
            Vector3
                .Distance(Vector3.Transform(axis.ToVector3(), actual), expected.Rotate(axis).ToVector3())
                .Should()
                .BeLessThan(1e-6f, $"{actual} does not turn {axis} as {expected} does");
    }

    // ---- The chain -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheChainRunsFromTheCamerasFrameThroughItsSemanticParentsToTheRoot()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);

        test.Run(1);

        test.World.Latest().Chain.Select(frame => frame.FrameId).Should().Equal(chain.CameraFirst.Select(IdOf));
    }

    [Fact]
    public void TheCamerasFrameLiesInItsOwnAxes()
    {
        using var test = new TestWorld();
        SpawnCabinChain(test);

        test.Run(1);

        test.World.Latest().Chain[0].Rotation.Should().Be(Quaternion.Identity);
    }

    [Fact]
    public void EachChainFramesRotationTurnsItsAxesIntoTheAxesOfTheCamerasFrame()
    {
        using var test = new TestWorld();
        SpawnCabinChain(test);

        test.Run(1);

        // The shuttle's axes are earth's turned by the surface's tilt and then by the shuttle's. Earth's axes, seen from the
        // shuttle, are turned back by both.
        ExpectSame(test.World.Latest().Chain[2].Rotation, (TiltAboutX * TiltAboutZ).Conjugate());
    }

    [Fact]
    public void TheRootRotationIsTheLastFrameOfTheChain()
    {
        using var test = new TestWorld();
        SpawnCabinChain(test);

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.RootRotation.Should().Be(snapshot.Chain[^1].Rotation);
        ExpectSame(snapshot.RootRotation, (QuarterTurnAboutY * TiltAboutX * TiltAboutZ).Conjugate());
    }

    [Fact]
    public void TheOriginFrameIdIsTheFirstFrameOfTheChain()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.OriginFrameId.Should().Be(snapshot.Chain[0].FrameId).And.Be(IdOf(chain.Shuttle));
    }

    [Fact]
    public void ASnapshotWithoutACameraHasNoChain()
    {
        using var test = new TestWorld();
        SpawnCabinChain(test);
        test.Run(1);
        test.World.Latest();
        test.World.Camera = Entity.Null;

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.Chain.Should().BeEmpty();
        (snapshot.OriginFrameId, snapshot.RootRotation).Should().Be((0L, Quaternion.Identity));
    }

    [Fact]
    public void TheChainsPreviousRotationsAreItsRotationsWhenTheBuildDoesNotInterpolate()
    {
        using var test = new TestWorld();
        FrameHandle spinningEarth = SpawnSpinningEarth(test);
        SpawnCabinChain(test, earth: spinningEarth);

        // The first build has no build before it to interpolate from.
        test.Run(1);

        test.World.Latest()
            .Chain.Should()
            .AllSatisfy(frame => frame.PreviousRotation.Should().Be(frame.Rotation))
            .And.Contain(frame => frame.Rotation != Quaternion.Identity, "the frames above the shuttle are turned");
    }

    [Fact]
    public void AFrameThatTurnedSinceTheLastBuildCarriesItsLastRotationInTheChain()
    {
        using var test = new TestWorld();
        FrameHandle spinningEarth = SpawnSpinningEarth(test);
        SpawnCabinChain(test, earth: spinningEarth);
        test.Run(1);
        Quaternion[] before = [.. test.World.Latest().Chain.Select(frame => frame.Rotation)];

        test.Run(1);
        RenderSnapshot after = test.World.Latest();

        after.Chain.Select(frame => frame.PreviousRotation).Should().Equal(before);
        after.Chain[^1].Rotation.Should().NotBe(before[^1], "earth turned by 1 rad/s for one tick under the shuttle");
    }

    /// <summary>A frame in the root that spins at 1 rad/s about +Y, to stand for earth.</summary>
    private static FrameHandle SpawnSpinningEarth(TestWorld test) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new SpawnDesc
                {
                    Frame = test.Root,
                    Pose = new(new(1e6, 0, 0), DQuaternion.Identity),
                    AsFrame = new(FrameBounds.Sphere(1e5f)),
                }.Spinning(test.World, new(Tilt: DQuaternion.Identity, Rate: 1))
            )
        );

    // ---- The chain depths ----------------------------------------------------------------------------------------

    [Fact]
    public void AnInstanceInTheCamerasFrameHasDepthZero()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);
        Entity seat = test.Show(chain.Shuttle, TestWorld.At(0, -1, 0));

        test.Run(1);

        test.World.Latest().InstanceOf(seat).ChainDepth.Should().Be(0);
    }

    [Fact]
    public void AnInstanceInAnAncestorOnTheChainHasThatAncestorsDepth()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);
        Entity moon = test.Show(chain.Earth, TestWorld.At(4e5, 0, 0));
        Entity tower = test.Show(chain.Surface, TestWorld.At(100, 0, 0));
        Entity sunSphere = test.Show(chain.Sun, DPose.Identity);

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        (snapshot.InstanceOf(tower).ChainDepth, snapshot.InstanceOf(moon).ChainDepth)
            .Should()
            .Be(((ushort)1, (ushort)2));
        snapshot.InstanceOf(sunSphere).ChainDepth.Should().Be(3);
    }

    [Fact]
    public void AnInstanceInAFrameOffTheChainTakesTheDepthOfItsNearestAncestorOnTheChain()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);
        // Another ship beside the shuttle, in earth-surface, with a hold of its own.
        FrameHandle ship = test.SpawnStaticFrame(new(new(-60, 2, 0), TiltAboutX), radius: 50, parent: chain.Surface);
        FrameHandle hold = test.SpawnStaticFrame(TestWorld.At(0, -3, 0), radius: 10, parent: ship);
        Entity crate = test.Show(hold, TestWorld.At(1, 0, 0));

        test.Run(1);

        test.World.Latest().InstanceOf(crate).ChainDepth.Should().Be(1, "earth-surface holds the ship");
    }

    [Fact]
    public void AFrameOwnerTakesTheDepthOfTheFrameThatItOwns()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);
        Entity hull = test.World.Spawn(
            new()
            {
                Frame = chain.Surface,
                Pose = TestWorld.At(-30, 2, 5),
                AsFrame = new(FrameBounds.Sphere(50)),
                Render = Visible(),
            }
        );
        test.World.Camera = test.SpawnCamera(test.World.GetOwnedFrame(hull), TestWorld.At(0, 1, 0));

        test.Run(1);

        test.World.Latest()
            .InstanceOf(hull)
            .ChainDepth.Should()
            .Be(0, "the hull, seen from its cabin, is in the cabin");
    }

    [Fact]
    public void ABodyInANumericFrameTakesTheDepthOfItsSemanticFrame()
    {
        using var test = TestWorld.OnJolt(
            faults: out _,
            configure: options => options.NumericFrames = NumericFrameTesting.EveryGroupFalls
        );
        CabinChain chain = SpawnCabinChain(test);
        Entity crate = test.World.Spawn(
            new()
            {
                Frame = chain.Surface,
                Pose = TestWorld.At(100, 0, 0),
                Body = TestWorld.Ball,
                Render = Visible(),
            }
        );
        test.Run(2);
        test.World.Frames.IsNumeric(test.World.PhysicsHomeOf(crate)).Should().BeTrue("every group falls");

        test.Run(1);

        test.World.Latest()
            .InstanceOf(crate)
            .ChainDepth.Should()
            .Be(1, "earth-surface holds the crate's falling frame");
    }

    [Fact]
    public void AnInstanceInAnotherTreeTakesTheDepthOfTheRoot()
    {
        using var test = new TestWorld();
        SpawnCabinChain(test);
        FrameHandle otherRoot = test.World.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity probe = test.Show(otherRoot, TestWorld.At(5, 0, 0));

        test.Run(1);

        test.World.Latest().InstanceOf(probe).ChainDepth.Should().Be(3, "the chain of four frames ends at depth 3");
    }

    [Fact]
    public void ALightTakesTheDepthOfTheEntityThatCarriesIt()
    {
        using var test = new TestWorld();
        CabinChain chain = SpawnCabinChain(test);
        test.World.Spawn(
            new()
            {
                Frame = chain.Surface,
                Pose = TestWorld.At(0, 10, 0),
                Render = new()
                {
                    Light = new PointLightData(Offset: Pose.Identity, Color: Vector3.One, Intensity: 3, Range: 20),
                },
            }
        );
        // A searchlight in earth, two frames above the camera's.
        test.World.Spawn(
            new()
            {
                Frame = chain.Earth,
                Pose = TestWorld.At(0, 7e4, 0),
                Render = new()
                {
                    Light = new SpotLightData(
                        Offset: Pose.Identity,
                        Color: Vector3.One,
                        Intensity: 50,
                        Range: 10,
                        InnerConeAngle: 0.5f,
                        OuterConeAngle: 0.8f
                    ),
                },
            }
        );

        test.Run(1);

        // The lights keep the slot order: the apron's lamp, then the searchlight.
        test.World.Latest()
            .Lights.Select(light =>
                light switch
                {
                    PointRenderLight { ChainDepth: var depth } => depth,
                    SpotRenderLight { ChainDepth: var depth } => depth,
                    null => ushort.MaxValue,
                }
            )
            .Should()
            .Equal(1, 2);
    }

    [Fact]
    public void APuffTakesTheDepthOfTheFrameOfItsRange()
    {
        using var test = TestWorld.OnJolt(faults: out _);
        CabinChain chain = SpawnCabinChain(test);
        test.World.Spawn(
            new()
            {
                Frame = chain.Surface,
                Pose = TestWorld.At(20, 0, 0),
                Render = new RenderData
                {
                    Emitter = new(
                        Rate: 60,
                        Speed: 10,
                        Spread: 0,
                        Look: new(
                            Life: 2,
                            StartRadius: 0.5f,
                            EndRadius: 2,
                            TemperatureRatio: 3,
                            Shading: PuffShading.Glow(new(4, 2, 1))
                        )
                    ),
                },
            }
        );

        test.Run(5);

        test.World.Latest()
            .Puffs.Should()
            .NotBeEmpty()
            .And.AllSatisfy(puff => puff.ChainDepth.Should().Be(1, "the emitter's puffs are in earth-surface"));
    }
}
