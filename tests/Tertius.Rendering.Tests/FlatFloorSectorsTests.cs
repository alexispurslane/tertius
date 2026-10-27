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
        Double3 ray = RayAt((texel.X, texel.Y));
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
        Double3 center = RayAt((texel.X, texel.Y)) * (z * HorizonSearch.CenterBias);
        Double3 toEye = (-center).Normalized();
        (float sliceNoise, float stepNoise) = OcclusionNoise.At((uint)texel.X, (uint)texel.Y);
        double radiusInTexels = setting.RadiusMeters / HorizonSearch.TexelWidth(z, TanHalfX, Width);
        for (int slice = 0; slice < setting.Slices; slice++)
        {
            double angle = HorizonSearch.SliceAngle(slice, setting.Slices, sliceNoise);
            var sectors = new SliceSectors(new HorizonSlice(toEye, Up, angle));
            List<NearStep> steps = SectorSearch.SliceSteps(
                texel: texel,
                slice: slice,
                sliceAngle: angle,
                stepsPerSide: setting.StepsPerSide,
                stepNoise: stepNoise,
                radiusInTexels: radiusInTexels,
                halfSize: HalfSize,
                rayThrough: RayAt,
                texelAt: FloorAt,
                ownSurface: new OwnPlane(Up)
            );
            NearSlice near = SectorSearch.Near(
                sectors: sectors,
                center: center,
                ownSurface: new OwnPlane(Up),
                radius: setting.RadiusMeters,
                thicknessShare: CellBounds.ThicknessShare,
                steps: [.. steps]
            );
            yield return (sectors, near);
        }
    }

    /// <summary>What a step reads at the floor texel <paramref name="texel"/>, or null for sky.</summary>
    private static StepTexel? FloorAt((int X, int Y) texel) =>
        DepthAt(texel) is { } z
            ? new(Position: RayAt((texel.X, texel.Y)) * z, GeometricNormal: Up, DirectLight: FloorLight)
            : null;
}
