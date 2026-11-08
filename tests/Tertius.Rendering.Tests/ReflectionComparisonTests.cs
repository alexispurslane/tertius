using System.Runtime.InteropServices;
using System.Text.Json;
using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>The matched comparison reads outgoing light once and integrates the recorded receiver at a pixel center.</summary>
public sealed class ReflectionComparisonTests
{
    [Fact]
    public void ADirectlyVisibleEmitterHasZeroPlateContributionWithoutLosingItsRawLight()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-plate-emitter-{Guid.NewGuid():N}");
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1),
            Emitter: new(Minimum: new(-0.5, 1, -0.5), Maximum: new(0.5, 2, 0.5), Radiance: new(16, 16, 16)),
            Roughness: 0.2,
            ExposureStops: -2
        );
        float[] inputs = [2, 4, 1, 0, 0, 0, 0, 1, 0, 1, 0, -1, 0, 0, 1, 0];
        var frame = new ComparisonFrame(Draw: 0, Inputs: inputs, Distances: [1], History: [0, 0, 0, 0]);
        try
        {
            ComparisonMeasurement measured = ReferenceComparison.RetainAndMeasure(
                path: path,
                fixture: fixture,
                frame: frame,
                measured: false
            );

            measured.RawSpecular.Should().Equal(8);
            measured.PlateSpecular.Should().Equal(0);
            measured.EmitterSpecular.Should().Equal(8);
            measured.Surfaces.Should().Equal((byte)ReferenceSurface.Emitter);
            MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "probe-inputs.f32")))
                .ToArray()
                .Should()
                .Equal(inputs);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    [Fact]
    public void AVisiblePlateRetainsItsMeasuredSignal()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-plate-visible-{Guid.NewGuid():N}");
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
        try
        {
            ComparisonMeasurement measured = ReferenceComparison.RetainAndMeasure(
                path: path,
                fixture: fixture,
                frame: frame,
                measured: false
            );

            measured.RawSpecular.Should().Equal(8);
            measured.PlateSpecular.Should().Equal(8);
            measured.EmitterSpecular.Should().Equal(0);
            measured.Surfaces.Should().Equal((byte)ReferenceSurface.Plate);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    [Fact]
    public void InvalidSurfaceInputsRetainTheEvidenceAndNameTheFailingPixel()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-plate-invalid-{Guid.NewGuid():N}");
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1),
            Emitter: new(Minimum: new(2, 1, -0.5), Maximum: new(3, 2, 0.5), Radiance: new(16, 16, 16)),
            Roughness: 0.2,
            ExposureStops: -2
        );
        float[] inputs = [2, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -2, 0, 0, 1, 0];
        var frame = new ComparisonFrame(Draw: 0, Inputs: inputs, Distances: [3], History: [0, 0, 0, 0]);
        try
        {
            Action measure = () =>
                ReferenceComparison.RetainAndMeasure(path: path, fixture: fixture, frame: frame, measured: false);

            measure.Should().Throw<InvalidOperationException>().WithMessage("*primary-position*pixel (0,0)*");
            MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "probe-inputs.f32")))
                .ToArray()
                .Should()
                .Equal(inputs);
            MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "distance.f32")))
                .ToArray()
                .Should()
                .Equal(3);
            MemoryMarshal
                .Cast<byte, float>(File.ReadAllBytes(Path.Combine(path, "source-history.f32")))
                .ToArray()
                .Should()
                .Equal(0, 0, 0, 0);
            using var diagnostics = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "validation.json")));
            JsonElement failure = diagnostics.RootElement.GetProperty("Failures")[0];
            failure.GetProperty("Predicate").GetString().Should().Be("primary-position");
            failure.GetProperty("Units").GetString().Should().Be("m");
            failure.GetProperty("Tolerance").GetDouble().Should().Be(1e-4);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    [Fact]
    public void AnUnrecognizedMaterialIsRejectedRatherThanExcludedFromTheComparison()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-plate-material-{Guid.NewGuid():N}");
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1),
            Emitter: new(Minimum: new(2, 1, -0.5), Maximum: new(3, 2, 0.5), Radiance: new(16, 16, 16)),
            Roughness: 0.2,
            ExposureStops: -2
        );
        var frame = new ComparisonFrame(
            Draw: 0,
            Inputs: [2, 0, 0.9f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -3, 0, 0, 1, 0],
            Distances: [3],
            History: [0, 0, 0, 0]
        );
        try
        {
            Action measure = () =>
                ReferenceComparison.RetainAndMeasure(path: path, fixture: fixture, frame: frame, measured: false);

            measure.Should().Throw<InvalidOperationException>().WithMessage("*plate-roughness*pixel (0,0)*");
            File.Exists(Path.Combine(path, "probe-inputs.f32")).Should().BeTrue();
            File.Exists(Path.Combine(path, "metadata.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    [Fact]
    public void TheCenterReferenceUsesTheOriginalEmitterBeforePlateVisibilityRule()
    {
        var camera = new ReferenceCamera(
            Eye: new(0, 3, 0),
            Target: Double3.Zero,
            VerticalFov: Math.PI / 3,
            Width: 1,
            Height: 1
        );
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 2, 1), Radiance: new(16, 16, 16));
        var material = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0.2, Reflectance: 0.5);
        var crop = new ReferenceCrop(Left: 0, Top: 0, Width: 1, Height: 1);
        DecodedReceiver[] receivers =
        [
            new(Position: Double3.Zero, Normal: Double3.UnitY, ToView: Double3.UnitY, Material: material),
        ];

        ReferenceImage center = ReferenceComparison.CenterLevels(
            receivers: receivers,
            emitter: emitter,
            crop: crop,
            levels: [128],
            seed: 41,
            camera: camera
        )[0];
        ReferenceImage original = ReferenceImage.RenderLevels(
            camera: camera,
            crop: crop,
            emitter: emitter,
            material: material,
            levels: [128],
            seed: 41
        )[0];

        original.Pixels[0].Should().Be(Double3.Zero);
        center.Pixels[0].Should().Be(original.Pixels[0]);
    }

    [Fact]
    public void TheProbeWritesEachExportOnceWithoutExceedingTheRenderHelpersThreeTargets()
    {
        IReadOnlyList<ProbeOutputGroup> groups = ReflectionComparisonProbe.OutputGroups;

        groups
            .SelectMany(group => Enumerable.Range(group.First, group.Count))
            .Should()
            .BeEquivalentTo(Enumerable.Range(0, 8));
        groups.Should().OnlyContain(group => group.Count >= 1 && group.Count <= 3);
    }

    [Fact]
    public void ProbeReadbackUndoesPreExposureWithoutApplyingTheMaterialTwice()
    {
        float[] values = [2, 4, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, -1, -3, -2, 0, 0.5f, 0.8660254f];

        ComparisonPixel pixel = ReferenceComparison.Decode(values, new(0, 3, 6), -2);

        pixel.Specular.Should().Be(8);
        pixel.Emission.Should().Be(16);
        pixel.Receiver.Position.Should().Be(new Double3(-1, 0, 4));
        pixel.Receiver.Normal.Should().Be(Double3.UnitY);
        pixel.Receiver.Material.Roughness.Should().Be((double)0.2f);
        pixel.Receiver.Material.Reflectance.Should().Be(0.5);
        pixel.Receiver.Material.BaseColor.Should().Be(new Double3(0.06f, 0.06f, 0.06f));
        pixel.Receiver.Material.Metallic.Should().Be(0);
        pixel.Receiver.ToView.Should().Be(new Double3(0, 0.5f, 0.8660254f));
    }

    [Fact]
    public void CenterReferenceUsesTheRecordedReceiverInsteadOfTheIdealPlane()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0.2, Reflectance: 0.5);
        DecodedReceiver[] receivers =
        [
            new(Position: Double3.Zero, Normal: Double3.UnitY, ToView: Double3.UnitY, Material: material),
            new(Position: new(0, 2, 0), Normal: Double3.UnitY, ToView: Double3.UnitY, Material: material),
        ];

        ReferenceImage image = ReferenceComparison.CenterLevels(
            receivers: receivers,
            emitter: emitter,
            crop: new(Left: 530, Top: 240, Width: 2, Height: 1),
            levels: [2048],
            seed: 41
        )[0];

        image.Pixels[0].X.Should().BeInRange(0.039, 0.042);
        image.Pixels[1].Should().Be(Double3.Zero);
    }

    [Fact]
    public void CenterReferenceUsesTheRecordedNormalAndViewDirection()
    {
        var emitter = new ReferenceEmitter(Minimum: new(1, -1, -1), Maximum: new(1.1, 1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0.2, Reflectance: 0.5);
        DecodedReceiver[] receivers =
        [
            new(Position: Double3.Zero, Normal: Double3.UnitX, ToView: Double3.UnitX, Material: material),
        ];

        ReferenceImage image = ReferenceComparison.CenterLevels(
            receivers: receivers,
            emitter: emitter,
            crop: new(Left: 530, Top: 240, Width: 1, Height: 1),
            levels: [2048],
            seed: 41
        )[0];

        image.Pixels[0].X.Should().BeInRange(0.039, 0.042);
    }
}
