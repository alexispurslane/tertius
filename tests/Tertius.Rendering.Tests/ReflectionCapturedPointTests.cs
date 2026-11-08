using System.Runtime.InteropServices;
using System.Text.Json;
using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Point verification preserves decoded receivers, visibility, stored source units and exhausted budgets.</summary>
public sealed class ReflectionCapturedPointTests
{
    [Fact]
    public void APointUsesTheWarmedDecodedReceiverRatherThanAnIdealPlane()
    {
        float[] inputs = [2, 0, 0.2f, 0.5019608f, 0.05958486f, 0, 0, 1, 0, 3, 0.125f, -3.125f, 1.25f, 0, 1, 0];
        var source = new ComparisonSource(
            Inputs: inputs,
            ExposureStops: -2,
            PreExposureScale: 0.25,
            EmissionRadiance: 16
        );

        DecodedReceiver receiver = ReferenceComparison.PlateReceiver(
            source: source,
            surfaces: [(byte)ReferenceSurface.Plate],
            crop: new(Left: 550, Top: 260, Width: 1, Height: 1),
            eye: new(10, 3, -2),
            pixelX: 550,
            pixelY: 260
        );

        receiver.Position.Should().Be(new Double3(10.125, -0.125, -0.75));
        receiver.Normal.Should().Be(Double3.UnitY);
        receiver.ToView.Should().Be(Double3.UnitY);
        receiver.Material.Roughness.Should().Be((double)0.2f);
        receiver.Material.Reflectance.Should().Be((double)0.5019608f);
        receiver.Material.BaseColor.X.Should().Be((double)0.05958486f);
    }

    [Theory]
    [InlineData((byte)ReferenceSurface.Emitter)]
    [InlineData(byte.MaxValue)]
    public void ASelectedNonPlatePointStopsRatherThanBeingReplaced(byte surface)
    {
        var source = new ComparisonSource(
            Inputs: [0, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -3, 0, 0, 1, 0],
            ExposureStops: -2,
            PreExposureScale: 0.25,
            EmissionRadiance: 16
        );

        Action read = () =>
            ReferenceComparison.PlateReceiver(
                source: source,
                surfaces: [surface],
                crop: new(Left: 550, Top: 260, Width: 1, Height: 1),
                eye: new(0, 3, 0),
                pixelX: 550,
                pixelY: 260
            );

        read.Should().Throw<InvalidOperationException>().WithMessage("*plate*550,260*");
    }

    [Fact]
    public void TheExistingYNormalQuadratureRejectsADifferentDecodedNormal()
    {
        var source = new ComparisonSource(
            Inputs: [0, 0, 0.2f, 0.5f, 0.06f, 0, 1, 0, 0, 3, 0, -3, 0, 0, 1, 0],
            ExposureStops: -2,
            PreExposureScale: 0.25,
            EmissionRadiance: 16
        );

        Action read = () =>
            ReferenceComparison.PlateReceiver(
                source: source,
                surfaces: [(byte)ReferenceSurface.Plate],
                crop: new(Left: 550, Top: 260, Width: 1, Height: 1),
                eye: new(0, 3, 0),
                pixelX: 550,
                pixelY: 260
            );

        read.Should().Throw<InvalidOperationException>().WithMessage("*normal*");
    }

