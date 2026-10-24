using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The medium of a frame on the World (docs/plans/particles.md, "Where it lives, who sets it, and who reads it"). It matches
/// the gravity field point by point: the spawn gives it from <see cref="FrameSpec.Medium"/>, the game changes it between
/// ticks with <see cref="World.SetFrameMedium"/>, and the World refuses a medium that is not valid, or a frame that is
/// gone, and keeps the old medium. A frame keeps its medium when it hands off or is reparented, so the air of a cabin goes
/// with the ship. A numeric frame has none, by an invariant of the frame tree. The worlds run on real Jolt.
/// </summary>
public class FrameMediumTests
{
    private static readonly MediumField CabinAir = MediumField.Uniform(1.225f, 2, new BoundingBox(new(-3), new(3)));
    private static readonly MediumField Thin = MediumField.Planet(radius: 6_371_000, surfaceDensity: 1.225f, scaleHeight: 8_500, drag: 0.5f);
    private static readonly MediumField NotValid = MediumField.Planet(radius: 6_371_000, surfaceDensity: 1.225f, scaleHeight: 0, drag: 0.5f);

    private static FrameHandle SpawnFrame(TestWorld test, FrameHandle parent, DPose pose, MediumField medium,
        float radius = 10,
        BodyDesc? hull = null) =>
        test.World.GetOwnedFrame(test.World.Spawn(new()
        {
            Frame = parent,
            Pose = pose,
            Body = hull,
            AsFrame = new(FrameBounds.Sphere(radius), Medium: medium)
        }));

    [Fact]
    public void AFrameSpawnsWithTheMediumOfItsSpec()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);

        FrameHandle cabin = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(100), medium: CabinAir);
        FrameHandle planet = test.World.CreateRootFrame(new(FrameBounds.Sphere(1e8f), GravityField.Point(4e14), Thin));
        FrameHandle bare = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(-100), medium: default);

        test.World.Frames.Medium(cabin).Should().Be(CabinAir);
        test.World.Frames.Medium(planet).Should().Be(Thin);
        test.World.Frames.Medium(bare).Should().Be(MediumField.None, "a frame spec without a medium gives vacuum");
        test.World.Frames.Medium(test.Root).Should().Be(MediumField.None);
    }

    [Fact]
    public void SetFrameMediumGivesAFrameANewMediumBetweenTicks()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle cabin = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(100), medium: CabinAir);
        test.Run(2);

        test.World.SetFrameMedium(cabin, Thin);

        test.World.Frames.Medium(cabin).Should().Be(Thin);
        test.Run(2);
        test.World.Frames.Medium(cabin).Should().Be(Thin, "no tick changes a medium");
        test.World.Frames.LastLooseBodyStep(cabin).Medium.Should()
            .Be(Thin, "the steps after the change read the new medium");
        test.World.SetFrameMedium(cabin, MediumField.None);
        test.World.Frames.Medium(cabin).Should().Be(MediumField.None);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SetFrameMediumThrowsDuringATick()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle cabin = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(100), medium: CabinAir);
        Exception? thrown = null;
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            try
            {
                test.World.SetFrameMedium(cabin, Thin);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
        }));

        test.Run(1);

        thrown.Should().BeOfType<InvalidOperationException>();
        test.World.Frames.Medium(cabin).Should().Be(CabinAir);
    }

    [Fact]
    public void SetFrameMediumRefusesAGoneFrameOrAMediumThatIsNotValidAndKeepsTheOldOne()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle cabin = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(100), medium: CabinAir);
        FrameHandle doomed = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(-100), medium: CabinAir);
        test.SpawnWith(SendOnce.Of(target: doomed.Entity, payload: new Destroy()));
        test.Run(3);
        test.World.Frames.IsAlive(doomed).Should().BeFalse();

        Action invalid = () => test.World.SetFrameMedium(cabin, NotValid);
        Action gone = () => test.World.SetFrameMedium(doomed, Thin);

        invalid.Should().Throw<ArgumentException>().WithMessage("*medium*not valid*");
        gone.Should().Throw<ArgumentException>().WithMessage("*gone*");
        test.World.Frames.Medium(cabin).Should().Be(CabinAir);
    }

    [Fact]
    public void ASpawnWithAMediumThatIsNotValidThrows()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        int frames = test.World.Frames.Count;

        Action spawn = () => SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(100), medium: NotValid);
        Action root = () =>
            test.World.CreateRootFrame(new(FrameBounds.Sphere(10), Medium: MediumField.Uniform(float.NaN, 1)));

        spawn.Should().Throw<ArgumentException>().WithMessage("*medium*not valid*");
        root.Should().Throw<ArgumentException>().WithMessage("*medium*not valid*");
        test.World.Frames.Count.Should().Be(frames, "a refused spawn makes no frame");
    }

    [Fact]
    public void AFrameKeepsItsMediumWhenItHandsOffOrIsReparented()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        // The ship starts in the root, 100 m inside the bubble of a carrier of 500 m, so Jolt hands it into the carrier in the
        // step of tick 0. A station of 15 m in the carrier, 5 m from the ship, is too small for the ship's bubble of 10 m to
        // enter on its own, so the game docks the ship there.
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(105), radius: 15, parent: carrier);
        FrameHandle ship = SpawnFrame(test: test, parent: test.Root, pose: TestWorld.At(1100), medium: CabinAir,
            hull: new(Shape: new SphereShapeDesc(5), Layer: BodyLayer.FrameHull));
        test.Run(4);
        test.World.GetFrame(ship.Entity).Should().Be(carrier, "Jolt handed the ship into the carrier");
        test.World.Frames.Parent(ship).Should().Be(carrier);
        test.World.Frames.Medium(ship).Should().Be(CabinAir, "the air of the cabin goes with the ship");

        test.World.Frames.Reparent(ship, station);
        test.Run(2);

        test.World.Frames.Parent(ship).Should().Be(station);
        test.World.Frames.Medium(ship).Should().Be(CabinAir);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ANumericFrameHasNoMediumByAnInvariantOfTheFrameTree()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle space = Scenes.AsteroidSpace(test.World);
        FrameHandle moon = test.World.GetOwnedFrame(Scenes.FastKeplerHull(test.World, space,
            new(Shape: new SphereShapeDesc(0.5f), Motion: BodyMotion.Kinematic)));
        test.Run(3);
        FrameHandle following = test.World.Frames.TreeParent(moon);
        test.World.Frames.IsNumeric(following).Should()
            .BeTrue("the hull at 2 km/s lives in a following frame of its own");
        test.World.Frames.Medium(following).Should().Be(MediumField.None);
        test.World.Frames.AssertInvariants();

        test.World.Frames.SetMedium(following, CabinAir);

        test.World.Frames.Invoking(tree => tree.AssertInvariants()).Should().Throw<ContractException>();
    }
}
