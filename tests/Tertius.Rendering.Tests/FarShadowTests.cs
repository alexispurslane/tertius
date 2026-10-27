using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Far shadows of the test star, a far light with a cascade set: every shadow caster is also a sphere of its bounding
/// radius, from any frame, which shadows the star for points outside it and never for points inside it. A caster whose
/// bounding sphere comes within 2 km of the camera is also in the shadow maps: its sphere then counts only where the maps
/// end, for receivers farther than 2 km, and those casters come first in the star's run of occluders. A sphere counts only
/// while the star shines, and only when it can be noticed: a caster beyond the maps must cover a pixel or a fifth of the
/// star's angular radius seen from the camera, and a caster in the maps must hide 4% of the star from 2 km away, where its
/// far receivers start. The output is 1600 by 900 with a 60 degree field of view, so one pixel is about 1.28e-3 rad, and
/// the star is 4.67e-3 rad in radius.
/// </summary>
public sealed class FarShadowTests
{
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 600, VertexOffset: 0, Radius: 1f, Deform: null),
    ];
    private static readonly MeshId Sphere = new(0);

    private static RenderInstance Caster(Vector3 position, float radius, bool visible = true) =>
        RenderInstance.Create(
            mesh: Sphere,
            material: new(0),
            position: position,
            rotation: Quaternion.Identity,
            scale: new(radius),
            boundingRadius: 0,
            hemisphere: HemisphereUp.Even,
            chainDepth: TestChains.CamerasFrame,
            flags: (visible ? RenderFlags.Visible : RenderFlags.None) | RenderFlags.CastsShadow,
            entityIndex: 0,
            part: 0
        );

    private static PreparedScene Prepare(RenderSnapshot snapshot, float alpha = 1f, int cascadedLights = 1)
    {
        var scene = new PreparedScene();
        Batchers
            .WithCascadedLights(cascadedLights)
            .Prepare(
                snapshot: snapshot,
                alpha: alpha,
                width: 1600,
                height: 900,
                meshes: Meshes,
                materialCount: 8,
                environment: default,
                region: DeformRegion.None,
                scene: scene
            );
        return scene;
    }

    private static Vector4[] FarShadows(PreparedScene scene) =>
        scene.Occluders.AsSpan(0, scene.OccluderCount).ToArray();

    /// <summary>How many of the star's occluders the star's cascade set also draws.</summary>
    private static int Mapped(PreparedScene scene) =>
        scene.Lights[0].Shadow is CascadeSet { Mapped: var mapped }
            ? mapped
            : throw new InvalidOperationException("The star has no cascade set.");

    /// <summary>The cascade set and the far shadows of the far light <paramref name="light"/> of the prepared lights.</summary>
    private static CascadeSet SetOf(PreparedScene scene, int light) =>
        scene.Lights[light].Shadow is CascadeSet set
            ? set
            : throw new InvalidOperationException($"Light {light} has no cascade set.");

    /// <summary>The far shadows of the far light <paramref name="light"/> of the prepared lights, in their order.</summary>
    private static Vector4[] FarShadowsOf(PreparedScene scene, int light)
    {
        OccluderRange range = SetOf(scene, light).Occluders;
        return scene.Occluders.AsSpan(range.Start, range.Count).ToArray();
    }

    /// <summary>
    /// A second star, dimmer than the test star, so it takes set 1 under it: behind the camera and a little below the
    /// horizon, so its set's boxes reach back from the view toward it.
    /// </summary>
    private static PointRenderLight StarBehindTheCamera() =>
        Gpu.StarLight(
            direction: new(0, -0.1f, 1),
            irradiance: 1,
            angularRadius: Gpu.StarAngularRadius,
            color: Gpu.StarColor,
            chainDepth: TestChains.Root
        );

    /// <summary>The star's light as the shaders read it, with its run of occluders.</summary>
    private static GpuLight StarOf(PreparedScene scene) => GpuLight.From(scene.Lights[0]);

    // ---- Casters beyond the shadow maps -----------------------------------------------------------------------

    [Fact]
    public void APlanetCastsAFarShadowAtItsCenterWithItsRadius()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(x: 0, y: 0, z: -3e7f, w: 6e6f));
        Mapped(scene).Should().Be(0);
        StarOf(scene).Occluders.Y.Should().Be(1u);
    }

    [Fact]
    public void ACasterBeyondTheShadowMapsThatCoversSixPixelsCastsAFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 5 km: 8e-3 rad, about six pixels.
        snapshot.Instances.Add(Caster(new(0, 0, -5_000), 20));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(x: 0, y: 0, z: -5_000, w: 20));
    }

    [Fact]
    public void ACasterJustOverAPixelCastsAFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 25 km: 1.6e-3 rad, about 1.25 pixels.
        snapshot.Instances.Add(Caster(new(0, 0, -25_000), 20));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(x: 0, y: 0, z: -25_000, w: 20));
    }

    [Fact]
    public void ACasterUnderAPixelThatHidesLittleOfTheSunCastsNoFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // 40 m across at 50 km: 8e-4 rad, about 0.6 pixels. It hides at most 0.7% of the sun.
        snapshot.Instances.Add(Caster(new(0, 0, -50_000), 20));
        // 2,000 km across at 2e9 m: 1e-3 rad, also under a pixel, though it is far bigger than a kilometer.
        snapshot.Instances.Add(Caster(new(0, 0, -2e9f), 1e6f));

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void ACasterUnderAPixelStillCastsAFarShadowWhenItHidesAShareOfASmallSun()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // Near Saturn, the Sun is 4.85e-4 rad in radius, less than half a pixel. A moon 9e-4 rad across is under a pixel,
        // but it can hide the whole Sun.
        Gpu.Star(snapshot: snapshot, angularRadius: 4.85e-4f);
        snapshot.Instances.Add(Caster(new(0, 0, -1e9f), 4.5e5f));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(x: 0, y: 0, z: -1e9f, w: 4.5e5f));
    }

    [Fact]
    public void AnInvisibleCasterStillCastsAFarShadow()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new(0, 0, -5_000), 1_000, visible: false));

        FarShadows(Prepare(snapshot)).Should().Equal(new Vector4(x: 0, y: 0, z: -5_000, w: 1_000));
    }

    // ---- Casters in the shadow maps -----------------------------------------------------------------------------

    [Fact]
    public void ASmallCasterNearTheCameraIsInTheShadowMapsOnly()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // From 2 km away, a crate 1 m in radius hides at most 1% of the sun, and a person at the camera less.
        snapshot.Instances.Add(Caster(new(0, 0, -10), 1));
        snapshot.Instances.Add(Caster(new(0, -1, 0), 0.9f));

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void AShipInTheShadowMapsAlsoCastsAFarShadowForReceiversBeyondThem()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // A 50 m ship 500 m away, at least 1.5 km from any receiver beyond the maps, hides up to all of the sun there.
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(x: 0, y: 0, z: -500, w: 50));
        Mapped(scene).Should().Be(1);
        StarOf(scene).Occluders.Z.Should().Be(1u);
    }

    [Fact]
    public void APlanetUnderTheCameraIsInTheShadowMapsAndStillShadowsEverythingBeyondThem()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new(0, -6.371e6f - 2, 0), 6.371e6f));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(x: 0, y: -6.371e6f - 2, z: 0, w: 6.371e6f));
        Mapped(scene).Should().Be(1);
    }

    [Fact]
    public void TheFarShadowsOfCastersInTheShadowMapsComeFirst()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new(0, 0, -5_000), 20));
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50));
        snapshot.Instances.Add(Caster(new(0, 0, -6_000), 30));
        snapshot.Instances.Add(Caster(new(0, 0, -700), 60));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Select(shadow => shadow.Z).Should().Equal(-500f, -700f, -5_000f, -6_000f);
        Mapped(scene).Should().Be(2);
    }

    // ---- Casters that a light's set does not draw -------------------------------------------------------------
    // A 10 m caster 600 m behind the camera comes within 2 km, but lies beside every box of the set of a star overhead,
    // whose boxes stand on the view; it is big enough to hide 4% of the star from 2 km.

    [Fact]
    public void AFarLightWhoseSetDrawsNoCasterHasNoMappedOccludersWhileAnotherSetDrawsOne()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Lights.Add(StarBehindTheCamera());
        snapshot.Instances.Add(Caster(new(0, 0, 600), 10));

        PreparedScene scene = Prepare(snapshot, cascadedLights: 2);

        FarShadowsOf(scene, 0).Should().Equal(new Vector4(x: 0, y: 0, z: 600, w: 10));
        SetOf(scene, 0)
            .Mapped.Should()
            .Be(0, "the star overhead's set draws no caster, so the sphere is its only shadow");
        SetOf(scene, 1).Mapped.Should().Be(1, "the set of the star behind the camera draws the caster");
    }

    [Fact]
    public void AnOccluderThatItsLightsSetDoesNotDrawFollowsTheMappedOnesWhileTheSetDrawsOthers()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(Caster(new(0, 0, 600), 10));
        // In the boxes of the star's set, 20 m ahead.
        snapshot.Instances.Add(Caster(new(0, -5, -20), 5));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene)
            .Should()
            .Equal(new Vector4(x: 0, y: -5, z: -20, w: 5), new Vector4(x: 0, y: 0, z: 600, w: 10));
        Mapped(scene).Should().Be(1, "the set draws the caster ahead, but not the one behind the camera");
    }

    // ---- Every caster ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnObjectThatCastsNoShadowCastsNoFarShadowHoweverBigItIs()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        // A star: drawn, but the light itself, so it casts no shadow.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: Sphere,
                material: new(0),
                position: new(1.5e11f, 0, 0),
                scale: new(7e8f),
                flags: RenderFlags.Celestial
            )
        );

        FarShadows(Prepare(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void WithoutAStarThereAreNoFarShadows()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX, starIrradiance: 0);
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().BeEmpty("a far shadow only blocks a far light");
        scene.LightCount.Should().Be(0, "a star of no power does not shine");
    }

    [Fact]
    public void AFarShadowFollowsTheInterpolatedPlaceOfItsCasterRelativeToTheInterpolatedCamera()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Camera.PreviousPosition = new(0, 0, 8);
        RenderInstance caster = Caster(new(0, 0, -5_000), 1_000);
        caster.PreviousPosition = new(0, 2_000, -5_000);
        snapshot.Instances.Add(caster);

        // A quarter of the way, the caster is at (0, 1,500, -5,000) and the camera at (0, 0, 6).
        FarShadows(Prepare(snapshot, alpha: 0.25f)).Should().Equal(new Vector4(x: 0, y: 1_500, z: -5_006, w: 1_000));
    }

    [Fact]
    public void EveryFarShadowCountsInTheOrderOfItsCasters()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        for (int i = 0; i < 20; i++)
            snapshot.Instances.Add(Caster(new(i * 100, 0, -5_000 - i * 2_000), 50));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene)
            .Select(shadow => shadow.Z)
            .Should()
            .Equal(Enumerable.Range(0, 20).Select(i => -5_000f - i * 2_000));
        StarOf(scene).Occluders.Y.Should().Be(20u);
    }

    [Theory]
    [InlineData(1, 10, true)]
    [InlineData(500, 2_499, true)]
    [InlineData(500, 2_500, true)]
    [InlineData(500, 2_501, false)]
    [InlineData(3_000, 100, true)]
    [InlineData(6.371e6, 6.371e6 + 2, true)]
    [InlineData(6e6, 3e7, false)]
    public void TheShadowMapsTakeACasterWhoseBoundingSphereComesWithin2Km(float radius, float distance, bool mapped) =>
        ShadowCascades.TakesCaster(distance, radius).Should().Be(mapped);
}