    [Fact]
    public void SourceStorageAndExposureAreReadFromTheRetainedManifest() =>
        InDirectory(path =>
        {
            File.WriteAllText(
                Path.Combine(path, "manifest.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        ExposureStops = -2,
                        PreExposureScale = 0.25,
                        EmissionRadiance = 16,
                    }
                )
            );
            float[] inputs = [2, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -3, 0, 0, 1, 0];
            File.WriteAllBytes(
                Path.Combine(path, "decoded-inputs.f32"),
                MemoryMarshal.AsBytes(inputs.AsSpan()).ToArray()
            );

            ComparisonSource source = ReferenceComparison.ReadSource(path);

            source.Inputs.Should().Equal(inputs);
            source.PreExposureScale.Should().Be(0.25);
            source.EmissionRadiance.Should().Be(16);
            source.ExposureStops.Should().Be(-2);
        });

    [Fact]
    public void RetainedPfmPointsUseTheOriginalBottomUpRowOrder() =>
        InDirectory(path =>
        {
            string filename = Path.Combine(path, "small.pfm");
            using (FileStream output = File.Create(filename))
                new ReferenceImage(2, 2, [new(1, 1, 1), new(2, 2, 2), new(3, 3, 3), new(4, 4, 4)]).WritePfm(output);

            double radiance = ReferenceComparison.ReadPfmPoint(
                path: filename,
                crop: new(Left: 550, Top: 260, Width: 2, Height: 2),
                pixelX: 551,
                pixelY: 261
            );

            radiance.Should().Be(4);
        });

    [Fact]
    public void AnExhaustedPointRetainsItsActualBudgetAndCannotSatisfyTheCriteria()
    {
        var exhausted = new ReferenceQuadratureResult(
            Light: new(Diffuse: Double3.Zero, Specular: new(100, 100, 100)),
            EstimatedAbsoluteError: null,
            Evaluations: 34,
            Converged: false
        );

        var verification = new PointVerification(Coarse: exhausted, Fine: exhausted, SavedMean: 100, Peak: 1000);

        verification.DiagnosticCriteriaSatisfied.Should().BeFalse();
        verification.Fine.Evaluations.Should().Be(34);
        verification.Fine.EstimatedAbsoluteError.Should().BeNull();
    }

    [Fact]
    public void ConvergedFlagsDoNotExcuseExcessiveCoarseFineDrift()
    {
        var coarse = new ReferenceQuadratureResult(
            Light: new(Diffuse: Double3.Zero, Specular: new(101, 101, 101)),
            EstimatedAbsoluteError: 0,
            Evaluations: 100,
            Converged: true
        );
        var fine = new ReferenceQuadratureResult(
            Light: new(Diffuse: Double3.Zero, Specular: new(100, 100, 100)),
            EstimatedAbsoluteError: 0,
            Evaluations: 200,
            Converged: true
        );

        var verification = new PointVerification(Coarse: coarse, Fine: fine, SavedMean: 100, Peak: 1000);

        verification.DiagnosticCriteriaSatisfied.Should().BeFalse();
        verification.CoarseFineDrift.Should().Be(1);
    }

    [Fact]
    public void ConvergedFlagsDoNotExcuseASavedMeanOutsideTheDiagnosticLimit()
    {
        var result = new ReferenceQuadratureResult(
            Light: new(Diffuse: Double3.Zero, Specular: new(100, 100, 100)),
            EstimatedAbsoluteError: 0,
            Evaluations: 200,
            Converged: true
        );

        var verification = new PointVerification(Coarse: result, Fine: result, SavedMean: 120, Peak: 1000);

        verification.DiagnosticCriteriaSatisfied.Should().BeFalse();
        verification.SignedDifference.Should().Be(20);
        verification.FractionalDifference.Should().Be(0.2);
        verification.PeakNormalizedDifference.Should().Be(0.02);
    }

    [Fact]
    public void AStablePointWithinBothLimitsReportsSuccessWithoutChangingItsValues()
    {
        var coarse = new ReferenceQuadratureResult(
            Light: new(Diffuse: Double3.Zero, Specular: new(100.05, 100.05, 100.05)),
            EstimatedAbsoluteError: 0.01,
            Evaluations: 100,
            Converged: true
        );
        var fine = new ReferenceQuadratureResult(
            Light: new(Diffuse: Double3.Zero, Specular: new(100, 100, 100)),
            EstimatedAbsoluteError: 0.001,
            Evaluations: 200,
            Converged: true
        );

        var verification = new PointVerification(Coarse: coarse, Fine: fine, SavedMean: 109, Peak: 1000);

        verification.DiagnosticCriteriaSatisfied.Should().BeTrue();
        verification.Fine.Should().Be(fine);
        verification.FractionalDifference.Should().Be(0.09);
    }

    private static void InDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-captured-point-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
