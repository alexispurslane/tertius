using System.Runtime.InteropServices;
using System.Text.Json;
using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Direct geometry measurements retain evidence and reject incompatible receivers without clipping raw light.</summary>
public sealed class ReflectionGeometryAuditTests
{
    [Fact]
    public void EveryAuditInputIsRetainedBeforeAReceiverCanBeRejected()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Plate();
        frame.Inputs[11] = -2;
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(path: path, fixture: fixture, frame: frame, audit: audit);

            measure.Should().Throw<InvalidOperationException>();
            File.Exists(Path.Combine(path, "audit-inputs.f32"))
                .Should()
                .BeTrue("audit fields must survive a rejected receiver");
            File.Exists(Path.Combine(path, "audit-metadata.json")).Should().BeTrue();
            File.Exists(Path.Combine(path, "audit-snapshots.json")).Should().BeTrue();
            MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "probe-inputs.f32")))
                .ToArray()
                .Should()
                .Equal(frame.Inputs);
            File.ReadAllBytes(Path.Combine(path, "distance.f32")).Length.Should().Be(4);
            File.ReadAllBytes(Path.Combine(path, "source-history.f32")).Length.Should().Be(16);
        });
    }

    [Fact]
    public void AnAuditedDistanceMustMatchTheIndependentDistanceExactly()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Plate();
        audit.Values[3] = float.BitIncrement(3);
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(path: path, fixture: fixture, frame: frame, audit: audit);

            measure.Should().Throw<InvalidOperationException>().WithMessage("*audit-distance*pixel (0,0)*");
        });
    }

    [Fact]
    public void AnUnknownAuditedMaterialIsNotMaskedAsAPlate()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Plate();
        audit.Values[7] = 9;
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(path: path, fixture: fixture, frame: frame, audit: audit);

            measure.Should().Throw<InvalidOperationException>().WithMessage("*audit-surface*pixel (0,0)*");
        });
    }

    [Fact]
    public void AChangedLiveImageIsRejectedFromSurroundingRawCopies()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Plate();
        audit.Snapshots[0].After[0] = 255;
        InDirectory(path =>
        {
            Action measure = () =>
                ReferenceComparison.RetainAuditedMeasurement(path: path, fixture: fixture, frame: frame, audit: audit);

            measure.Should().Throw<InvalidOperationException>().WithMessage("*audit-non-modification*");
        });
    }

    [Fact]
    public void AnAuditedPlateKeepsItsRawAndPlateLight()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Plate();
        InDirectory(path =>
        {
            ComparisonMeasurement measured = ReferenceComparison.RetainAuditedMeasurement(
                path: path,
                fixture: fixture,
                frame: frame,
                audit: audit
            );

            measured.RawSpecular.Should().Equal(8);
            measured.PlateSpecular.Should().Equal(8);
            measured.EmitterSpecular.Should().Equal(0);
        });
    }

    [Fact]
    public void AnAuditedEmitterKeepsItsRawLightAndZeroPlateContribution()
    {
        (ComparisonFixture fixture, ComparisonFrame frame, GeometryAuditFrame audit) = Plate();
        fixture = fixture with
        {
            Emitter = new(Minimum: new(-0.5, 1, -0.5), Maximum: new(0.5, 2, 0.5), Radiance: new(16, 16, 16)),
        };
        frame = frame with { Inputs = [2, 4, 1, 0, 0, 0, 0, 1, 0, 1, 0, -1, 0, 0, 1, 0], Distances = [1] };
        audit = audit with { Values = [0, -1, 0, 1, 0, 1, 0, 1] };
        InDirectory(path =>
        {
            ComparisonMeasurement measured = ReferenceComparison.RetainAuditedMeasurement(
                path: path,
                fixture: fixture,
                frame: frame,
                audit: audit
            );

            measured.RawSpecular.Should().Equal(8);
            measured.PlateSpecular.Should().Equal(0);
            measured.EmitterSpecular.Should().Equal(8);
            MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "probe-inputs.f32")))
                .ToArray()
                .Should()
                .Equal(frame.Inputs);
        });
    }

    [Fact]
    public void TheAuditEmbedsTheExactFrozenProductionMeshVertexShader()
    {
        byte[] production = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, "mesh.vert");
        byte[] tool = ShaderLibrary.LoadSpirv(typeof(GeometryAuditFrame).Assembly, "mesh.vert");

        tool.Should().Equal(production);
    }

    private static (ComparisonFixture Fixture, ComparisonFrame Frame, GeometryAuditFrame Audit) Plate()
    {
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1),
            Emitter: new(Minimum: new(2, 1, -0.5), Maximum: new(3, 2, 0.5), Radiance: new(16, 16, 16)),
            Roughness: 0.2,
            ExposureStops: -2
        );
        var frame = new ComparisonFrame(
            Draw: 0,
            Inputs: [2, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -3, 0, 0, 1, 0],
            Distances: [3],
            History: [0, 0, 0, 0]
        );
        GeometrySnapshot[] snapshots =
        [
            new("distance", [0, 1], [0, 1]),
            new("history", [2, 3], [2, 3]),
            new("composition", [4, 5], [4, 5]),
        ];
        var audit = new GeometryAuditFrame(
            Values: [0, -3, 0, 3, 0, 1, 0, 0],
            Snapshots: snapshots,
            Metadata: JsonSerializer.SerializeToElement(new { OwnedAttachments = true })
        );
        return (fixture, frame, audit);
    }

    private static void InDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-geometry-audit-{Guid.NewGuid():N}");
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
