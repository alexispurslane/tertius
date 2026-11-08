using System.Text.Json;
using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Audit acceptance cannot bypass measured light, history, exposure or source compatibility.</summary>
public sealed class ReflectionMeasuredComparisonTests
{
    [Fact]
    public void AnAcceptedEmitterWithAnOldAnalyticResidualStillChecksItsMaterialFactor()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Receiver(true);
        frame.Inputs[10] = 0.0002f;
        frame.Inputs[0] = 2;
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(
                    path: path,
                    fixture: fixture,
                    frame: frame,
                    audit: audit,
                    measured: true
                );

            measure.Should().Throw<InvalidOperationException>().WithMessage("*material-factor-once*pixel (0,0)*");
            File.Exists(Path.Combine(path, "source-history.f32")).Should().BeTrue();
            File.ReadAllText(Path.Combine(path, "audit-validation.json")).Should().Contain("primary-position");
        });
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 1f)]
    [InlineData(float.NaN, float.NaN, float.NaN, 1f)]
    [InlineData(0f, 0f, 0f, float.PositiveInfinity)]
    public void AnAuditedMeasuredReceiverRejectsNonAchromaticOrNonFiniteHistory(
        float red,
        float green,
        float blue,
        float alpha
    )
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Receiver(true);
        frame = frame with { History = [red, green, blue, alpha] };
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(
                    path: path,
                    fixture: fixture,
                    frame: frame,
                    audit: audit,
                    measured: true
                );

            measure.Should().Throw<InvalidOperationException>().WithMessage("*achromatic-source-history*");
        });
    }

    [Fact]
    public void AnAuditedMeasuredReceiverRejectsAPreviousExposureMismatch()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Receiver(true);
        frame = frame with { PreviousExposureStops = -1 };
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(
                    path: path,
                    fixture: fixture,
                    frame: frame,
                    audit: audit,
                    measured: true
                );

            measure.Should().Throw<InvalidOperationException>().WithMessage("*previous-exposure-stops*");
        });
    }

    [Fact]
    public void AnAuditedMeasuredPlateAppliesTheMaterialOnceAndRetainsItsSignal()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Receiver(false);
        ComparisonPixel pixel = ReferenceComparison.Decode(frame.Inputs, fixture.Camera.Eye, fixture.ExposureStops);
        double factor = pixel
            .Receiver.Material.CompensatedSpecularAlbedo(
                pixel.Receiver.Normal,
                pixel.Receiver.ToView,
                BrdfTables.Shared
            )
            .X;
        float history = (float)(2 / factor);
        frame = frame with { History = [history, history, history, 1] };
        InDirectory(path =>
        {
            ComparisonMeasurement result = ReferenceComparison.RetainAuditedMeasurement(
                path: path,
                fixture: fixture,
                frame: frame,
                audit: audit,
                measured: true
            );

            result.RawSpecular.Should().Equal(8);
            result.PlateSpecular.Should().Equal(8);
            result.EmitterSpecular.Should().Equal(0);
            result.MaximumFactorRelativeError.Should().BeLessThan(0.01);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "metadata.json")));
            metadata.RootElement.GetProperty("Measured").GetBoolean().Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("emission")]
    [InlineData("exposure")]
    [InlineData("scale")]
    [InlineData("radiance")]
    public void OffOnSourcesMustMatchEvenWhenTheirReceiversMatch(string difference)
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame _) = Receiver(true);
        var control = new ComparisonSource(
            Inputs: frame.Inputs,
            ExposureStops: -2,
            PreExposureScale: 0.25,
            EmissionRadiance: 16
        );
        ComparisonSource prototype = control with { Inputs = (float[])frame.Inputs.Clone() };
        if (difference == "emission")
            prototype.Inputs[1] = 5;
        if (difference == "exposure")
            prototype = prototype with { ExposureStops = -1 };
        if (difference == "scale")
            prototype = prototype with { PreExposureScale = 0.5 };
        if (difference == "radiance")
            prototype = prototype with { EmissionRadiance = 17 };

        Action compare = () => ReferenceComparison.RequireCompatibleSources(control, prototype, fixture.Crop);

        compare.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DifferentOutgoingLightIsAllowedForOtherwiseIdenticalOffOnInputs()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame _) = Receiver(true);
        var control = new ComparisonSource(
            Inputs: frame.Inputs,
            ExposureStops: -2,
            PreExposureScale: 0.25,
            EmissionRadiance: 16
        );
        ComparisonSource prototype = control with { Inputs = (float[])frame.Inputs.Clone() };
        prototype.Inputs[0] = 9;

        Action compare = () => ReferenceComparison.RequireCompatibleSources(control, prototype, fixture.Crop);

        compare.Should().NotThrow();
    }

    private static (ComparisonFixture Fixture, ComparisonFrame Frame, GeometryAuditFrame Audit) Receiver(bool emitter)
    {
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1),
            Emitter: new(
                Minimum: new(emitter ? -0.5 : 2, 1, -0.5),
                Maximum: new(emitter ? 0.5 : 3, 2, 0.5),
                Radiance: new(16, 16, 16)
            ),
            Roughness: 0.2,
            ExposureStops: -2
        );
        float[] inputs = emitter
            ? [0, 4, 1, 0, 0, 0, 0, 1, 0, 1, 0, -1, 0, 0, 1, 0]
            : [2, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -3, 0, 0, 1, 0];
        var frame = new ComparisonFrame(Draw: 64, Inputs: inputs, Distances: [emitter ? 1 : 3], History: [0, 0, 0, 1])
        {
            ActualExposureStops = -2,
            PreviousExposureStops = -2,
        };
        GeometrySnapshot[] snapshots =
        [
            new("distance", [0], [0]),
            new("history", [1], [1]),
            new("composition", [2], [2]),
        ];
        var audit = new GeometryAuditFrame(
            Values: emitter ? [0, -1, 0, 1, 0, 1, 0, 1] : [0, -3, 0, 3, 0, 1, 0, 0],
            Snapshots: snapshots,
            Metadata: JsonSerializer.SerializeToElement(new { OwnedAttachments = true })
        );
        return (fixture, frame, audit);
    }

    private static void InDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-measured-audit-{Guid.NewGuid():N}");
        try
        {
            action(path);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
