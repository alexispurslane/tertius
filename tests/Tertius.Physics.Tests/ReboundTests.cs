using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The minimum rebound (ter-4kaa). Jolt finds a pair within its speculative contact distance of 2 cm. When a landing
/// step begins inside that distance, Jolt gives a bouncing body its rebound at once, from where it is, so a small bounce
/// can carry the body out of the 2 cm, and it lands a second time. Whether it does depended only on where the landing step
/// begins. So a contact whose bounce would rise less than 4 cm, twice the 2 cm, does not bounce, and every bounce that
/// remains leaves the 2 cm. The bounce is Jolt's rebound, the combined restitution times the approach speed less the speed
/// that the pull toward the surface adds in the step, against that pull. Until the review of ter-ejx9 the limit was a
/// rebound of 1 m/s, the restitution times the approach speed, at any gravity: at 1 g that is about the same, but at 2 g
/// and more a bounce that it kept could stay inside the 2 cm, and in zero gravity it stopped bounces that never come back.
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
    private static IEnumerable<double> LandingGaps() =>
        Enumerable.Range(0, GapCount).Select(k => (k + 0.5) / GapCount * SpeculativeDistance);

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
    private sealed record Drop(
        List<ContactNews> BallNews,
        List<ContactNews> FloorNews,
        List<float> ApproachSpeeds,
        List<double> Flights,
        double FinalHeight,
        float FinalSpeed);

    /// <summary>
    /// Drops a ball of 1 kg with radius <see cref="Radius"/> and restitution <paramref name="restitution"/> from rest onto a
    /// static floor with the default restitution, under gravity of <paramref name="g"/> m/s², and ticks
    /// <paramref name="ticks"/> times. The ball starts at the height from which it falls for <paramref name="fallSteps"/>
    /// whole steps and then begins its next step, the landing step, <paramref name="gap"/> m above the floor. Physics gives
    /// Jolt the chord of each step, so the drop at the start of step n is exactly ½ g (n Δt)².
    /// </summary>
    private static Drop DropBall(int fallSteps, double gap, float restitution, int ticks, float g = G) =>
        DropBall(fallSteps, gap, restitution, ticks, g, thrust: 0);

    /// <summary>
    /// Like the drop under gravity, but with no gravity: an engine of the ball pushes it down at <paramref name="thrust"/>
    /// m/s² on every tick, a force that acts from the step of the next tick (see <see cref="Pilot"/>). Jolt integrates a
    /// force step by step, so after n steps of the push the ball has fallen a Δt² n (n + 1) / 2, and it lands at
    /// a (n + 1) Δt.
    /// </summary>
    private static Drop ThrustBall(int fallSteps, double gap, float restitution, int ticks, float thrust) =>
        DropBall(fallSteps, gap, restitution, ticks, g: 0, thrust);

    private static Drop DropBall(int fallSteps, double gap, float restitution, int ticks, float g, float thrust)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, g);
        var floorLog = new CollisionLog();
        Entity floor = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new(new BoxShapeDesc(new(50, 0.5f, 50)), BodyMotion.Static),
            Components = [floorLog]
        });
        var ballLog = new CollisionLog();
        var engine = new Pilot();
        if (thrust != 0)
            engine.EveryTick = body => body.AddForce(new(0, -thrust, 0));
        double fall = 0.5 * g * Math.Pow(fallSteps * world.DeltaTime, 2) +
                      0.5 * thrust * Math.Pow(world.DeltaTime, 2) * fallSteps * (fallSteps + 1);
        Entity ball = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, Radius + fall + gap),
            Body = new(new SphereShapeDesc(Radius), Mass: 1, Restitution: restitution, LinearDamping: 0,
                AngularDamping: 0),
            Components = [ballLog, engine]
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
        return new(ballLog.NewsOf(floor), floorLog.NewsOf(ball), [.. ballLog.Seen.Select(seen => seen.Impulse)],
            flights,
            world.PoseInOwnFrame(ball).Position.Y - Radius, world.VelocityInOwnFrame(ball).Length());
    }

    /// <summary>
    /// Each drop lands at an approach speed that does not depend on where its landing step begins, about g (n + ½) Δt for a
    /// fall of n steps. Jolt rebounds at the restitution times that speed less g Δt = 0.16 m/s. The first four would rise
    /// less than 4 cm, so they do not bounce. The last two rebound at 1.7 and 1.8 m/s, which carries the ball about 15 cm
    /// up, out of Jolt's 2 cm, and land again at about 2 m/s, which would rise less than 4 cm. (Before: the limit was a
    /// rebound of 1 m/s, without the 0.16 m/s. Every row gives the same landings under both limits.)
    /// </summary>
    [Theory]
    [InlineData(27, 0.1f, 1)] // 0.99 m: lands at 4.5 m/s, a rebound of 0.43 m/s
    [InlineData(38, 0.1f, 1)] // 1.97 m: lands at 6.3 m/s, a rebound of 0.61 m/s
    [InlineData(19, 0.2f, 1)] // 0.49 m: lands at 3.2 m/s, a rebound of 0.61 m/s
    [InlineData(15, 0.3f, 1)] // 0.31 m: lands at 2.5 m/s, a rebound of 0.70 m/s
    [InlineData(27, 0.4f, 2)] // 0.99 m: lands at 4.5 m/s, a rebound of 1.7 m/s
    [InlineData(38, 0.3f, 2)] // 1.97 m: lands at 6.3 m/s, a rebound of 1.8 m/s
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
    /// The limit follows the pull. Jolt takes the speed that the pull adds in the step off a rebound, and a stronger pull
    /// brings a bounce back sooner. At 2 g and 3 g, a bounce whose rebound reached 1 m/s before the pull came off could
    /// stay inside Jolt's 2 cm, so one drop reported one landing or two, depending on where its landing step began. Every
    /// bounce that the rule keeps must leave the 2 cm, so every gap reports the same landings. The first column is the
    /// gravity in g. No row lands again within the spread of landing speeds of the limit: a bounce from the air rises up
    /// to 2 cm more, so a later landing can come up to about 0.1 m/s faster in one drop than in another, and a landing that
    /// near the limit bounces in some drops and not in others under any limit.
    /// </summary>
    [Theory]
    [InlineData(1f, 0.8f, 19)]
    [InlineData(1f, 1f, 12)]
    [InlineData(2f, 0.5f, 6)]
    [InlineData(2f, 0.8f, 4)]
    [InlineData(2f, 1f, 3)]
    [InlineData(2f, 1f, 6)]
    [InlineData(3f, 0.5f, 4)]
    [InlineData(3f, 0.8f, 3)]
    [InlineData(3f, 1f, 2)]
    [InlineData(3f, 1f, 4)]
    public void ADropReportsTheSameLandingsWhereverItsLandingStepBeginsInAnyGravity(float gravity, float restitution,
        int fallSteps)
    {
        float g = gravity * G;
        List<ContactNews>? landings = null;
        foreach (double gap in LandingGaps())
        {
            Drop drop = DropBall(fallSteps, gap, restitution, fallSteps + 240, g);

            string because = $"the landing step begins {gap * 100:F3} cm above the floor";
            landings ??= drop.BallNews;
            drop.BallNews.Should().Equal(landings, because);
            drop.FloorNews.Should().Equal(landings, because);
            drop.Flights.Should().OnlyContain(highest => highest > SpeculativeDistance, because);
        }
    }

    /// <summary>
    /// Jolt takes the push of every accumulated force off a rebound, not only gravity's, and the rule reads that force
    /// from Jolt. In zero gravity an engine pushes a ball down onto a floor at 3 g, so a rule that saw only gravity would
    /// keep every bounce. Every gap reports the same landings, and every bounce that the rule keeps leaves Jolt's 2 cm.
    /// </summary>
    [Theory]
    [InlineData(1f, 2)] // lands at 1.47 m/s: Jolt's rebound of 0.98 m/s would rise 1.6 cm
    [InlineData(0.8f, 2)] // lands at 1.47 m/s: Jolt's rebound of 0.78 m/s would rise 1 cm
    [InlineData(0.8f, 4)] // lands at 2.45 m/s: Jolt's rebound of 1.57 m/s rises 4.2 cm
    public void ADropThatAnEnginePushesReportsTheSameLandingsWhereverItStarts(float restitution, int fallSteps)
    {
        List<ContactNews>? landings = null;
        foreach (double gap in LandingGaps())
        {
            Drop drop = ThrustBall(fallSteps, gap, restitution, fallSteps + 240, 3 * G);

            string because = $"the landing step begins {gap * 100:F3} cm above the floor";
            drop.ApproachSpeeds[0].Should().BeApproximately((fallSteps + 1) * 3 * G / 60, 0.01f, because);
            landings ??= drop.BallNews;
            drop.BallNews.Should().Equal(landings, because);
            drop.FloorNews.Should().Equal(landings, because);
            drop.Flights.Should().OnlyContain(highest => highest > SpeculativeDistance, because);
        }
    }

    /// <summary>
    /// In zero gravity nothing brings a bounce back, so it leaves Jolt's 2 cm however slowly it goes, and the rule keeps
    /// every bounce. A crate of 1 kg with restitution 0.5 hits a static wall at 1.5 m/s and flies back at 0.75 m/s. Under
    /// the old limit of a rebound of 1 m/s, it stuck to the wall.
    /// </summary>
    [Fact]
    public void InZeroGravityACrateThatHitsAWallSlowlyStillBouncesBack()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var wallLog = new CollisionLog();
        Entity wall = world.Spawn(new()
        {
            Frame = space,
            Body = new(new BoxShapeDesc(new(0.5f, 5, 5)), BodyMotion.Static),
            Components = [wallLog]
        });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new()
        {
            Frame = space,
            Pose = At(-3),
            LinearVelocity = new Vector3(1.5f, 0, 0),
            Body = new(new BoxShapeDesc(new(0.5f)), Mass: 1, Restitution: 0.5f, LinearDamping: 0, AngularDamping: 0),
            Components = [crateLog]
        });

        Tick(world, 120);

        world.VelocityInOwnFrame(crate).X.Should().BeApproximately(-0.75f, 0.02f);
        crateLog.NewsOf(wall).Should().Equal(Started, Ended);
        wallLog.NewsOf(crate).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Jolt decides restitution at each contact point, from that point's own approach, but a contact callback sets one
    /// restitution for the whole manifold. The point whose rebound is largest bounces the body, so the rule decides from
    /// that point. A box of 1 m, 10 kg and restitution 0.5 spins at 1.5 rad/s about +Z and at 1 rad/s about +X, and lands flat on
    /// a floor, its landing step beginning 1 cm above it. With the gravity of the step its center comes down at 1.66 m/s.
    /// Its four bottom corners come down at 2.91, 1.91, 1.41 and 0.41 m/s, and Jolt bounces the three that come down
    /// faster than 1 m/s. It rebounds the leading corner at 0.5 × (2.91 − 0.16) m/s = 1.37 m/s, which rises 9.6 cm. The
    /// next corner would rebound at 0.87 m/s, which rises 3.9 cm, and the face's center, the mean of the points, at only
    /// 0.75 m/s. A rule that decided at either of those, or at the slowest corner that bounces, removed the bounce, so the
    /// leading corner only closed its gap of 1 cm.
    /// </summary>
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(1f, -1f)]
    [InlineData(-1f, -1f)]
    public void ASpinningBoxThatLandsFlatBouncesAtItsLeadingCorner(float signX, float signZ)
    {
        // The four rows mirror the spin, so that the leading corner is a different point of Jolt's manifold in each.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        Floor(world, ground);
        Entity box = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, 0.51),
            LinearVelocity = new Vector3(0, -1.5f, 0),
            AngularVelocity = new(signX * 1, 0, signZ * 1.5f),
            Body = new(new BoxShapeDesc(new(0.5f)), Mass: 10, Restitution: 0.5f, LinearDamping: 0, AngularDamping: 0)
        });

        world.Tick();

        // The spin carries the bottom corner at -X, +Z down fastest, or its mirror.
        DPose pose = world.PoseIn(box, ground);
        Vector3 corner = pose.Rotation.Rotate(new(-signZ * 0.5f, -0.5f, signX * 0.5f));
        Vector3 cornerVelocity = world.VelocityIn(box, ground).ToVector3() +
                                 Vector3.Cross(world.AngularVelocityIn(box, ground), corner);
        cornerVelocity.Y.Should().BeGreaterThan(1f, "Jolt bounced the leading corner at about 1.37 m/s");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Numeric frames are an engine detail, so the rule keeps or removes a bounce as it does without them. A ball of
    /// restitution 0.5 skims a floor 3 km long at 1,200 m/s under 1 g, 20 m per step, fast enough for a falling frame of its
    /// own under the default rules, as a twin of it with the same velocity, 5 km to the side of the floor, shows. With
    /// numeric frames the merge test links the ball to the floor, an anchor, so the ball stays in the floor's system, where
    /// it lands on the real floor (ter-dhkf). After a fall of 10 steps the ball lands at about 1.7 m/s. Its bounce of
    /// 0.5 × (1.7 − 0.16) m/s = 0.78 m/s would rise 3.1 cm under the pull of 1 g, so it does not bounce, with numeric frames
    /// or without. After a fall of 30 steps it lands at 5 m/s and bounces 30 cm and then 7 cm, both ways. Before ter-dhkf,
    /// the ball fell in a frame of its own, where the floor's ghost came up at it through the ghost pass, and the rule had to
    /// read the real pull of 1 g, not Jolt's pull there.
    /// </summary>
    [Theory]
    [InlineData(true, 10, 1)]
    [InlineData(false, 10, 1)]
    [InlineData(true, 30, 3)]
    [InlineData(false, 30, 3)]
    public void AFastBallThatSkimsAFloorJoinsItsSystemAndBouncesAsItDoesWithoutNumericFrames(bool numericFrames,
        int fallSteps, int landings)
    {
        using World world = NewWorld(out _,
            configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);
        FrameHandle ground = Ground(world, G);
        var floorLog = new CollisionLog();
        Entity floor = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(1_400, -0.5),
            Body = new(new BoxShapeDesc(new(1_500, 0.5f, 10)), BodyMotion.Static),
            Components = [floorLog]
        });
        var ballLog = new CollisionLog();
        double fall = 0.5 * G * Math.Pow(fallSteps * world.DeltaTime, 2);
        var skim = new Vector3(1_200, 0, 0);
        var ballBody = new BodyDesc(new SphereShapeDesc(Radius), Mass: 1, Restitution: 0.5f, LinearDamping: 0,
            AngularDamping: 0);
        Entity ball = world.Spawn(new()
        {
            Frame = ground, Pose = At(0, Radius + fall + 0.01), LinearVelocity = skim, Body = ballBody,
            Components = [ballLog]
        });
        Entity twin = world.Spawn(new()
            { Frame = ground, Pose = At(0, Radius + fall + 0.01, 5_000), LinearVelocity = skim, Body = ballBody });
        Tick(world, 2);
        world.Frames.Numeric(world.PhysicsHomeOf(twin)).Should().Be(
            numericFrames ? NumericMode.Falling : NumericMode.None,
            "the twin of the ball, which no anchor links, falls in a numeric frame of its own, when there are numeric frames");
        world.PhysicsHomeOf(ball).Should()
            .Be(ground, "the ball joins the home of the floor, with numeric frames or without");

        Tick(world, fallSteps + 90);

        ballLog.NewsOf(floor).Should().Equal(Landings(landings));
        floorLog.NewsOf(ball).Should().Equal(Landings(landings));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ball at restitution 0.3 lands after a fall of 22 steps (0.66 m) at 3.68 m/s. Jolt takes off the speed that gravity
    /// added in the step, so the ball leaves at 0.3 × (3.68 − 0.16) m/s = 1.05 m/s and rises about 5.7 cm, above the
    /// minimum of 4 cm and out of Jolt's 2 cm: the contact ends, and the ball lands again. (Before: the test named the old
    /// limit, a rebound of 1 m/s without the 0.16 m/s, which this bounce also passed at 1.1 m/s.)
    /// </summary>
    [Fact]
    public void ABounceThatRisesFourCentimetersEndsTheContactAndTheBallLandsAgain()
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
    /// and 1.7 m/s. The fifth lands at about 1.4 m/s, or at 1.21 m/s, whose bounce of 0.8 × (1.21 − 0.16) m/s = 0.84 m/s
    /// would rise only 3.6 cm, so it does not bounce in its first step: the speculative contact lets the ball close its
    /// gap at almost its full speed, so it touches at 1.19 m/s, and the gravity of the next step raises its approach to
    /// 1.35 m/s, a bounce of 0.95 m/s that rises 4.6 cm. Every bounce leaves Jolt's 2 cm. The sixth landing comes at 1.0
    /// to 1.2 m/s, whose bounce would rise less than 4 cm, and the ball rests there, wherever its first landing step
    /// begins. (Before: the test named the old limit, a rebound of 1 m/s without the 0.16 m/s. The landings are the same.)
    /// </summary>
    [Fact]
    public void ARubberBallBouncesUntilItsBounceWouldRiseLessThanFourCentimetersAndThenRests()
    {
        const float rubber = 0.8f;
        foreach (double gap in LandingGaps())
        {
            Drop drop = DropBall(19, gap, rubber, 240);

            string because = $"the first landing step begins {gap * 100:F3} cm above the floor";
            drop.BallNews.Should().Equal(Landings(6), because);
            drop.FloorNews.Should().Equal(Landings(6), because);
            drop.Flights.Should().HaveCount(5).And.OnlyContain(highest => highest > 0.035, because);
            float lastBounce = rubber * (drop.ApproachSpeeds[^1] - G / 60);
            (lastBounce * lastBounce / (2 * G)).Should().BeLessThan(FrameWorld.MinReboundHeight, because);
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
        Entity crate = world.Spawn(new()
            { Frame = ground, Pose = At(0, 10.5), Body = new(new BoxShapeDesc(new(0.5f))), Components = [crateLog] });

        double highest = double.NegativeInfinity;
        for (int i = 0; i < 150; i++)
        {
            world.Tick();
            if (crateLog.Seen.Count > 0)
                highest = Math.Max(highest, world.PoseInOwnFrame(crate).Position.Y - 0.5);
        }

        crateLog.Seen.Should().ContainSingle().Which.Impulse.Should()
            .BeGreaterThan(13f, "the crate of 1 kg landed at about 14 m/s");
        crateLog.NewsOf(floor).Should().Equal(Started);
        highest.Should().BeLessThan(0.002, "the crate did not rise after it landed");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ball of radius 2 cm, thrown down at 8 m/s, moves 14 cm in a step, much more than 0.75 of its radius, so Jolt casts
    /// it along its motion (<see cref="MotionQuality.LinearCast"/>). Its landing step begins 2.5 cm above a plate of 4 mm,
    /// beyond Jolt's speculative contact distance, and would end with the whole ball below the plate, so only the cast finds
    /// the plate. The ball lands at 8.57 m/s. The cast does not take the pull of the step off the rebound, but the rule
    /// does, as for every contact, so it judges the bounce by 0.16 m/s less. At restitution 0.1 that is 0.84 m/s, which
    /// rises 3.6 cm, so the ball does not bounce. At 0.2 it is 1.7 m/s, so the ball bounces about 15 cm up and lands a
    /// second time. (Before: the test named the old limit, a rebound of 1 m/s. The landings are the same.)
    /// </summary>
    [Theory]
    [InlineData(0.1f, 1)]
    [InlineData(0.2f, 2)]
    public void AFastBallThatJoltCastsBouncesOnlyWhenItsBounceRisesFourCentimeters(float restitution, int landings)
    {
        const float radius = 0.02f, speed = 8f;
        const int fallSteps = 3;
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, G);
        var plateLog = new CollisionLog();
        Entity plate = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, -0.002),
            Body = new(new BoxShapeDesc(new(5, 0.002f, 5)), BodyMotion.Static),
            Components = [plateLog]
        });
        var ballLog = new CollisionLog();
        double t = fallSteps * world.DeltaTime;
        Entity ball = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, radius + 0.025 + speed * t + 0.5 * G * t * t),
            LinearVelocity = new Vector3(0, -speed, 0),
            Body = new(new SphereShapeDesc(radius), Mass: 1, Restitution: restitution, LinearDamping: 0,
                AngularDamping: 0),
            Components = [ballLog]
        });
        world.Tick();
        backend.BodiesIn(ground).Single(facts => BodyUserData.Names(facts.UserData, ball)).Quality.Should()
            .Be(MotionQuality.LinearCast);

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
    /// A kinematic block in a child frame sticks 1 m up out of the frame's bubble, so its ghost stands in the parent. A
    /// ball in the parent falls 1.48 m onto the ghost under 1 g and lands at 5.5 m/s. The rule holds for a contact with a
    /// ghost too: at restitution 0.1 the bounce of 0.1 × (5.5 − 0.16) m/s = 0.53 m/s would rise 1.4 cm, so the ball does
    /// not bounce, and at 0.35 it rises 18 cm and lands again, at a speed whose bounce would rise 2 cm. (Before: the ball
    /// flew at the ghost in zero gravity, where the rule now keeps every bounce, so the scene now has gravity.)
    /// </summary>
    [Theory]
    [InlineData(0.1f, 1)]
    [InlineData(0.35f, 2)]
    public void ABallThatFallsOntoAGhostBouncesOnlyWhenItsBounceRisesFourCentimeters(float restitution, int landings)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Ground(world, G);
        FrameHandle bubble = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space, AsFrame = new(FrameBounds.Sphere(10))
        }));
        Entity block = world.Spawn(new()
            { Frame = bubble, Pose = At(0, 10), Body = new(new BoxShapeDesc(new(1)), BodyMotion.Kinematic) });
        var ballLog = new CollisionLog();
        double fall = 0.5 * G * Math.Pow(33 * world.DeltaTime, 2);
        Entity ball = world.Spawn(new()
        {
            Frame = space,
            Pose = At(0, 11 + Radius + fall + 0.01),
            Body = new(new SphereShapeDesc(Radius), Mass: 1, Restitution: restitution, LinearDamping: 0,
                AngularDamping: 0),
            Components = [ballLog]
        });
        world.Tick();
        faults.Jolt.HasGhostIn(block, space).Should().BeTrue();

        Tick(world, 150);

        world.GetFrame(ball).Should().Be(space);
        world.PoseInOwnFrame(ball).Position.Y.Should()
            .BeGreaterThan(11 + Radius - 0.05, "the ball did not go through the ghost");
        ballLog.NewsOf(block).Should().Equal(Landings(landings));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ghost moves as its real body does, so the ball bounces as on the real floor (ter-dq92). A ship flies at 0.7 m/s
    /// past a parked station whose hull is a floor of triangles, under the ship's own gravity of 1 g, and the ship's bubble
    /// takes in the hull, so the ship's system holds a ghost of it. That ghost moves in the ship at the ship's velocity
    /// backward, so it is a kinematic body that Jolt moves with that velocity. A ball of restitution 0.5 in the ship
    /// falls onto the ghost. Jolt computes the bounce from the approach to the ghost, which is the approach to the real
    /// floor, and the minimum rebound reads the same approach.
    /// <list type="bullet">
    /// <item>The ship sinks, so the floor comes up at the ball. The ball lands at about 1.7 m/s in the ship, 2.4 m/s
    /// against the floor. Jolt rebounds it at 0.5 × (2.4 − 0.16) m/s = 1.12 m/s from the floor, which rises 6.4 cm, so
    /// the ball bounces. (Before, the ghost was a static body that the backend put at the hull's place after every step,
    /// so Jolt saw it at rest and rebounded at 0.5 × (1.7 − 0.16) m/s, which rises 3 cm, and the ball did not bounce.)</item>
    /// <item>The ship rises, so the floor falls away. The ball lands at about 2.3 m/s in the ship, 1.6 m/s against the
    /// floor. Jolt would rebound it at 0.5 × (1.6 − 0.16) m/s = 0.72 m/s, which rises 2.6 cm, so the ball does not bounce,
    /// and it stops on the floor. (Before, Jolt rebounded at 0.5 × (2.3 − 0.16) m/s, and the ball bounced.)</item>
    /// </list>
    /// The test reads the ball's velocity in space, where the floor rests, after its landing step: the step after which
    /// it no longer falls faster than 0.5 m/s.
    /// </summary>
    [Theory]
    [InlineData(-0.7f, 0.268, true)]
    [InlineData(0.7f, 0.106, false)]
    public void ABallThatFallsOntoTheGhostOfAMovingMeshBouncesAsOnTheRealFloor(float shipVelocity, double drop,
        bool bounces)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        // The station's hull: 40 m by 40 m of triangles at y = 0, facing up. Its bubble is 3 m around the origin, far from
        // where the ball lands, so the ball never enters it.
        var hull = new MeshShapeDesc([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)],
            [0, 2, 1, 0, 3, 2]);
        Entity station = world.Spawn(new()
        {
            Frame = space,
            Body = new(hull, BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new(FrameBounds.Sphere(3))
        });
        Entity shipOwner = world.Spawn(new()
        {
            Frame = space,
            Pose = At(12, 20),
            LinearVelocity = new Vector3(0, shipVelocity, 0),
            Body = new(new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0,
                InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new(FrameBounds.Sphere(30), GravityField.Uniform(new(0, -G, 0)))
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity ball = world.Spawn(new()
        {
            Frame = ship,
            Pose = At(0, -20 + Radius + drop),
            Body = new(new SphereShapeDesc(Radius), Mass: 1, Restitution: 0.5f, LinearDamping: 0, AngularDamping: 0)
        });

        double? afterLanding = null;
        bool falling = false;
        for (int i = 0; i < 60 && afterLanding is null; i++)
        {
            world.Tick();
            double vertical = world.VelocityIn(ball, space).Y;
            if (vertical < -1)
                falling = true;
            else if (falling && vertical > -0.5)
                afterLanding = vertical;
        }

        faults.Jolt.HasGhostIn(station, ship).Should().BeTrue();
        world.GetFrame(ball).Should().Be(ship);
        afterLanding.Should().NotBeNull("the ball landed");
        if (bounces)
            afterLanding.Should().BeGreaterThan(0.9, "Jolt rebounds the ball at about 1.12 m/s from the floor");
        else
            afterLanding.Should().BeInRange(-0.2, 0.1, "the ball stops on the floor");
        world.Errors.Should().BeEmpty();
    }
}
