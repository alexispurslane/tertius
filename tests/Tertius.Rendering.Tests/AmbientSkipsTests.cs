using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The ambient pass skips the work of the map's terms that add nothing to the image (docs/plans/environment-map.md,
/// "Cost"): the footprint where the lobe alone ends the stars' fade, and the reflected stars where the lobe sees no open
/// sky. The reference is the same pass built by this project with TERTIUS_EVERY_AMBIENT_TERM, which computes every term.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientSkipsTests
{
    private const int Width = 320,
        Height = 180;

    private static readonly float Fov = 30f * MathF.PI / 180;

    /// <summary>6 stops above the stars' radiance scale of 2⁻¹⁰, so that the reflected stars show in the image.</summary>
    private static readonly float ExposureStops = -MathF.Log2(StarField.UnitRadiance) + 6;

    /// <summary>
    /// The scene's roughnesses: a mirror floor, which shows single stars at the footprint; a sphere in the fade, where the
    /// footprint changes the stars; and spheres past it, where the lobe alone ends the fade.
    /// </summary>
    private static readonly float[] SphereRoughnesses = [0.07f, 0.2f, 0.6f];

    private static VulkanRenderer Renderer() =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                Exposure = new FixedExposure(ExposureStops),
                Bloom = new NoBloom(),
                EnvironmentMap = SceneEnvironmentMap.Default with { FaceSize = 64 },
            }
        );

    /// <summary>
    /// A mirror floor 1 m below the camera, which looks down at it, and three metal spheres on it, each of a roughness of
    /// <see cref="SphereRoughnesses"/>. The floor and the spheres reflect each other, where the lobe sees no open sky, and
    /// the starry sky elsewhere. A faint sun lights them.
    /// </summary>
    private static RenderSnapshot StarsInMetal(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Normalize(new(0.3f, 0.6f, 0.5f)), starIrradiance: 2e-7f);
        snapshot.Ambient.Intensity = 0;
        snapshot.Camera.VerticalFov = Fov;
        snapshot.Camera.Rotation = Gpu.PitchDown(0.2f);
        snapshot.Camera.PreviousRotation = snapshot.Camera.Rotation;
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(40f)),
                material: renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0, Metallic: 1)),
                position: new(0, -1, -20),
                scale: Vector3.One
            )
        );
        MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
        for (int index = 0; index < SphereRoughnesses.Length; index++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: renderer.CreateMaterial(
                        new(BaseColor: Vector4.One, Roughness: SphereRoughnesses[index], Metallic: 1)
                    ),
                    position: new(x: 2.2f * (index - 1), y: -0.2f, z: -9),
                    scale: new(0.8f)
                )
            );
        return snapshot;
    }

    /// <summary>The HDR image of the scene's second draw, by the engine's ambient pass or by the reference.</summary>
    private static float[] HdrOfStarsInMetal(bool reference)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            if (reference)
            {
                renderer.Graph.Skipped.Add("ambient light");
                renderer.Graph.Add(FrameGraph.Stage.AmbientLight, new EveryTermAmbientPass());
            }

            RenderSnapshot snapshot = StarsInMetal(renderer);
            renderer.Render(snapshot, 1f);
            renderer.Render(snapshot, 1f);
            return renderer.ReadHdr();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSkipsKeepEveryBitOfTheImageOfStarsReflectedInSmoothAndRoughMetal()
    {
        float[] skipping = HdrOfStarsInMetal(reference: false);
        float[] everyTerm = HdrOfStarsInMetal(reference: true);

        int differing = Enumerable
            .Range(0, skipping.Length)
            .Count(i => BitConverter.SingleToInt32Bits(skipping[i]) != BitConverter.SingleToInt32Bits(everyTerm[i]));
        differing.Should().Be(0, "a skipped term adds nothing, so every channel of every pixel keeps its bits");
        skipping.Distinct().Count().Should().BeGreaterThan(1000, "the scene shows stars, not one flat color");
    }

    /// <summary>
    /// The ambient pass with the shaders of this project's build, which computes every term: an
    /// <see cref="AmbientLightPass"/> under another name, made at its first frame from the frame's device and layouts.
    /// </summary>
    private sealed class EveryTermAmbientPass : RenderPass
    {
        private ShaderLibrary? shaders;
        private AmbientLightPass? pass;

        public override string Name => "ambient light, every term";

        public override void Prepare(FrameContext context)
        {
            if (pass is not null)
                return;
            shaders = new ShaderLibrary(context.Device, typeof(EveryTermAmbientPass).Assembly);
            pass = new AmbientLightPass(
                device: context.Device,
                shaders: shaders,
                layouts: context.Layouts,
                constants: new ShaderConstants(EnvironmentMap: true)
            );
        }

        public override void Record(FrameContext context) => pass!.Record(context);

        public override void Dispose()
        {
            pass?.Dispose();
            shaders?.Dispose();
            base.Dispose();
        }
    }
}
