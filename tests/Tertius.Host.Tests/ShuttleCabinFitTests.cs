using System.Numerics;
using Tertius.Assets;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The fitted Lunar Shuttle cabin against the converted Pioneer model (ter-tiei, docs/plans/blue-danube-fidelity.md):
/// opaque hull panels block front-face rays in both directions, every window opening and the starboard doorway pass rays
/// cleanly in both directions without hitting opaque hull geometry, the glazing is clear dielectric glass, and the hull
/// carries spatially varied metallic/roughness and normal maps.
/// </summary>
public sealed class ShuttleCabinFitTests
{
    private readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal);

    private static CookedModel LoadShuttleModel()
    {
        ContentFolders? content = ContentFolders.Find();
        Assert.SkipWhen(
            content is null || !File.Exists(content.CookedModel(BlueDanubeArt.Shuttle)),
            $"The Blue Danube art is not converted: run `{BlueDanubeArt.SetupCommand}`."
        );
        return CookedModel.Load(content.CookedModel(BlueDanubeArt.Shuttle)).Ok
            ?? throw new InvalidOperationException("The converted Lunar Shuttle model does not load.");
    }

    /// <summary>
    /// All opaque triangles of the <c>shuttle</c> piece in shuttle axes (<see cref="Shuttle.ArtScale"/> and
    /// <see cref="Shuttle.ArtPlace"/>), keeping their exact GPU winding (front faces only, matching backface culling).
    /// </summary>
    private static List<Triangle> OpaqueTriangles(CookedModel model, string pieceName = "shuttle")
    {
        CookedPiece piece = model.Pieces.Single(p => p.Name == pieceName);
        var tris = new List<Triangle>();
        foreach (CookedPart part in piece.Parts)
        {
            if (part.Material is int matIndex && model.Materials[matIndex].Alpha.Value is not OpaqueAlpha)
                continue;
            Vertex[] verts = part.Mesh.Vertices;
            uint[] indices = part.Mesh.Indices;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = ToShuttle(part, verts[indices[i]].Position);
                Vector3 b = ToShuttle(part, verts[indices[i + 1]].Position);
                Vector3 c = ToShuttle(part, verts[indices[i + 2]].Position);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float len = cross.Length();
                if (len < 1e-8f)
                    continue;
                tris.Add(new(A: a, B: b, C: c, Normal: cross / len));
            }
        }

        return tris;
    }

    private static Vector3 ToShuttle(CookedPart part, Vector3 local) =>
        part.Offset.TransformPoint(part.Scale * local) * Shuttle.ArtScale + Shuttle.ArtPlace;

    /// <summary>
    /// True when the segment from <paramref name="origin"/> to <paramref name="target"/> hits any front-facing triangle
    /// (matching GPU backface culling: ray travels against the triangle's outward normal).
    /// </summary>
    private static bool HitsFrontFace(IReadOnlyList<Triangle> triangles, Vector3 origin, Vector3 target)
    {
        Vector3 delta = target - origin;
        float maxDist = delta.Length();
        if (maxDist < 1e-6f)
            return false;
        Vector3 dir = delta / maxDist;
        foreach (Triangle tri in triangles)
        {
            // GPU backface culling skips triangles whose front normal faces away from the incoming ray.
            if (Vector3.Dot(dir, tri.Normal) >= -1e-6f)
                continue;
            if (RayIntersectsTriangle(origin: origin, dir: dir, maxDist: maxDist, tri: tri))
                return true;
        }

        return false;
    }

    private static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir, float maxDist, Triangle tri)
    {
        Vector3 e1 = tri.B - tri.A;
        Vector3 e2 = tri.C - tri.A;
        Vector3 pvec = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, pvec);
        if (MathF.Abs(det) < 1e-8f)
            return false;
        float invDet = 1f / det;
        Vector3 tvec = origin - tri.A;
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < -1e-4f || u > 1f + 1e-4f)
            return false;
        Vector3 qvec = Vector3.Cross(tvec, e1);
        float v = Vector3.Dot(dir, qvec) * invDet;
        if (v < -1e-4f || u + v > 1f + 1e-4f)
            return false;
        float t = Vector3.Dot(e2, qvec) * invDet;
        return t > 1e-4f && t < maxDist - 1e-4f;
    }

    /// <summary>
    /// Opaque panels of the shuttle hull and fitted cabin must block sightlines in both directions: rays from inside the
    /// cabin toward solid roof, aft bulkhead, floor, lower side walls and nose below the windshield must hit a front-facing
    /// opaque triangle, and rays from outside toward those same solid panels must also hit one.
    /// </summary>
    [Fact]
    public void OpaqueHullPanelsBlockFrontFaceSightlinesInBothDirections()
    {
        CookedModel model = LoadShuttleModel();
        List<Triangle> opaque = OpaqueTriangles(model);

        (string Name, Vector3 Inside, Vector3 Outside)[] probes =
        [
            ("roof above passenger cabin", new(0, 0.5f, 0), new(0, 8, 0)),
            ("aft engine bulkhead", new(0, 0.2f, 1.0f), new(0, 0.2f, 16.0f)),
            ("cabin floor and belly", new(0, 0.2f, -1.0f), new(0, -8.0f, -1.0f)),
            ("solid starboard lower wall forward of hatch", new(0, -0.3f, -2.0f), new(10.0f, -0.3f, -2.0f)),
            ("solid port lower wall", new(0, -0.3f, 2.0f), new(-10.0f, -0.3f, 2.0f)),
            ("solid nose below front windshield", new(0, -0.8f, -10.5f), new(0, -0.8f, -18.0f)),
            ("solid roof behind cockpit windshield", new(0, 0.8f, -8.0f), new(0, 8.0f, -8.0f)),
        ];

        foreach ((string name, Vector3 inside, Vector3 outside) in probes)
        {
            HitsFrontFace(opaque, inside, outside)
                .Should()
                .BeTrue($"an interior camera looking at {name} must be blocked by an opaque front face");
            HitsFrontFace(opaque, outside, inside)
                .Should()
                .BeTrue($"an exterior camera looking at {name} must be blocked by an opaque front face");
        }
    }

    /// <summary>
    /// Every window pane of <see cref="Shuttle.Glass"/> corresponds to a real opening cut through both the exterior hull
    /// and the fitted cabin liner: a ray through the center of each pane passes cleanly in both directions without hitting
    /// any opaque triangle.
    /// </summary>
    [Fact]
    public void EveryWindowOpeningIsClearInBothDirectionsAndMatchesItsGlassPane()
    {
        CookedModel model = LoadShuttleModel();
        List<Triangle> opaque = OpaqueTriangles(model);

        Shuttle.Glass.Should().HaveCount(21, "5 cockpit panes plus 8 port and 8 starboard cabin windows");
        foreach (HullPane pane in Shuttle.Glass)
        {
            var normal = PaneChecks.Normal(pane).ToVector3();
            Vector3 inside = pane.Center - normal * 0.6f;
            Vector3 outside = pane.Center + normal * 3.0f;
            HitsFrontFace(opaque, inside, outside)
                .Should()
                .BeFalse($"inside-to-outside sightline through {pane.Id} must not hit opaque hull");
            HitsFrontFace(opaque, outside, inside)
                .Should()
                .BeFalse($"outside-to-inside sightline through {pane.Id} must not hit opaque hull");
        }
    }

    /// <summary>
    /// The starboard boarding doorway (<see cref="Shuttle.Hatch"/>) is a real cut opening through the hull and cabin liner,
    /// passing rays cleanly in both directions, and the converted model provides a separate <c>hatch_door</c> piece sized
    /// to seal it in flight.
    /// </summary>
    [Fact]
    public void TheStarboardBoardingDoorwayIsClearInBothDirectionsAndHasASeparateHatchDoorPiece()
    {
        CookedModel model = LoadShuttleModel();
        List<Triangle> opaque = OpaqueTriangles(model);
        BoundingBox hatch = Shuttle.Hatch;

        float[] ys = [hatch.Min.Y + 0.35f, hatch.Center.Y, hatch.Max.Y - 0.35f];
        float[] zs = [hatch.Min.Z + 0.25f, hatch.Center.Z, hatch.Max.Z - 0.25f];
        foreach (float y in ys)
        foreach (float z in zs)
        {
            var inside = new Vector3(hatch.Min.X - 0.6f, y, z);
            var outside = new Vector3(Shuttle.Hull.Max.X + 2.0f, y, z);
            HitsFrontFace(opaque, inside, outside)
                .Should()
                .BeFalse($"inside-to-outside doorway ray at y={y:F2}, z={z:F2} must pass cleanly");
            HitsFrontFace(opaque, outside, inside)
                .Should()
                .BeFalse($"outside-to-inside doorway ray at y={y:F2}, z={z:F2} must pass cleanly");
        }

        model.Pieces.Should().Contain(p => p.Name == "hatch_door", "the hull carries a stowable hatch door piece");
    }

    /// <summary>
    /// The glazing material on the shuttle's panes is clear dielectric glass (zero diffuse opacity, avoiding any blue wash)
    /// while keeping anti-reflective Fresnel highlights.
    /// </summary>
    [Fact]
    public void TheShuttleGlazingUsesClearDielectricGlassWithoutBlueWash()
    {
        using World world = NewJoltWorld();
        var resources = new RecordingResources();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, resources);

        MaterialDesc glass = resources.Materials[world.GetRender(scene["pane-nose"]).Material];
        glass.Alpha.Should().Be(new AlphaMode(new GlassAlpha()));
        glass.BaseColor.W.Should().Be(0f, "clear dielectric glazing must have zero diffuse opacity");
        glass.Reflectance.Should().BeLessThan(0.25f, "anti-reflective coating keeps interior lamp reflections tame");
    }

    /// <summary>
    /// The converted shuttle hull carries spatially varied PBR roughness, metallic and normal maps that distinguish painted
    /// ceramic hull panels from exposed bare metal and panel seams.
    /// </summary>
    [Fact]
    public void TheConvertedShuttleHullCarriesSpatiallyVariedMetallicRoughnessAndNormalMaps()
    {
        CookedModel model = LoadShuttleModel();
        CookedMaterial hull = model
            .Materials.Where(m => m.MetallicRoughnessMap is not null && m.NormalMap is not null)
            .FirstOrDefault()!;

        hull.Should().NotBeNull("the converted shuttle hull material must carry both MR and normal maps");
        TextureData mr = model.Textures[hull.MetallicRoughnessMap!.Value];
        TextureData normal = model.Textures[hull.NormalMap!.Value];

        int dielectricCount = 0;
        int metalCount = 0;
        byte minRough = 255;
        byte maxRough = 0;
        for (int i = 0; i < mr.Rgba.Length; i += 4)
        {
            byte rough = mr.Rgba[i + 1];
            byte metal = mr.Rgba[i + 2];
            if (rough < minRough)
                minRough = rough;
            if (rough > maxRough)
                maxRough = rough;
            if (metal < 25)
                dielectricCount++;
            else if (metal > 175)
                metalCount++;
        }

        dielectricCount.Should().BeGreaterThan(10_000, "painted hull panels are dielectric");
        metalCount.Should().BeGreaterThan(2_000, "gear, nozzles, trims and frames are exposed metal");
        (maxRough - minRough)
            .Should()
            .BeGreaterThan(60, "roughness varies meaningfully across smooth metal, painted hull and matte grime/seals");

        int nonFlatNormals = 0;
        for (int i = 0; i < normal.Rgba.Length; i += 4)
        {
            int dx = Math.Abs(normal.Rgba[i] - 128);
            int dy = Math.Abs(normal.Rgba[i + 1] - 128);
            if (dx > 8 || dy > 8)
                nonFlatNormals++;
        }

        nonFlatNormals.Should().BeGreaterThan(5_000, "panel seams and surface relief tilt the tangent-space normals");
    }

    /// <summary>
    /// From the cockpit seat, the pilot's forward sightline passes cleanly through <c>pane-nose</c> with zero opaque hull
    /// hits, and from outside forward-starboard (<c>cockpit-outside-camera</c>), the sightline to the pilot's eye passes
    /// cleanly through <c>pane-cockpit-starboard-fore</c> with zero opaque hull hits.
    /// </summary>
    [Fact]
    public void ThePilotSightlinePassesThroughTheRealFrontWindshieldAndExteriorSeesThePilotThroughStarboardGlazing()
    {
        CookedModel model = LoadShuttleModel();
        List<Triangle> opaque = OpaqueTriangles(model);
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);

        Double3 eye = Shuttle.SeatStand + DemoSurface.Eye;
        Double3 ahead = eye + new Double3(0, 0, -20);
        HullPane nose = Shuttle.Glass.Single(p => p.Id == "pane-nose");

        PaneChecks.Through(nose, eye, ahead).Should().BeTrue("forward sightline from seat passes through pane-nose");
        HitsFrontFace(opaque, eye.ToVector3(), ahead.ToVector3())
            .Should()
            .BeFalse("no opaque hull geometry blocks the pilot's forward sightline through pane-nose");

        Double3 exterior = world.PoseIn(scene["cockpit-outside-camera"], cabin).Position;
        HullPane stbd = Shuttle.Glass.Single(p => p.Id == "pane-cockpit-starboard-fore");
        PaneChecks
            .Through(stbd, exterior, eye)
            .Should()
            .BeTrue("exterior cockpit camera looks through pane-cockpit-starboard-fore to the pilot's eye");
        HitsFrontFace(opaque, exterior.ToVector3(), eye.ToVector3())
            .Should()
            .BeFalse("no opaque hull geometry blocks the exterior view into the cockpit");
    }

    /// <summary>
    /// The converted shuttle model's exterior hull and all five interior cabin PBR materials (floor tread, wall cladding,
    /// structural frames, console shroud and seat upholstery) carry active tangent-space normal maps and PBR
    /// metallic-roughness maps, while optical glazing remains smooth.
    /// </summary>
    [Fact]
    public void InteriorCabinMaterialsCarryActiveNormalMapsWhileOpticalGlazingStaysSmooth()
    {
        CookedModel model = LoadShuttleModel();
        List<CookedMaterial> pbrWithRelief = [];
        foreach (CookedMaterial mat in model.Materials)
        {
            if (mat.NormalMap is not int nIdx || mat.MetallicRoughnessMap is null)
                continue;
            TextureData normal = model.Textures[nIdx];
            int tilted = 0;
            for (int i = 0; i < normal.Rgba.Length; i += 4)
            {
                if (Math.Abs(normal.Rgba[i] - 128) > 6 || Math.Abs(normal.Rgba[i + 1] - 128) > 6)
                    tilted++;
            }

            if (tilted > 2_000)
                pbrWithRelief.Add(mat);
        }

        pbrWithRelief
            .Select(m => m.NormalMap)
            .Distinct()
            .Should()
            .HaveCountGreaterThanOrEqualTo(
                6,
                "exterior hull plus floor, wall, frame, console and upholstery all carry distinct active normal maps"
            );

        using World world = NewJoltWorld();
        var resources = new RecordingResources();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, resources);
        MaterialDesc glass = resources.Materials[world.GetRender(scene["pane-nose"]).Material];
        glass.NormalMap.IsNone.Should().BeTrue("optical glazing must stay smooth with no normal map");
    }

    /// <summary>
    /// While parked static on the pad, the stowable hatch door stays open (hidden) and the boarding bridge stays deployed
    /// (visible). Once the shuttle launches (<see cref="Ignition"/>, dynamic motion), the bridge retracts and the hatch
    /// door seals flush across the starboard doorway.
    /// </summary>
    [Fact]
    public void TheHatchDoorClosesInFlightAndOpensWhenParkedWhileTheBoardingBridgeDoesTheOpposite()
    {
        ContentFolders? content = ContentFolders.Find();
        Assert.SkipWhen(
            content is null || BlueDanubeArt.All.Any(r => !File.Exists(content.CookedModel(r))),
            $"The Blue Danube art is not converted: run `{BlueDanubeArt.SetupCommand}`."
        );
        using World world = NewJoltWorld();
        var resources = new RecordingResources();
        SceneInstance scene = DemoScenes.For(content).Single(s => s.Name == "demo-surface").Build(world, resources);

        world.Tick();
        (world.GetRender(scene["porch"]).Flags & RenderFlags.Visible)
            .Should()
            .NotBe(RenderFlags.None, "boarding bridge is visible while parked");
        (world.GetRender(scene["shuttle-hatch-door"]).Flags & RenderFlags.Visible)
            .Should()
            .Be(RenderFlags.None, "hatch door is open while parked");

        world.Post(Event.Create(target: scene["shuttle"], sender: Entity.Null, payload: new Ignition()));
        for (int i = 0; i < 10; i++)
            world.Tick();

        (world.GetRender(scene["porch"]).Flags & RenderFlags.Visible)
            .Should()
            .Be(RenderFlags.None, "boarding bridge retracts in flight");
        (world.GetRender(scene["shuttle-hatch-door"]).Flags & RenderFlags.Visible)
            .Should()
            .NotBe(RenderFlags.None, "hatch door seals closed in flight");
    }

    /// <summary>
    /// In <c>demo-surface</c> on the real Jolt physics backend, the passenger walks smoothly from the terminal across the
    /// pad, up the boarding stair, across the boarding bridge through the real starboard doorway, up the 10-degree cabin
    /// ramp, and reaches <see cref="Shuttle.SeatStand"/> in the cockpit without getting stuck.
    /// </summary>
    [Fact]
    public void APersonBoardsFromTheTerminalThroughTheStarboardDoorwayUpTheRampToTheCockpitSeat()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        Entity person = scene["person"];
        Passenger passenger = world.GetComponent<Passenger>(person)!;

        int tick = 0;
        for (; tick < 60 * 60 && passenger.State.Value is Boarding; tick++)
            world.Tick();

        passenger.State.Value.Should().NotBeOfType<Boarding>("the passenger finishes boarding within 60 s");
        FrameHandle cabin = world.GetOwnedFrame(scene["shuttle"]);
        world.GetFrame(person).Should().Be(cabin);
        Double3
            .Distance(world.PoseIn(person, cabin).Position, Shuttle.SeatStand)
            .Should()
            .BeLessThan(0.35, "the passenger stands at the cockpit seat after boarding");
    }
}
