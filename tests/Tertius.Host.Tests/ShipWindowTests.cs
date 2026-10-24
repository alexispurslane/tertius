using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The windows of the ship in the scene <c>orbit</c> (ticket ter-ka2o), as the render snapshot at the start shows them.
/// Each window has a pane of glass: a thin transparent box with a slight blue tint, which casts no shadow. The side walls
/// have a row of windows at eye height, one every 6 m from 24 m ahead of the ship's center to 24 m aft of it, and the
/// port wall leaves out the one that would cut into the airlock's chamber, 12 m aft. The nose wall has one wide window.
/// The expected layout comes from the scene's doc, not from the scene's code.
/// </summary>
public sealed class ShipWindowTests
{
    // In the ship's axes: the side walls' middles are 9.75 m from the center, the nose wall's middle is 29.75 m ahead of it,
    // and the eye of a person on the floor, 3.5 m below the center, is 1.65 m above the floor.
    private const float SideWallMiddle = 9.75f,
        NoseWallMiddle = -29.75f,
        EyeHeight = -3.5f + 1.65f;

    /// <summary>Resources that give each mesh, texture and material its own id, and keep each mesh and each material's description.</summary>
    private sealed class RecordingResources : IRenderResources
    {
        private int next = 1;

        public Dictionary<MeshId, MeshData> Meshes { get; } = [];

        public Dictionary<MaterialId, MaterialDesc> Materials { get; } = [];

        public MeshId CreateMesh(MeshData mesh)
        {
            var id = new MeshId(next++);
            Meshes[id] = mesh;
            return id;
        }

        public TextureId CreateTexture(TextureData texture) => new(next++);

        public MaterialId CreateMaterial(MaterialDesc material)
        {
            var id = new MaterialId(next++);
            Materials[id] = material;
            return id;
        }
    }

    /// <summary>The scene at its start, seen from the chase camera in the ship's frame, with its meshes and materials.</summary>
    private static (RenderSnapshot Snapshot, RecordingResources Resources, SceneInstance Scene) Start(World world)
    {
        var resources = new RecordingResources();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(world, resources);
        world.PublishRenderSnapshot();
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should().BeTrue();
        return (snapshot, resources, scene);
    }

    private static RenderInstance[] Panes(RenderSnapshot snapshot) =>
        [.. snapshot.Instances.Where(instance => (instance.Flags & RenderFlags.Transparent) != 0)];

    [Fact]
    public void TheShipHasAPaneInEachWindowAndNoPaneCastsAShadowButTheHullDoes()
    {
        using World world = NewJoltWorld();
        (RenderSnapshot snapshot, _, _) = Start(world);

        RenderInstance[] panes = Panes(snapshot);

        // Nine windows in the starboard wall, eight in the port wall and one in the nose wall.
        panes.Should().HaveCount(9 + 8 + 1);
        panes
            .Should()
            .AllSatisfy(pane =>
                (pane.Flags & (RenderFlags.Visible | RenderFlags.CastsShadow)).Should().Be(RenderFlags.Visible)
            );
        RenderInstance hull = snapshot.Instances.Single(instance => instance.BoundingRadius == 32);
        (hull.Flags & RenderFlags.CastsShadow)
            .Should()
            .Be(RenderFlags.CastsShadow, "the hull keeps the sunlight out, except through the windows");
    }

    [Fact]
    public void EachPaneIsMostlyClearWithASlightBlueTint()
    {
        using World world = NewJoltWorld();
        (RenderSnapshot snapshot, RecordingResources resources, _) = Start(world);

        RenderInstance[] panes = Panes(snapshot);

        panes.Should().NotBeEmpty();
        foreach (RenderInstance pane in panes)
        {
            Vector4 color = resources.Materials[pane.Material].BaseColor;
            color.W.Should().BeInRange(0.05f, 0.3f, "the pane lets most of the light through");
            (color.Z > color.Y && color.Y > color.X).Should().BeTrue($"the glass is tinted blue, not {color}");
        }
    }

