namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="MapSearch"/>: where a reflected ray first goes behind the stored surfaces of a map, against analytic
/// scenes, and the inverse distance that a half float stores (docs/plans/environment-map-distances.md, "The search, on
/// the CPU"). The eye is the origin, and every position is in m.
/// </summary>
public sealed class MapSearchTests
{
    /// <summary>The face size of the default map, whose level-0 texel spans 2 / W rad at a face's center.</summary>
    private const int FaceSize = 128;

    /// <summary>α of the roughness floor: a mirror, whose reads all take level 0.</summary>
    private static readonly double MirrorAlpha = new GgxLobe(0).Alpha;

    [Theory]
    [InlineData(0, -1.5, -4, 0, 1.5, -4)]
    [InlineData(0, 0, -5, 0, 0, 1)]
    [InlineData(3, 1, 2, -1, 0.2, 0.4)]
    public void ARayUnderAnOpenSkyReadsTheMapAlongItsOwnDirection(
        double x,
        double y,
        double z,
        double alongX,
        double alongY,
        double alongZ
    )
    {
        Scene sky = Scene.Sky;
        Double3 direction = new Double3(alongX, alongY, alongZ).Normalized();

        MapHit hit = MapSearch.Hit(
            field: ref sky,
            position: new(x, y, z),
            direction: direction,
            alpha: 0.25,
            faceSize: FaceSize
        );

        hit.Found.Should().BeFalse("no sample lies behind a sky whose every w is 0");
        hit.Direction.Should().Be(direction, "the read without a hit is today's read, bit for bit");
        hit.Reach.Should().Be(1);
    }

