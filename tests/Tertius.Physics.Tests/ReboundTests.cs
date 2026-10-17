using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The minimum rebound speed (ter-4kaa). Jolt finds a pair within its speculative contact distance of 2 cm. When a landing
/// step begins inside that distance, Jolt gives a bouncing body its rebound at once, from where it is, so a small bounce
/// can carry the body out of the 2 cm, and it lands a second time. Whether it does depended only on where the landing step
/// begins. So a contact whose rebound, its combined restitution times its approach speed, is below 1 m/s does not bounce,
/// and every bounce that remains leaves the 2 cm.
/// </summary>
public class ReboundTests
{
    private const float G = 9.81f;

    /// <summary>The radius of a dropped ball, in m.</summary>
    private const float Radius = 0.25f;

    /// <summary>Jolt's speculative contact distance, in m: Jolt finds the pair of two bodies that are this close.</summary>
    private const double SpeculativeDistance = 0.02;

    /// <summary>How many gaps at the start of the landing step a drop tries.</summary>
    private const int GapCount = 16;

    /// <summary>
    /// The gaps between a ball and the floor at the start of its landing step that a drop tries, in m: <see cref="GapCount"/>
    /// gaps spread evenly over Jolt's speculative contact distance, each in the middle of its share.
    /// </summary>
    private static IEnumerable<double> LandingGaps() => Enumerable.Range(0, GapCount).Select(k => (k + 0.5) / GapCount * SpeculativeDistance);

    /// <summary>The news of <paramref name="landings"/> landings that each bounce ends, but the last.</summary>
    private static List<ContactNews> Landings(int landings) =>
        [Started, .. Enumerable.Range(1, landings - 1).SelectMany(_ => new[] { Ended, Started })];

    /// <summary>
    /// What a ball and the floor heard in a drop. <see cref="ApproachSpeeds"/> holds the approach speed of each landing in
    /// its first step of touch, in m/s: the impulse of the ball's Collision, which is the approach speed times the reduced
    /// mass, here the ball's 1 kg. <see cref="Flights"/> holds the highest that the bottom of the ball rose above the floor
    /// between each landing and the next, in m. <see cref="FinalHeight"/> is the height of the bottom of the ball above the
    /// floor at the end, in m, and <see cref="FinalSpeed"/> its speed then, in m/s.
    /// </summary>
    private sealed record Drop(List<ContactNews> BallNews, List<ContactNews> FloorNews, List<float> ApproachSpeeds,
        List<double> Flights, double FinalHeight, float FinalSpeed);

