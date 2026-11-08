using System.Text.Json;
using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Numeric raster inputs remain available after either successful or failed geometry validation.</summary>
public sealed class ReflectionRasterMetadataTests
{
    [Fact]
    public void RasterInputsAreRetainedWithAValidPlateMeasurement()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-raster-valid-{Guid.NewGuid():N}");
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1), Emitter: new(Minimum: new(2, 1, -0.5), Maximum: new(3, 2, 0.5), Radiance: new(16, 16, 16)),
            Roughness: 0.2, ExposureStops: -2
        );
        JsonElement raster = JsonSerializer.SerializeToElement(new
        {
            Device = new { SubPixelPrecisionBits = 8 },
            Viewport = new double[] { 1, 1, 1, 1 },
            Instances = new[] { new { ModelRow0 = new double[] { 0.2, 0, 0, -1 } } },
        });
        var frame = new ComparisonFrame(
            Draw: 0, Inputs: [2, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -3, 0, 0, 1, 0], Distances: [3], History: [0, 0, 0, 0]
        ) { RasterMetadata = raster };
        try
        {
            ComparisonMeasurement result = ReferenceComparison.RetainAndMeasure(path: path, fixture: fixture, frame: frame, measured: false);

            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "metadata.json")));
            saved.RootElement.TryGetProperty("RasterMetadata", out JsonElement retained).Should().BeTrue("numeric inputs must accompany the retained frame");
            retained.GetProperty("Device").GetProperty("SubPixelPrecisionBits").GetUInt32().Should().Be(8);
            retained.GetProperty("Viewport")[0].GetDouble().Should().Be(1);
            retained.GetProperty("Instances")[0].GetProperty("ModelRow0")[3].GetDouble().Should().Be(-1);
            result.PlateSpecular.Should().Equal(8);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    [Fact]
    public void FailedGeometryRetainsNumericRasterInputsBeforeThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-raster-invalid-{Guid.NewGuid():N}");
        var fixture = new ComparisonFixture(
            Camera: new(Eye: new(0, 3, 0), Target: Double3.Zero, VerticalFov: Math.PI / 3, Width: 1, Height: 1),
            Crop: new(Left: 0, Top: 0, Width: 1, Height: 1), Emitter: new(Minimum: new(2, 1, -0.5), Maximum: new(3, 2, 0.5), Radiance: new(16, 16, 16)),
            Roughness: 0.2, ExposureStops: -2
        );
        var frame = new ComparisonFrame(
            Draw: 0, Inputs: [2, 0, 0.2f, 0.5f, 0.06f, 0, 0, 1, 0, 3, 0, -2, 0, 0, 1, 0], Distances: [3], History: [0, 0, 0, 0]
        ) { RasterMetadata = JsonSerializer.SerializeToElement(new { SubPixelPrecisionBits = 8, CameraRight = new double[] { 1, 0, 0, 0 } }) };
        try
        {
            Action measure = () => ReferenceComparison.RetainAndMeasure(path: path, fixture: fixture, frame: frame, measured: false);

            measure.Should().Throw<InvalidOperationException>().WithMessage("*primary-position*pixel (0,0)*");
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "metadata.json")));
            saved.RootElement.TryGetProperty("RasterMetadata", out JsonElement retained).Should().BeTrue("a failed check must not discard its numeric inputs");
            retained.GetProperty("SubPixelPrecisionBits").GetUInt32().Should().Be(8);
            retained.GetProperty("CameraRight")[0].GetDouble().Should().Be(1);
            File.ReadAllBytes(Path.Combine(path, "probe-inputs.f32")).Length.Should().Be(64);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
