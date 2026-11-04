using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The cutout draws of every opaque pass on the GPU (docs/plans/skin-and-hair.md, "The cutout test"): a texel of a
/// cutout material below its cutoff is not there in the G-buffer, the shadow maps, the environment faces and the
/// impostor captures, and a texel at or above it is. The card is the plane mesh, 2 m square, whose image has three
/// stripes across u of alpha 0, 0.4 and 1, with a cutoff of 0.5. The exposure is fixed at 0 stops.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class CutoutRenderTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The cutoff of the cutout cards: between the alphas of the middle and the last stripe.</summary>
    private const float Cutoff = 0.5f;

    /// <summary>The offset along the card's u axis, in m from its center, of the middle of the stripe of alpha 0.</summary>
    private const float ClearStripe = -2f / 3;

    /// <summary>The offset of the middle of the stripe of alpha 0.4, the card's center.</summary>
    private const float FaintStripe = 0;

    /// <summary>The offset of the middle of the stripe of alpha 1.</summary>
    private const float SolidStripe = 2f / 3;

    /// <summary>A card that faces the camera: the plane mesh's front, +Y, turned to +Z, with u along +X.</summary>
    private static readonly Quaternion FacingTheCamera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>
    /// The image of the cards: 12 × 1 white texels whose alphas are 0, 102 and 255 in stripes of 4, so that the middle
    /// half of each stripe filters to its alpha alone: 0, 0.4 and 1.
    /// </summary>
    private static TextureData Stripes(bool opaque = false)
    {
        byte[] rgba = new byte[12 * 4];
        for (int texel = 0; texel < 12; texel++)
        {
            rgba[texel * 4] = rgba[texel * 4 + 1] = rgba[texel * 4 + 2] = 255;
            byte stripe = (texel / 4) switch
            {
                0 => 0,
                1 => 102,
                _ => 255,
            };
            rgba[texel * 4 + 3] = opaque ? (byte)255 : stripe;
        }

        return new(Width: 12, Height: 1, Rgba: rgba);
    }

    /// <summary>A material of the striped image that emits <paramref name="radiance"/> and reflects nothing, drawn as <paramref name="alpha"/>.</summary>
    private static MaterialId StripedGlow(
        VulkanRenderer renderer,
        Vector3 radiance,
        AlphaMode alpha,
        bool opaque = false
    ) =>
        renderer.CreateMaterial(
            new MaterialDesc(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 0.5f, Reflectance: 0) with
            {
                Emissive = radiance,
                BaseColorMap = renderer.CreateTexture(Stripes(opaque)),
                Alpha = alpha,
            }
        );

    /// <summary>A white matte material of the striped image, drawn as <paramref name="alpha"/>.</summary>
    private static MaterialId StripedMatte(VulkanRenderer renderer, AlphaMode alpha, bool opaque = false) =>
        renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.6f) with
            {
                BaseColorMap = renderer.CreateTexture(Stripes(opaque)),
                Alpha = alpha,
            }
        );

    /// <summary>A snapshot with no ambient light and the sun along <paramref name="starDirection"/>.</summary>
    private static RenderSnapshot Unlit(Vector3 starDirection, float starIrradiance)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(starDirection, starIrradiance: starIrradiance);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>A green square that emits light, 6 m ahead and 6 m wide: what lies behind the cards.</summary>
    private static RenderInstance Backdrop(VulkanRenderer renderer, MeshId plane) =>
        Gpu.Instance(
            mesh: plane,
            material: Glow(renderer, new(0, 0.5f, 0)),
            position: new(0, 0, -6),
            scale: new(3),
            rotation: FacingTheCamera,
            radius: 4.3f,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>The bounding radius of the cards, in m: a little more than the half diagonal, √2.</summary>
    private const float CardRadius = 1.5f;

    /// <summary>A card of <paramref name="material"/> that faces the camera, centered at <paramref name="center"/>.</summary>
    private static RenderInstance Card(
        MeshId plane,
        MaterialId material,
        Vector3 center,
        RenderFlags flags = default
    ) =>
        Gpu.Instance(
            mesh: plane,
            material: material,
            position: center,
            scale: Vector3.One,
            rotation: FacingTheCamera,
            flags: flags,
            radius: CardRadius,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>The RGB of the pixel that holds <paramref name="pixel"/> in an HDR image of RGBA per pixel.</summary>
    private static Vector3 At(float[] hdr, Vector2 pixel)
    {
        int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
        return new(hdr[i], hdr[i + 1], hdr[i + 2]);
    }

    /// <summary>The pixel of a camera-relative point.</summary>
    private static Vector2 PixelOf(Vector3 point) => Gpu.Project(point: point, width: Width, height: Height);

    /// <summary>The luma of the pixel that holds <paramref name="pixel"/> in an HDR image.</summary>
    private static float Luma(float[] hdr, Vector2 pixel) =>
        Vector3.Dot(At(hdr, pixel), new(0.2126f, 0.7152f, 0.0722f));

    [Fact]
    public void ATexelBelowTheCutoffShowsWhatLiesBehind()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            MaterialId card = StripedGlow(renderer: renderer, radiance: new(1, 0, 0), alpha: new CutoutAlpha(Cutoff));
            RenderSnapshot behind = Unlit(Vector3.UnitY, starIrradiance: 0);
            behind.Instances.Add(Backdrop(renderer, plane));
            renderer.Render(behind, 1f);
            float[] without = renderer.ReadHdr();
            RenderSnapshot withCard = Unlit(Vector3.UnitY, starIrradiance: 0);
            withCard.Instances.Add(Backdrop(renderer, plane));
            withCard.Instances.Add(Card(plane: plane, material: card, center: new(0, 0, -3)));
            renderer.Render(withCard, 1f);
            float[] with = renderer.ReadHdr();

            foreach (float stripe in (float[])[ClearStripe, FaintStripe])
            {
                Vector2 pixel = PixelOf(new(stripe, 0, -3));
                At(with, pixel)
                    .Should()
                    .Be(
                        At(without, pixel),
                        "the texels {0} m from the card's center, below the cutoff, are not there",
                        stripe
                    );
            }

            Vector2 solid = PixelOf(new(SolidStripe, 0, -3));
            At(with, solid).X.Should().BeGreaterThan(0.5f, "the texels of alpha 1 show the card's red light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ATexelAtTheCutoffOrAboveWritesTheGBuffer()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            Vector2 center = PixelOf(new(0, 0, -3));

            float DistanceAt(float alpha)
            {
                // No map: every texel's alpha is the base color's, which the material buffer holds exactly.
                MaterialId card = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: alpha), roughness: 0.6f) with
                    {
                        Alpha = new CutoutAlpha(Cutoff),
                    }
                );
                RenderSnapshot snapshot = Unlit(Vector3.UnitY, starIrradiance: 1);
                snapshot.Instances.Add(Backdrop(renderer, plane));
                snapshot.Instances.Add(Card(plane: plane, material: card, center: new(0, 0, -3)));
                renderer.Render(snapshot, 1f);
                return renderer.ReadDistance()[(int)center.Y * Width + (int)center.X];
            }

            DistanceAt(Cutoff).Should().BeApproximately(3, 0.05f, "a texel at the cutoff is covered");
            DistanceAt(1).Should().BeApproximately(3, 0.05f, "a texel above the cutoff is covered");
            DistanceAt(Cutoff - 0.01f).Should().BeApproximately(6, 0.1f, "the backdrop shows through a texel below it");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    // The shadows. The floor lies 1 m below the eye. A horizontal card 4.5 m above it, out of the view, casts its stripes
    // onto the floor 5 m ahead; a box behind it casts too, so that every view has a solid caster and a cutout caster.

    private static readonly Vector3 CardAbove = new(0, 3.5f, -5);
    private static readonly Vector3 BoxAbove = new(0, 3.5f, -7.5f);

    /// <summary>The floor, the card above it, and the box beside it, in <paramref name="snapshot"/>.</summary>
    private static void AddFloorAndCasters(VulkanRenderer renderer, RenderSnapshot snapshot, bool withCard)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.9f)
                ),
                position: new(0, -1, -5),
                scale: new(10, 1, 10),
                radius: 15,
                chainDepth: TestChains.CamerasFrame
            )
        );
        if (!withCard)
            return;
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: StripedMatte(renderer, new CutoutAlpha(Cutoff)),
                position: CardAbove,
                scale: Vector3.One,
                flags: RenderFlags.CastsShadow,
                entity: 1,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.9f)),
                position: BoxAbove,
                scale: Vector3.One,
                flags: RenderFlags.CastsShadow,
                entity: 2,
                chainDepth: TestChains.CamerasFrame
            )
        );
    }

    /// <summary>The floor's luma under the middle of each stripe, at <paramref name="spread"/> times its offset.</summary>
    private static (float Clear, float Faint, float Solid) FloorUnderStripes(float[] hdr, float spread) =>
        (
            Luma(hdr, PixelOf(new(ClearStripe * spread, -1, -5))),
            Luma(hdr, PixelOf(new(FaintStripe * spread, -1, -5))),
            Luma(hdr, PixelOf(new(SolidStripe * spread, -1, -5)))
        );

    /// <summary>Asserts that only the stripe of alpha 1 shadows the floor, against the same points of the floor without the casters.</summary>
    private static void OnlyTheCoreShadows(float[] withCard, float[] withoutCard, float spread)
    {
        (float clear, float faint, float solid) = FloorUnderStripes(withCard, spread);
        (float clearLit, float faintLit, float solidLit) = FloorUnderStripes(withoutCard, spread);
        MathF.Min(clearLit, MathF.Min(faintLit, solidLit)).Should().BeGreaterThan(0.05f, "the light reaches the floor");
        clear.Should().BeApproximately(clearLit, 0.02f * clearLit, "a texel of alpha 0 casts no shadow");
        faint
            .Should()
            .BeApproximately(faintLit, 0.02f * faintLit, "a texel of alpha 0.4, below the cutoff, casts no shadow");
        solid.Should().BeLessThan(0.15f * solidLit, "a texel of alpha 1 casts its shadow");
    }

    /// <summary>The places of the GPU instances that a run draws.</summary>
    private static List<Vector3> Placed(PreparedScene scene, CommandRange commands)
    {
        var places = new List<Vector3>();
        for (int c = commands.First; c < commands.First + commands.Count; c++)
        {
            DrawIndexedIndirectCommand command = scene.Commands[c];
            for (uint i = command.FirstInstance; i < command.FirstInstance + command.InstanceCount; i++)
                places.Add(
                    new(scene.Instances[i].ModelRow0.W, scene.Instances[i].ModelRow1.W, scene.Instances[i].ModelRow2.W)
                );
        }

        return places;
    }

    /// <summary>Asserts that each view with casters draws the box in its solid run and the card in its cutout run.</summary>
    private static void TheCardDrawsInTheCutoutRun(PreparedScene scene, IReadOnlyList<OpaqueRuns> views)
    {
        views.Should().NotBeEmpty();
        foreach (OpaqueRuns runs in views)
        {
            Placed(scene, runs.Solid).Should().Equal([BoxAbove], "the box is solid");
            Placed(scene, runs.Cutout).Should().Equal([CardAbove], "the card is a cutout");
        }
    }

    [Fact]
    public void ACutoutCasterShadowsOnlyWhereItsTexelsReachTheCutoffInACascade()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            float[] Floor(bool withCard)
            {
                RenderSnapshot snapshot = Unlit(Vector3.UnitY, starIrradiance: 3);
                AddFloorAndCasters(renderer, snapshot, withCard);
                renderer.Render(snapshot, 1f);
                return renderer.ReadHdr();
            }

            // The sun stands straight above, so each stripe's shadow lies right under it.
            OnlyTheCoreShadows(withCard: Floor(withCard: true), withoutCard: Floor(withCard: false), spread: 1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACutoutCasterShadowsOnlyWhereItsTexelsReachTheCutoffInAnAtlasTile()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, localShadows: LocalShadowAtlas.Default);
        try
        {
            float[] Floor(bool withCard)
            {
                RenderSnapshot snapshot = Unlit(Vector3.UnitY, starIrradiance: 0);
                snapshot.Lights.Add(SpotAbove());
                AddFloorAndCasters(renderer, snapshot, withCard);
                renderer.Render(snapshot, 1f);
                return renderer.ReadHdr();
            }

            // The spot light stands 4.5 m above the card, which is 4.5 m above the floor: the shadows spread twice as wide.
            OnlyTheCoreShadows(withCard: Floor(withCard: true), withoutCard: Floor(withCard: false), spread: 2);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A white spot light 9 m above the floor, over the card, pointing down, that casts shadows.</summary>
    private static SpotRenderLight SpotAbove() =>
        Gpu.Spot(
            position: new(0, 8, -5),
            direction: -Vector3.UnitY,
            color: Vector3.One,
            intensity: 300,
            innerConeAngle: 0.48f,
            outerConeAngle: 0.6f,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            CastsShadow = true,
            EntityIndex = 100,
        };

    [Fact]
    public void ACutoutCasterDrawsInTheCutoutRunOfEachCascade()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Unlit(Vector3.UnitY, starIrradiance: 3);
            AddFloorAndCasters(renderer, snapshot, withCard: true);
            renderer.Render(snapshot, 1f);

            PreparedScene scene = renderer.LastScene;
            TheCardDrawsInTheCutoutRun(
                scene,
                [.. scene.ShadowCommands.Take(ShadowCascades.Count).Where(runs => runs.Count > 0)]
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACutoutCasterDrawsInTheCutoutRunOfEachAtlasTile()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, localShadows: LocalShadowAtlas.Default);
        try
        {
            RenderSnapshot snapshot = Unlit(Vector3.UnitY, starIrradiance: 0);
            snapshot.Lights.Add(SpotAbove());
            AddFloorAndCasters(renderer, snapshot, withCard: true);
            renderer.Render(snapshot, 1f);

            PreparedScene scene = renderer.LastScene;
            TheCardDrawsInTheCutoutRun(
                scene,
                [.. scene.DrawnTiles.Select(tile => tile.Commands).Where(runs => runs.Count > 0)]
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnEnvironmentFaceSeesOnlyTheCore()
    {
        static Vector3 FaceCenter(float alpha)
        {
            VulkanRenderer renderer = EnvironmentScenes.Renderer();
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
                RenderSnapshot snapshot = EnvironmentScenes.Dark();
                snapshot.Instances.Add(
                    EnvironmentScenes.Square(
                        plane: plane,
                        material: EnvironmentScenes.Emissive(renderer, new(0, 0, 1)),
                        center: new(10, 0, 0),
                        halfSize: 5,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                MaterialId card = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0, y: 0, z: 0, w: alpha), roughness: 1f) with
                    {
                        Emissive = new(1, 0, 0),
                        Reflectance = 0f,
                        Alpha = new FringedAlpha(Cutoff),
                    }
                );
                snapshot.Instances.Add(
                    EnvironmentScenes.Facing(
                        plane: plane,
                        material: card,
                        center: new(5, 0, 0),
                        normal: -Vector3.UnitX,
                        halfSize: 3,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                renderer.Render(snapshot, 1f);
                return EnvironmentScenes.Center(EnvironmentScenes.Face(renderer, CubeFace.PositiveX));
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        EnvironmentScenes.ShouldBeNear(
            FaceCenter(0.4f),
            new(0, 0, 1),
            "a fringe texel is not in the face, which has no transparent pass"
        );
        EnvironmentScenes.ShouldBeNear(FaceCenter(0.6f), new(1, 0, 0), "a core texel is");
    }

    [Fact]
    public void AnImpostorCaptureKeepsTheHolesOfACutout()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, impostors: true);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            MaterialId card = StripedGlow(
                renderer: renderer,
                radiance: new(1, 0.5f, 0),
                alpha: new CutoutAlpha(Cutoff)
            );
            RenderSnapshot snapshot = Unlit(Vector3.UnitY, starIrradiance: 0);
            snapshot.Instances.Add(Card(plane: plane, material: card, center: new(0, 0, -6)));

            renderer.Render(snapshot, 1f);

            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1, "a 2 m card 6 m away covers fewer than 200 pixels");
            int tile = renderer
                .Impostors!.Status(
                    new(
                        Entity: new(Index: 0, Generation: 0),
                        Part: 0,
                        Mesh: plane.Value,
                        Material: MaterialStore.Slot(card)
                    )
                )!
                .Value.Tile;
            (float[] rgba, _) = renderer.ReadImpostorTile(tile);
            const int size = ImpostorAtlas.TileSize;
            float Coverage(int x) => rgba[(size / 2 * size + x) * 4 + 3];
            // The tile's half width spans r / √(d² − r²) · d times the margin, in m at the card.
            float halfWidth = CardRadius / MathF.Sqrt(36 - CardRadius * CardRadius) * 6 * ImpostorManager.MarginScale;
            int Texel(float offset) => (int)(size / 2 + offset / halfWidth * size / 2);

            Coverage(Texel(FaintStripe)).Should().Be(0, "the texels of alpha 0.4 are holes");
            // The tile's x axis may run either way along the card's u, so the outer stripes are told apart by their sum.
            (Coverage(Texel(ClearStripe)) + Coverage(Texel(SolidStripe)))
                .Should()
                .Be(1, "one outer stripe is a hole and the other is covered");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// An opaque material's card of stripes draws as one whose alpha is 1 everywhere: no shading reads the alpha. This
    /// does not guard the routing into the solid run, because an opaque material's cutoff is 0 and a cutout pipeline
    /// would discard none of its texels. <see cref="BatcherTests.AnOpaqueMaterialWithTheGlassBitOfOldIsOpaque"/> pins
    /// the routing.
    /// </summary>
    [Fact]
    public void AnOpaqueMaterialIgnoresItsAlpha()
    {
        static float[] Render(bool opaqueImage)
        {
            VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
            try
            {
                MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
                RenderSnapshot snapshot = Unlit(Vector3.Normalize(new(0.3f, 0.6f, 0.75f)), starIrradiance: 2);
                snapshot.Instances.Add(Backdrop(renderer, plane));
                snapshot.Instances.Add(
                    Card(
                        plane: plane,
                        material: StripedMatte(renderer, new OpaqueAlpha(), opaqueImage),
                        center: new(0, 0, -3),
                        flags: RenderFlags.CastsShadow
                    )
                );
                renderer.Render(snapshot, 1f);
                return renderer.ReadHdr();
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Render(opaqueImage: false).Should().Equal(Render(opaqueImage: true), "the solid pipelines read no alpha");
    }
}
