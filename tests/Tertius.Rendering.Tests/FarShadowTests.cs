using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Far shadows: a shadow caster that the shadow maps do not take, because its bounding sphere stays farther than 2 km from
/// the camera, casts an analytic sphere shadow of its bounding radius instead, from any frame. It does so only when the
/// sun lights the scene, and only when the sphere covers at least one pixel: a smaller sphere hides too little of the sun
/// to notice. The output is 1600 by 900 with a 60 degree field of view, so one pixel is about 1.28e-3 rad.
/// </summary>
public sealed class FarShadowTests
{
    private static readonly MeshInfo[] Meshes = [new(FirstIndex: 0, IndexCount: 600, VertexOffset: 0, Radius: 1f)];
    private static readonly MeshId Sphere = new(0);

    private static RenderInstance Caster(Vector3 position, float radius, bool visible = true) =>
        RenderInstance.Create(Sphere, new MaterialId(0), position, Quaternion.Identity, new Vector3(radius), 0,
            visible ? RenderFlags.Visible | RenderFlags.CastsShadow : RenderFlags.CastsShadow, 0, 0);

    private static PreparedScene Prepare(RenderSnapshot snapshot, float alpha = 1f)
    {
        var scene = new PreparedScene();
        new SceneBatcher().Prepare(snapshot, alpha, 1600, 900, Meshes, 8, scene);
        return scene;
    }

    private static Vector4[] FarShadows(PreparedScene scene) => scene.Occluders.AsSpan(0, scene.OccluderCount).ToArray();

    [Fact]
    public void APlanetCastsAFarShadowAtItsCenterWithItsRadius()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -3e7f), 6e6f));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(0, 0, -3e7f, 6e6f));
        scene.Constants.OccluderCount.Should().Be(1u);
    }

    [Fact]
    public void ACasterThatComesWithin2KmCastsNoFarShadowHoweverBigItIs()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -10), 1));
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -1_900), 500));
        // A ground plane 6 km across around the camera, and a planet 2 m under it.
        snapshot.Instances.Add(Caster(new Vector3(0, -1, -100), 3_000));
        snapshot.Instances.Add(Caster(new Vector3(0, -6.371e6f - 2, 0), 6.371e6f));

        FarShadows(Prepare(snapshot)).Should().BeEmpty("the shadow maps shadow them");
    }

    [Fact]
    public void ASmallCasterBeyondTheShadowMapsCastsAFarShadowWhenItCoversAPixel()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 5 km: 8e-3 rad, about six pixels.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -5_000), 20));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, 0, -5_000, 20));
    }

    [Fact]
    public void ACasterThatCoversLessThanAPixelCastsNoFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 50 km: 8e-4 rad, less than a pixel. It hides at most 3% of the sun anywhere near the camera.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -50_000), 20));
        // 2,000 km across at 2e9 m: 1e-3 rad, also less than a pixel, though it is far bigger than a kilometer.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -2e9f), 1e6f));

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void AnInvisibleCasterStillCastsAFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -5_000), 1_000, visible: false));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, 0, -5_000, 1_000));
    }

    [Fact]
    public void AnObjectThatCastsNoShadowCastsNoFarShadowHoweverBigItIs()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // A star: drawn, but the light itself, so it casts no shadow.
        snapshot.Instances.Add(Gpu.Instance(Sphere, new MaterialId(0), new Vector3(1.5e11f, 0, 0), new Vector3(7e8f)));

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void WithoutASunThereAreNoFarShadows()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX, sunIntensity: 0);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -3e7f), 6e6f));

        FarShadows(Prepare(snapshot)).Should().BeEmpty("a far shadow only blocks the sun");
    }

    [Fact]
    public void AFarShadowFollowsTheInterpolatedPlaceOfItsCasterRelativeToTheCamera()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Camera.PreviousPosition = new Vector3(0, 0, 2);
        RenderInstance planet = Caster(new Vector3(0, 0, -3e7f), 6e6f);
        planet.PreviousPosition = new Vector3(0, 2e6f, -3e7f);
        snapshot.Instances.Add(planet);

        // Halfway, the planet is at (0, 1e6, -3e7) and the camera at (0, 0, 1).
        FarShadows(Prepare(snapshot, alpha: 0.5f)).Should().Equal(new Vector4(0, 1e6f, -3e7f - 1, 6e6f));
    }

    [Theory]
    [InlineData(1, 10, true)]
    [InlineData(500, 2_499, true)]
    [InlineData(500, 2_501, false)]
    [InlineData(3_000, 100, true)]
    [InlineData(6.371e6, 6.371e6 + 2, true)]
    [InlineData(6e6, 3e7, false)]
    public void TheShadowMapsTakeACasterWhoseBoundingSphereComesWithin2Km(float radius, float distance, bool mapped)
    {
        ShadowCascades.TakesCaster(distance, radius).Should().Be(mapped);
    }
}