    [Fact]
    public void ARayTowardAFarPlanetMovesOnlyByThePlanetsParallax()
    {
        const double planetDistance = 1e7;
        const double planetRadius = 6.4e6;
        var position = new Double3(0, -6, -8);
        var direction = new Double3(0, 0, -1);
        var planet = new Scene(new Sphere(Center: new(0, 0, -(planetDistance + planetRadius)), Radius: planetRadius));

        MapHit hit = MapSearch.Hit(
            field: ref planet,
            position: position,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        double sine = Double3.Cross(position.Normalized(), direction).Length;
        double parallax = position.Length * sine / planetDistance;
        Angle(hit.Direction, direction).Should().BeApproximately(parallax, 0.01 * parallax);
    }

    [Fact]
    public void AFlatWallIsFoundExactlyByTheFirstSecantStep()
    {
        var wall = new Plane(Normal: Double3.UnitX, Offset: 4);
        var position = new Double3(1, -1.5, -6);
        Double3 direction = new Double3(1, 0.3, 0.2).Normalized();
        var scene = new Scene(wall);

        MapSearch.Hit(
            field: ref scene,
            position: position,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        scene.Reads.Should().HaveCountGreaterThan(MapSearch.SecantSteps, "the march and the refinement read the wall");
        Double3 meeting = position + (wall.Offset - position.X) / direction.X * direction;
        // The refinement always takes SecantSteps reads after the march, so this is the first secant step's read.
        Double3 firstSecantRead = scene.Reads[^MapSearch.SecantSteps].Direction;
        Angle(firstSecantRead, meeting).Should().BeLessThan(1e-5, "the clearance is linear in the sweep on a plane");
    }

    [Fact]
    public void TheRefinementTakesEverySecantStepAfterTheFirstLandsOnAFlatWall()
    {
        // A wall 1 km ahead: the ray goes behind it only between t = 7/8 and t = 1, the march's last bracket.
        var scene = new Scene(new Plane(Normal: new(0, 0, -1), Offset: 1000));

        MapSearch.Hit(
            field: ref scene,
            position: new(0, -1.5, -4),
            direction: new Double3(0, 0.5, -1).Normalized(),
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        scene
            .Reads.Should()
            .HaveCount(
                MapSearch.Samples + 1 + MapSearch.SecantSteps,
                "every pixel's search has one bound of reads, with no early stop on a small clearance"
            );
    }

    [Fact]
    public void ARayThatMeetsARoundSurfaceFindsItWithinAHundredthOfATexel()
    {
        // A ball 2 m in radius whose clearance along the ray curves, so that a secant place can land behind the ball.
        var ball = new Sphere(Center: new(0, 1, -8), Radius: 2);
        var scene = new Scene(ball, new Plane(Normal: new(0, 0, -1), Offset: 30));
        var position = new Double3(0, -1.5, -4);
        Double3 direction = new Double3(0, 0.375, -1).Normalized();

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: position,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        Double3 fromCenter = position - ball.Center;
        double along = Double3.Dot(fromCenter, direction);
        double rayLength = -along - Math.Sqrt(along * along - (fromCenter.LengthSquared - ball.Radius * ball.Radius));
        Double3 meeting = position + rayLength * direction;
        hit.Found.Should().BeTrue();
        Angle(hit.Direction, meeting)
            .Should()
            .BeLessThan(0.01 * 2.0 / FaceSize, "the refinement narrows the bracket from whichever end the sample lies at");
    }

    [Fact]
    public void ARayFindsTheBoxBeforeTheWallBehindIt()
    {
        var box = new Box(Min: new(-1, -1.7, -6), Max: new(1, -0.7, -5));
        var scene = new Scene(
            new Plane(Normal: new(0, -1, 0), Offset: 1.7),
            new Plane(Normal: new(0, 0, -1), Offset: 10),
            box
        );
        var onTheFloor = new Double3(0, -1.7, -4);
        Double3 mirrored = new Double3(0, 1.7, -4).Normalized();

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: onTheFloor,
            direction: mirrored,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        hit.Found.Should().BeTrue();
        scene.Nearest(hit.Direction)!.Value.Surface.Should().BeSameAs(box);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    public void ARoughFloorsRayFindsTheBaseOfAWallNearerThanTheFirstEvenSample(double distanceToWall)
    {
        // A floor point 4.35 m from the eye, and a wall that meets the floor. distanceToWall is the floor point's
        // distance in m from the wall's base, along the floor. The ray meets the wall nearer than |x| / 7, the ray's
        // length at the first even sample, t = 1/8.
        var onTheFloor = new Double3(0, -1.5, -4.08);
        var wall = new Plane(Normal: new(0, 0, -1), Offset: -onTheFloor.Z + distanceToWall);
        var scene = new Scene(new Plane(Normal: new(0, -1, 0), Offset: 1.5), wall);
        Double3 mirrored = new Double3(0, 1.5, -4.08).Normalized();

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: onTheFloor,
            direction: mirrored,
            alpha: 0.6 * 0.6,
            faceSize: FaceSize
        );

        hit.Found.Should().BeTrue("decision C: a rough floor beside a wall reflects the wall's base");
        Double3 meeting = onTheFloor + distanceToWall / -mirrored.Z * mirrored;
        Angle(hit.Direction, meeting).Should().BeLessThan(1e-9, "the clearance is linear in the sweep on a plane");
    }

    [Fact]
    public void ARayThatGoesBehindANearBarBeforeItsFirstEvenSampleStopsBeforeThatSample()
    {
        // A bar floats between the eye and a floor point 4.35 m away. The ray's point at the near sample lies in front,
        // and its point at the first even sample, t = 1/8, lies behind the bar. The even samples alone come out in front
        // after the bar and find the far wall.
        var bar = new Box(Min: new(-1, -0.9, -3), Max: new(1, -0.78, -2.9));
        var scene = new Scene(
            new Plane(Normal: new(0, -1, 0), Offset: 1.5),
            new Plane(Normal: new(0, 0, -1), Offset: 9),
            bar
        );
        var onTheFloor = new Double3(0, -1.5, -4.08);
        Double3 mirrored = new Double3(0, 1.5, -4.08).Normalized();
        Double3 nearPoint = onTheFloor + onTheFloor.Length / 63 * mirrored;
        Double3 firstEvenPoint = onTheFloor + onTheFloor.Length / 7 * mirrored;
        scene.Nearest(nearPoint)!.Value.Surface.Should().NotBeSameAs(bar, "the eye sees the near sample's point");
        scene
            .Nearest(firstEvenPoint)!
            .Value.Surface.Should()
            .BeSameAs(bar, "the bar hides the first even sample's point");

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: onTheFloor,
            direction: mirrored,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        hit.Found.Should().BeTrue();
        // The lookup vectors lie in one plane, so the two angles add up to the arc only for a hit between its ends.
        (Angle(hit.Direction, nearPoint) + Angle(hit.Direction, firstEvenPoint))
            .Should()
            .BeApproximately(
                Angle(nearPoint, firstEvenPoint),
                1e-12,
                "decision 16: the first crossing is the hit, and it lies between the two samples"
            );
    }

    [Fact]
    public void ARayThatStartsBehindAStoredSurfaceCountsOnlyTheCrossingsAfterItComesOutInFront()
    {
        var panel = new Box(Min: new(-0.5, -0.5, -2.1), Max: new(0.5, 0.5, -2));
        var sideWall = new Plane(Normal: Double3.UnitX, Offset: 3);
        var scene = new Scene(panel, sideWall, new Plane(Normal: new(0, 0, -1), Offset: 5));
        // The near sample, 5/63 m along the ray, also lies behind the panel, so front first ignores it too.
        var hidden = new Double3(0, 0, -5);
        Double3 direction = new Double3(1, 0, 0.3).Normalized();
        scene.Nearest(hidden)!.Value.Surface.Should().BeSameAs(panel, "the shaded point lies behind the panel");

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: hidden,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        hit.Found.Should().BeTrue();
        scene
            .Nearest(hit.Direction)!
            .Value.Surface.Should()
            .BeSameAs(sideWall, "the ray goes behind the panel before it comes out in front, and that does not count");
    }

    [Fact]
    public void AMirrorThatFacesTheEyeReflectsWhatLiesBehindTheCamera()
    {
        // A room 8 m around the eye, and a mirror 5 m ahead that faces the eye: the ray goes back through the eye.
        var scene = new Scene(new Sphere(Center: Double3.Zero, Radius: 8), new Plane(Normal: new(0, 0, -1), Offset: 5));

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: new(0, 0, -5),
            direction: Double3.UnitZ,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        hit.Found.Should().BeTrue();
        Angle(hit.Direction, Double3.UnitZ).Should().BeLessThan(1e-9, "the room's wall behind the camera");
        hit.Reach.Should().BeApproximately(13.0 / 8, 1e-9, "13 m of ray to a wall 8 m from the eye");
    }

    [Fact]
    public void ARayThroughTheEyeTakesNoReadThere()
    {
        var scene = new Scene(new Sphere(Center: Double3.Zero, Radius: 8), new Plane(Normal: new(0, 0, -1), Offset: 5));

        MapSearch.Hit(
            field: ref scene,
            position: new(0, 0, -5),
            direction: Double3.UnitZ,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        scene
            .Reads.Should()
            .OnlyContain(
                read => read.Direction.Length >= 1.0 / 1024,
                "a cube read along a zero vector is undefined, and the eye's neighborhood lies in front of every surface"
            );
    }

    [Fact]
    public void ARayThatPassesBehindANearPoleMeetsThePole()
    {
        // The pole's front spans |x| ≤ 0.3 m at 3 m, and the ray passes 0.4 m behind its back, parallel to the far wall.
        const double poleHalfAngle = 0.1;
        var scene = new Scene(
            new Box(Min: new(-0.3, -10, -3.2), Max: new(0.3, 10, -3)),
            new Plane(Normal: new(0, 0, -1), Offset: 30)
        );

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: new(-1.5, 0, -3.6),
            direction: Double3.UnitX,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        hit.Found.Should().BeTrue("the space behind the pole counts as solid");
        double beyondThePole = Math.Atan(Math.Abs(hit.Direction.X / hit.Direction.Z)) - Math.Atan(poleHalfAngle);
        Math.Abs(beyondThePole)
            .Should()
            .BeLessThan(2.0 / FaceSize, "the hit lies within a level-0 texel of the pole's edge");
    }

    [Fact]
    public void TheReachOfAHitIsItsDistanceFromTheShadedPointOverItsDistanceFromTheEye()
    {
        var position = new Double3(1, -1.2, -4);
        Double3 direction = new Double3(0.3, 0.5, -1).Normalized();
        var scene = new Scene(new Plane(Normal: new(0, 0, -1), Offset: 9));

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: position,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        double rayLength = (9 + position.Z) / -direction.Z;
        Double3 meeting = position + rayLength * direction;
        hit.Reach.Should().BeApproximately(rayLength / meeting.Length, 1e-9);
    }

    [Fact]
    public void AHitNearTheShadedPointIsReadSharperThanAHitFarAway()
    {
        const double roughness = 0.6;
        var position = new Double3(0, -1.5, -4);
        Double3 direction = new Double3(0, 0.5, -1).Normalized();
        var nearWall = new Scene(new Plane(Normal: new(0, 0, -1), Offset: 4.9));
        var farWall = new Scene(new Plane(Normal: new(0, 0, -1), Offset: 24));

        MapHit near = MapSearch.Hit(
            field: ref nearWall,
            position: position,
            direction: direction,
            alpha: roughness * roughness,
            faceSize: FaceSize
        );
        MapHit far = MapSearch.Hit(
            field: ref farWall,
            position: position,
            direction: direction,
            alpha: roughness * roughness,
            faceSize: FaceSize
        );

        double rayLength = 0.9 / -direction.Z;
        double reach = rayLength / (position + rayLength * direction).Length;
        near.ReadRoughness(roughness).Should().BeApproximately(roughness * Math.Sqrt(reach), 1e-9);
        near.ReadRoughness(roughness).Should().BeLessThan(far.ReadRoughness(roughness));
    }

    [Fact]
    public void ARayThatMeetsNoSurfaceIsReadAtItsOwnRoughness()
    {
        Scene sky = Scene.Sky;

        MapHit hit = MapSearch.Hit(
            field: ref sky,
            position: new(0, -1.5, -4),
            direction: Double3.UnitY,
            alpha: 0.37 * 0.37,
            faceSize: FaceSize
        );

        hit.ReadRoughness(0.37).Should().Be(0.37, "the read without a hit is today's read");
    }

    [Fact]
    public void AHitFartherFromTheShadedPointThanFromTheEyeIsReadNoRougherThanOne()
    {
        var scene = new Scene(new Sphere(Center: Double3.Zero, Radius: 8), new Plane(Normal: new(0, 0, -1), Offset: 5));

        MapHit hit = MapSearch.Hit(
            field: ref scene,
            position: new(0, 0, -5),
            direction: Double3.UnitZ,
            alpha: 0.81,
            faceSize: FaceSize
        );

        hit.ReadRoughness(0.9).Should().Be(1, "0.9 √(13 / 8) lies above the specular map's roughest level");
    }

    [Fact]
    public void TheConeReadsLevelZeroNearTheShadedPointAndCoarserLevelsFartherAlongTheRay()
    {
        const double alpha = 0.04;
        Scene sky = Scene.Sky;

        MapSearch.Hit(
            field: ref sky,
            position: new(0, -1.5, -4),
            direction: new Double3(0, 1.5, -4).Normalized(),
            alpha: alpha,
            faceSize: FaceSize
        );

        double[] levels = [.. sky.Reads.Select(read => read.Level)];
        levels
            .Should()
            .HaveCount(MapSearch.Samples + 1, "a ray that meets nothing takes the near sample and every even one");
        levels[0].Should().Be(0, "the cone's cross-section at the near sample is narrower than a level-0 texel");
        levels.Should().BeInAscendingOrder();
        levels[^1]
            .Should()
            .BeApproximately(
                Math.Log2(MapSearch.ConeWidthPerAlpha * alpha * FaceSize / 2),
                1e-9,
                "at the ray's end the eye sees the lobe's own width"
            );
    }

    [Fact]
    public void AVeryRoughLobeReadsNoLevelBeyondTheLastOfTheChain()
    {
        Scene sky = Scene.Sky;

        MapSearch.Hit(
            field: ref sky,
            position: new(0, -1.5, -4),
            direction: new Double3(0, 1.5, -4).Normalized(),
            alpha: 1,
            faceSize: FaceSize
        );

        double lastLevel = Math.Log2(FaceSize);
        sky.Reads.Should().OnlyContain(read => read.Level <= lastLevel, "the chain ends at 1 × 1, level log₂ W");
        sky.Reads[^1].Level.Should().Be(lastLevel, "the lobe at the ray's end is wider than a whole face");
    }

    [Fact]
    public void AtTheRaysEndTheReadsTexelSpansTheFullWidthOfTheGgxLobeAtHalfItsPeak()
    {
        const double alpha = 0.04;
        Scene sky = Scene.Sky;

        MapSearch.Hit(
            field: ref sky,
            position: new(0, -1.5, -4),
            direction: new Double3(0, 1.5, -4).Normalized(),
            alpha: alpha,
            faceSize: FaceSize
        );

        // GGX D(θ) ∝ 1 / ((α² − 1) cos² θ + 1)² falls to half its peak where sin² θ = α² (√2 − 1) / (1 − α²). The
        // reflected direction turns by twice the half vector's angle, on both sides of the central ray.
        double halfVector = Math.Asin(alpha * Math.Sqrt((Math.Sqrt(2) - 1) / (1 - alpha * alpha)));
        double fullWidth = 4 * halfVector;
        sky.Reads[^1]
            .Level.Should()
            .BeApproximately(
                Math.Log2(fullWidth * FaceSize / 2),
                0.01,
                "at a reach of 1 the read's texel, 2 / W rad at a face's center, spans the lobe's full width"
            );
    }

    [Fact]
    public void TheInverseDistanceOfASurfaceIsAThousandMetersOverItsDistance() =>
        MapSearch.InverseDistance(4).Should().Be(250);

    [Fact]
    public void ASurfaceAtTheNearPlaneHoldsAnInverseDistanceThatAHalfFloatHolds()
    {
        double nearest = MapSearch.InverseDistance(DepthRanges.NearPlane);

        nearest.Should().BeApproximately(20_000, 1e-3);
        nearest.Should().BeLessThan((double)Half.MaxValue);
        ((double)(Half)nearest)
            .Should()
            .BeApproximately(nearest, nearest * Math.Pow(2, -11), "a normal half float rounds to 2⁻¹¹ of its value");
    }

    [Fact]
    public void ASurfaceNearerThanTheNearPlaneHoldsTheNearPlanesInverseDistance() =>
        MapSearch.InverseDistance(0.01).Should().Be(MapSearch.InverseDistance(DepthRanges.NearPlane));

    [Fact]
    public void ASurfaceBeyondTheHalfFloatsNormalRangeLosesUnderAHundredthOfATexelOfParallax()
    {
        // 16,500 km ahead: just beyond 16,400 km, where w falls below the half float's smallest normal value.
        var surface = new Scene(new Plane(Normal: new(0, 0, -1), Offset: 1.65e7));
        var position = new Double3(2_000, 0, 0);
        var direction = new Double3(0, 0, -1);
        Half.IsSubnormal((Half)surface.At(direction, level: 0)).Should().BeTrue();
        var kept = new HalfStored(surface, flushesSubnormals: false);
        var flushed = new HalfStored(surface, flushesSubnormals: true);

        MapHit keptHit = MapSearch.Hit(
            field: ref kept,
            position: position,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );
        MapHit flushedHit = MapSearch.Hit(
            field: ref flushed,
            position: position,
            direction: direction,
            alpha: MirrorAlpha,
            faceSize: FaceSize
        );

        keptHit.Found.Should().BeTrue("a device that keeps the subnormal finds the surface");
        Angle(keptHit.Direction, flushedHit.Direction).Should().BeLessThan(0.01 * 2.0 / FaceSize);
    }

    /// <summary>The angle between two directions, which need not be unit, in rad.</summary>
    private static double Angle(Double3 a, Double3 b) => Math.Atan2(Double3.Cross(a, b).Length, Double3.Dot(a, b));

    /// <summary>A surface of an analytic scene.</summary>
    private interface ISurface
    {
        /// <summary>The distance in m from the eye to the surface along the unit direction, or null where it misses.</summary>
        double? DistanceAlong(Double3 unit);
    }

    /// <summary>The plane of the points p with p · Normal = Offset, where Offset &gt; 0 m: a wall, a floor or a mirror.</summary>
    private sealed record Plane(Double3 Normal, double Offset) : ISurface
    {
        public double? DistanceAlong(Double3 unit)
        {
            double cosine = Double3.Dot(unit, Normal);
            return cosine > 0 ? Offset / cosine : null;
        }
    }

    /// <summary>A box along the axes from Min to Max, which the eye sees from outside.</summary>
    private sealed record Box(Double3 Min, Double3 Max) : ISurface
    {
        public double? DistanceAlong(Double3 unit)
        {
            double entry = 0;
            double exit = double.PositiveInfinity;
            (double Lower, double Upper, double Along)[] slabs =
            [
                (Min.X, Max.X, unit.X),
                (Min.Y, Max.Y, unit.Y),
                (Min.Z, Max.Z, unit.Z),
            ];
            foreach ((double lower, double upper, double along) in slabs)
            {
                if (along == 0)
                {
                    if (lower > 0 || upper < 0)
                        return null;
                    continue;
                }
                entry = Math.Max(entry, Math.Min(lower / along, upper / along));
                exit = Math.Min(exit, Math.Max(lower / along, upper / along));
            }
            return entry <= exit ? entry : null;
        }
    }

    /// <summary>A sphere, which the eye sees from outside, as a planet, or from inside, as a room.</summary>
    private sealed record Sphere(Double3 Center, double Radius) : ISurface
    {
        public double? DistanceAlong(Double3 unit)
        {
            double along = Double3.Dot(unit, Center);
            double discriminant = along * along - (Center.LengthSquared - Radius * Radius);
            if (discriminant < 0)
                return null;
            double root = Math.Sqrt(discriminant);
            if (along - root > 0)
                return along - root;
            return along + root > 0 ? along + root : null;
        }
    }

    /// <summary>
    /// An analytic map: the inverse distance of the nearest of its surfaces, 0 where none lies, at every level. It records
    /// each read in <see cref="Reads"/>.
    /// </summary>
    private readonly struct Scene(params ISurface[] surfaces) : IInverseDistanceField
    {
        /// <summary>A map of open sky: every w is 0.</summary>
        public static Scene Sky => new([]);

        public List<(Double3 Direction, double Level)> Reads { get; } = [];

        public double At(Double3 direction, double level)
        {
            Reads.Add((direction, level));
            return Nearest(direction) is { Distance: var distance } ? MapSearch.InverseDistance(distance) : 0;
        }

        /// <summary>The surface that the eye sees along a direction, which need not be unit, and its distance in m.</summary>
        public (ISurface Surface, double Distance)? Nearest(Double3 direction)
        {
            Double3 unit = direction.Normalized();
            (ISurface Surface, double Distance)? nearest = null;
            foreach (ISurface surface in surfaces)
                if (
                    surface.DistanceAlong(unit) is { } distance
                    && !(nearest is { Distance: var held } && held <= distance)
                )
                    nearest = (surface, distance);
            return nearest;
        }
    }

    /// <summary>A scene as the distance cube stores it: each w rounded to a half float, its subnormals flushed to 0 or kept.</summary>
    private readonly struct HalfStored(Scene scene, bool flushesSubnormals) : IInverseDistanceField
    {
        public double At(Double3 direction, double level)
        {
            var stored = (Half)scene.At(direction, level);
            return flushesSubnormals && Half.IsSubnormal(stored) ? 0 : (double)stored;
        }
    }
}
