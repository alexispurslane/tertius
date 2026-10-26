namespace Tertius.Rendering.Tests;

/// <summary>
/// The near search of the C# copy beside a convex edge whose second face the eye sees at a grazing angle
/// (docs/plans/screen-space-illumination.md, "A near step"): a box's front face F and its side face S, which meet at
/// the edge E. Nothing in view rises above F's plane, so a receiver on F blocks only the sectors below F and bounces
/// nothing. A level camera at the origin, at 640 × 360 and a vertical field of view of 60°, as the GPU tests see it.
/// </summary>
public sealed class ConvexEdgeSectorsTests(ITestOutputHelper output)
{
    private const int Width = 640;
    private const int Height = 360;

    /// <summary>The view depth of F, the plane z = 5 m for x from <see cref="EdgeX"/> to <see cref="FarX"/>.</summary>
    private const double FrontDepth = 5;

    /// <summary>The box's back, where S ends: S is the plane x = <see cref="EdgeX"/> for z from 5 m to 6 m.</summary>
    private const double BackDepth = 6;

    /// <summary>The view x of E, in m: S, seen from the eye at x = 0, lies about 6° off its view rays.</summary>
    private const double EdgeX = 0.5;

    /// <summary>The view x of F's other edge, whose side face the eye does not see.</summary>
    private const double FarX = 2;

    private static readonly double TanHalfY = Math.Tan(Math.PI / 6);
    private static readonly double TanHalfX = TanHalfY * Width / Height;
    private static readonly (int Width, int Height) HalfSize = (Width / 2, Height / 2);

    /// <summary>F's view-space normal, toward the eye: XeGTAO's view space has x right, y up and z forward.</summary>
    private static readonly Double3 FrontNormal = -Double3.UnitZ;

    /// <summary>S's view-space normal, toward the eye's side of S, away from a receiver on F.</summary>
    private static readonly Double3 SideNormal = -Double3.UnitX;

    private static readonly Double3 FaceLight = new(1, 1, 1);

    [Fact]
    public void AReceiverOnAFaceBesideAGrazingConvexEdgeBlocksOnlyTheSectorsBelowItsFace()
    {
        VisibilityBitmaskLight setting = VisibilityBitmaskLight.Default;
        int texels = 0;
        int wrong = 0;
        double farthestFromTheEdge = 0;
        string? first = null;
        for (int y = 0; y < HalfSize.Height; y++)
        for (int x = 0; x < HalfSize.Width; x++)
        {
            if (
                FaceAt((X: x, Y: y)) is not { GeometricNormal: var normal, Position: var position }
                || normal != FrontNormal
            )
                continue;
            texels++;
            foreach ((SliceSectors sectors, NearSlice near) in Slices((X: x, Y: y), position.Z, setting))
            {
                SectorMask own = sectors.BelowPlane(FrontNormal);
                if (near.Blocked == own && near.Light == Double3.Zero)
                    continue;
                wrong++;
                farthestFromTheEdge = Math.Max(farthestFromTheEdge, position.X - EdgeX);
                first ??=
                    $"texel ({x}, {y}) at {position.X - EdgeX:0.000} m from E: blocked {near.Blocked.Bits:X8}, below F {own.Bits:X8}, light {near.Light}";
                break;
            }
        }
        output.WriteLine(
            $"{wrong} of {texels} texels on F block above F, the farthest {farthestFromTheEdge:0.000} m from E; the first: {first}"
        );

        texels.Should().BeGreaterThan(8_000, "F spans 47 of the 320 texel columns, in every row");
        wrong.Should().Be(0, "nothing in view rises above F's plane: {0}", first);
    }

    /// <summary>
    /// What a step reads at the representative pixel of <paramref name="texel"/>: F, S or sky, which is null. The box
    /// reaches without end up and down.
    /// </summary>
    private static StepTexel? FaceAt((int X, int Y) texel)
    {
        Double3 ray = RayAt((texel.X, texel.Y));
        double frontX = ray.X * FrontDepth;
        if (frontX is >= EdgeX and <= FarX)
            return new(Position: ray * FrontDepth, GeometricNormal: FrontNormal, DirectLight: FaceLight);
        double sideDepth = EdgeX / ray.X;
        if (ray.X > 0 && sideDepth is >= FrontDepth and <= BackDepth)
            return new(Position: ray * sideDepth, GeometricNormal: SideNormal, DirectLight: FaceLight);
        return null;
    }

    /// <summary>
    /// The view ray, whose z is 1, through the place <paramref name="place"/> in half-resolution texels, where whole
    /// numbers are a texel's representative pixel.
    /// </summary>
    private static Double3 RayAt((double X, double Y) place)
    {
        double screenX = (2 * place.X + 0.5) / Width;
        double screenY = (2 * place.Y + 0.5) / Height;
        return new((2 * screenX - 1) * TanHalfX, (1 - 2 * screenY) * TanHalfY, 1);
    }

    /// <summary>The C# copy's near slices of the texel <paramref name="texel"/> on F at the view depth <paramref name="z"/>.</summary>
    private static IEnumerable<(SliceSectors Sectors, NearSlice Near)> Slices(
        (int X, int Y) texel,
        double z,
        VisibilityBitmaskLight setting
    )
    {
        Double3 center = RayAt((texel.X, texel.Y)) * (z * HorizonSearch.CenterBias);
        Double3 toEye = (-center).Normalized();
        (float sliceNoise, float stepNoise) = OcclusionNoise.At((uint)texel.X, (uint)texel.Y);
        double radiusInTexels = setting.RadiusMeters / HorizonSearch.TexelWidth(z, TanHalfX, Width);
        for (int slice = 0; slice < setting.Slices; slice++)
        {
            double angle = HorizonSearch.SliceAngle(slice, setting.Slices, sliceNoise);
            var sectors = new SliceSectors(new HorizonSlice(toEye, FrontNormal, angle));
            List<NearStep> steps = SectorSearch.SliceSteps(
                texel: texel,
                slice: slice,
                sliceAngle: angle,
                stepsPerSide: setting.StepsPerSide,
                stepNoise: stepNoise,
                radiusInTexels: radiusInTexels,
                halfSize: HalfSize,
                rayThrough: RayAt,
                texelAt: FaceAt
            );
            NearSlice near = SectorSearch.Near(
                sectors: sectors,
                center: center,
                geometricNormal: FrontNormal,
                radius: setting.RadiusMeters,
                thicknessShare: CellBounds.ThicknessShare,
                steps: [.. steps]
            );
            yield return (sectors, near);
        }
    }
}