    [Fact]
    public void EachPaneIsThinAcrossItsWallAndOneCentimeterLargerThanItsWindowOnEachSideAlongTheOtherTwoAxes()
    {
        // So the pane's edges hide in the wall, and no edge of the glass shares a plane with a side of the window. The
        // windows come from the scene's public list, and the pane's box from the mesh that the scene made for it.
        const float Thickness = 0.02f,
            Overlap = 0.01f;
        using World world = NewJoltWorld();
        (RenderSnapshot snapshot, RecordingResources resources, SceneInstance scene) = Start(world);

        foreach ((string name, Opening window) in DemoScenes.Windows)
        {
            RenderInstance pane = snapshot.Instances.Single(instance => instance.EntityIndex == scene[name].Index);
            // The pane's box in the ship's axes: the box of its mesh, scaled, around the pane's place.
            Vertex[] vertices = resources.Meshes[pane.Mesh].Vertices;
            Vector3 place = pane.Position + snapshot.OriginInFrame.ToVector3();
            Vector3 min =
                place
                + pane.Scale
                    * vertices.Aggregate(
                        new Vector3(float.MaxValue),
                        (low, vertex) => Vector3.Min(low, vertex.Position)
                    );
            Vector3 max =
                place
                + pane.Scale
                    * vertices.Aggregate(
                        new Vector3(float.MinValue),
                        (high, vertex) => Vector3.Max(high, vertex.Position)
                    );

            int wallAxis = window.Through == Vector3.UnitX ? 0 : 2;
            for (int axis = 0; axis < 3; axis++)
            {
                float middle = window.Bounds.Center[axis];
                float expectedMin = axis == wallAxis ? middle - Thickness / 2 : window.Bounds.Min[axis] - Overlap;
                float expectedMax = axis == wallAxis ? middle + Thickness / 2 : window.Bounds.Max[axis] + Overlap;
                min[axis].Should().BeApproximately(expectedMin, 1e-4f, $"{name} starts there along axis {axis}");
                max[axis].Should().BeApproximately(expectedMax, 1e-4f, $"{name} ends there along axis {axis}");
            }
        }
    }

    [Fact]
    public void ThePanesStandInTheMiddleOfTheSideWallsAtEyeHeightAndInTheNoseWall()
    {
        using World world = NewJoltWorld();
        (RenderSnapshot snapshot, _, _) = Start(world);

        // The chase camera rides in the ship's frame, so the snapshot's axes are the ship's, and its positions are relative
        // to the camera.
        Vector3[] centers = [.. Panes(snapshot).Select(pane => pane.Position + snapshot.OriginInFrame.ToVector3())];

        static float[] Along(IEnumerable<Vector3> side) => [.. side.Select(center => MathF.Round(center.Z, 3)).Order()];
        float[] row = [-24, -18, -12, -6, 0, 6, 12, 18, 24];
        Along(centers.Where(center => MathF.Abs(center.X - SideWallMiddle) < 1e-3f)).Should().Equal(row);
        Along(centers.Where(center => MathF.Abs(center.X + SideWallMiddle) < 1e-3f))
            .Should()
            .Equal(row.Where(z => z != 12), "the port wall has no window where the airlock's chamber stands");
        centers
            .Where(center => MathF.Abs(center.Z - NoseWallMiddle) < 1e-3f)
            .Should()
            .ContainSingle()
            .Which.X.Should()
            .BeApproximately(0, 1e-3f);
        centers.Should().AllSatisfy(center => center.Y.Should().BeApproximately(EyeHeight, 1e-3f));
    }

    [Fact]
    public void ACrateThrownAtAWindowStopsAtTheGlassAsAtAWall()
    {
        // A person cannot fit through a window, which starts 1.05 m above the floor and is 1.2 m tall, but a crate can. The
        // crate, 0.4 m on a side, flies at 10 m/s toward the starboard window 12 m aft, from 1.5 m inside the wall at the
        // window's middle height. It reaches the glass in 0.13 s, after falling 8 cm in the cabin's gravity, well inside the
        // window. Through a hole it would leave the cabin, and 0.5 s after the throw it would be 3.5 m past the wall's inner
        // face, which is 3 m past the hull's outer face.
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None);
        FrameHandle ship = world.GetOwnedFrame(scene["ship"]);
        Entity crate = world.Spawn(
            new()
            {
                Frame = ship,
                Pose = new(new(8, EyeHeight, 12), DQuaternion.Identity),
                LinearVelocity = new Vector3(10, 0, 0),
                Body = new(Shape: new BoxShapeDesc(new(0.2f)), Mass: 5, LinearDamping: 0, AngularDamping: 0),
            }
        );

        for (int tick = 0; tick < 30; tick++)
            world.Tick();

        world.GetFrame(crate).Should().Be(ship);
        world
            .PoseInOwnFrame(crate)
            .Position.X.Should()
            .BeLessThan(9.5 - 0.2, "the glass, like the wall, keeps the crate in the cabin");
    }
}