    /// <summary>
    /// Drops a ball of 1 kg with radius <see cref="Radius"/> and restitution <paramref name="restitution"/> from rest onto a
    /// static floor with the default restitution, and ticks <paramref name="ticks"/> times. The ball starts at the height
    /// from which it falls for <paramref name="fallSteps"/> whole steps and then begins its next step, the landing step,
    /// <paramref name="gap"/> m above the floor. Physics gives Jolt the chord of each step, so the drop at
    /// the start of step n is exactly ½ g (n Δt)².
    /// </summary>
    private static Drop DropBall(int fallSteps, double gap, float restitution, int ticks)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        var floorLog = new CollisionLog();
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
            Components = [floorLog],
        });
        var ballLog = new CollisionLog();
        double fall = 0.5 * G * Math.Pow(fallSteps * world.DeltaTime, 2);
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, Radius + fall + gap),
            Body = new BodyDesc(new SphereShapeDesc(Radius), Mass: 1, Restitution: restitution, LinearDamping: 0, AngularDamping: 0),
            Components = [ballLog],
        });

        var flights = new List<double>();
        double flightHighest = double.NegativeInfinity;
        int landings = 0;
        for (int i = 0; i < ticks; i++)
        {
            world.Tick();
            double height = world.PoseInOwnFrame(ball).Position.Y - Radius;
            if (ballLog.Seen.Count > landings)
            {
                if (landings > 0)
                    flights.Add(flightHighest);
                landings = ballLog.Seen.Count;
                flightHighest = double.NegativeInfinity;
            }
            if (landings > 0)
                flightHighest = Math.Max(flightHighest, height);
        }
        world.Errors.Should().BeEmpty();
        return new Drop(ballLog.NewsOf(floor), floorLog.NewsOf(ball), ballLog.Seen.Select(seen => seen.Impulse).ToList(), flights,
            world.PoseInOwnFrame(ball).Position.Y - Radius, world.VelocityInOwnFrame(ball).Length());
    }

    /// <summary>
    /// Each drop lands at an approach speed that does not depend on where its landing step begins, about g (n + ½) Δt for a
    /// fall of n steps. The first four rebound slower than 1 m/s, so they do not bounce. The last two rebound at 1.8 and
    /// 1.9 m/s, which carries the ball about 15 cm up, out of Jolt's 2 cm, and land again at about 2 m/s, which rebounds
    /// slower than 1 m/s.
    /// </summary>
    [Theory]
    [InlineData(27, 0.1f, 1)] // 0.99 m: lands at 4.5 m/s, a rebound of 0.45 m/s
    [InlineData(38, 0.1f, 1)] // 1.97 m: lands at 6.3 m/s, a rebound of 0.63 m/s
    [InlineData(19, 0.2f, 1)] // 0.49 m: lands at 3.2 m/s, a rebound of 0.64 m/s
    [InlineData(15, 0.3f, 1)] // 0.31 m: lands at 2.5 m/s, a rebound of 0.76 m/s
    [InlineData(27, 0.4f, 2)] // 0.99 m: lands at 4.5 m/s, a rebound of 1.8 m/s
    [InlineData(38, 0.3f, 2)] // 1.97 m: lands at 6.3 m/s, a rebound of 1.9 m/s
    public void ADropReportsTheSameLandingsWhereverItsLandingStepBegins(int fallSteps, float restitution, int landings)
    {
        foreach (double gap in LandingGaps())
        {
            Drop drop = DropBall(fallSteps, gap, restitution, fallSteps + 90);

            string because = $"the landing step begins {gap * 100:F3} cm above the floor";
            drop.BallNews.Should().Equal(Landings(landings), because);
            drop.FloorNews.Should().Equal(Landings(landings), because);
            drop.Flights.Should().OnlyContain(highest => highest > 0.1, because);
        }
    }

    /// <summary>
    /// A ball at restitution 0.3 lands after a fall of 22 steps (0.66 m) at 3.68 m/s, so its rebound is 1.1 m/s, just above
    /// the minimum. Jolt takes off the speed that gravity added in the step, so the ball leaves at 1.05 m/s and rises about
    /// 5.7 cm, out of Jolt's 2 cm: the contact ends, and the ball lands again.
    /// </summary>
    [Fact]
    public void ABounceOfAtLeastOneMeterPerSecondEndsTheContactAndTheBallLandsAgain()
    {
        foreach (double gap in LandingGaps())
        {
            Drop drop = DropBall(22, gap, 0.3f, 90);

            string because = $"the landing step begins {gap * 100:F3} cm above the floor";
            drop.ApproachSpeeds[0].Should().BeApproximately(3.68f, 0.01f);
            drop.BallNews.Should().Equal([Started, Ended, Started], because);
            drop.FloorNews.Should().Equal([Started, Ended, Started], because);
            drop.Flights.Should().ContainSingle().Which.Should().BeGreaterThan(0.05, because);
        }
    }

    /// <summary>
    /// A rubber ball at restitution 0.8 falls 0.49 m and bounces five times. The first four land at about 3.2, 2.6, 2.1
    /// and 1.7 m/s. The fifth lands at about 1.4 m/s, or at 1.21 m/s, a rebound of 0.97 m/s that does not bounce in its first
    /// step: the speculative contact lets the ball close its gap at almost its full speed, so it touches at 1.19 m/s, and the
    /// gravity of the next step raises its approach to 1.35 m/s, a rebound above 1 m/s. Every bounce leaves Jolt's 2 cm.
    /// The sixth landing comes at 1.0 to 1.2 m/s, a rebound below 1 m/s, and the ball rests there, wherever its first
    /// landing step begins.
    /// </summary>
    [Fact]
    public void ARubberBallBouncesUntilItsReboundFallsBelowOneMeterPerSecondAndThenRests()
    {
        const float rubber = 0.8f;
        foreach (double gap in LandingGaps())
        {
            Drop drop = DropBall(19, gap, rubber, 240);

            string because = $"the first landing step begins {gap * 100:F3} cm above the floor";
            drop.BallNews.Should().Equal(Landings(6), because);
            drop.FloorNews.Should().Equal(Landings(6), because);
            drop.Flights.Should().HaveCount(5).And.OnlyContain(highest => highest > 0.035, because);
            (rubber * drop.ApproachSpeeds[^1]).Should().BeLessThan(1f, because);
            drop.FinalHeight.Should().BeInRange(-0.002, 0.002, because);
            drop.FinalSpeed.Should().BeLessThan(0.01f, because);
        }
    }

    /// <summary>
    /// A crate with every default of a body falls 10 m onto a floor with every default, and lands at about 14 m/s. It
    /// does not bounce: the default restitution is 0, as in Jolt. At the old default of 0.1, its rebound was 1.4 m/s.
    /// </summary>
    [Fact]
    public void ABodyWithTheDefaultRestitutionDoesNotBounce()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        Entity floor = Floor(world, ground);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 10.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f))), Components = [crateLog] });

        double highest = double.NegativeInfinity;
        for (int i = 0; i < 150; i++)
        {
            world.Tick();
            if (crateLog.Seen.Count > 0)
                highest = Math.Max(highest, world.PoseInOwnFrame(crate).Position.Y - 0.5);
        }

        crateLog.Seen.Should().ContainSingle().Which.Impulse.Should().BeGreaterThan(13f, "the crate of 1 kg landed at about 14 m/s");
        crateLog.NewsOf(floor).Should().Equal([Started]);
        highest.Should().BeLessThan(0.002, "the crate did not rise after it landed");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ball of radius 2 cm, thrown down at 8 m/s, moves 14 cm in a step, much more than 0.75 of its radius, so Jolt casts
    /// it along its motion (<see cref="MotionQuality.LinearCast"/>). Its landing step begins 2.5 cm above a plate of 4 mm,
    /// beyond Jolt's speculative contact distance, and would end with the whole ball below the plate, so only the cast finds
    /// the plate. The ball lands at 8.57 m/s. At restitution 0.1 its rebound is 0.86 m/s, so it does not bounce. At 0.2 its
    /// rebound is 1.7 m/s, so it bounces about 15 cm up and lands a second time.
    /// </summary>
    [Theory]
    [InlineData(0.1f, 1)]
    [InlineData(0.2f, 2)]
    public void AFastBallThatJoltCastsBouncesOnlyWhenItsReboundReachesOneMeterPerSecond(float restitution, int landings)
    {
        const float radius = 0.02f, speed = 8f;
        const int fallSteps = 3;
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, G);
        var plateLog = new CollisionLog();
        Entity plate = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.002),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 0.002f, 5)), BodyMotion.Static),
            Components = [plateLog],
        });
        var ballLog = new CollisionLog();
        double t = fallSteps * world.DeltaTime;
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, radius + 0.025 + speed * t + 0.5 * G * t * t),
            LinearVelocity = new Vector3(0, -speed, 0),
            Body = new BodyDesc(new SphereShapeDesc(radius), Mass: 1, Restitution: restitution, LinearDamping: 0, AngularDamping: 0),
            Components = [ballLog],
        });
        world.Tick();
        backend.BodiesIn(ground).Single(facts => BodyUserData.Names(facts.UserData, ball)).Quality.Should().Be(MotionQuality.LinearCast);

        double highest = double.NegativeInfinity;
        for (int i = 0; i < 90; i++)
        {
            world.Tick();
            if (ballLog.Seen.Count > 0)
                highest = Math.Max(highest, world.PoseInOwnFrame(ball).Position.Y - radius);
        }

        ballLog.Seen[0].Impulse.Should().BeApproximately(8.57f, 0.01f, "the ball landed at 8.57 m/s");
        ballLog.NewsOf(plate).Should().Equal(Landings(landings));
        plateLog.NewsOf(ball).Should().Equal(Landings(landings));
        world.PoseInOwnFrame(ball).Position.Y.Should().BeGreaterThan(0, "the ball rests on the plate");
        if (landings > 1)
            highest.Should().BeGreaterThan(0.05);
        else
            highest.Should().BeLessThan(0.002, "the ball did not rise after it landed");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A kinematic block in a child frame sticks 1 m out of the frame's bubble, so its ghost stands in the parent. A ball
    /// in the parent flies at the ghost at 5 m/s, with no gravity, so Jolt's rebound is exactly the restitution times
    /// 5 m/s. Physics reports no contact with a ghost, but the minimum rebound speed holds for it too: at restitution
    /// 0.15 the rebound of 0.75 m/s is too slow, so the ball stops, and at 0.4 it flies back at 2 m/s.
    /// </summary>
    [Theory]
    [InlineData(0.15f, 0f)]
    [InlineData(0.4f, 2f)]
    public void ABallThatHitsAGhostBouncesOnlyWhenItsReboundReachesOneMeterPerSecond(float restitution, float rebound)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        FrameHandle bubble = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
        }));
        Entity block = world.Spawn(new SpawnDesc { Frame = bubble, Pose = At(10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic) });
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(16),
            LinearVelocity = new Vector3(-5, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.25f), Mass: 1, Restitution: restitution, LinearDamping: 0, AngularDamping: 0),
        });
        world.Tick();
        faults.Jolt.HasGhostIn(block, space).Should().BeTrue();

        Tick(world, 90);

        world.GetFrame(ball).Should().Be(space);
        world.PoseInOwnFrame(ball).Position.X.Should().BeGreaterThan(11.25 - 0.05, "the ball did not go through the ghost");
        world.VelocityInOwnFrame(ball).X.Should().BeApproximately(rebound, 0.02f);
        world.Errors.Should().BeEmpty();
    }
}
