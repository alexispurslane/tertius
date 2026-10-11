using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Far shadows: a caster casts an analytic sphere shadow, from any frame, in two cases. A round body (Celestial, such as
/// a planet), whose surface is its sphere, always does, and never goes into the shadow maps. Any other caster does when
/// its bounding sphere stays farther than 2 km from the camera, where the shadow maps end. A round body's sphere has a
/// positive radius, and it darkens a point inside it as the ground darkens it at night. Any other caster's bounding sphere
/// has a negative radius, and it does not shadow what lies inside it, such as its own surface. A far shadow counts only
/// while the sun lights the scene, and only when its sphere covers at least a pixel or a fifth of the sun's angular
/// radius. The output is 1600 by 900 with a 60 degree field of view, so one pixel is about 1.28e-3 rad.
/// </summary>
public sealed class FarShadowTests
{
    private static readonly MeshInfo[] Meshes = [new(FirstIndex: 0, IndexCount: 600, VertexOffset: 0, Radius: 1f)];
    private static readonly MeshId Sphere = new(0);

    private static RenderInstance Caster(Vector3 position, float radius, bool visible = true, bool round = false) =>
        RenderInstance.Create(Sphere, new MaterialId(0), position, Quaternion.Identity, new Vector3(radius), 0,
            (visible ? RenderFlags.Visible : RenderFlags.None) | RenderFlags.CastsShadow | (round ? RenderFlags.Celestial : RenderFlags.None), 0, 0);

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
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -3e7f), 6e6f, round: true));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(0, 0, -3e7f, 6e6f));
        scene.Constants.OccluderCount.Should().Be(1u);
    }

    [Fact]
    public void ARoundBodyCastsAFarShadowEvenWhenTheCameraStandsOnIt()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // A planet 2 m under the camera: things farther than the shadow maps reach still need its shadow at night.
        snapshot.Instances.Add(Caster(new Vector3(0, -6.371e6f - 2, 0), 6.371e6f, round: true));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, -6.371e6f - 2, 0, 6.371e6f));
    }

    [Fact]
    public void ACasterThatIsNotRoundAndComesWithin2KmCastsNoFarShadowHoweverBigItIs()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -10), 1));
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -1_900), 500));
        // A ground plane 6 km across around the camera.
        snapshot.Instances.Add(Caster(new Vector3(0, -1, -100), 3_000));

        FarShadows(Prepare(snapshot)).Should().BeEmpty("the shadow maps shadow them");
    }

    [Fact]
    public void ACasterThatIsNotRoundCastsItsBoundingSphereAsAFarShadowBeyondTheShadowMaps()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 5 km: 8e-3 rad, about six pixels.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -5_000), 20));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, 0, -5_000, -20));
    }

    [Fact]
    public void ACasterJustOverAPixelCastsAFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 25 km: 1.6e-3 rad, about 1.25 pixels.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -25_000), 20));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, 0, -25_000, -20));
    }

    [Fact]
    public void ACasterUnderAPixelThatHidesLittleOfTheSunCastsNoFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 50 km: 8e-4 rad, about 0.6 pixels. Against the sun at 1 AU, 4.67e-3 rad in radius, it hides at
        // most 0.7% of the disc.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -50_000), 20));
        // 2,000 km across at 2e9 m: 1e-3 rad, also under a pixel, though it is far bigger than a kilometer.
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -2e9f), 1e6f, round: true));

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void ACasterUnderAPixelStillCastsAFarShadowWhenItHidesAShareOfASmallSun()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // Near Saturn, the sun is 4.85e-4 rad in radius, less than half a pixel. A moon 9e-4 rad across is under a pixel,
        // but it can hide the whole sun.
        snapshot.Sun.AngularRadius = 4.85e-4f;
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -1e9f), 4.5e5f, round: true));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, 0, -1e9f, 4.5e5f));
    }

    [Fact]
    public void AnInvisibleCasterStillCastsAFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -5_000), 1_000, visible: false));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(0, 0, -5_000, -1_000));
    }

    [Fact]
    public void AnObjectThatCastsNoShadowCastsNoFarShadowHoweverBigItIs()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // A star: drawn, but the light itself, so it casts no shadow.
        snapshot.Instances.Add(Gpu.Instance(Sphere, new MaterialId(0), new Vector3(1.5e11f, 0, 0), new Vector3(7e8f), flags: RenderFlags.Celestial));

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void WithoutASunThereAreNoFarShadows()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX, sunIntensity: 0);
        snapshot.Instances.Add(Caster(new Vector3(0, 0, -3e7f), 6e6f, round: true));

        FarShadows(Prepare(snapshot)).Should().BeEmpty("a far shadow only blocks the sun");
    }

    [Fact]
    public void AFarShadowFollowsTheInterpolatedPlaceOfItsCasterRelativeToTheInterpolatedCamera()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Camera.PreviousPosition = new Vector3(0, 0, 8);
        RenderInstance caster = Caster(new Vector3(0, 0, -5_000), 1_000);
        caster.PreviousPosition = new Vector3(0, 2_000, -5_000);
        snapshot.Instances.Add(caster);

        // A quarter of the way, the caster is at (0, 1,500, -5,000) and the camera at (0, 0, 6).
        FarShadows(Prepare(snapshot, alpha: 0.25f)).Should().Equal(new Vector4(0, 1_500, -5_006, -1_000));
    }

    [Fact]
    public void EveryFarShadowCountsInTheOrderOfItsCasters()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        for (int i = 0; i < 20; i++)
            snapshot.Instances.Add(Caster(new Vector3(i * 100, 0, -5_000 - i * 2_000), 50));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Select(shadow => shadow.Z).Should().Equal(Enumerable.Range(0, 20).Select(i => -5_000f - i * 2_000));
        scene.Constants.OccluderCount.Should().Be(20u);
    }

    [Theory]
    [InlineData(1, 10, false, true)]
    [InlineData(500, 2_499, false, true)]
    [InlineData(500, 2_500, false, true)]
    [InlineData(500, 2_501, false, false)]
    [InlineData(3_000, 100, false, true)]
    [InlineData(6.371e6, 6.371e6 + 2, false, true)]
    [InlineData(6.371e6, 6.371e6 + 2, true, false)]
    [InlineData(1, 10, true, false)]
    [InlineData(6e6, 3e7, false, false)]
    public void TheShadowMapsTakeACasterThatIsNotRoundWhoseBoundingSphereComesWithin2Km(float radius, float distance, bool round, bool mapped)
    {
        ShadowCascades.TakesCaster(distance, radius, round ? RenderFlags.Celestial | RenderFlags.CastsShadow : RenderFlags.CastsShadow).Should().Be(mapped);
    }
}
