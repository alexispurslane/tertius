using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>The independent emitter/plane integral: geometry, finite pixels, absolute light and sampling agreement.</summary>
public sealed class ReflectionReferenceTests
{
    [Fact]
    public void ABoxRayReturnsTheNearestEmittingFace()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 2, 1), Radiance: new(1, 1, 1));

        ReferenceHit? hit = emitter.Intersect(Double3.Zero, Double3.UnitY);

        hit.Should().NotBeNull();
        hit!.Value.Position.Should().Be(new Double3(0, 1, 0));
        hit.Value.Normal.Should().Be(-Double3.UnitY);
        hit.Value.Distance.Should().Be(1);
    }

    [Fact]
    public void ARayParallelToABoxFaceCanMissTheBox()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 2, 1), Radiance: new(1, 1, 1));

        emitter.Intersect(new(2, 0, 0), Double3.UnitY).Should().BeNull();
    }

    [Fact]
    public void ARayFromInsideABoxFindsItsExit()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 2, 1), Radiance: new(1, 1, 1));

        ReferenceHit? hit = emitter.Intersect(new(0, 1.5, 0), Double3.UnitX);

        hit.Should().NotBeNull();
        hit!.Value.Normal.Should().Be(Double3.UnitX);
        hit.Value.Distance.Should().Be(1);
    }

    [Fact]
    public void DifferentPositionsWithinAPixelSeeDifferentDirections()
    {
        var camera = new ReferenceCamera(
            Eye: new(0, 3, 6),
            Target: Double3.Zero,
            VerticalFov: Math.PI / 3,
            Width: 1280,
            Height: 720
        );

        Double3 left = camera.Ray(640, 360);
        Double3 right = camera.Ray(641, 360);

        right.X.Should().BeGreaterThan(left.X);
        right.Length.Should().BeApproximately(1, 1e-14);
    }

    [Fact]
    public void ALambertPlateMatchesTheAnalyticIrradianceOfASquareEmitter()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 0, Roughness: 0.6, Reflectance: 0.5);
        var integral = new ReferenceIntegral(emitter, material);

        DirectTerms light = integral.Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 16384, seed: 71);

        double expected = 4 / (Math.PI * Math.Sqrt(2)) * Math.Atan(1 / Math.Sqrt(2));
        light.Diffuse.X.Should().BeApproximately(expected, 0.002);
    }

    [Fact]
    public void ANearlySmoothPlateRetainsTheLightOfABroadEmitter()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0, Reflectance: 0.5);
        var integral = new ReferenceIntegral(emitter, material);

        DirectTerms light = integral.Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 16384, seed: 71);

        light.Specular.X.Should().BeInRange(0.0398, 0.0402);
    }

    [Fact]
    public void DoublingEmittedRadianceDoublesReflectedRadiance()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 2, 3));
        var material = new MaterialSample(
            BaseColor: new(0.06, 0.06, 0.06),
            Metallic: 0,
            Roughness: 0.2,
            Reflectance: 0.5
        );
        var original = new ReferenceIntegral(emitter, material);
        var doubled = new ReferenceIntegral(emitter with { Radiance = 2 * emitter.Radiance }, material);

        Double3 first = original.Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 2048, seed: 9).Specular;
        Double3 second = doubled.Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 2048, seed: 9).Specular;

        first.X.Should().BeGreaterThan(0);
        second.Should().Be(2 * first);
    }

    [Fact]
    public void UnseenEmittersProvideNoReflectedLight()
    {
        var emitter = new ReferenceEmitter(
            Minimum: new(-1, -2, -1),
            Maximum: new(1, -1, 1),
            Radiance: new(6800, 6800, 6800)
        );
        var material = new MaterialSample(
            BaseColor: new(0.06, 0.06, 0.06),
            Metallic: 0,
            Roughness: 0.2,
            Reflectance: 0.5
        );
        var integral = new ReferenceIntegral(emitter, material);

        integral
            .Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 2048, seed: 9)
            .Should()
            .Be(default(DirectTerms));
    }

    [Fact]
    public void TwoIndependentSamplingDensitiesAgreeAtBroadRoughness()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(
            BaseColor: new(0.06, 0.06, 0.06),
            Metallic: 0,
            Roughness: 0.6,
            Reflectance: 0.5
        );
        var integral = new ReferenceIntegral(emitter, material);

        Double3 mixed = integral.Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 16384, seed: 29).Specular;
        Double3 area = integral
            .Estimate(point: Double3.Zero, toView: Double3.UnitY, pairs: 32768, seed: 83, emitterOnly: true)
            .Specular;

        mixed.X.Should().BeGreaterThan(0);
        area.X.Should().BeApproximately(mixed.X, mixed.X * 0.01);
    }

    [Fact]
    public void ATopDownCameraKeepsTheHorizontalPixelFootprint()
    {
        var camera = new ReferenceCamera(
            Eye: new(0, 3, 0),
            Target: Double3.Zero,
            VerticalFov: Math.PI / 3,
            Width: 1280,
            Height: 720
        );

        camera.Ray(641, 360).X.Should().BeGreaterThan(camera.Ray(640, 360).X);
    }

    [Fact]
    public void APixelOfTheBarReflectionContainsLight()
    {
        var camera = new ReferenceCamera(
            Eye: new(0, 3, 6),
            Target: new(0, 0, 6 - 3 * Math.Sqrt(3)),
            VerticalFov: Math.PI / 3,
            Width: 1280,
            Height: 720
        );
        var emitter = new ReferenceEmitter(
            Minimum: new(-1.1, 0, -2.1),
            Maximum: new(-0.9, 2, -1.9),
            Radiance: new(6800, 6800, 6800)
        );
        var material = new MaterialSample(
            BaseColor: new(0.06, 0.06, 0.06),
            Metallic: 0,
            Roughness: 0.2,
            Reflectance: 0.5
        );

        ReferenceImage[] images = ReferenceImage.RenderLevels(
            camera: camera,
            crop: new(Left: 560, Top: 310, Width: 2, Height: 2),
            emitter: emitter,
            material: material,
            levels: [64, 256],
            seed: 41
        );

        images[1].Pixels.Sum(value => value.X).Should().BeGreaterThan(0);
        images[1].Pixels.Should().HaveCount(4);
    }

    [Fact]
    public void LinearHdrOutputRetainsBrightValuesInBottomUpRgbOrder()
    {
        var image = new ReferenceImage(Width: 1, Height: 2, Pixels: [new(1000, 2000, 3000), new(4, 5, 6)]);
        using var stream = new MemoryStream();

        image.WritePfm(stream);

        byte[] bytes = stream.ToArray();
        bytes.Length.Should().Be(36);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 12).Should().Be("PF\n1 2\n-1.0\n");
        BitConverter.ToSingle(bytes, 12).Should().Be(4);
        BitConverter.ToSingle(bytes, 24).Should().Be(1000);
    }

    [Fact]
    public void LightLossIsMeasuredAgainstTheUnchangedReference()
    {
        var reference = new ReferenceImage(Width: 2, Height: 1, Pixels: [new(4, 4, 4), new(4, 4, 4)]);
        var candidate = new ReferenceImage(Width: 2, Height: 1, Pixels: [new(2, 2, 2), new(4, 4, 4)]);

        var difference = ReferenceDifference.Compare(reference, candidate);

        difference.MissingFraction.Should().BeApproximately(0.25, 1e-14);
        difference.ExcessFraction.Should().Be(0);
        difference.RelativeL1.Should().BeApproximately(0.25, 1e-14);
    }

    [Fact]
    public void ExtraLightDoesNotCancelMissingLight()
    {
        var reference = new ReferenceImage(Width: 2, Height: 1, Pixels: [new(4, 4, 4), new(4, 4, 4)]);
        var candidate = new ReferenceImage(Width: 2, Height: 1, Pixels: [new(2, 2, 2), new(6, 6, 6)]);

        var difference = ReferenceDifference.Compare(reference, candidate);

        difference.MissingFraction.Should().BeApproximately(0.25, 1e-14);
        difference.ExcessFraction.Should().BeApproximately(0.25, 1e-14);
        difference.RelativeL1.Should().BeApproximately(0.5, 1e-14);
    }
}
