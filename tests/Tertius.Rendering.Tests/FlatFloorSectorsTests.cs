namespace Tertius.Rendering.Tests;

/// <summary>
/// The near search of the C# copy over a flat floor with nothing else in view (docs/plans/screen-space-illumination.md,
/// "A near step", decisions 12 and 39): GTAO's steps of every slice, each at its surface point on its slice, block only
/// the sectors below the floor and bring none of its light. A level camera 1.5 m above an endless floor, at 640 × 360
/// and a vertical field of view of 60°, as the GPU tests see it.
/// </summary>
public sealed class FlatFloorSectorsTests(ITestOutputHelper output)
{
    private const int Width = 640;
    private const int Height = 360;
    private const double EyeHeight = 1.5;

    private static readonly double TanHalfY = Math.Tan(Math.PI / 6);
    private static readonly double TanHalfX = TanHalfY * Width / Height;
    private static readonly (int Width, int Height) HalfSize = (Width / 2, Height / 2);

    /// <summary>The floor's view-space normal: XeGTAO's view space has x right, y up and z forward.</summary>
    private static readonly Double3 Up = Double3.UnitY;

    private static readonly Double3 FloorLight = new(1, 1, 1);

    [Fact]
    public void AFlatFloorBlocksOnlyTheSectorsBelowItselfAndBouncesNothingOnItself()
    {
        VisibilityBitmaskLight setting = VisibilityBitmaskLight.Default;
        int texels = 0;
        int wrong = 0;
        string? first = null;
        for (int y = 0; y < HalfSize.Height; y++)
        for (int x = 0; x < HalfSize.Width; x++)
        {
            if (DepthAt((X: x, Y: y)) is not { } z)
                continue;
            texels++;
            foreach ((SliceSectors sectors, NearSlice near) in Slices((X: x, Y: y), z, setting))
            {
                SectorMask own = sectors.BelowPlane(Up);
                if (near.Blocked == own && near.Light == Double3.Zero)
                    continue;
                wrong++;
                first ??=
                    $"texel ({x}, {y}) at {z:0.00} m: blocked {near.Blocked.Bits:X8}, below the floor {own.Bits:X8}, light {near.Light}";
                break;
            }
        }
        output.WriteLine($"{wrong} of {texels} floor texels block or bounce on themselves; the first: {first}");

        texels.Should().BeGreaterThan(20_000, "the floor fills the lower half of the 320 × 180 texels");
        wrong.Should().Be(0, "a flat floor blocks only what lies below it and bounces nothing on itself: {0}", first);
    }

    /// <summary>The view depth of the floor at the representative pixel of <paramref name="texel"/>, or null for sky.</summary>
    private static double? DepthAt((int X, int Y) texel)
    {
        Double3 ray = RayAt((X: texel.X, Y: texel.Y));
        return ray.Y < 0 ? EyeHeight / -ray.Y : null;
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

    /// <summary>The C# copy's near slices of the floor texel <paramref name="texel"/> at the view depth <paramref name="z"/>.</summary>
    private static IEnumerable<(SliceSectors Sectors, NearSlice Near)> Slices(
        (int X, int Y) texel,
        double z,
        VisibilityBitmaskLight setting
    )
    {
        Double3 center = RayAt((X: texel.X, Y: texel.Y)) * (z * HorizonSearch.CenterBias);
        Double3 toEye = (-center).Normalized();
        (float sliceNoise, float stepNoise) = OcclusionNoise.At((uint)texel.X, (uint)texel.Y);
        double radiusInTexels = setting.RadiusMeters / HorizonSearch.TexelWidth(z, TanHalfX, Width);
        for (int slice = 0; slice < setting.Slices; slice++)
        {
            double angle = HorizonSearch.SliceAngle(slice, setting.Slices, sliceNoise);
            var sectors = new SliceSectors(new HorizonSlice(toEye, Up, angle));
            var steps = new List<NearStep>();
            for (int step = 0; HorizonSearch.NearRangeHoldsSteps(radiusInTexels) && step < setting.StepsPerSide; step++)
            {
                double fraction = HorizonSearch.StepFraction(
                    step: step,
                    slice: slice,
                    stepsPerSide: setting.StepsPerSide,
                    stepNoise: stepNoise,
                    radiusInTexels: radiusInTexels
                );
                (double x, double y) exact = HorizonSearch.ExactStepOffset(
                    stepFraction: fraction,
                    sliceAngle: angle,
                    radiusInTexels: radiusInTexels,
                    halfSize: HalfSize
                );
                (int x, int y) snapped = HorizonSearch.StepOffset(
                    stepFraction: fraction,
                    sliceAngle: angle,
                    radiusInTexels: radiusInTexels,
                    halfSize: HalfSize
                );
                AddStep(
                    steps: steps,
                    texel: (X: texel.X + snapped.x, Y: texel.Y + snapped.y),
                    exact: (X: texel.X + exact.x, Y: texel.Y + exact.y),
                    side: SliceSide.Plus
                );
                AddStep(
                    steps: steps,
                    texel: (X: texel.X - snapped.x, Y: texel.Y - snapped.y),
                    exact: (X: texel.X - exact.x, Y: texel.Y - exact.y),
                    side: SliceSide.Minus
                );
            }
            NearSlice near = SectorSearch.Near(
                sectors: sectors,
                center: center,
                geometricNormal: Up,
                radius: setting.RadiusMeters,
                thicknessShare: CellBounds.ThicknessShare,
                steps: [.. steps]
            );
            yield return (sectors, near);
        }
    }

    /// <summary>
    /// Adds the floor's step from <paramref name="texel"/> at its exact place <paramref name="exact"/>, in
    /// half-resolution texels, unless the texel lies outside the image or on sky, or the floor does not reach the place.
    /// </summary>
    private static void AddStep(List<NearStep> steps, (int X, int Y) texel, (double X, double Y) exact, SliceSide side)
    {
        bool inside = texel.X >= 0 && texel.Y >= 0 && texel.X < HalfSize.Width && texel.Y < HalfSize.Height;
        if (!inside || DepthAt(texel) is not { } z)
            return;
        Double3? position = SectorSearch.SurfacePoint(
            exactRay: RayAt(exact),
            texelPosition: RayAt((X: texel.X, Y: texel.Y)) * z,
            texelNormal: Up
        );
        if (position is { } onSlice)
            steps.Add(new(Position: onSlice, GeometricNormal: Up, DirectLight: FloorLight, Side: side));
    }
}
