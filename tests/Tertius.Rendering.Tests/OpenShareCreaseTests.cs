namespace Tertius.Rendering.Tests;

/// <summary>
/// The open share S in a crease beside a small bright light in the map, on the C# copy
/// (docs/plans/screen-space-illumination.md, decision 42): the open directions' light is D(n) · S, where D comes from
/// another store of the map than the reads of S, so the two stores need not agree. The receiver faces the eye 2 m away;
/// a black wall, the plane x = 0.05 m facing −x, meets it in a crease. The map is a dim sky and a bright spot that lies
/// along the directions that the wall blocks within the radius, as a map drawn from the camera holds what the camera
/// sees over a near blocker (the third item of "Known limits").
/// </summary>
public sealed class OpenShareCreaseTests(ITestOutputHelper output)
{
    private const double Radius = 1;
    private const double ThicknessShare = 0.1;
    private const double WallX = 0.05;

    /// <summary>The share by which the store of D falls short of the reads' irradiance: band-2 spherical harmonics fell short by more under a small bright light (ter-xgra).</summary>
    private const double StoreShortfall = 0.15;

    private static readonly Double3 Center = new(0, 0, 2);
    private static readonly Double3 TowardTheEye = new(0, 0, -1);
    private static readonly Double3 WallNormal = new(-1, 0, 0);
    private static readonly Double3 SkyLight = new(0.2, 0.3, 0.5);
    private static readonly Double3 SpotLight = new(40, 30, 15);

    /// <summary>The spot's direction from the center, toward the wall: the wall lies about 6 cm away along it.</summary>
    private static readonly Double3 SpotDirection = new Double3(0.8, 0.2, -0.55).Normalized();

    /// <summary>The spot's angular spread, in rad: a sixteenth of the hemisphere's width.</summary>
    private const double SpotSpread = 0.2;

    /// <summary>
    /// A store of D that falls 15 % short of the reads' irradiance scales the open light by 0.85 and changes nothing
    /// else: S is a ratio of reads from the specular map alone. The subtraction D(n) − M of before took the stores'
    /// disagreement from the whole irradiance, which is twenty times the open light in red here: it gave 2.5 times the
    /// reference in red and 0.66 times it in blue, a change of hue.
    /// </summary>
    [Fact]
    public void AStoreOfDThatFallsShortScalesTheCreasesOpenLightAndNothingElse()
    {
        Double3 irradiance = Irradiance();
        AmbientEstimate estimate = Estimate(slices: 3, sliceNoise: 0.5f);

        Double3 exact = DiffuseLight(irradiance, estimate);
        Double3 short_ = DiffuseLight(irradiance * (1 - StoreShortfall), estimate);
        output.WriteLine($"open light with D exact {exact}, with D 15 % short {short_}");

        foreach (
            (double actual, double expected) in (ReadOnlySpan<(double Actual, double Expected)>)
                [(short_.X, exact.X), (short_.Y, exact.Y), (short_.Z, exact.Z)]
        )
            actual.Should().BeApproximately((1 - StoreShortfall) * expected, 1e-12 * expected);
    }

    /// <summary>
    /// The crease's S at 3 slices, in the mean over 64 turns of the slices, as the denoise's neighbors give it, lies
    /// within 0.03 of S at 64 slices in each channel: the open light within 3 % of the map's irradiance. The tolerance is
    /// the bias of a ratio of sums over 3 slices where one blocked direction holds most of the map's light, as the spot
    /// does here: S at 3 slices measured +0.017, +0.025 and +0.027 above S at 64 slices in red, green and blue
    /// (1.34, 1.27 and 1.13 times it). A bias of 40 % on S at 3 slices takes every channel past it.
    /// </summary>
    [Fact]
    public void ACreaseBesideASmallBrightLightInTheMapHasAtThreeSlicesTheOpenShareOfManySlicesWithinThreeHundredths()
    {
        const int turns = 64;
        Double3 reference = Estimate(slices: 64, sliceNoise: 0.5f).OpenShare;
        Double3 open = Double3.Zero;
        for (int turn = 0; turn < turns; turn++)
            open += Estimate(slices: 3, sliceNoise: (turn + 0.5f) / turns).OpenShare / turns;
        output.WriteLine($"S at 64 slices {reference}, at 3 slices {open}");

        reference.X.Should().BeLessThan(0.3, "the test needs a crease whose open share is small");
        foreach (
            (double actual, double expected) in (ReadOnlySpan<(double Actual, double Expected)>)
                [(open.X, reference.X), (open.Y, reference.Y), (open.Z, reference.Z)]
        )
            actual.Should().BeApproximately(expected, 0.03, "the slices' noise averages out over the turns");
    }

