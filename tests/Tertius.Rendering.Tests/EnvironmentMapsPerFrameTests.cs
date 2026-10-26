using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Scenes for the GPU tests of maps per frame (docs/plans/environment-maps-per-frame.md, "Tests", Part 3). The room is the
/// camera's frame, id 1, and the outside is its parent and the root, id 0, both in the camera's axes. The room is a box
/// 6 m wide around the eye, open toward −Z: its walls and ceiling glow while its lamps are on, and its floor is matte.
/// Outside lie a matte ground 3.5 m below the eye and a blue glowing square 40 m above it, the sky. The sun is off, so the
/// faces and the maps hold only what glows and what the lamps light.
/// </summary>
internal sealed class RoomScenes(VulkanRenderer renderer)
{
    /// <summary>The chain depth of an object in the room, the camera's frame: map 1 of the two maps.</summary>
    public const ushort InRoom = 0;

    /// <summary>The chain depth of an object outside, in the root: map 0 of the two maps.</summary>
    public const ushort Outside = 1;

    /// <summary>The radiance of the lamps: each wall and the ceiling of the lit room.</summary>
    public static readonly Vector3 LampRadiance = new(2f);

    /// <summary>The radiance of the sky square outside.</summary>
    public static readonly Vector3 SkyRadiance = new(0.3f, 0.5f, 1f);

    /// <summary>The camera looks 30° down from −Z, so it sees the room's floor and, through the opening, the ground.</summary>
    public static readonly Quaternion Look = Gpu.PitchDown(MathF.PI / 6);

    /// <summary>A point of the ground outside that the camera sees through the opening.</summary>
    public static readonly Vector3 Ground = new(0, -3.5f, -12);

    /// <summary>A point of the room's floor that the camera sees.</summary>
    public static readonly Vector3 Floor = new(0, -3, -2.5f);

    public MeshId Plane { get; } = renderer.CreateMesh(Primitives.Plane(1f));
    public MaterialId Lamps { get; } = EnvironmentScenes.Emissive(renderer, LampRadiance);
    public MaterialId Off { get; } = EnvironmentScenes.Emissive(renderer, Vector3.Zero);
    public MaterialId Matte { get; } = EnvironmentScenes.Matte(renderer);
    public MaterialId Sky { get; } = EnvironmentScenes.Emissive(renderer, SkyRadiance);

    /// <summary>
    /// The room scene, with the lamps on or off. With <paramref name="oneFrame"/>, the room and the outside are one frame,
    /// the root, which has one map: the scene as one map lights it.
    /// </summary>
    public RenderSnapshot Scene(bool lampsOn, bool oneFrame = false)
    {
        RenderSnapshot snapshot = EnvironmentScenes.Dark();
        snapshot.Camera.Rotation = Look;
        snapshot.Camera.PreviousRotation = Look;
        if (oneFrame)
            TestChains.Of(snapshot, (0, Quaternion.Identity));
        else
            TestChains.Of(snapshot, (1, Quaternion.Identity), (0, Quaternion.Identity));
        ushort room = InRoom;
        ushort outside = oneFrame ? InRoom : Outside;
        MaterialId walls = lampsOn ? Lamps : Off;
        Vector3[] sides = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        foreach (Vector3 side in sides)
            snapshot.Instances.Add(
                In(room, EnvironmentScenes.Square(plane: Plane, material: walls, center: side * 3, halfSize: 3.3f))
            );
        snapshot.Instances.Add(
            In(room, EnvironmentScenes.Square(plane: Plane, material: Matte, center: new(0, -3, 0), halfSize: 3.3f))
        );
        snapshot.Instances.Add(
            In(
                outside,
                EnvironmentScenes.Facing(
                    plane: Plane,
                    material: Matte,
                    center: new(0, -3.5f, -20),
                    normal: Vector3.UnitY,
                    halfSize: 40
                )
            )
        );
        snapshot.Instances.Add(
            In(
                outside,
                EnvironmentScenes.Facing(
                    plane: Plane,
                    material: Sky,
                    center: new(0, 40, -20),
                    normal: -Vector3.UnitY,
                    halfSize: 40
                )
            )
        );
        return snapshot;
    }

    /// <summary><paramref name="instance"/> with its chain frame <paramref name="chainDepth"/> above the camera's.</summary>
    public static RenderInstance In(ushort chainDepth, RenderInstance instance) =>
        instance with
        {
            ChainDepth = chainDepth,
        };

