using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    public static partial class StationExit
    {
        private const float DoorHalfWidth = 0.75f;

        private static Entity LandingGate(
            World world,
            FrameHandle frame,
            Double3 landing,
            bool open,
            IRenderResources resources,
            MaterialId cladding
        )
        {
            Vector3 half = new(1.24f, 0.065f, DoorHalfWidth);
            Vector3 slide = new(0, 0, 2 * DoorHalfWidth + 0.04f);
            Double3 shut = landing + new Double3(half.X, Layout.LiftHalf + 0.15, 0);
            return world.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = new(open ? shut + slide : shut, DQuaternion.Identity),
                    Body = new(Shape: new BoxShapeDesc(half), Motion: BodyMotion.Kinematic),
                    Render = PanelRender(
                        resources: resources,
                        mesh: new BoxMesh().Box(new(-half, half)).Build(),
                        radius: half.Length(),
                        material: cladding
                    ),
                    Components = [new Door(slide, openSeconds: 0.9f, openAtStart: open)],
                }
            );
        }

        private static void SpawnLanding(
            World world,
            FrameHandle frame,
            IRenderResources resources,
            MaterialId cladding
        )
        {
            // The front of the car slides past this continuous shaft wall between the landing gates.
            // Steel guides run immediately outside the cabin on either side of the back wall.
            float foot = -(float)Ride.Foot;
            float half = Layout.LiftHalf;
            float across = Layout.LiftAcross;
            float rear = -half - 0.18f;
            float front = half + 0.12f;
            float floor = -Layout.FloorRadius;
            BoundingBox[] structure =
            [
                new(
                    new(foot + CabinHeight + 0.74f, front, across - half),
                    new(Layout.Deck - LiftFloorThickness, front + 0.15f, across + half)
                ),
                new(
                    new(foot, rear, across - half),
                    new(Layout.Deck + CabinHeight, rear + 0.12f, across - half + 0.12f)
                ),
                new(
                    new(foot, rear, across + half - 0.12f),
                    new(Layout.Deck + CabinHeight, rear + 0.12f, across + half)
                ),
                // A recessed waiting area beside the platform, with an open front toward the concourse.
                new(new(floor, -6, -10), new(floor + 3.5f, 6.5f, -9.8f)),
                new(new(floor, -6, -9.8f), new(floor + 3.5f, -5.8f, -0.2f)),
                new(new(floor + 3.5f, -6, -10), new(floor + 3.68f, 6.5f, -0.2f)),
            ];
            IEnumerable<BoundingBox> GateFrame(float sill)
            {
                float jamb = front + 0.2f;
                yield return new(
                    new(sill, jamb, across - half),
                    new(sill + CabinHeight, jamb + 0.14f, across - DoorHalfWidth - 0.06f)
                );
                yield return new(
                    new(sill, jamb, across + DoorHalfWidth + 0.06f),
                    new(sill + CabinHeight, jamb + 0.14f, across + half)
                );
                yield return new(
                    new(sill + CabinHeight, front, across - half),
                    new(sill + CabinHeight + 0.74f, jamb + 0.14f, across + half)
                );
            }

            foreach (BoundingBox box in structure.Concat(GateFrame(Layout.Deck)).Concat(GateFrame(foot)))
            {
                Vector3 halfSize = box.Extents;
                world.Spawn(
                    new()
                    {
                        Frame = frame,
                        Pose = new(box.Center, DQuaternion.Identity),
                        Body = new(Shape: new BoxShapeDesc(halfSize), Motion: BodyMotion.Static),
                        Render = PanelRender(
                            resources: resources,
                            mesh: new BoxMesh().Box(new(-halfSize, halfSize)).Build(),
                            radius: halfSize.Length(),
                            material: cladding
                        ),
                    }
                );
            }
        }

        /// <summary>One metre square of powder-coated sheet with an inset gasket and corner fixings. BoxMesh UVs are in metres.</summary>
        internal static MaterialDesc PanelFinish(IRenderResources resources)
        {
            const int size = 64;
            byte[] pixels = new byte[size * size * 4];
            float[] heights = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int inset = Math.Min(Math.Min(x, size - x - 1), Math.Min(y, size - y - 1));
                bool screw = (x is 4 or 59) && (y is 4 or 59);
                byte tone = inset switch
                {
                    0 => 133,
                    1 => 164,
                    _ => screw ? (byte)90 : (byte)179,
                };
                heights[y * size + x] = inset switch
                {
                    0 => 0,
                    1 => 0.002f,
                    _ => screw ? 0.001f : 0.0035f,
                };
                int i = (y * size + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (
                    tone,
                    (byte)(tone + 4),
                    (byte)(tone + 5),
                    255
                );
            }
            return new(
                BaseColor: Vector4.One,
                Roughness: 0.64f,
                Model: new StandardShading(0.25f),
                BaseColorMap: resources.CreateTexture(new(Width: size, Height: size, Rgba: pixels)),
                NormalMap: resources.CreateTexture(ReliefMap(size, heights))
            );
        }

        private static RenderData PanelRender(
            IRenderResources resources,
            MeshData mesh,
            float radius,
            MaterialId material
        ) =>
            new()
            {
                Mesh = resources.CreateMesh(mesh),
                Material = material,
                BoundingRadius = radius,
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
            };
    }

    /// <summary>Periodic tangent-space normals for a one-metre tile. Heights and texel pitch are in metres.</summary>
    private static TextureData ReliefMap(int size, float[] heights)
    {
        byte[] rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float du = heights[y * size + (x + 1) % size] - heights[y * size + (x + size - 1) % size];
            float dv = heights[((y + 1) % size) * size + x] - heights[((y + size - 1) % size) * size + x];
            Vector3 n = Vector3.Normalize(new(-du, -dv, 2f / size));
            int i = (y * size + x) * 4;
            rgba[i] = (byte)MathF.Round(127.5f * (n.X + 1));
            rgba[i + 1] = (byte)MathF.Round(127.5f * (n.Y + 1));
            rgba[i + 2] = (byte)MathF.Round(127.5f * (n.Z + 1));
            rgba[i + 3] = 255;
        }
        return new(Width: size, Height: size, Rgba: rgba, Srgb: false);
    }
}
