using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the guided diffuse rays (ARCHITECTURE.md, "Guided diffuse rays", <see cref="LightPyramid"/>): the light
/// pyramid's sums and its descent, the guided density's Jacobian, and the one-sample mixture's estimate of a receiver's
/// bounce light against a numerical integral over the cosine lobe. The scenes are synthetic rectangles, walked by the C#
/// copy of the walk (<see cref="ScreenRay.Walk"/>).
/// </summary>
public sealed class GuidedRaysTests
{
    private const int Width = 96,
        Height = 54;

    /// <summary>The walk's cap of a diffuse ray, <see cref="ScreenTrace.Default"/>'s.</summary>
    private const int DiffuseSteps = 62;

    /// <summary>The light of a ray that hits nothing.</summary>
    private const float MissLight = 0.05f;

    /// <summary>The light of the dull surfaces and of a bright patch, about 1,000 times as bright, as a sunlit patch is.</summary>
    private const float DullLight = 0.04f,
        PatchLight = 40;

    /// <summary>A floor 1.5 m below the eye and a back wall 10 m ahead.</summary>
    private static readonly SceneRectangle[] Room =
    [
        new(new(0, -1.5f, -7), new(6, 0, 0), new(0, 0, 6)),
        new(new(0, 1, -10), new(6, 0, 0), new(0, 2.5f, 0)),
    ];

    [Fact]
    public void ALightPyramidNeedsOneLightForEachPixel()
    {
        float[] lights = new float[7 * 5 - 1];

        Action make = () => LightPyramid.Of(lights, width: 7, height: 5);

        make.Should().Throw<ArgumentException>().WithMessage("*needs 35 lights, not 34*");
    }

    [Fact]
    public void EachCellOfTheLightPyramidHoldsTheLightOfThePixelsUnderIt()
    {
        float[] lights = [.. Enumerable.Range(0, 7 * 5).Select(index => (float)(index % 4))];

        var pyramid = LightPyramid.Of(lights, width: 7, height: 5);

        pyramid.TopLevel.Should().Be(3);
        pyramid.Total.Should().Be(lights.Sum());
        // Level 1 is 4 × 3 cells; its last column covers the screen's last column alone.
        pyramid.Light(level: 1, x: 3, y: 0).Should().Be(lights[6] + lights[13]);
        pyramid.Light(level: 1, x: 3, y: 2).Should().Be(lights[34], "the last row and column cover one pixel");
        pyramid.Light(level: 2, x: 1, y: 1).Should().Be(lights[32] + lights[33] + lights[34]);
    }

    [Fact]
    public void TheDescentDrawsEachPixelInProportionToItsLight()
    {
        float[] lights = new float[13 * 9];
        lights[2 * 13 + 3] = 1;
        lights[2 * 13 + 4] = 3;
        lights[8 * 13 + 12] = 4;
        var pyramid = LightPyramid.Of(lights, width: 13, height: 9);
        var random = new Random(5);
        var counts = new Dictionary<(int X, int Y), int>();
        const int draws = 80_000;

        float[] units = new float[pyramid.TopLevel];
        for (int draw = 0; draw < draws; draw++)
        {
            for (int level = 0; level < units.Length; level++)
                units[level] = random.NextSingle();
            (int X, int Y) pixel = pyramid.Drawn(units);
            counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
        }

        counts.Keys.Should().BeEquivalentTo([(3, 2), (4, 2), (12, 8)], "a pixel without light is never drawn");
        (counts[(3, 2)] / (double)draws).Should().BeApproximately(1 / 8.0, 0.006);
        (counts[(4, 2)] / (double)draws).Should().BeApproximately(3 / 8.0, 0.006);
        (counts[(12, 8)] / (double)draws).Should().BeApproximately(4 / 8.0, 0.006);
    }

