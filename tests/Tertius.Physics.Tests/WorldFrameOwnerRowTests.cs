using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// The rows of frame owners after the numeric update of a sync point (step 7 of "Threads and the tick" in ARCHITECTURE.md).
/// A nesting frame that jumps after its frames moves no ship frame and writes no row, but it changes the sum that gives the
/// place of each ship frame below it in its semantic frame: the ship's place in its falling frame, plus the falling frame's
/// place in the nest, plus the nest's place. When the ship's place has bits finer than the falling frame's place in the nest,
/// the first addition rounds, and the sum after the jump can round to another double. So the sync point passes over every
/// owner view again whenever the frame tree changed since its first pass, and every owner's row ends the tick on its
/// frame's place to the bit.
/// </summary>
public class WorldFrameOwnerRowTests
{
    /// <summary>
    /// How fast the ships fly along X, in m/s: 2^19, as in WorldNumericNestingTests, so that they pass the nest distance of
    /// 2^21 m from their nest's origin within 5 s.
    /// </summary>
    private const double Speed = 1 << 19;

    private static readonly BodyDesc ShipHull = new(new BoxShapeDesc(new(2)), Mass: 1000, LinearDamping: 0,
        AngularDamping: 0,
        InteriorShape: new SphereShapeDesc(0.05f));

    /// <summary>A light crate, whose drift away from its ship moves the ship a little in their shared falling frame.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new(0.5f)), Mass: 0.01f, LinearDamping: 0,
        AngularDamping: 0);

    [Fact]
    public void Every_ship_owners_row_ends_the_tick_in_which_its_nest_jumps_on_its_frames_place_to_the_bit()
    {
        // 24 ships 3,000 km out, each with a crate a little ahead of it that drifts away at 1/16 m/s. Ship and crate share a
        // falling frame, which nests at once. The crate's drift moves the ship in that frame by fractions of a nanometer each
        // tick, so its place there has fine bits. The crates sit at other distances from their ships, so the bits differ.
        using World world = WorldTesting.NewWorld(out _, debugChecks: false,
            configure: o => o.NumericFrames = new() { NestDistance = 1 << 21 });
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e18f), GravityField.None));
        var ships = new List<FrameHandle>();
        for (int i = 0; i < 24; i++)
        {
            double x = 3e6 + 37.123 * i;
            Entity ship = world.Spawn(new()
            {
                Frame = space,
                Pose = WorldTesting.At(x, 500 * i),
                LinearVelocity = new(Speed, 0, 0),
                Body = ShipHull,
                AsFrame = new(FrameBounds.Sphere(20))
            });
            ships.Add(world.GetOwnedFrame(ship));
            world.Spawn(new()
            {
                Frame = space,
                Pose = WorldTesting.At(x + 8 + 0.37 * i, 500 * i),
                LinearVelocity = new(Speed + 1.0 / 16, 0, 0),
                Body = Crate
            });
        }

        FrameTree frames = world.Frames;
        var lastNestPlace = new Dictionary<FrameHandle, (FrameHandle Nest, double X)>();
        int roundedOtherwise = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            foreach (FrameHandle ship in ships)
            {
                FrameHandle pod = frames.TreeParent(ship);
                FrameHandle nest = frames.TreeParent(pod);
                frames.Numeric(nest).Should().Be(NumericMode.Nesting,
                    "every ship falls in a frame that nests from the first tick");
                double nestX = frames.TreeLocalPose(nest).Position.X;
                if (lastNestPlace.TryGetValue(ship, out (FrameHandle Nest, double X) last) && last.Nest == nest &&
                    last.X != nestX)
                {
                    // The nest jumped this tick. Summed over the frames before the jump, the ship's place differs.
                    double step = nestX - last.X;
                    double shipX = frames.TreeLocalPose(ship).Position.X, podX = frames.TreeLocalPose(pod).Position.X;
                    if (shipX + podX + nestX != shipX + (podX + step) + last.X)
                        roundedOtherwise++;
                }

                lastNestPlace[ship] = (nest, nestX);

                Double3 place = frames.InSemantic(pod, space, frames.TreeLocalPose(ship).Position);
                world.Entities.FrontRow(ship.Entity).Pose.Position.Should().Be(place,
                    $"the row of the owner of {ship} mirrors its frame at tick {tick}");
            }
        }

        roundedOtherwise.Should().BeGreaterThan(0,
            "the scene must have a jump after which a ship's place rounds to another double");
        world.Errors.Should().BeEmpty();
    }
}
