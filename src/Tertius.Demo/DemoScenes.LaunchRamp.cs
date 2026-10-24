using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The launch ramp of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The sequence", step 2): a straight track
    /// up which the shuttle slides like a rocket sled. It is one static body in the frame <c>earth-surface</c>, at the north
    /// pole, where +Y is up and the parked shuttle's nose points along -Z.
    /// <list type="bullet">
    /// <item>The <see cref="Foot"/> of the ramp lies <see cref="BedRise"/> above the slab's top, under the parked shuttle's
    /// nose. From there the incline rises at <see cref="Angle"/> toward -Z for <see cref="Length"/>, to its
    /// <see cref="Top"/>, 125 m up and 216.5 m along -Z.</item>
    /// <item>A flat bed lies under the parked shuttle, from the foot to 1 m aft of the hull. Its top, the
    /// <see cref="BedTop"/>, lies <see cref="BedRise"/> above the slab's top, so the parked hull rests on the bed alone, and
    /// the hull slides along it onto the incline without touching the slab, which has friction. The bed is not drawn: the
    /// drawn pad round the shuttle is as thick as the rise.</item>
    /// <item>Both pieces are <see cref="Width"/> wide, narrower than the hull, so the hull overhangs them, and a person who
    /// walks to the hatch never steps onto them. They are boxes with sharp edges, so physics removes the contacts with the
    /// edge between them, and they have no friction: the rails of the sled are greased.</item>
    /// <item>The drawn ramp is a solid gray wedge under the incline, which reaches 5 m below its foot, so that it stands on
    /// the drawn planet beyond the slab, which lies lower there.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The incline is one straight box, so the hull meets one joint only, at the foot, at the speed of its first meters.
    /// A curved ramp of many short planks threw the hull off near 90 m/s: from there, the hull moves more than 0.75 of its
    /// inner radius in one step, and Jolt sweeps it. The sweep stopped the hull at each joint between two planks.
    /// </remarks>
    public static class LaunchRamp
    {
        /// <summary>The angle of the incline above the horizontal, in radians: 30 degrees.</summary>
        public const double Angle = Math.PI / 6;

        /// <summary>The length of the incline from its foot to its top, in meters.</summary>
        public const float Length = 250;

        /// <summary>The width of the ramp, in meters.</summary>
        public const float Width = 4;

        /// <summary>The thickness of the incline and the bed, in meters.</summary>
        public const float Thickness = 1;

        /// <summary>
        /// How far the bed's top lies above the slab's top, in meters: 2 cm. A hull that rests on the bed can sink into
        /// it by Jolt's penetration slop, 5 mm (<c>JoltBackendOptions.PenetrationSlop</c>), so it stays clear of the slab.
        /// </summary>
        public const double BedRise = 0.02;

        /// <summary>The height of the bed's top, where the parked hull's bottom rests, in <c>earth-surface</c>, in meters from Earth's center.</summary>
        public const double BedTop = EarthRadius + BedRise;

        // How far the drawn wedge reaches below the slab's top, in meters.
        private const float Sink = 5;

        /// <summary>Where the incline starts, in <c>earth-surface</c>: on the bed's top, under the parked shuttle's nose.</summary>
        public static Double3 Foot { get; } = new(0, BedTop, -Shuttle.Hull.Z);

        /// <summary>The direction up the incline, a unit vector in the axes of <c>earth-surface</c>.</summary>
        public static Double3 Up { get; } = new(0, Math.Sin(Angle), -Math.Cos(Angle));

        /// <summary>The middle of the incline's upper end, on its surface, in <c>earth-surface</c>.</summary>
        public static Double3 Top => Foot + Up * Length;

        /// <summary>How high the top of the incline lies above its foot, in meters: 125.</summary>
        public static double Height => Length * Math.Sin(Angle);

        /// <summary>The boxes of the ramp's static shape, relative to the <see cref="Foot"/>, in the axes of <c>earth-surface</c>.</summary>
        private static CompoundChild[] Shape()
        {
            float bedLength = 2 * Shuttle.Hull.Z + 1;
            Vector3 up = Up.ToVector3(), normal = new(0, (float)Math.Cos(Angle), (float)Math.Sin(Angle));
            return
            [
                new(new BoxShapeDesc(new(Width / 2, Thickness / 2, bedLength / 2), ConvexRadius: 0),
                    new(new(0, -Thickness / 2, bedLength / 2), Quaternion.Identity)),
                new(new BoxShapeDesc(new(Width / 2, Thickness / 2, Length / 2), ConvexRadius: 0),
                    new(up * (Length / 2) - normal * (Thickness / 2),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Angle)))
            ];
        }

        /// <summary>
        /// The drawn wedge, relative to the <see cref="Foot"/>: its top is the incline's surface, its back is vertical under
        /// the top, and its bottom lies 5 m below the foot. Its six faces wind counterclockwise from outside, as in
        /// <see cref="Primitives"/>.
        /// </summary>
        public static MeshData Mesh()
        {
            float h = (float)Height, d = (float)(Length * Math.Cos(Angle)), w = Width / 2;
            // The wedge's outline in its side, as (y, z), from the foot round to the foot.
            Vector2[] outline = [new(0, 0), new(h, -d), new(-Sink, -d), new(-Sink, 0)];
            var vertices = new List<Vertex>();
            var indices = new List<uint>();

            void Face(params Vector3[] corners)
            {
                Vector3 n = Vector3.Normalize(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]));
                Vector3 tangent = Vector3.Normalize(corners[1] - corners[0]);
                uint start = (uint)vertices.Count;
                foreach (Vector3 corner in corners)
                    vertices.Add(new() { Position = corner, Normal = n, Tangent = new(tangent, 1) });
                for (uint i = 1; i + 1 < corners.Length; i++)
                    indices.AddRange([start, start + i, start + i + 1]);
            }

            Vector3 Left(Vector2 p) => new(-w, p.X, p.Y);
            Vector3 Right(Vector2 p) => new(w, p.X, p.Y);
            Face([.. outline.Select(Right).Reverse()]);
            Face([.. outline.Select(Left)]);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                Face(Right(a), Right(b), Left(b), Left(a));
            }

            return new([.. vertices], [.. indices]);
        }

        /// <summary>Spawns the ramp in <paramref name="surface"/>, drawn with <paramref name="render"/>.</summary>
        public static Entity Spawn(World world, FrameHandle surface, RenderData? render = null) => world.Spawn(new()
        {
            Frame = surface,
            Pose = new(Foot, DQuaternion.Identity),
            Body = new(Shape: new CompoundShapeDesc(Shape()), Motion: BodyMotion.Static, Friction: 0),
            Render = render
        });
    }
}