    [Fact]
    public void TheGuidedDensityOfAPixelSumsOverAllDirectionsToThePixelsShareOfTheLight()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        LitFrame scene = Lit(camera, Room, patch: null);
        Receiver receiver = scene.ReceiverAt(30, 48);
        (int X, int Y) target = (60, 20);
        float share = scene.Light.Light(level: 0, target.X, target.Y) / scene.Light.Total;
        float inverseDepth = scene.Depth.Finest(target.X, target.Y);
        // A grid of directions in polar angles about the slab's center, wide enough to hold the whole slab.
        var axis = Vector3.Normalize(
            LightPyramid.TargetOf(
                camera: camera,
                pixel: target,
                place: new(0.5f),
                depthShare: 0.5f,
                inverseDepth: inverseDepth
            ) - receiver.Origin
        );
        (Vector3 tangent, Vector3 bitangent) = BasisOf(axis);
        const int steps = 1200;
        const double widest = 0.2;
        double sum = 0;
        for (int radial = 0; radial < steps; radial++)
        for (int turn = 0; turn < steps; turn++)
        {
            double theta = (radial + 0.5) / steps * widest;
            double phi = (turn + 0.5) / steps * 2 * Math.PI;
            var direction = Vector3.Normalize(
                (float)Math.Cos(theta) * axis
                    + (float)(Math.Sin(theta) * Math.Cos(phi)) * tangent
                    + (float)(Math.Sin(theta) * Math.Sin(phi)) * bitangent
            );
            double solidAngle = Math.Sin(theta) * (widest / steps) * (2 * Math.PI / steps);
            float density = scene.Light.PixelDensity(
                camera: camera,
                origin: receiver.Origin,
                direction: direction,
                pixel: target,
                inverseDepth: inverseDepth,
                jitter: Vector2.Zero
            );
            sum += density * solidAngle;
        }

