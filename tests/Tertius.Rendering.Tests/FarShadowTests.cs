using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Far shadows: every shadow caster is also a sphere of its bounding radius, from any frame, which shadows the sun for
/// points outside it and never for points inside it. A caster whose bounding sphere comes within 2 km of the camera is also
/// in the shadow maps: its sphere then counts only where the maps end, for receivers farther than 2 km, and those casters
/// come first in the list. A sphere counts only while the sun lights the scene, and only when it can be noticed: a caster
/// beyond the maps must cover a pixel or a fifth of the sun's angular radius seen from the camera, and a caster in the maps
/// must hide 4% of the sun from 2 km away, where its far receivers start. The output is 1600 by 900 with a 60 degree field
/// of view, so one pixel is about 1.28e-3 rad, and the sun is 4.67e-3 rad in radius.
/// </summary>
public sealed class FarShadowTests
{
    private static readonly MeshInfo[] Meshes = [new(FirstIndex: 0, IndexCount: 600, VertexOffset: 0, Radius: 1f)];
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

    private static PreparedScene Prepare(RenderSnapshot snapshot, float alpha = 1f)
    {
        var scene = new PreparedScene();
        new SceneBatcher().Prepare(
            snapshot: snapshot,
            alpha: alpha,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materialCount: 8,
            environment: default,
            scene: scene
        );
        return scene;
    }

    private static Vector4[] FarShadows(PreparedScene scene) =>
        scene.Occluders.AsSpan(0, scene.OccluderCount).ToArray();

    // ---- Casters beyond the shadow maps -----------------------------------------------------------------------

    [Fact]
    public void APlanetCastsAFarShadowAtItsCenterWithItsRadius()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(x: 0, y: 0, z: -3e7f, w: 6e6f));
        scene.MappedOccluderCount.Should().Be(0);
        scene.Constants.OccluderCount.Should().Be(1u);
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
        // Near Saturn, the sun is 4.85e-4 rad in radius, less than half a pixel. A moon 9e-4 rad across is under a pixel,
        // but it can hide the whole sun.
        snapshot.Sun.AngularRadius = 4.85e-4f;
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
        scene.MappedOccluderCount.Should().Be(1);
        scene.Constants.MappedOccluderCount.Should().Be(1u);
    }

    [Fact]
    public void APlanetUnderTheCameraIsInTheShadowMapsAndStillShadowsEverythingBeyondThem()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX);
        snapshot.Instances.Add(Caster(new(0, -6.371e6f - 2, 0), 6.371e6f));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().Equal(new Vector4(x: 0, y: -6.371e6f - 2, z: 0, w: 6.371e6f));
        scene.MappedOccluderCount.Should().Be(1);
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
        scene.MappedOccluderCount.Should().Be(2);
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
    public void WithoutASunThereAreNoFarShadows()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitX, sunIntensity: 0);
        snapshot.Instances.Add(Caster(new(0, 0, -3e7f), 6e6f));
        snapshot.Instances.Add(Caster(new(0, 0, -500), 50));

        PreparedScene scene = Prepare(snapshot);

        FarShadows(scene).Should().BeEmpty("a far shadow only blocks the sun");
        scene.MappedOccluderCount.Should().Be(0);
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
        scene.Constants.OccluderCount.Should().Be(20u);
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
