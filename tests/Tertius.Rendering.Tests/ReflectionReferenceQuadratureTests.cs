using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Independent area integration resolves a narrow lobe and refuses to approve an exhausted calculation.</summary>
public sealed class ReflectionReferenceQuadratureTests
{
    [Fact]
    public void PointOnlyModeProducesNumericalEvidenceWithoutImages()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tertius-reference-point-test-{Guid.NewGuid():N}");
        try
        {
            int result = Program.Main(["--points", "--out", path]);

            result.Should().Be(0);
            Directory.GetFiles(path).Select(Path.GetFileName).Should().BeEquivalentTo("points.json", "points.csv");
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "points.json")));
            document.RootElement.GetProperty("Cases").GetArrayLength().Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    [Fact]
    public void IndependentAreaIntegrationResolvesTheObliqueSmoothBarReflection()
    {
        var camera = new ReferenceCamera(
            Eye: new(0, 3, 6), Target: new(0, 0, 6 - 3 * Math.Sqrt(3)),
            VerticalFov: MathF.PI / 3f, Width: 1280, Height: 720
        );
        Double3 ray = camera.Ray(570.5, 320.5);
        Double3 point = camera.Eye + (-camera.Eye.Y / ray.Y) * ray;
        var emitter = new ReferenceEmitter(Minimum: new(-1.1, 0, -2.1), Maximum: new(-0.9, 2, -1.9), Radiance: new(6800, 6800, 6800));
        var material = new MaterialSample(BaseColor: new(0.06f, 0.06f, 0.06f), Metallic: 0, Roughness: 0, Reflectance: 0.5);
        var integral = new ReferenceIntegral(emitter, material);
        double mixed = integral.Estimate(point: point, toView: -ray, pairs: 262144, seed: 311).Specular.X;

        ReferenceQuadratureResult area = integral.IntegrateEmitter(
            point: point, toView: -ray, relativeTolerance: 1e-6, absoluteTolerance: 1e-7, maxEvaluations: 300000
        );

        area.Light.Specular.X.Should().BeApproximately(mixed, mixed * 0.0005);
        area.Converged.Should().BeTrue();
    }

    [Fact]
    public void AreaIntegrationMatchesTheAnalyticLambertIrradiance()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 0, Roughness: 0.6, Reflectance: 0.5);
        var integral = new ReferenceIntegral(emitter, material);

        ReferenceQuadratureResult area = integral.IntegrateEmitter(
            point: Double3.Zero, toView: Double3.UnitY, relativeTolerance: 1e-7, absoluteTolerance: 1e-9, maxEvaluations: 300000
        );

        double expected = 4 / (Math.PI * Math.Sqrt(2)) * Math.Atan(1 / Math.Sqrt(2));
        area.Light.Diffuse.X.Should().BeApproximately(expected, 1e-7);
        area.Converged.Should().BeTrue();
    }

    [Fact]
    public void AnExhaustedIntegrationCannotClaimConvergence()
    {
        var emitter = new ReferenceEmitter(Minimum: new(-1, 1, -1), Maximum: new(1, 1.1, 1), Radiance: new(1, 1, 1));
        var material = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 0, Roughness: 0, Reflectance: 0.5);
        var integral = new ReferenceIntegral(emitter, material);

        ReferenceQuadratureResult area = integral.IntegrateEmitter(
            point: Double3.Zero, toView: Double3.UnitY, relativeTolerance: 1e-9, absoluteTolerance: 1e-12, maxEvaluations: 34
        );

        area.Converged.Should().BeFalse();
        area.Evaluations.Should().BeLessThanOrEqualTo(34);
    }
}
