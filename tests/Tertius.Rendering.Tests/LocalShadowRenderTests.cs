using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The local shadow atlas on the GPU (docs/plans/local-light-shadows.md, Part 6): near spot and point lights that cast
/// shadows shadow their casters onto every surface that the shading passes light, within the tile budget, from cached
/// tiles as from drawn ones. The exposure is fixed at 0 stops, no ambient light and no star light the scenes, so a
/// shadowed surface is black. The camera is at the origin and looks down −Z unless a test turns it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LocalShadowRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The largest share of a lit pixel's luma that a shadowed pixel nearby may show.</summary>
    private const float ShadowedShare = 0.15f;

    /// <summary>The radiant intensity of the test lamps, in W/sr: about 2 W/m² at 4 m, a mid gray on white.</summary>
    private const float LampIntensity = 30;

    private const uint LampEntity = 100;
    private const uint SecondLampEntity = 101;
    private const uint BoxEntity = 7;

    /// <summary>The spot light of <see cref="SpotOverABox"/>, 4 m above the floor, pointing down.</summary>
    private static readonly Vector3 SpotPlace = new(0, 2, -7);

    /// <summary>The 1 m box of <see cref="SpotOverABox"/>, 3 m below the spot light and 1 m above the floor.</summary>
    private static readonly Vector3 BoxPlace = new(0, -1, -7);

    /// <summary>A point of the floor in the box's shadow, 0.72 m from the spot light's axis, which the camera sees.</summary>
    private static readonly Vector3 UnderTheBox = new(0.4f, -2, -6.4f);

    /// <summary>A point of the floor outside the box's shadow, 1.6 m from the axis, inside the spot light's inner cone.</summary>
    private static readonly Vector3 BesideTheBox = new(1.5f, -2, -6.4f);

    private static VulkanRenderer Renderer(
        LocalShadowSetting? setting = null,
        EnvironmentMapSetting? environmentMap = null
    ) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            localShadows: setting ?? LocalShadowAtlas.Default,
            environmentMap: environmentMap
        );

    /// <summary>A snapshot with no light at all, and the camera turned by <paramref name="camera"/>.</summary>
    private static RenderSnapshot Dark(Quaternion? camera = null)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        snapshot.Camera.Rotation = camera ?? Quaternion.Identity;
        snapshot.Camera.PreviousRotation = snapshot.Camera.Rotation;
        return snapshot;
    }

    /// <summary>A white spot light of radius 0 that casts shadows, with an outer half-angle of 0.6 rad.</summary>
    private static SpotRenderLight ShadowingSpot(
        Vector3 position,
        Vector3 direction,
        float intensity = LampIntensity,
        uint entity = LampEntity
    ) =>
        Gpu.Spot(
            position: position,
            direction: direction,
            color: Vector3.One,
            intensity: intensity,
            innerConeAngle: 0.48f,
            outerConeAngle: 0.6f,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            CastsShadow = true,
            EntityIndex = entity,
        };

    /// <summary>A white point light of radius 0 that casts shadows.</summary>
    private static PointRenderLight ShadowingLamp(Vector3 position, uint entity = LampEntity) =>
        Gpu.Lamp(
            position: position,
            color: Vector3.One,
            intensity: LampIntensity,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            CastsShadow = true,
            EntityIndex = entity,
        };

    /// <summary>
    /// The rotation of a camera at the origin that looks at <paramref name="target"/>, with its up as near
    /// <paramref name="upHint"/> as the view allows.
    /// </summary>
    private static Quaternion LookAt(Vector3 target, Vector3 upHint)
    {
        var forward = Vector3.Normalize(target);
        var right = Vector3.Normalize(Vector3.Cross(forward, upHint));
        var up = Vector3.Cross(right, forward);
        // Row-vector form: the rows are the images of the camera's x, y and z axes, and the camera looks down its −Z.
        var axes = new Matrix4x4(
            m11: right.X,
            m12: right.Y,
            m13: right.Z,
            m14: 0,
            m21: up.X,
            m22: up.Y,
            m23: up.Z,
            m24: 0,
            m31: -forward.X,
            m32: -forward.Y,
            m33: -forward.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
        return Quaternion.CreateFromRotationMatrix(axes);
    }

    /// <summary>The mean luma of the 3 × 3 pixels around the pixel that sees <paramref name="point"/>.</summary>
    private static float LumaAt(Capture image, Vector3 point, Quaternion? camera = null) =>
        image.MeanLuma(
            Gpu.Project(point: point, cameraRotation: camera ?? Quaternion.Identity, width: Width, height: Height),
            radius: 1
        );

    /// <summary>
    /// A white floor at y = −2 under a spot light 4 m above it that points down, and a box of 1 m that casts shadows at
    /// <paramref name="box"/>, <see cref="BoxPlace"/> by default, or no box.
    /// </summary>
    private static RenderSnapshot SpotOverABox(Kit kit, Vector3? box = null, bool withBox = true)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(kit.Floor(center: new(0, -2, -7), halfSize: 8));
        snapshot.Lights.Add(ShadowingSpot(position: SpotPlace, direction: -Vector3.UnitY));
        if (withBox)
            snapshot.Instances.Add(kit.Caster(center: box ?? BoxPlace, size: Vector3.One));
        return snapshot;
    }

    /// <summary>The red, green and blue bytes of every pixel of <paramref name="image"/>: the fourth byte holds no defined value.</summary>
    private static byte[] Rgb(Capture image) => [.. image.Rgba.Where((_, index) => index % 4 != 3)];

    [Fact]
    public void ASpotLightOverABoxShadowsTheFloorUnderIt()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);

            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: SpotOverABox(kit),
                name: "spot-over-a-box"
            );

            float lit = LumaAt(image, BesideTheBox);
            lit.Should().BeGreaterThan(0.2f, "the spot light lights the floor beside the box");
            LumaAt(image, UnderTheBox)
                .Should()
                .BeLessThan(ShadowedShare * lit, "the box hides the floor under it from the spot light");
            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(1, "a spot light of 0.6 rad takes one tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APointLightShadowsEachOfSixPillarsAroundIt()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // One view per axis: the camera stands beside the axis inside the room and looks at the wall where the
            // pillar on that axis throws its shadow, so each face of the light's cube is seen once.
            foreach (CubeFace face in Enum.GetValues<CubeFace>())
            {
                Vector3 axis = CubeFaces.Basis(face).Forward;
                Vector3 aside = face switch
                {
                    CubeFace.PositiveX or CubeFace.NegativeX => Vector3.UnitY,
                    CubeFace.PositiveY or CubeFace.NegativeY => Vector3.UnitZ,
                    CubeFace.PositiveZ or CubeFace.NegativeZ => Vector3.UnitX,
                };
                Vector3 lamp = -(axis * 2 + aside * 3);
                Vector3 shadow = lamp + axis * 4;
                Quaternion camera = LookAt(target: shadow, upHint: Vector3.Cross(axis, aside));
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(ShadowingLamp(lamp));
                kit.AddRoomWithPillars(snapshot: snapshot, center: lamp);

                Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: $"pillars-{face}");

                float lit = LumaAt(image, shadow + aside * 1.5f, camera);
                lit.Should().BeGreaterThan(0.2f, "the lamp lights the wall of {0} beside the shadow", face);
                LumaAt(image, shadow, camera)
                    .Should()
                    .BeLessThan(ShadowedShare * lit, "the pillar on the axis of {0} shadows the wall behind it", face);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AShadowAcrossTheSeamOfTwoFacesHasNoGap()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // The diagonal between +X and +Z, where the two faces meet, and the rod across it.
            var diagonal = Vector3.Normalize(new(1, 0, 1));
            var across = Vector3.Normalize(new(1, 0, -1));
            Vector3 lamp = -(diagonal + Vector3.UnitY * 1.2f);
            Vector3 wall = lamp + diagonal * 4;
            Quaternion camera = LookAt(target: wall, upHint: Vector3.UnitY);
            RenderSnapshot snapshot = Dark(camera);
            snapshot.Lights.Add(ShadowingLamp(lamp));
            snapshot.Instances.Add(kit.Panel(center: wall, normal: -diagonal, halfSize: 3));
            snapshot.Instances.Add(
                kit.Caster(
                    center: lamp + diagonal * 1.5f,
                    size: new(0.15f, 0.15f, 2),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 3 * MathF.PI / 4)
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "seam");

            float lit = LumaAt(image, wall + Vector3.UnitY, camera);
            lit.Should().BeGreaterThan(0.2f, "the lamp lights the wall above the rod's shadow");
            // The rod's shadow crosses the seam at the wall's center, 0.4 m wide and 5 m long: every point along it is dark.
            for (int step = -20; step <= 20; step++)
            {
                Vector3 point = wall + across * (step * 0.05f);
                LumaAt(image, point, camera)
                    .Should()
                    .BeLessThan(
                        ShadowedShare * lit,
                        "the rod shadows the wall {0} m along it from the seam",
                        step * 0.05f
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheShadowsEdgeIsSoftOverTheFilter()
    {
        const int tileSize = 128;
        VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 8, TileSize: tileSize));
        try
        {
            var kit = Kit.Create(renderer);
            // A slab whose edge lies in the plane x = 0 through the light, so its shadow's edge on the floor 4 m below
            // the light is the line x = 0, on the light's axis.
            var spot = new Vector3(0, 3, -6);
            RenderSnapshot Scene(bool withSlab)
            {
                RenderSnapshot snapshot = Dark();
                snapshot.Instances.Add(kit.Floor(center: new(0, -1, -6), halfSize: 6));
                snapshot.Lights.Add(ShadowingSpot(position: spot, direction: -Vector3.UnitY));
                if (withSlab)
                    snapshot.Instances.Add(kit.Caster(center: new(-1.5f, 1, -6), size: new(3, 0.2f, 3)));
                return snapshot;
            }

            // The green channel of the HDR image, linear light, with and without the slab.
            float Share(float[] shadowed, float[] lit, Vector3 point)
            {
                Vector2 pixel = Gpu.Project(point: point, width: Width, height: Height);
                int i = ((int)pixel.Y * Width + (int)pixel.X) * 4 + 1;
                return shadowed[i] / lit[i];
            }

            renderer.Render(Scene(withSlab: false), 1f);
            float[] unshadowed = renderer.ReadHdr();
            renderer.Render(Scene(withSlab: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            // The share of the light that reaches the floor at x, from 0.6 m inside the shadow to 0.6 m outside it.
            float[] xs = [.. Enumerable.Range(-120, 241).Select(step => step * 0.005f)];
            float[] shares = [.. xs.Select(x => Share(shadowed: shadowed, lit: unshadowed, point: new(x, -1, -6)))];
            shares[0].Should().BeLessThan(0.02f, "the slab hides the floor 0.6 m inside its shadow");
            shares[^1].Should().BeGreaterThan(0.98f, "the floor 0.6 m outside the shadow sees the whole light");
            float start = xs[Array.FindIndex(shares, share => share >= 0.05f)];
            float end = xs[Array.FindIndex(shares, share => share >= 0.95f)];
            float texel = 2 * MathF.Tan(0.6f) * tileSize / (tileSize - 2 * ShadowTiles.MarginTexels) / tileSize * 4;
            ((end - start) / texel)
                .Should()
                .BeInRange(2.5f, 5.5f, "the 4 × 4 bilinear taps one texel apart blur the edge over 4 texels");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(60f)]
    public void ALitFloorUnderASpotLightHasNoAcne(float angleDegrees)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            float angle = angleDegrees * MathF.PI / 180;
            var center = new Vector3(0, -1.5f, -6);
            Vector3 spot = center + 4 * new Vector3(MathF.Sin(angle), MathF.Cos(angle), 0);
            Quaternion camera = LookAt(target: center, upHint: Vector3.UnitY);
            RenderSnapshot Scene(bool floorCasts)
            {
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(ShadowingSpot(position: spot, direction: Vector3.Normalize(center - spot)));
                snapshot.Instances.Add(
                    kit.Floor(
                        center: center,
                        halfSize: 8,
                        flags: floorCasts ? RenderFlags.CastsShadow : RenderFlags.None
                    )
                );
                return snapshot;
            }

            Capture reference = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(floorCasts: false),
                name: "acne-reference"
            );
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: Scene(floorCasts: true), name: "acne");

            // In the reference the floor casts nothing, so its lit pixels cannot have acne; with self-shadowing they must
            // come out the same.
            int lit = 0,
                differing = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float expected = reference.Luma(x, y);
                if (expected < 0.1f)
                    continue;
                lit++;
                if (MathF.Abs(image.Luma(x, y) - expected) > 2.5f / 255f)
                    differing++;
            }

            lit.Should().BeGreaterThan(Width * Height / 20, "the spot light lights a large patch of the floor");
            differing.Should().BeLessThanOrEqualTo(lit / 1000, "a lit floor that casts shadows must not shadow itself");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The upper-bound partner of <see cref="ALitFloorUnderASpotLightHasNoAcne"/>: a thin caster resting on the floor still
    /// shadows the floor one texel behind its base. Each case puts the caster's face that the light sees steep to it, where
    /// the slope-scaled bias is largest: a wall 4 cm thick at 30 degrees, a plate 5 cm thick at 60. A bias of 4 instead of
    /// 2.5, or a normal offset of 3 texels instead of 1, lights that floor.
    /// </summary>
    [Theory]
    [InlineData(30f, 0.5f, 0.04f)]
    [InlineData(60f, 0.05f, 0.5f)]
    public void AShadowMeetsTheBaseOfAThinCasterOnTheFloor(float angleDegrees, float height, float depth)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            float angle = angleDegrees * MathF.PI / 180;
            // The camera looks steeply down at the floor 2 m below it, so a pixel there spans about one tile texel.
            var center = new Vector3(0, -2, -1);
            Vector3 spot = center + 4 * new Vector3(0, MathF.Cos(angle), -MathF.Sin(angle));
            Quaternion camera = LookAt(target: center, upHint: Vector3.UnitY);
            RenderSnapshot Scene(bool withCaster)
            {
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(ShadowingSpot(position: spot, direction: Vector3.Normalize(center - spot)));
                snapshot.Instances.Add(kit.Floor(center: center, halfSize: 8));
                if (withCaster)
                    snapshot.Instances.Add(
                        kit.Caster(center: center + new Vector3(0, height / 2, 0), size: new(1, height, depth))
                    );
                return snapshot;
            }

            renderer.Render(Scene(withCaster: false), 1f);
            float[] lit = renderer.ReadHdr();
            renderer.Render(Scene(withCaster: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            // The light comes from −Z, so the shadow falls toward the camera, at +Z. A texel of the default tile, 4 m from
            // the light, as TheShadowsEdgeIsSoftOverTheFilter computes it.
            float texel = 2 * MathF.Tan(0.6f) / (LocalShadowAtlas.Default.TileSize - 2 * ShadowTiles.MarginTexels) * 4;
            Vector2 pixel = Gpu.Project(
                point: center + new Vector3(0, 0, depth / 2 + texel),
                cameraRotation: camera,
                width: Width,
                height: Height
            );
            int green = ((int)pixel.Y * Width + (int)pixel.X) * 4 + 1;
            lit[green].Should().BeGreaterThan(0, "the spot light lights the floor there without the caster");
            (shadowed[green] / lit[green])
                .Should()
                .BeLessThan(0.05f, "the caster hides the floor one texel behind its base from the spot light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightPastTheBudgetLightsWithoutAShadow()
    {
        VulkanRenderer renderer = Renderer(LocalShadowAtlas.Default with { Tiles = 1 });
        try
        {
            var kit = Kit.Create(renderer);
            // Two spot lights over two boxes, 5 m apart; the brighter one at the eye takes the one tile.
            var shift = new Vector3(2.5f, 0, 0);
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(kit.Floor(center: new(0, -2, -7), halfSize: 8));
            snapshot.Lights.Add(ShadowingSpot(position: SpotPlace - shift, direction: -Vector3.UnitY));
            snapshot.Lights.Add(
                ShadowingSpot(
                    position: SpotPlace + shift,
                    direction: -Vector3.UnitY,
                    intensity: 20,
                    entity: SecondLampEntity
                )
            );
            snapshot.Instances.Add(kit.Caster(center: BoxPlace - shift, size: Vector3.One, entity: BoxEntity));
            snapshot.Instances.Add(kit.Caster(center: BoxPlace + shift, size: Vector3.One, entity: BoxEntity + 1));

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "past-the-budget");

            var mirror = new Vector3(-1, 1, 1);
            LumaAt(image, mirror * UnderTheBox - shift)
                .Should()
                .BeLessThan(
                    ShadowedShare * LumaAt(image, mirror * BesideTheBox - shift),
                    "the brighter light has the tile"
                );
            LumaAt(image, UnderTheBox + shift)
                .Should()
                .BeGreaterThanOrEqualTo(
                    LumaAt(image, BesideTheBox + shift),
                    "the dimmer light has no tile, so it lights the floor under its box, nearer its axis, more brightly"
                );
            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACachedTileShowsWhatADrawnTileShows()
    {
        VulkanRenderer renderer = Renderer();
        VulkanRenderer fresh = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            _ = Gpu.RenderAndCapture(renderer: renderer, snapshot: SpotOverABox(kit), name: "drawn-tile");
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "the first frame draws the new tile");

            Capture cached = Gpu.RenderAndCapture(renderer: renderer, snapshot: SpotOverABox(kit), name: "cached-tile");
            RenderStats stats = renderer.LastFrameStats;
            Capture drawn = Gpu.RenderAndCapture(
                renderer: fresh,
                snapshot: SpotOverABox(Kit.Create(fresh)),
                name: "fresh-tile"
            );

            stats.ShadowTilesDrawn.Should().Be(0, "neither the light nor the box moved");
            stats.ShadowTilesInUse.Should().Be(1);
            LumaAt(cached, UnderTheBox)
                .Should()
                .BeLessThan(ShadowedShare * LumaAt(cached, BesideTheBox), "the cached tile holds the box");
            Rgb(cached).Should().Equal(Rgb(drawn), "a cached tile holds what the frame that drew it drew");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(fresh);
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACasterThatMovesMovesItsShadowInTheSameFrame()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            _ = Gpu.RenderAndCapture(renderer: renderer, snapshot: SpotOverABox(kit), name: "before-the-move");

            // The box moves 1 m toward −X: its shadow now spans x = −2.4 to −0.57 m on the floor.
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: SpotOverABox(kit, box: BoxPlace - Vector3.UnitX),
                name: "after-the-move"
            );

            float lit = LumaAt(image, BesideTheBox);
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "the box moved far more than a quarter of a texel");
            LumaAt(image, new(-1.4f, -2, -6.4f))
                .Should()
                .BeLessThan(ShadowedShare * lit, "the shadow follows the box in the frame it moved");
            LumaAt(image, UnderTheBox)
                .Should()
                .BeGreaterThanOrEqualTo(lit, "the floor where the shadow was is lit again");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void GlassAndALitPuffTakeTheTileShadows()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // A pane and a puff side by side under the box, in its shadow, with no floor behind them.
            var pane = new Vector3(-0.25f, -1.8f, -6.6f);
            var puff = new Vector3(0.25f, -1.8f, -6.6f);
            MaterialId glass = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 0.8f),
                    Roughness: 0.5f,
                    Reflectance: 0,
                    Alpha: new GlassAlpha()
                )
            );
            RenderSnapshot Scene(bool withBox) =>
                GlassAndPuffUnderASpot(kit: kit, glass: glass, pane: pane, puff: puff, withBox: withBox);

            Capture lit = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(withBox: false),
                name: "glass-puff-lit"
            );
            Capture shadowed = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(withBox: true),
                name: "glass-puff-shadowed"
            );

            foreach (
                (string name, Vector3 point) in (ReadOnlySpan<(string Name, Vector3 Point)>)
                    [("pane", pane), ("puff", puff)]
            )
            {
                LumaAt(lit, point).Should().BeGreaterThan(0.1f, "the spot light lights the {0}", name);
                LumaAt(shadowed, point)
                    .Should()
                    .BeLessThan(ShadowedShare * LumaAt(lit, point), "the box shadows the {0}", name);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A pane of <paramref name="glass"/> and a lit puff under the spot light, in the shadow of the box when there is one.</summary>
    private static RenderSnapshot GlassAndPuffUnderASpot(
        Kit kit,
        MaterialId glass,
        Vector3 pane,
        Vector3 puff,
        bool withBox
    )
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(ShadowingSpot(position: SpotPlace, direction: -Vector3.UnitY, intensity: 100));
        if (withBox)
            snapshot.Instances.Add(kit.Caster(center: BoxPlace, size: Vector3.One));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: kit.Plane,
                material: glass,
                position: pane,
                scale: new(0.12f, 1, 0.12f),
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.PuffLooks.Add(
            new(
                Life: 2,
                StartRadius: 0.1f,
                EndRadius: 0.1f,
                TemperatureRatio: 0.5f,
                Shading: PuffShading.Lit(new(0.8f))
            )
        );
        snapshot.Puffs.Add(
            new()
            {
                Position = puff,
                PreviousPosition = puff,
                Age = 0,
                Look = 0,
            }
        );
        return snapshot;
    }

    [Fact]
    public void AnEnvironmentFaceShowsALampsShadow()
    {
        const int faceSize = 64;
        VulkanRenderer renderer = Renderer(
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = faceSize,
                FacesPerFrame = CubeFaces.Count,
            }
        );
        try
        {
            var kit = Kit.Create(renderer);
            // The camera, at the origin, stands 2 m above the floor; the face that looks down sees the box's shadow.
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(kit.Floor(center: new(1.5f, -2, 0), halfSize: 4));
            snapshot.Lights.Add(ShadowingSpot(position: new(1.5f, 1, 0), direction: -Vector3.UnitY));
            snapshot.Instances.Add(kit.Caster(center: new(1.5f, -1, 0), size: new(0.6f)));

            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: 0);
            Half[] face = EnvironmentScenes.Face(renderer, CubeFace.NegativeY);

            float Luminance(Vector3 point)
            {
                (CubeFace located, float s, float t) = CubeFaces.Locate(point);
                located.Should().Be(CubeFace.NegativeY);
                Vector4 texel = EnvironmentScenes.Texel(
                    face: face,
                    size: faceSize,
                    x: (int)(s * faceSize),
                    y: (int)(t * faceSize)
                );
                return 0.2126f * texel.X + 0.7152f * texel.Y + 0.0722f * texel.Z;
            }

            float lit = Luminance(new(1.5f, -2, 1.2f));
            lit.Should().BeGreaterThan(0, "the spot light lights the floor beside the box");
            Luminance(new(1.5f, -2, 0)).Should().BeLessThan(ShadowedShare * lit, "the face samples the lamp's tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The lens of <see cref="FarBoxUnderASpot"/>: 15°.</summary>
    private const float FarBoxLens = MathF.PI / 12;

    /// <summary>The floor point under the box of <see cref="FarBoxUnderASpot"/>: 20 m away, 30° below the horizon.</summary>
    private static readonly Vector3 FarBoxFloor = new(0, -10, -17.32f);

    /// <summary>The camera of <see cref="FarBoxUnderASpot"/>, which looks at <see cref="FarBoxFloor"/>.</summary>
    private static Quaternion FarBoxCamera => LookAt(FarBoxFloor, Vector3.UnitY);

    /// <summary>
    /// The image of a box of 0.15 m 0.2 m over a floor at <see cref="FarBoxFloor"/>, under a spot light 0.5 m over the
    /// floor, through <see cref="FarBoxLens"/>, with a local shadow budget of one tile and all six faces of the map drawn
    /// in the frame. With <paramref name="withTheLightBehind"/>, a second spot light stands 20 m behind the camera with a
    /// reach of 0.95 of that, so only face +Z keeps it and no surface lies within its reach. Both are under the cutoff
    /// irradiance at the eye, the light behind brighter: 0.9 of it against 0.6.
    /// </summary>
    private static Capture FarBoxUnderASpot(bool withTheLightBehind)
    {
        double cutoff = LightUnits.CutoffIrradiance(0);
        Vector3 spotPlace = FarBoxFloor + new Vector3(0, 0.5f, 0);
        Vector3 behindPlace = -20 * Vector3.Normalize(FarBoxFloor);
        VulkanRenderer renderer = Renderer(
            setting: LocalShadowAtlas.Default with
            {
                Tiles = 1,
            },
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = EnvironmentMapSetting.MinFaceSize,
                FacesPerFrame = CubeFaces.Count,
            }
        );
        try
        {
            var kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Dark(FarBoxCamera);
            snapshot.Camera.VerticalFov = FarBoxLens;
            snapshot.Instances.Add(kit.Floor(center: FarBoxFloor, halfSize: 4));
            snapshot.Instances.Add(kit.Caster(center: FarBoxFloor + new Vector3(0, 0.2f, 0), size: new(0.15f)));
            snapshot.Lights.Add(
                ShadowingSpot(
                    position: spotPlace,
                    direction: -Vector3.UnitY,
                    intensity: (float)(0.6 * cutoff * spotPlace.LengthSquared())
                )
            );
            if (withTheLightBehind)
                snapshot.Lights.Add(
                    ShadowingSpot(
                        position: behindPlace,
                        direction: Vector3.Normalize(behindPlace),
                        intensity: (float)(0.9 * cutoff * behindPlace.LengthSquared()),
                        entity: SecondLampEntity
                    )
                );
            return Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: withTheLightBehind ? "face-only-light" : "no-face-only-light"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The mean luma of the 3 × 3 pixels around the pixel of <see cref="FarBoxUnderASpot"/> that sees <paramref name="point"/>.</summary>
    private static float FarBoxLumaAt(Capture image, Vector3 point) =>
        image.MeanLuma(
            Gpu.Project(point: point, cameraRotation: FarBoxCamera, width: Width, height: Height, fov: FarBoxLens),
            radius: 1
        );

    [Fact]
    public void ALightThatOnlyAFaceKeepsLeavesTheMainViewsImageBitForBit()
    {
        Capture withIt = FarBoxUnderASpot(withTheLightBehind: true);
        Capture withoutIt = FarBoxUnderASpot(withTheLightBehind: false);

        float lit = FarBoxLumaAt(withIt, FarBoxFloor + new Vector3(0.22f, 0, 0));
        lit.Should().BeGreaterThan(0.05f, "the main view's spot light lights the floor beside the box");
        FarBoxLumaAt(withIt, FarBoxFloor + new Vector3(0.03f, 0, 0))
            .Should()
            .BeLessThan(ShadowedShare * lit, "the main view's spot light has the one tile");
        Rgb(withIt).Should().Equal(Rgb(withoutIt), "the light that only a face keeps adds nothing to the main view");
    }

    [Fact]
    public void WithLocalShadowsOffNoNearLightIsShadowedAndNoPassIsTimed()
    {
        VulkanRenderer renderer = Renderer(new NoLocalShadows());
        try
        {
            var kit = Kit.Create(renderer);

            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: SpotOverABox(kit),
                name: "local-shadows-off"
            );

            LumaAt(image, UnderTheBox)
                .Should()
                .BeGreaterThanOrEqualTo(
                    LumaAt(image, BesideTheBox),
                    "without tiles the spot light lights the floor under the box"
                );
            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(0);
            renderer.Graph.Passes.Select(pass => pass.Name).Should().NotContain("local shadows");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EveryAtlasLayerReadsBackAsItsLightsTile()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // A point light with a box along each face's axis, then a spot light with a box along its axis, each box 0.25 m
            // farther than the one before, so each layer holds its own depth. The point light, first in the list, takes
            // layers 0 to 5 in face order, and the spot light layer 6.
            var lamp = new Vector3(0, 0, -10);
            var spot = new Vector3(5, 0, -10);
            RenderSnapshot snapshot = Dark();
            snapshot.Lights.Add(ShadowingLamp(lamp));
            snapshot.Lights.Add(ShadowingSpot(position: spot, direction: Vector3.UnitX, entity: SecondLampEntity));
            Vector3[] axes = [.. Enum.GetValues<CubeFace>().Select(face => CubeFaces.Basis(face).Forward)];
            for (int i = 0; i < axes.Length; i++)
                snapshot.Instances.Add(
                    kit.Caster(center: lamp + axes[i] * BoxDistance(i), size: new(0.4f), entity: BoxEntity + (uint)i)
                );
            snapshot.Instances.Add(
                kit.Caster(
                    center: spot + Vector3.UnitX * BoxDistance(CubeFaces.Count),
                    size: new(0.4f),
                    entity: BoxEntity + CubeFaces.Count
                )
            );

            renderer.Render(snapshot, 1f);

            int size = LocalShadowAtlas.Default.TileSize;
            for (int layer = 0; layer <= CubeFaces.Count; layer++)
            {
                float[] tile = renderer.ReadShadowTile(layer);
                // Reversed-Z with an infinite far plane: depth = near / distance, with the near plane at 5 cm.
                float boxFront = DepthRanges.NearPlane / (BoxDistance(layer) - 0.2f);
                tile[size / 2 * size + size / 2]
                    .Should()
                    .BeApproximately(
                        boxFront,
                        boxFront * 0.01f,
                        "layer {0} sees its box's front {1} m away",
                        layer,
                        BoxDistance(layer) - 0.2f
                    );
                tile[0].Should().Be(0, "the corner of layer {0} sees no caster", layer);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }

        // The distance of the center of the box in layer i from its light, in m.
        static float BoxDistance(int i) => 2f + 0.25f * i;
    }

    [Fact]
    public void AnAtlasOfMoreTilesThanTheDeviceHasLayersIsRefused()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var allocator = new DeviceAllocator(device);
        int tiles = (int)device.Properties.Limits.MaxImageArrayLayers + 1;

        Action make = () =>
            new ShadowAtlas(
                device: device,
                allocator: allocator,
                setting: LocalShadowAtlas.Default with
                {
                    Tiles = tiles,
                }
            ).Dispose();

        make.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maxImageArrayLayers*");
        allocator.LiveAllocations.Should().Be(0, "the refusal comes before the atlas makes its image");
    }

    /// <summary>The meshes and materials of the tests' scenes.</summary>
    private sealed class Kit
    {
        public required MeshId Box { get; init; }
        public required MeshId Plane { get; init; }
        public required MaterialId White { get; init; }

        public static Kit Create(VulkanRenderer renderer) =>
            new()
            {
                Box = renderer.CreateMesh(Primitives.Box(new(0.5f))),
                Plane = renderer.CreateMesh(Primitives.Plane(1f)),
                White = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.9f)
                ),
            };

        /// <summary>A white square of half size <paramref name="halfSize"/> that faces up, at <paramref name="center"/>.</summary>
        public RenderInstance Floor(Vector3 center, float halfSize, RenderFlags flags = RenderFlags.None) =>
            Panel(center: center, normal: Vector3.UnitY, halfSize: halfSize, flags: flags);

        /// <summary>A white square of half size <paramref name="halfSize"/> whose front faces along <paramref name="normal"/>.</summary>
        public RenderInstance Panel(
            Vector3 center,
            Vector3 normal,
            float halfSize,
            RenderFlags flags = RenderFlags.None
        ) =>
            EnvironmentScenes.Facing(
                plane: Plane,
                material: White,
                center: center,
                normal: normal,
                halfSize: halfSize,
                flags: flags,
                chainDepth: TestChains.CamerasFrame
            );

        /// <summary>A white box of <paramref name="size"/> that casts shadows.</summary>
        public RenderInstance Caster(
            Vector3 center,
            Vector3 size,
            uint entity = BoxEntity,
            Quaternion? rotation = null
        ) =>
            Gpu.Instance(
                mesh: Box,
                material: White,
                position: center,
                scale: size,
                rotation: rotation,
                flags: RenderFlags.CastsShadow,
                entity: entity,
                chainDepth: TestChains.CamerasFrame
            );

        /// <summary>
        /// A closed room of six walls 4 m from <paramref name="center"/> that face it, and a pillar of 0.3 m that casts
        /// shadows 1.5 m from it along each axis.
        /// </summary>
        public void AddRoomWithPillars(RenderSnapshot snapshot, Vector3 center)
        {
            foreach (CubeFace face in Enum.GetValues<CubeFace>())
            {
                Vector3 axis = CubeFaces.Basis(face).Forward;
                snapshot.Instances.Add(Panel(center: center + axis * 4, normal: -axis, halfSize: 4));
                snapshot.Instances.Add(
                    Caster(center: center + axis * 1.5f, size: new(0.3f), entity: BoxEntity + (uint)face)
                );
            }
        }
    }
}
