using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

public sealed class ReflectionReconstructionTests
{
    [Fact]
    public void ReconstructedPlateAndEmitterOutputsStaySeparateFromTheUnfilteredMeasurement()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var unfiltered = new ComparisonMeasurement(RawSpecular: [4, 8], PlateSpecular: [4, 0], EmitterSpecular: [0, 8], Surfaces: [0, 1], MaximumFactorRelativeError: 0);
        try
        {
            ComparisonMeasurement result = ReferenceComparison.RetainReconstruction(path: path, unfiltered: unfiltered, output: [12, 8, 10, 16], exposureScale: 2);

            result.RawSpecular.Should().Equal(6, 5);
            result.PlateSpecular.Should().Equal(6, 0);
            result.EmitterSpecular.Should().Equal(0, 5);
            unfiltered.RawSpecular.Should().Equal(4, 8);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void AnInvalidReconstructionRetainsItsActualValuesBeforeRejection()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var unfiltered = new ComparisonMeasurement(RawSpecular: [4], PlateSpecular: [4], EmitterSpecular: [0], Surfaces: [0], MaximumFactorRelativeError: 0);
        try
        {
            Action reconstruct = () => ReferenceComparison.RetainReconstruction(path: path, unfiltered: unfiltered, output: [float.NaN, 8], exposureScale: 2);

            reconstruct.Should().Throw<InvalidDataException>();
            File.ReadAllBytes(Path.Combine(path, "reconstructed-output.f32")).Length.Should().Be(8);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
