using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The composite skips the work of the map's terms that add nothing to the image (docs/plans/environment-map.md,
/// "Cost"): the footprint where the lobe alone ends the stars' fade, and the reflected stars where the lobe sees no open
/// sky. The reference is the same pass built by this project with TERTIUS_EVERY_AMBIENT_TERM, which computes every term:
/// composite.frag without the skin scatter, and composite_scatter.frag, the default renderer's pass, under it.
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
    /// The spheres' roughnesses, beside a mirror floor, which shows single stars at the footprint. The pixel angle p is
    /// 2 tan 15° / 180 = 0.00298 rad. At 0.07 the lobe's width at p is 1.56c, so the footprint changes the stars. At
    /// 0.08, which the G-buffer stores as 20 / 255, it is 1.90c, from the 8-bit roughnesses the closest below the
    /// threshold of 2c (1 + 2⁻¹⁰): the lobe alone stays in the fade, but the sphere's footprint ends it, so a threshold
    /// below 1.90c changes these pixels. At 0.2 and 0.6 the lobe alone ends the fade.
    /// </summary>
    private static readonly float[] SphereRoughnesses = [0.07f, 0.08f, 0.2f, 0.6f];

    /// <param name="scatter">True for <see cref="SeparableSubsurface.Default"/>, false for <see cref="NoSubsurface"/>.</param>
    private static VulkanRenderer Renderer(bool scatter) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                // A fixed exposure, whose still draws once: the test compares the composite's ambient terms alone.
                Exposure = new FixedExposure(ExposureStops),
                Bloom = new NoBloom(),
                EnvironmentMap = SceneEnvironmentMap.Default with { FaceSize = 64 },
                // The scene has no skin, so the scatter changes no pixel: under it, the pass draws from
                // composite_scatter.frag, which also writes the diffuse image, and without it from composite.frag.
                Subsurface = scatter ? SeparableSubsurface.Default : new NoSubsurface(),
            }
        );

    /// <summary>
    /// A mirror floor 1 m below the camera, which looks down at it, and four metal spheres on it, each of a roughness of
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
                material: renderer.CreateMaterial(
                    new(BaseColor: Vector4.One, Roughness: 0, Model: new StandardShading(Metallic: 1))
                ),
                position: new(0, -1, -20),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
        for (int index = 0; index < SphereRoughnesses.Length; index++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: renderer.CreateMaterial(
                        new(
                            BaseColor: Vector4.One,
                            Roughness: SphereRoughnesses[index],
                            Model: new StandardShading(Metallic: 1)
                        )
                    ),
                    position: new(x: 2f * (index - 1.5f), y: -0.2f, z: -9),
                    scale: new(0.8f),
                    chainDepth: TestChains.CamerasFrame
                )
            );
        return snapshot;
    }

    /// <summary>
    /// The HDR image of the scene's second draw, by the engine's composite or by the reference, with the skin scatter
    /// on or off.
    /// </summary>
    private static float[] HdrOfStarsInMetal(bool reference, bool scatter)
    {
        VulkanRenderer renderer = Renderer(scatter);
        try
        {
            if (reference)
            {
                renderer.Graph.Skipped.Add("composite");
                renderer.Graph.Add(
                    FrameGraph.Stage.Composite,
                    new EveryTermCompositePass(scatter: renderer.ScatterImages is not null)
                );
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

    /// <param name="scatter">
    /// True for the skin scatter, whose composite is composite_scatter.frag, false for composite.frag. Both include
    /// composite_pass.glsl, but each is its own compiled shader.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSkipsKeepEveryBitOfTheImageOfStarsReflectedInSmoothAndRoughMetal(bool scatter)
    {
        float[] skipping = HdrOfStarsInMetal(reference: false, scatter: scatter);
        float[] everyTerm = HdrOfStarsInMetal(reference: true, scatter: scatter);

        int differing = Enumerable
            .Range(0, skipping.Length)
            .Count(i => BitConverter.SingleToInt32Bits(skipping[i]) != BitConverter.SingleToInt32Bits(everyTerm[i]));
        differing.Should().Be(0, "a skipped term adds nothing, so every channel of every pixel keeps its bits");
        skipping.Distinct().Count().Should().BeGreaterThan(1000, "the scene shows stars, not one flat color");
    }

    /// <summary>
    /// The composite with the shaders of this project's build, which computes every term: a
    /// <see cref="CompositePass"/> under another name, made at its first frame from the frame's device and layouts.
    /// </summary>
    /// <param name="scatter">True under the skin scatter.</param>
    private sealed class EveryTermCompositePass(bool scatter) : RenderPass
    {
        private ShaderLibrary? shaders;
        private CompositePass? pass;

        public override string Name => "composite, every term";

        public override void Prepare(FrameContext context)
        {
            if (pass is not null)
                return;
            shaders = new ShaderLibrary(context.Device, typeof(EveryTermCompositePass).Assembly);
            pass = new CompositePass(
                device: context.Device,
                shaders: shaders,
                layouts: context.Layouts,
                constants: new ShaderConstants(EnvironmentMap: true),
                scatter: scatter
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