    /// <summary>The pixel of a camera-relative point for the room's camera.</summary>
    public static Vector2 Pixel(Vector3 point, int width, int height) =>
        Gpu.Project(point: point, cameraRotation: Look, width: width, height: height);

    /// <summary>The RGB at the center of level 0 of <paramref name="face"/> of map <paramref name="map"/>.</summary>
    public static Vector3 Center(VulkanRenderer renderer, int map, CubeFace face) =>
        EnvironmentScenes.Center(
            renderer.ReadEnvironmentFace(map: map, face: face, level: 0, image: EnvironmentImage.Radiance)
        );
}

/// <summary>
/// Environment maps per frame on the GPU (docs/plans/environment-maps-per-frame.md): each frame of the camera's chain has a
/// map of its own, an object takes the map of its chain frame, a map leaves out the subtree below it, its lamps included,
/// and lies in its frame's axes, and a change of the camera's frame redraws only the maps that changed.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class EnvironmentMapsPerFrameTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The largest difference of one channel between two images that must agree: rounding to 8 bits and a step.</summary>
    private const float Tolerance = 2f / 255;

    /// <summary>The time of a draw one step of 60 Hz after the first: not a long step, so its maps draw their faces in turn.</summary>
    private const double Step = 1.0 / 60;

    private static VulkanRenderer Renderer(bool impostors = false, ReflectionSetting reflections = default) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            impostors: impostors,
            reflections: reflections,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = EnvironmentScenes.FaceSize,
            }
        );

    /// <summary>Renders <paramref name="snapshot"/> twice and captures the second draw.</summary>
    private static Capture Shown(VulkanRenderer renderer, RenderSnapshot snapshot, string name)
    {
        renderer.Render(snapshot, 1f);
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>The color of the room scene's <paramref name="point"/>, in a new renderer.</summary>
    private static Vector3 RoomColor(Vector3 point, bool lampsOn, bool oneFrame, string name)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var room = new RoomScenes(renderer);
            Capture shown = Shown(renderer, room.Scene(lampsOn: lampsOn, oneFrame: oneFrame), name);
            return shown.Color(RoomScenes.Pixel(point, Width, Height));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitRoomDoesNotLightTheGroundSeenThroughItsOpening()
    {
        Vector3 lit = RoomColor(point: RoomScenes.Ground, lampsOn: true, oneFrame: false, name: "maps-ground-lit-room");
        Vector3 dark = RoomColor(
            point: RoomScenes.Ground,
            lampsOn: false,
            oneFrame: false,
            name: "maps-ground-dark-room"
        );
        Vector3 oneMap = RoomColor(
            point: RoomScenes.Ground,
            lampsOn: true,
            oneFrame: true,
            name: "maps-ground-one-map"
        );

        dark.Should().NotBe(Vector3.Zero, "the sky outside lights the ground");
        BrdfTesting
            .LargestChannelDifference(lit, dark)
            .Should()
            .BeLessThanOrEqualTo(
                Tolerance,
                "the ground takes the outside's map, which leaves out the room: {0} and {1}",
                lit,
                dark
            );
        (oneMap.Length() - lit.Length())
            .Should()
            .BeGreaterThan(0.1f, "one map would light the ground with the lit room: {0} against {1}", oneMap, lit);
    }

    [Fact]
    public void TheRoomsFloorTakesTheLightOfTheRoom()
    {
        Vector3 lit = RoomColor(point: RoomScenes.Floor, lampsOn: true, oneFrame: false, name: "maps-floor-lit-room");
        Vector3 dark = RoomColor(
            point: RoomScenes.Floor,
            lampsOn: false,
            oneFrame: false,
            name: "maps-floor-dark-room"
        );

        (lit.Length() - dark.Length())
            .Should()
            .BeGreaterThan(0.2f, "the floor takes the room's map, which holds the lamps: {0} against {1}", lit, dark);
    }

    [Fact]
    public void TheMapOfAnAncestorHoldsWhatTheEyeSeesWithoutTheSubtreeBelowIt()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var room = new RoomScenes(renderer);

            renderer.Render(room.Scene(lampsOn: true), 1f);

            EnvironmentScenes.ShouldBeNear(
                RoomScenes.Center(renderer, map: 0, CubeFace.PositiveY),
                RoomScenes.SkyRadiance,
                "the outside's map sees the sky where the room's ceiling would hide it"
            );
            EnvironmentScenes.ShouldBeNear(
                RoomScenes.Center(renderer, map: 1, CubeFace.PositiveY),
                RoomScenes.LampRadiance,
                "the room's map sees its ceiling"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheMapOfAnAncestorLeavesOutTheLampsOfTheSubtree()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var room = new RoomScenes(renderer);
            RenderSnapshot snapshot = room.Scene(lampsOn: false);
            snapshot.Lights.Add(
                Gpu.Lamp(position: new(0, -2, 0), color: Vector3.One, intensity: 20, chainDepth: RoomScenes.InRoom)
            );

            renderer.Render(snapshot, 1f);

            Vector3 outside = RoomScenes.Center(renderer, map: 0, CubeFace.NegativeY);
            Vector3 inside = RoomScenes.Center(renderer, map: 1, CubeFace.NegativeY);
            outside.Should().Be(Vector3.Zero, "the outside's map sees the ground under the lamp, which it leaves out");
            inside.Length().Should().BeGreaterThan(0.1f, "the room's map sees its floor under its lamp: {0}", inside);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheMapOfTheCamerasFrameHoldsEverything()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var room = new RoomScenes(renderer);
            RenderSnapshot snapshot = room.Scene(lampsOn: true);
            var marker = new Vector3(1, 0.5f, 0);
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.Outside,
                    EnvironmentScenes.Square(
                        plane: room.Plane,
                        material: EnvironmentScenes.Emissive(renderer, marker),
                        center: new(0, 0, -30),
                        halfSize: 5
                    )
                )
            );

            renderer.Render(snapshot, 1f);

            EnvironmentScenes.ShouldBeNear(
                RoomScenes.Center(renderer, map: 1, CubeFace.NegativeZ),
                marker,
                "the room's map sees the outside through the opening"
            );
            EnvironmentScenes.ShouldBeNear(
                RoomScenes.Center(renderer, map: 1, CubeFace.PositiveX),
                RoomScenes.LampRadiance,
                "the room's map sees its own wall"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheMapOfAnAncestorLiesInItsFramesAxes()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            // The root's axes are a quarter turn about +Y from the camera's, so its +X lies along the camera's −Z.
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            TestChains.Of(snapshot, (1, Quaternion.Identity), (0, quarter));
            var marker = new Vector3(0.2f, 1, 0.4f);
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.Outside,
                    EnvironmentScenes.Square(
                        plane: renderer.CreateMesh(Primitives.Plane(1f)),
                        material: EnvironmentScenes.Emissive(renderer, marker),
                        center: new(0, 0, -10),
                        halfSize: 2
                    )
                )
            );

            renderer.Render(snapshot, 1f);

            EnvironmentScenes.ShouldBeNear(
                RoomScenes.Center(renderer, map: 0, CubeFace.PositiveX),
                marker,
                "the root's map holds the square on its own +X"
            );
            EnvironmentScenes.ShouldBeNear(
                RoomScenes.Center(renderer, map: 1, CubeFace.NegativeZ),
                marker,
                "the camera's frame's map holds it on the camera's −Z"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMirrorReadsItsMapInItsFramesAxes()
    {
        // The root's axes are a quarter turn about +Y from the camera's. A mirror in the root faces the camera and reflects
        // the camera's +Z, behind the eye, where a green square of the root glows: the root's +X, by the map's own axes.
        VulkanRenderer renderer = Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            TestChains.Of(
                snapshot,
                (1, Quaternion.Identity),
                (0, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2))
            );
            MaterialId glow = EnvironmentScenes.Emissive(renderer, new Vector3(0, 0.5f, 0));
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.Outside,
                    EnvironmentScenes.Square(plane: plane, material: glow, center: new(0, 0, 20), halfSize: 20)
                )
            );
            MaterialId mirror = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0, Metallic: 1));
            var center = new Vector3(0, 0, -6);
            snapshot.Instances.Add(
                FacingTheCamera(plane: plane, material: mirror, center: center, chainDepth: RoomScenes.Outside)
            );

            Vector3 seen = Shown(renderer, snapshot, "maps-mirror-axes")
                .Color(Gpu.Project(point: center, width: Width, height: Height));

            seen.Y.Should().BeGreaterThan(0.2f, "the mirror shows the green square, which it finds on its map's +X");
            seen.Y.Should().BeGreaterThan(4 * Math.Max(seen.X, seen.Z), "the mirror shows the square's green alone");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A snapshot whose chain is the frames <paramref name="chain"/>, camera first and the root, id 0, last, inside a dome of
    /// <paramref name="dome"/> in the root.
    /// </summary>
    private static RenderSnapshot DomeScene(MeshId plane, MaterialId dome, params long[] chain)
    {
        RenderSnapshot snapshot = EnvironmentScenes.Dark();
        snapshot.Chain.Clear();
        foreach (long id in chain)
            snapshot.Chain.Add(new(FrameId: id, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        int start = snapshot.Instances.Count;
        EnvironmentScenes.AddEnclosure(snapshot: snapshot, plane: plane, material: dome, distance: 20);
        for (int i = start; i < snapshot.Instances.Count; i++)
            snapshot.Instances[i] = RoomScenes.In((ushort)(chain.Length - 1), snapshot.Instances[i]);
        return snapshot;
    }

    /// <summary>The level-0 center of every face of map <paramref name="map"/>, in layer order.</summary>
    private static Vector3[] Centers(VulkanRenderer renderer, int map) =>
        [.. Enumerable.Range(0, CubeFaces.Count).Select(layer => RoomScenes.Center(renderer, map, (CubeFace)layer))];

    /// <summary>
    /// Draws frames 0, 1 and 2 of a new renderer inside a dome that glows A, B and C in turn: the camera in frame 2 under
    /// frame 1 under the root, then in frame 1, then in frame 2 again. Returns the colors.
    /// </summary>
    private static Vector3[] LeaveAndEnterAChild(VulkanRenderer renderer)
    {
        Vector3[] colors = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId[] domes = [.. colors.Select(color => EnvironmentScenes.Emissive(renderer, color))];
        EnvironmentScenes.Still(renderer, DomeScene(plane, domes[0], 2, 1, 0), time: 0);
        EnvironmentScenes.Still(renderer, DomeScene(plane, domes[1], 1, 0), time: Step);
        EnvironmentScenes.Still(renderer, DomeScene(plane, domes[2], 2, 1, 0), time: 2 * Step);
        return colors;
    }

    [Fact]
    public void WhenTheCameraEntersAChildTheMapsOutsideKeepTheirFaces()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            Vector3[] colors = LeaveAndEnterAChild(renderer);

            // At one face per frame, frame n draws slot n mod 6M: frame 1 face −X of map 0, frame 2 face +Y of map 0.
            Vector3[] root = Centers(renderer, map: 0);
            root[(int)CubeFace.PositiveX].Should().Be(colors[0], "the root's map is kept, so frame 0's face stays");
            root[(int)CubeFace.NegativeX].Should().Be(colors[1], "frame 1 drew this face of the kept map in turn");
            root[(int)CubeFace.PositiveY].Should().Be(colors[2], "frame 2 drew this face of the kept map in turn");
            root[(int)CubeFace.NegativeZ].Should().Be(colors[0], "no frame since frame 0 drew this face");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WhenTheCameraEntersAChildItsNewMapAndTheOldCameraFramesMapRefresh()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            Vector3[] colors = LeaveAndEnterAChild(renderer);

            for (int map = 1; map < 3; map++)
                Centers(renderer, map)
                    .Should()
                    .AllBeEquivalentTo(colors[2], $"map {map}'s frame or the frame below it changed in frame 2");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AChainLongerThanEverBeforeGrowsTheMapsAndRefreshesEveryOne()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            var before = new Vector3(1, 0, 0);
            var after = new Vector3(0, 1, 0);
            EnvironmentScenes.Still(
                renderer,
                DomeScene(plane, EnvironmentScenes.Emissive(renderer, before), 1, 0),
                time: 0
            );

            // The root's map keeps its frame and its frame below, but the growth to three maps makes its images anew.
            EnvironmentScenes.Still(
                renderer,
                DomeScene(plane, EnvironmentScenes.Emissive(renderer, after), 2, 1, 0),
                time: Step
            );

            for (int map = 0; map < 3; map++)
                Centers(renderer, map).Should().AllBeEquivalentTo(after, $"the growth refreshes map {map}");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Every level of every face of map <paramref name="map"/>'s specular map, in layer and level order.</summary>
    private static Half[] SpecularLevels(VulkanRenderer renderer, int map)
    {
        var all = new List<Half>();
        int levels = PrefilterLevels.Count(EnvironmentScenes.FaceSize);
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int level = 0; level < levels; level++)
            all.AddRange(
                renderer.ReadEnvironmentFace(
                    map: map,
                    face: (CubeFace)layer,
                    level: level,
                    image: EnvironmentImage.Specular
                )
            );
        return [.. all];
    }

    [Fact]
    public void AMapWithNoDrawnFaceKeepsItsSpecularLevelsBitForBit()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            EnvironmentScenes.Still(
                renderer,
                DomeScene(plane, EnvironmentScenes.Emissive(renderer, new(1, 0, 0)), 2, 1, 0),
                time: 0
            );
            Half[] rootBefore = SpecularLevels(renderer, map: 0);
            Half[] cameraBefore = SpecularLevels(renderer, map: 2);

            // Frame 1 draws face −X of map 0 alone, in a dome of another color.
            EnvironmentScenes.Still(
                renderer,
                DomeScene(plane, EnvironmentScenes.Emissive(renderer, new(0, 1, 0)), 2, 1, 0),
                time: Step
            );

            SpecularLevels(renderer, map: 0).Should().NotEqual(rootBefore, "map 0's new face changes its levels");
            SpecularLevels(renderer, map: 2).Should().Equal(cameraBefore, "no face of map 2 was drawn");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    // Mirrors and glass. The camera looks down −Z from a room that is open toward −Z. Behind the camera, the room's back wall
    // glows red; behind the room, outside, a square glows blue. A surface facing the camera reflects what lies behind it:
    // the red wall in the room's map, and the blue square in the outside's map, which leaves out the room.

    /// <summary>The radiance of the red back wall and of the blue square behind the room.</summary>
    private const float MirrorRadiance = 8;

    /// <summary>A surface in the room, 2.5 m ahead and to the left, and one outside, 8 m ahead and to the right.</summary>
    private static readonly Vector3 InTheRoom = new(-1, 0, -2.5f),
        OutsideTheRoom = new(1, 0, -8);

    /// <summary>The room with the red back wall, and the blue square behind it outside, and no other light.</summary>
    private static RenderSnapshot MirrorRoom(VulkanRenderer renderer, MeshId plane)
    {
        RenderSnapshot snapshot = EnvironmentScenes.Dark();
        TestChains.Of(snapshot, (1, Quaternion.Identity), (0, Quaternion.Identity));
        MaterialId black = EnvironmentScenes.Emissive(renderer, Vector3.Zero);
        MaterialId red = EnvironmentScenes.Emissive(renderer, new(MirrorRadiance, 0, 0));
        MaterialId blue = EnvironmentScenes.Emissive(renderer, new(0, 0, MirrorRadiance));
        Vector3[] sides = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY];
        foreach (Vector3 side in sides)
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.InRoom,
                    EnvironmentScenes.Square(plane: plane, material: black, center: side * 3, halfSize: 3.3f)
                )
            );
        snapshot.Instances.Add(
            RoomScenes.In(
                RoomScenes.InRoom,
                EnvironmentScenes.Square(plane: plane, material: red, center: new(0, 0, 3), halfSize: 3.3f)
            )
        );
        snapshot.Instances.Add(
            RoomScenes.In(
                RoomScenes.Outside,
                EnvironmentScenes.Square(plane: plane, material: blue, center: new(0, 0, 30), halfSize: 20)
            )
        );
        return snapshot;
    }

    /// <summary>A square of <paramref name="material"/> at <paramref name="center"/> facing the camera, +Z.</summary>
    private static RenderInstance FacingTheCamera(
        MeshId plane,
        MaterialId material,
        Vector3 center,
        ushort chainDepth,
        RenderFlags flags = RenderFlags.None
    ) =>
        RoomScenes.In(
            chainDepth,
            EnvironmentScenes.Facing(
                plane: plane,
                material: material,
                center: center,
                normal: Vector3.UnitZ,
                halfSize: 0.4f,
                flags: flags
            )
        );

    /// <summary>
    /// The colors that a mirror in the room and a mirror outside show, in a new renderer with
    /// <paramref name="reflections"/>.
    /// </summary>
    private static (Vector3 Inside, Vector3 Outside) Mirrors(ReflectionSetting reflections, string name)
    {
        VulkanRenderer renderer = Renderer(reflections: reflections);
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = MirrorRoom(renderer, plane);
            MaterialId mirror = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0, Metallic: 1));
            snapshot.Instances.Add(
                FacingTheCamera(plane: plane, material: mirror, center: InTheRoom, chainDepth: RoomScenes.InRoom)
            );
            snapshot.Instances.Add(
                FacingTheCamera(plane: plane, material: mirror, center: OutsideTheRoom, chainDepth: RoomScenes.Outside)
            );

            Capture shown = Shown(renderer, snapshot, name);

            return (
                Inside: shown.Color(Gpu.Project(point: InTheRoom, width: Width, height: Height)),
                Outside: shown.Color(Gpu.Project(point: OutsideTheRoom, width: Width, height: Height))
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>How far blue stands out over red in a shown color: positive for blue, negative for red.</summary>
    private static float BlueOverRed(Vector3 color) => color.Z - color.X;

    [Fact]
    public void AMirrorInTheRoomAndAMirrorOutsideReflectTheirOwnMaps()
    {
        (Vector3 inside, Vector3 outside) = Mirrors(new NoReflections(), "maps-mirrors");

        BlueOverRed(inside).Should().BeLessThan(-0.3f, "the room's mirror reflects the red wall: {0}", inside);
        BlueOverRed(outside).Should().BeGreaterThan(0.3f, "the mirror outside reflects the blue square: {0}", outside);
    }

    /// <summary>The traces of the reflections: one ray per pixel, and one per block of 2 × 2 pixels with the upsample.</summary>
    public static TheoryData<TraceResolution> Resolutions => [TraceResolution.Full, TraceResolution.Half];

    /// <summary>The reflections of the default setting, traced at <paramref name="resolution"/>.</summary>
    private static ConeTracedReflections TracedAt(TraceResolution resolution) =>
        ConeTracedReflections.Default with
        {
            Resolution = resolution,
        };

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void TheReflectionsFallBackToTheMapOfTheirPixel(TraceResolution resolution)
    {
        // The rays leave the screen toward the camera, so each falls back to its pixel's map.
        (Vector3 inside, Vector3 outside) = Mirrors(TracedAt(resolution), $"maps-mirrors-traced-{resolution}");

        BlueOverRed(inside).Should().BeLessThan(-0.3f, "the room's mirror falls back to the room's map: {0}", inside);
        BlueOverRed(outside)
            .Should()
            .BeGreaterThan(0.3f, "the mirror outside falls back to the outside's: {0}", outside);
    }

    // A hit in the room. A mirror on the floor reflects the view ray up and away from the eye, toward a green square at
    // the room's open side, which the room's map holds there and the outside's map does not. A dark screen in front of the
    // square catches the mirror's rays, but not the map's direction from the room's center, which passes above it.

    /// <summary>The center of the mirror on the floor, 1 m below the eye and 2.5 m ahead.</summary>
    private static readonly Vector3 FloorMirror = new(0, -1, -2.5f);

    /// <summary>The radiance of the green square, which only the room's map holds.</summary>
    private const float GreenRadiance = 4;

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void AHitInTheRoomTakesAwayTheRoomsMapFromItsPixel(TraceResolution resolution)
    {
        VulkanRenderer renderer = Renderer(reflections: TracedAt(resolution));
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = MirrorRoom(renderer, plane);
            MaterialId green = EnvironmentScenes.Emissive(renderer, new(0, GreenRadiance, 0));
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.InRoom,
                    EnvironmentScenes.Square(plane: plane, material: green, center: new(0, 0, -3.2f), halfSize: 2)
                )
            );
            MaterialId mirror = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0, Metallic: 1));
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.InRoom,
                    EnvironmentScenes.Facing(
                        plane: plane,
                        material: mirror,
                        center: FloorMirror,
                        normal: Vector3.UnitY,
                        halfSize: 0.3f
                    )
                )
            );
            // The mirror's rays meet the plane z = −3 from y = −0.93 to −0.64 and x within ±0.36.
            snapshot.Instances.Add(
                RoomScenes.In(
                    RoomScenes.InRoom,
                    EnvironmentScenes.Facing(
                        plane: plane,
                        material: EnvironmentScenes.Emissive(renderer, Vector3.Zero),
                        center: new(0, -0.7f, -3),
                        normal: Vector3.UnitZ,
                        halfSize: 0.4f
                    )
                )
            );

            Vector3 shown = Shown(renderer, snapshot, $"maps-mirror-hit-{resolution}")
                .Color(Gpu.Project(point: FloorMirror, width: Width, height: Height));

            // The lighting pass added the room's map, the green square. The delta takes away the map of the mirror's own
            // pixel, so the dark screen replaces the square. Taking away the outside's map, dark there, would leave it green.
            shown.Y.Should().BeLessThan(0.1f, "the hit replaces the room's map in the mirror: {0}", shown);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void GlassOutsideReflectsTheOutsidesMap()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = MirrorRoom(renderer, plane);
            MaterialId glass = renderer.CreateMaterial(
                new(BaseColor: new(x: 1, y: 1, z: 1, w: 0.2f), Roughness: 0, Reflectance: 0.5f)
            );
            snapshot.Instances.Add(
                FacingTheCamera(
                    plane: plane,
                    material: glass,
                    center: OutsideTheRoom,
                    chainDepth: RoomScenes.Outside,
                    flags: RenderFlags.Transparent
                )
            );

            Vector3 pane = Shown(renderer, snapshot, "maps-glass")
                .Color(Gpu.Project(point: OutsideTheRoom, width: Width, height: Height));

            BlueOverRed(pane).Should().BeGreaterThan(0.05f, "the pane outside reflects the blue square: {0}", pane);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A puff of the first look at <paramref name="center"/>, outside, newborn and still.</summary>
    private static RenderPuff PuffOutside(Vector3 center) =>
        new()
        {
            Position = center,
            PreviousPosition = center,
            Age = 0f,
            Look = 0,
            ChainDepth = RoomScenes.Outside,
        };

    [Fact]
    public void ALitPuffOutsideTakesTheMeanRadianceOfTheOutsidesMap()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var room = new RoomScenes(renderer);
            var albedo = new Vector3(0.8f, 0.6f, 0.4f);
            var look = new PuffLook(
                Life: 2f,
                StartRadius: 0.5f,
                EndRadius: 0.5f,
                TemperatureRatio: 0.5f,
                Shading: PuffShading.Lit(albedo)
            );
            RenderSnapshot snapshot = room.Scene(lampsOn: true);
            snapshot.PuffLooks.Add(look);
            snapshot.Puffs.Add(PuffOutside(RoomScenes.Ground + new Vector3(0, 1.5f, 0)));
            Vector2 pixel = RoomScenes.Pixel(RoomScenes.Ground + new Vector3(0, 1.5f, 0), Width, Height);

            Vector3 lit = Shown(renderer, snapshot, "maps-lit-puff").Color(pixel);
            Vector3 mean = renderer.ReadEnvironmentIrradiance(map: 0).MeanRadiance;
            snapshot.PuffLooks[0] = look with { Shading = PuffShading.Glow(albedo * mean) };
            Vector3 glow = Shown(renderer, snapshot, "maps-glow-puff").Color(pixel);

            mean.Should().NotBe(Vector3.Zero, "the sky lights the outside's map");
            Vector3 roomMean = renderer.ReadEnvironmentIrradiance(map: 1).MeanRadiance;
            mean.Length()
                .Should()
                .BeLessThan(roomMean.Length() / 4, "the outside's map leaves out the lit room: {0}", roomMean);
            BrdfTesting
                .LargestChannelDifference(lit, glow)
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "the puff scatters the outside's mean radiance {0}: {1} and {2}",
                    mean,
                    lit,
                    glow
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The lit room with a matte ball outside, 0.5 m wide, and one in the room, 0.3 m wide, each small enough to be a
    /// billboard unless <paramref name="flags"/> keep it geometry.
    /// </summary>
    private static RenderSnapshot BallsInAndOut(RoomScenes room, MeshId sphere, RenderFlags flags)
    {
        RenderSnapshot snapshot = room.Scene(lampsOn: true);
        (Vector3 Center, float Radius, ushort Depth)[] balls =
        [
            (BallOutside, 0.5f, RoomScenes.Outside),
            (BallInTheRoom, 0.15f, RoomScenes.InRoom),
        ];
        for (int i = 0; i < balls.Length; i++)
            snapshot.Instances.Add(
                RoomScenes.In(
                    balls[i].Depth,
                    Gpu.Instance(
                        mesh: sphere,
                        material: room.Matte,
                        position: balls[i].Center,
                        scale: new(balls[i].Radius),
                        flags: flags,
                        entity: (uint)(i + 1)
                    )
                )
            );
        return snapshot;
    }

    private static readonly Vector3 BallOutside = RoomScenes.Ground + new Vector3(0, 1, 0),
        BallInTheRoom = RoomScenes.Floor + new Vector3(0, 0.6f, 0);

    [Fact]
    public void AnImpostorOutsideBakesTheOutsidesMap()
    {
        VulkanRenderer renderer = Renderer(impostors: true);
        try
        {
            var room = new RoomScenes(renderer);
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            Vector2 outside = RoomScenes.Pixel(BallOutside, Width, Height);
            Vector2 inside = RoomScenes.Pixel(BallInTheRoom, Width, Height);
            Capture geometry = Shown(
                renderer,
                BallsInAndOut(room, sphere, RenderFlags.NoImpostor),
                "maps-impostor-geometry"
            );
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(BallsInAndOut(room, sphere, RenderFlags.None), 1f);
            Capture billboards = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: BallsInAndOut(room, sphere, RenderFlags.None),
                name: "maps-impostor-billboards"
            );

            renderer.LastFrameStats.Impostors.Should().Be(2, "both balls are small enough to be billboards");
            BrdfTesting
                .LargestChannelDifference(billboards.Color(outside), geometry.Color(outside))
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "the capture bakes the outside's map: {0} against {1}",
                    billboards.Color(outside),
                    geometry.Color(outside)
                );
            (billboards.Color(inside).Length() - billboards.Color(outside).Length())
                .Should()
                .BeGreaterThan(0.3f, "the ball in the room bakes the room's lamps, and the ball outside does not");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheGBufferCarriesTheMapCodeThatMapCodeGives()
    {
        // Six frames on the chain. Behind the camera, each map's own square is the nearest that the map draws, so a mirror
        // of map m, at chain depth 5 − m, shows the color of map m's square: the map that its pixel's code named.
        Vector3[] colors = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 0), new(0, 1, 1), new(1, 0, 1)];
        VulkanRenderer renderer = Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            TestChains.Of(snapshot, [.. Enumerable.Range(0, 6).Select(i => ((long)(5 - i), Quaternion.Identity))]);
            MaterialId mirror = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0, Metallic: 1));
            for (int map = 0; map < colors.Length; map++)
            {
                ushort depth = (ushort)(colors.Length - 1 - map);
                // Dim, so that the tone map keeps each hue apart: a bright green comes out pale, nearer yellow than green.
                MaterialId glow = EnvironmentScenes.Emissive(renderer, colors[map] * 0.5f);
                float distance = 60 - 8 * map;
                snapshot.Instances.Add(
                    RoomScenes.In(
                        depth,
                        EnvironmentScenes.Square(
                            plane: plane,
                            material: glow,
                            center: new(0, 0, distance),
                            halfSize: distance
                        )
                    )
                );
                snapshot.Instances.Add(
                    FacingTheCamera(plane: plane, material: mirror, center: new(map - 2.5f, 0, -6), chainDepth: depth)
                );
            }

            Capture shown = Shown(renderer, snapshot, "maps-codes");

            for (int map = 0; map < colors.Length; map++)
            {
                Vector3 seen = shown.Color(Gpu.Project(point: new(map - 2.5f, 0, -6), width: Width, height: Height));
                int named = Enumerable.Range(0, colors.Length).MinBy(i => Vector3.Distance(seen, colors[i]));
                named.Should().Be(map, "the mirror of map {0} shows {1}", map, seen);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The color bytes of a room of six walls of every color around a matte ball and a mirror ball, every object in the camera's
    /// frame, whose chain is <paramref name="chain"/>.
    /// </summary>
    private static byte[] EnclosedBytes(string name, params (long Id, Quaternion Rotation)[] chain)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));
            RenderSnapshot snapshot = EnvironmentScenes.Dark();
            TestChains.Of(snapshot, chain);
            for (int layer = 0; layer < CubeFaces.Count; layer++)
                snapshot.Instances.Add(
                    EnvironmentScenes.Square(
                        plane: plane,
                        material: EnvironmentScenes.Emissive(renderer, EnvironmentScenes.ColorOf(layer)),
                        center: CubeFaces.Basis((CubeFace)layer).Forward * 10,
                        halfSize: 11
                    )
                );
            MaterialId mirror = renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 0.1f, Metallic: 1));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: sphere,
                    material: EnvironmentScenes.Matte(renderer),
                    position: new(-1.5f, 0, -5),
                    scale: Vector3.One
                )
            );
            snapshot.Instances.Add(
                Gpu.Instance(mesh: sphere, material: mirror, position: new(1.5f, 0, -5), scale: Vector3.One)
            );
            byte[] rgba = Shown(renderer, snapshot, name).Rgba;
            // The fourth byte of each pixel holds no defined value.
            return [.. rgba.Where((_, i) => i % 4 != 3)];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithEveryObjectInTheCamerasFrameAChainOfTwoGivesTheBytesOfAChainOfOne()
    {
        byte[] one = EnclosedBytes("maps-chain-of-one", (0, Quaternion.Identity));
        byte[] two = EnclosedBytes(
            "maps-chain-of-two",
            (1, Quaternion.Identity),
            (0, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f))
        );

        two.Should().Equal(one, "the camera's frame's map is the same map whatever lies above it");
    }
}
