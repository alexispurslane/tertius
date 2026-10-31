using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The CPU side of the cascade sets (docs/plans/local-light-shadows.md, Part 4): each far light with a
/// <see cref="CascadeSet"/> gets four cascades of its own, along its own direction, and the shadow pass culls each caster
/// against every cascade of every set. No GPU needed. The camera sits at the render origin and looks down −Z at
/// 1600 × 900. Two stars cast shadows: the brighter, straight above, takes set 0, and the other, along +X, set 1.
/// </summary>
public sealed class ShadowPassTests
{
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f, Deform: null),
    ];

    /// <summary>A white star 1.5 × 10¹¹ m along <paramref name="direction"/>, that casts shadows.</summary>
    private static PointRenderLight Star(Vector3 direction, float irradiance) =>
        Gpu.StarLight(
            direction: direction,
            irradiance: irradiance,
            angularRadius: 0.00467f,
            color: Vector3.One,
            chainDepth: TestChains.Root
        );

    /// <summary>
    /// The scene of two stars, batched with two cascade sets, which the batcher fits, then with the sets written into the
    /// frame constants as the shadow pass writes them.
    /// </summary>
    private static PreparedScene TwoStars()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Lights.Add(Star(Vector3.UnitY, irradiance: 3));
        snapshot.Lights.Add(Star(Vector3.UnitX, irradiance: 1));
        var scene = new PreparedScene();
        Batchers
            .WithCascadedLights(2)
            .Prepare(
                snapshot: snapshot,
                alpha: 1f,
                highestStops: 0,
                width: 1600,
                height: 900,
                meshes: Meshes,
                materials: MaterialSlots.Of(1),
                environment: new WithoutMap(),
                region: DeformRegion.None,
                scene: scene
            );
        ShadowPass.WriteSets(scene);
        return scene;
    }

    private static PreparedInstance Caster(Vector3 position, float radius) =>
        new()
        {
            Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            Position = position,
            Rotation = Quaternion.Identity,
            Scale = new(radius),
            Radius = radius,
            Distance = position.Length(),
        };

    private static bool Touches(int mask, int set, int cascade) => (mask & (1 << ShadowMap.Layer(set, cascade))) != 0;

    [Fact]
    public void EachCascadeSetFitsItsOwnLightsDirection()
    {
        PreparedScene scene = TwoStars();

        scene.CascadeSetCount.Should().Be(2);
        Vector3.Distance(scene.CascadeBases[0].Z, Vector3.UnitY).Should().BeLessThan(1e-6f, "set 0 is the star above");
        Vector3
            .Distance(scene.CascadeBases[1].Z, Vector3.UnitX)
            .Should()
            .BeLessThan(1e-6f, "set 1 is the star along +X");
        for (int set = 0; set < 2; set++)
        {
            GpuCascadeSet gpu = scene.Constants.CascadeSets[set];
            new Vector3(gpu.AxisX.X, gpu.AxisX.Y, gpu.AxisX.Z).Should().Be(scene.CascadeBases[set].X);
            new Vector3(gpu.AxisY.X, gpu.AxisY.Y, gpu.AxisY.Z).Should().Be(scene.CascadeBases[set].Y);
            for (int cascade = 0; cascade < ShadowCascades.Count; cascade++)
                gpu.ViewProj[cascade].Should().Be(scene.CascadeSets[set][cascade].ViewProjection);
        }
        // Each set's boxes lie across its own light: a point toward the star of a set is nearer its light side.
        foreach ((int set, Vector3 towardLight) in new[] { (0, Vector3.UnitY), (1, Vector3.UnitX) })
        {
            Matrix4x4 viewProjection = scene.CascadeSets[set][0].ViewProjection;
            float Depth(Vector3 point)
            {
                var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
                return clip.Z / clip.W;
            }

            Depth(new Vector3(0, 0, -10) + towardLight).Should().BeGreaterThan(Depth(new(0, 0, -10)));
        }
    }

    [Fact]
    public void ACasterIsCulledPerSetAndCascade()
    {
        PreparedScene scene = TwoStars();

        int near = ShadowPass.CascadeMask(scene, Caster(new(0, 0, -5), 0.5f));
        int far = ShadowPass.CascadeMask(scene, Caster(new(0, 0, -1_000), 1));
        // 300 m to the right of the view, 30 m ahead: toward the star along +X from the nearest box of set 1, and 300 m
        // across the light from the nearest box of set 0, which is about 50 m wide.
        int aside = ShadowPass.CascadeMask(scene, Caster(new(300, 0, -30), 1));

        Touches(near, set: 0, cascade: 0).Should().BeTrue();
        Touches(near, set: 1, cascade: 0).Should().BeTrue();
        Touches(far, set: 0, cascade: 0).Should().BeFalse();
        Touches(far, set: 1, cascade: 0).Should().BeFalse();
        Touches(far, set: 0, cascade: 3).Should().BeTrue();
        Touches(far, set: 1, cascade: 3).Should().BeTrue();
        Touches(aside, set: 1, cascade: 0).Should().BeTrue("the caster stands between set 1's light and its box");
        Touches(aside, set: 0, cascade: 0).Should().BeFalse("the caster is beside set 0's box, across its light");
        (near & ~0xFF).Should().Be(0, "two sets have eight layers");
    }
}