        sum.Should()
            .BeApproximately(share, 0.01 * share, "the density per steradian is P(q) times the slab's Jacobian");
    }

    [Fact]
    public void TheGuidedEstimateOfAFloorsBounceLightKeepsTheCosineIntegralsMeanWithAMuchSmallerVariance()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // A bright patch of 1.2 m by 1.2 m on the back wall: a few dozen pixels of the 5,184.
        LitFrame scene = Lit(
            camera,
            Room,
            patch: point => point.Z < -9.9f && point.X is > 1 and < 2.2f && point.Y is > 0 and < 1.2f
        );
        foreach ((int x, int y) in new[] { (30, 48), (60, 46), (48, 51) })
        {
            Receiver receiver = scene.ReceiverAt(x, y);

            (double integral, double cosineVariance) = scene.CosineIntegral(receiver, side: 768);
            (double mean, double variance) = scene.MixtureEstimate(receiver, samples: 60_000, seed: x + y);

            integral.Should().BeGreaterThan(2 * DullLight, "the patch gives most of the floor's bounce light");
            mean.Should().BeApproximately(integral, 0.03 * integral, $"the mixture is unbiased at ({x}, {y})");
            variance.Should().BeLessThan(0.1 * cosineVariance, $"the guided rays find the patch at ({x}, {y})");
        }
    }

    [Fact]
    public void APatchBehindTheReceiversSurfaceAddsNothing()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // A box whose right face, at x = −2, faces +X, and a left wall at x = −5 whose bright patch lies behind that face.
        SceneRectangle[] room =
        [
            .. Room,
            new(new(-5, 1, -7), new(0, 2.5f, 0), new(0, 0, 5)),
            .. ScreenTesting.Box(new(-2.5f, -0.75f, -5.5f), new(0.5f, 0.75f, 1.5f)),
        ];
        LitFrame bright = Lit(camera, room, patch: point => point.X < -4.9f && point.Y is > -1 and < 2);
        LitFrame dull = Lit(camera, room, patch: null);
        Vector2 face = camera.PixelOf(new(-2, -0.75f, -5.5f));
        Receiver receiver = bright.ReceiverAt((int)face.X, (int)face.Y);
        receiver.Normal.Should().Be(Vector3.UnitX, "the receiver is the box's right face");

        (double withoutPatch, _) = dull.CosineIntegral(receiver, side: 256);
        (double mean, _) = bright.MixtureEstimate(receiver, samples: 40_000, seed: 3);

        mean.Should().BeApproximately(withoutPatch, 0.03 * withoutPatch);
    }

    [Fact]
    public void ASunlitPatchOnTheFloorLosesMostOfTheNoiseOfTheWallsBounceLight()
    {
        CameraView camera = ScreenTesting.Camera(width: 160, height: 90);
        // A patch of 2 m by 1.5 m on the floor, 5.5 to 7 m ahead: rays from the wall come from behind it and above.
        LitFrame scene = Lit(
            camera,
            Room,
            patch: point => point.Y < -1.49f && point.X is > -1 and < 1 && point.Z is > -7 and < -5.5f
        );
        foreach ((int x, int y) in new[] { (80, 30), (40, 25) })
        {
            Receiver receiver = scene.ReceiverAt(x, y);
            receiver.Normal.Should().Be(Vector3.UnitZ, "the receiver is on the back wall");

            (double integral, double cosineVariance) = scene.CosineIntegral(receiver, side: 768);
            (double mean, double variance) = scene.MixtureEstimate(receiver, samples: 100_000, seed: x + y);

            mean.Should().BeApproximately(integral, 0.03 * integral, $"the mixture is unbiased at ({x}, {y})");
            variance
                .Should()
                .BeLessThan(0.1 * cosineVariance, $"rays that enter the floor's slabs count at ({x}, {y})");
        }
    }

    /// <summary>
    /// The scene of <paramref name="rectangles"/>: every surface at <see cref="DullLight"/>, and those whose point
    /// <paramref name="patch"/> holds at <see cref="PatchLight"/>.
    /// </summary>
    private static LitFrame Lit(in CameraView camera, SceneRectangle[] rectangles, Func<Vector3, bool>? patch)
    {
        (float[] distances, Vector3[] normals) = ScreenTesting.Surfaces(camera, rectangles);
        float[] lights = new float[distances.Length];
        for (int index = 0; index < lights.Length; index++)
        {
            if (distances[index] <= 0)
                continue;
            Vector3 point =
                Vector3.Normalize(camera.ViewRayThrough(new(index % camera.Width + 0.5f, index / camera.Width + 0.5f)))
                * distances[index];
            lights[index] = patch?.Invoke(point) == true ? PatchLight : DullLight;
        }
        return new(
            Camera: camera,
            Depth: DepthPyramid.Of(camera: camera, distances: distances, geometricNormals: normals),
            Light: LightPyramid.Of(lights, camera.Width, camera.Height),
            Distances: distances,
            Normals: normals
        );
    }

    /// <summary>A unit tangent and bitangent of a unit normal.</summary>
    private static (Vector3 Tangent, Vector3 Bitangent) BasisOf(Vector3 normal)
    {
        Vector3 other = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var tangent = Vector3.Normalize(Vector3.Cross(other, normal));
        return (tangent, Vector3.Cross(normal, tangent));
    }

    /// <summary>A receiving pixel: its ray origin off its surface, its view depth and its unit normal.</summary>
    private readonly record struct Receiver(Vector3 Origin, float ViewDepth, Vector3 Normal);

    /// <summary>A synthetic frame: its depth pyramid and its light pyramid, whose level 0 is also the light of a hit.</summary>
    private sealed record LitFrame(
        CameraView Camera,
        DepthPyramid Depth,
        LightPyramid Light,
        float[] Distances,
        Vector3[] Normals
    )
    {
        public Receiver ReceiverAt(int x, int y)
        {
            Vector3 viewRay = Camera.ViewRayThrough(new(x + 0.5f, y + 0.5f));
            float distance = Distances[y * Camera.Width + x];
            float viewDepth = distance / viewRay.Length();
            Vector3 normal = Normals[y * Camera.Width + x];
            Vector3 origin =
                Vector3.Normalize(viewRay) * distance + normal * ScreenRay.OriginOffset(viewDepth, Camera.PixelAngle);
            return new(origin, viewDepth, normal);
        }

        /// <summary>
        /// The receiver's bounce light, the mean of a hit's light over the cosine lobe, by one jittered direction in each
        /// of side × side cells of the lobe's unit square, and the variance of one cosine ray's light.
        /// </summary>
        public (double Mean, double Variance) CosineIntegral(Receiver receiver, int side)
        {
            var random = new Random(side);
            double sum = 0,
                squares = 0;
            for (int row = 0; row < side; row++)
            for (int column = 0; column < side; column++)
            {
                var unit = new Vector2((column + random.NextSingle()) / side, (row + random.NextSingle()) / side);
                double light = LightAlong(receiver, CosineDirection(receiver.Normal, unit)).Light;
                sum += light;
                squares += light * light;
            }
            double mean = sum / (side * side);
            return (mean, squares / (side * side) - mean * mean);
        }

        /// <summary>
        /// The one-sample mixture's estimate: each ray drawn from the cosine lobe or guided at a pixel of the light
        /// pyramid, weighed by cos θ / π over the mixture density, its mean and its variance.
        /// </summary>
        public (double Mean, double Variance) MixtureEstimate(Receiver receiver, int samples, int seed)
        {
            var random = new Random(seed);
            float[] units = new float[Light.TopLevel];
            double sum = 0,
                squares = 0;
            for (int sample = 0; sample < samples; sample++)
            {
                (int X, int Y)? target = null;
                Vector3 direction;
                if (random.NextSingle() < LightPyramid.GuidedShare)
                {
                    for (int level = 0; level < units.Length; level++)
                        units[level] = random.NextSingle();
                    (int X, int Y) aimed = Light.Drawn(units);
                    Vector3 point = LightPyramid.TargetOf(
                        camera: Camera,
                        pixel: aimed,
                        place: new(random.NextSingle(), random.NextSingle()),
                        depthShare: random.NextSingle(),
                        inverseDepth: Depth.Finest(aimed.X, aimed.Y)
                    );
                    direction = Vector3.Normalize(point - receiver.Origin);
                    target = aimed;
                }
                else
                    direction = CosineDirection(receiver.Normal, new(random.NextSingle(), random.NextSingle()));
                double estimate = WeighedLight(receiver, direction, target);
                sum += estimate;
                squares += estimate * estimate;
            }
            double mean = sum / samples;
            return (mean, squares / samples - mean * mean);
        }

        /// <summary>
        /// A ray's light times cos θ / π over the mixture density, or 0 for a ray below the surface and for a guided ray
        /// whose walk hits no pixel of the 3 × 3 around its target.
        /// </summary>
        private double WeighedLight(Receiver receiver, Vector3 direction, (int X, int Y)? target)
        {
            float cosine = Vector3.Dot(receiver.Normal, direction);
            if (cosine <= 0)
                return 0;
            ((int X, int Y)? hit, float light) = LightAlong(receiver, direction);
            if (hit is not { } pixel)
                return target is null ? light / (1 - LightPyramid.GuidedShare) : 0;
            float guided = Light.GuidedDensity(
                camera: Camera,
                depth: Depth,
                origin: receiver.Origin,
                direction: direction,
                hit: pixel,
                jitter: Vector2.Zero
            );
            bool counts =
                target is not { } aimed
                || Light.Counts(
                    camera: Camera,
                    depth: Depth,
                    origin: receiver.Origin,
                    direction: direction,
                    hit: pixel,
                    target: aimed,
                    jitter: Vector2.Zero
                );
            if (!counts)
                return 0;
            float density = cosine / MathF.PI;
            return light * density / LightPyramid.MixtureDensity(density, guided);
        }

        /// <summary>The pixel that the walk along the direction hits, or null for a miss, and the light that it brings.</summary>
        private ((int X, int Y)? Hit, float Light) LightAlong(Receiver receiver, Vector3 direction)
        {
            RayTrace trace = ScreenRay
                .Of(origin: receiver.Origin, direction: direction, viewDepth: receiver.ViewDepth, camera: Camera)
                .Walk(Depth, direction, DiffuseSteps);
            if (!trace.TryGetValue(out RayHit hit))
                return (null, MissLight);
            return (hit.Pixel, Light.Light(level: 0, hit.Pixel.X, hit.Pixel.Y));
        }

        /// <summary>The cosine lobe's direction of a unit square's point, as diffuseDirection draws it.</summary>
        private static Vector3 CosineDirection(Vector3 normal, Vector2 unit)
        {
            (Vector3 tangent, Vector3 bitangent) = BasisOf(normal);
            float radius = MathF.Sqrt(unit.X);
            float phi = 2 * MathF.PI * unit.Y;
            return radius * MathF.Cos(phi) * tangent
                + radius * MathF.Sin(phi) * bitangent
                + MathF.Sqrt(1 - unit.X) * normal;
        }
    }
}