    /// <summary>
    /// D(n) · S at 64 slices lies within 15 % of the map's light over the directions that the wall leaves open within the
    /// radius, by quadrature, in each channel. It measured 13 %, 12 % and 8 % short in red, green and blue: the lobe
    /// bound's price (decision 42), as a read of an open piece beside the wall is blurred into the dim sky and a read of
    /// a blocked piece into the spot.
    /// </summary>
    [Fact]
    public void ACreaseBesideASmallBrightLightInTheMapGetsTheMapsLightOverItsOpenDirectionsWithinFifteenPercent()
    {
        Double3 irradiance = Irradiance();
        Double3 reference = Irradiance(onlyOpen: true);
        Double3 open = DiffuseLight(irradiance, Estimate(slices: 64, sliceNoise: 0.5f));
        output.WriteLine(
            $"irradiance {irradiance}, over the open directions {reference}, open light at 64 slices {open}"
        );

        reference.X.Should().BeLessThan(0.3 * irradiance.X, "the test needs a crease whose open light is small");
        foreach (
            (double actual, double expected) in (ReadOnlySpan<(double Actual, double Expected)>)
                [(open.X, reference.X), (open.Y, reference.Y), (open.Z, reference.Z)]
        )
            actual
                .Should()
                .BeApproximately(
                    expected,
                    0.15 * expected,
                    "the open directions bring the map's light along them, through the bound's lobe"
                );
    }

    /// <summary>The diffuse light of a white receiver of the map's irradiance <paramref name="irradiance"/> under <paramref name="estimate"/>.</summary>
    private static Double3 DiffuseLight(Double3 irradiance, AmbientEstimate estimate) =>
        new AmbientTerms(Diffuse: irradiance, Specular: Double3.Zero)
            .Occluded(
                occlusion: estimate.Occlusion,
                openShare: estimate.OpenShare,
                bounce: estimate.Bounce,
                diffuseColor: new(1, 1, 1),
                nDotV: 1,
                lobe: new GgxLobe(1)
            )
            .Diffuse;

    /// <summary>The estimate of the receiver with <paramref name="slices"/> slices, turned by <paramref name="sliceNoise"/>.</summary>
    private static AmbientEstimate Estimate(int slices, float sliceNoise)
    {
        var near = new NearSlice[slices];
        for (int slice = 0; slice < slices; slice++)
        {
            var sectors = new SliceSectors(
                new HorizonSlice(
                    toEye: TowardTheEye,
                    normal: TowardTheEye,
                    angle: HorizonSearch.SliceAngle(slice, slices, sliceNoise)
                )
            );
            near[slice] = SectorSearch.Near(
                sectors: sectors,
                center: Center,
                ownSurface: new OwnPlane(TowardTheEye),
                radius: Radius,
                thicknessShare: ThicknessShare,
                steps: [.. WallSteps(sectors.Slice)],
                mapLight: (direction, _) => MapLight(direction)
            );
        }
        return AmbientEstimate.OfSlices(near);
    }

    /// <summary>
    /// The steps of a slice that see the wall, nearest first and plus side first: every 2 mm along the slice's line on
    /// the screen plane of the receiver, a few to a texel, the point where the view ray through it meets the wall. The steps that see the
    /// receiver's own plane block nothing, so they are left out.
    /// </summary>
    private static IEnumerable<NearStep> WallSteps(HorizonSlice slice)
    {
        for (int step = 1; step <= 300; step++)
            foreach (SliceSide side in new[] { SliceSide.Plus, SliceSide.Minus })
            {
                Double3 place = Center + slice.Direction * ((int)side * 0.002 * step);
                if (place.X > WallX)
                    yield return new(
                        Position: place * (WallX / place.X),
                        GeometricNormal: WallNormal,
                        DirectLight: Double3.Zero,
                        Side: side
                    );
            }
    }

    /// <summary>The map's radiance along a unit view-space direction: the dim sky and the bright spot.</summary>
    private static Double3 MapLight(Double3 direction)
    {
        double angle = Math.Acos(Math.Clamp(Double3.Dot(direction, SpotDirection), -1, 1));
        return SkyLight + SpotLight * Math.Exp(-angle * angle / (2 * SpotSpread * SpotSpread));
    }

    /// <summary>
    /// The map's irradiance at the receiver's normal over π, by the midpoint rule over its hemisphere, or with
    /// <paramref name="onlyOpen"/> over the directions that the wall leaves open within the radius: those that reach
    /// the plane x = <see cref="WallX"/> only beyond <see cref="Radius"/>.
    /// </summary>
    private static Double3 Irradiance(bool onlyOpen = false)
    {
        const int rings = 400;
        const int segments = 800;
        Double3 sum = Double3.Zero;
        for (int ring = 0; ring < rings; ring++)
        {
            double polar = (ring + 0.5) * (Math.PI / 2) / rings;
            for (int segment = 0; segment < segments; segment++)
            {
                double azimuth = (segment + 0.5) * 2 * Math.PI / segments;
                Double3 direction = new(
                    Math.Sin(polar) * Math.Cos(azimuth),
                    Math.Sin(polar) * Math.Sin(azimuth),
                    -Math.Cos(polar)
                );
                if (onlyOpen && direction.X * Radius > WallX)
                    continue;
                sum += MapLight(direction) * (Math.Cos(polar) * Math.Sin(polar));
            }
        }
        return sum * (Math.PI / 2 / rings * (2 * Math.PI / segments) / Math.PI);
    }
}
