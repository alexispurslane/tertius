using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The first part of the Blue Danube demo (docs/plans/blue-danube-demo.md, "The sequence", step 1): a launch pad and a
    /// boarding terminal on Earth's surface, a parked shuttle on the pad, and a passenger who walks through the terminal and
    /// boards the shuttle.
    /// <list type="bullet">
    /// <item>The world is the Earth system of <see cref="SunAndEarth"/> at the demo's <see cref="Date"/>
    /// (docs/plans/blue-danube-demo.md, "The demo in the Earth system"): the Sun is the root frame <c>sun</c>; <c>earth</c>
    /// goes round it on Earth's Kepler orbit and does not turn; <c>earth-surface</c> sits at Earth's center, turns once per
    /// sidereal day about the north pole, earth's +Y, draws Earth, casts Earth's shadow, and its bubble ends 50 km up; the
    /// <c>moon</c> goes round Earth in earth. The demo keeps the world's own sunlight and ambient light, with which every
    /// film was judged.</item>
    /// <item>The ground is the surface yard's slab (see <see cref="Floor"/>) in <c>earth-surface</c>: at the north pole, on
    /// the axis of the ground's turn, a static gray slab, the <c>floor</c>, 140 by 100 m, with its top at y = R =
    /// <see cref="EarthRadius"/>. +Y is up. Every body of the ground stands still in earth-surface and turns with it once a
    /// day about the pole.</item>
    /// <item>The <c>terminal</c> is a static gray box building, 10 m deep along X, 16 m wide along Z and 5 m tall, centered
    /// at x = 22 m (see <see cref="TerminalCenter"/>): walls and a roof 0.3 m thick, standing on the slab, which is the
    /// hall's floor. Its entrance faces +X, 2.5 m wide and 3 m tall above a sill 3 cm high, centered at z = 0. Its gate, of the same size, faces -X toward the pad, centered
    /// at z = -4 m. Benches stand along the +Z wall and a check-in counter by the gate. Two lamps under the roof light the
    /// hall.</item>
    /// <item>The <c>shuttle</c> (see <see cref="Shuttle"/>) stands parked at <see cref="ShuttleParked"/>, over the
    /// origin, unturned: its nose points along -Z and its hatch faces +X, toward the terminal. It rests on the launch
    /// ramp's bed, 2 cm above the slab. A dark gray <c>pad</c>, only drawn, as thick as that rise, marks the ground round
    /// it, and a service <c>tower</c> stands on its port side. The <c>seat</c> is drawn in blue, and four lamps on the
    /// pillars of the walls light the cabin (see <see cref="Shuttle.Lamps"/>). The main engine's <c>nozzle</c> sits in the
    /// tail pocket.</item>
    /// <item>The <c>person</c>, the capsule character of the other scenes, 1.8 m tall and of 70 kg, starts 9 m outside the
    /// entrance, at x = 36 m. Its <see cref="Walker"/> walks it through the entrance, round to the gate, out across the pad
    /// to <see cref="Shuttle.OutsideHatch"/>, through the hatch, and along the cabin to <see cref="Shuttle.SeatStand"/>,
    /// where it stops and stays (see <see cref="Path"/>). The person enters the shuttle's frame on the pad, about 9.4 m
    /// from the shuttle's axis, when its bounding sphere touches the bubble. The slab has a ghost in the shuttle's bubble
    /// (ARCHITECTURE.md, "Ghosts"), so the person walks on in the shuttle's frame on the same ground, and climbs the
    /// hatch's sill as a stair. The person's own row draws nothing, since a character does not turn. Its view starts
    /// facing the entrance, and its <see cref="WalkHeading"/> turns the view toward the walk. The attached
    /// <c>person-body</c>, a blue box with the dark <c>person-visor</c> on its front, faces the way of the view and bobs
    /// with each step (see <see cref="SpawnDrawnBody"/> and <see cref="Gait"/>).</item>
    /// <item>The views: <c>overview</c>, the camera at the start, high over the terminal's entrance side, sees the terminal
    /// and the shuttle; <c>terminal-camera</c>, high in the hall's corner by the gate, sees the walk through the hall;
    /// <c>apron-camera</c>, 3 m up off the shuttle's aft starboard quarter, between the shuttle and the terminal's gate side,
    /// sees the hatch, the walk across the pad and the person's step into the hatch, with the terminal off to its right;
    /// <c>cabin-camera</c>, in the shuttle's frame in the cabin's aft port corner, sees the seat and the hatch.</item>
    /// <item>The launch (docs/plans/blue-danube-demo.md, "The sequence", step 2): the <c>ramp</c> (see
    /// <see cref="LaunchRamp"/>) starts under the parked shuttle and rises at 30 degrees along -Z. The shuttle carries its
    /// <see cref="Shuttle.FlightControls"/> and its actuators (see <see cref="Shuttle.FitActuators"/>): the main engine and
    /// the jets are parts of the hull. The flight controls wait for an <see cref="Ignition"/> that a script posts to it. Then the
    /// shuttle unparks, slides up the ramp at three gravities, and flies to a circle 200 km up. A red <c>pen</c>, 14 cm
    /// long, hangs in a <see cref="Clip"/> on the pillar of the cabin's port wall at z = -1 m, at head height, aft of the seat (see
    /// <see cref="ClippedPen"/>). The clip holds it through the climb, and lets it go with a small push and a slow spin a
    /// second after the cabin becomes weightless in orbit, so that the pen drifts into the middle of the cabin. The person
    /// is a <see cref="Passenger"/>: its belt holds it at <see cref="Shuttle.SeatStand"/> through the climb, and in the
    /// weightless cabin it floats to the drifting pen and takes it in its hand, where and as the pen floats. Its
    /// <see cref="Disembark"/> and its <see cref="BoxPlay"/> wait for the orders of the exit and of the coda.</item>
    /// <item>More views: <c>pad-camera</c>, 6 m up, 32 m to starboard of the shuttle and 30 m aft of it, sees the whole
    /// parked shuttle, with the tower behind it and the ramp rising beyond it; <c>ramp-camera</c> hovers 25 m above the
    /// ground, 140 m to starboard of the ramp, and sees all of it; <c>ramp-top-camera</c> hovers 105 m above the ground,
    /// 90 m to starboard of the ramp's upper end, and sees that end and the first 150 m of the flight past it;
    /// <c>chase-camera</c> rides in the shuttle's frame, 40 m from its middle on its starboard quarter, 30 degrees to
    /// starboard of astern and 12 degrees up, and looks at it, so that it sees the seated person through the starboard
    /// glass;
    /// <c>climb-camera</c> rides in the shuttle's frame, 50 m from it, high above and behind its roof, and looks down at it
    /// 55 degrees below its nose line, so that the ground stays in the view while the shuttle climbs steeply.</item>
    /// <item>Views of the pen, in the shuttle's frame: <c>pen-camera</c>, near the roof, 1.5 m to port of the cabin's
    /// middle line and just aft of the clip, looks down at the pen's way from the port wall, from 1.5 to 2 m. The pen
    /// shows most of its length there, in the clip and while it tumbles. The view also holds the seat and the start of the
    /// person's way from it. <c>hand-camera</c>, 0.9 m above the floor and 0.5 m to starboard of the middle line, looks up
    /// across at the place where the person's hand takes the pen, 1.5 m away, about 1.8 m above the floor and 2.1 m from
    /// the port wall, and sees the hand close on the pen from above and the pen in the hand.</item>
    /// <item>The <c>follow-camera</c> is a <see cref="ChaseCamera"/> attached to the person: it keeps 3.5 m behind the
    /// person's eye along the way the person walks, 0.8 m above it, and looks toward the point 2 m ahead of it, tilted down as
    /// far as the person's whole box needs to stay in the picture. It goes with the person into the shuttle's frame, and
    /// stops short of the terminal's walls and the cabin's. The <c>slide-camera</c> is attached to the person too, for the
    /// slide of the exit (see <see cref="StationExit.SpawnSlideCamera"/>).</item>
    /// <item>The shuttle has a glass canopy (see <see cref="Shuttle.Glass"/>): a band of glass along both sides from 0.7 m
    /// above the cabin floor, over the roof's bevels and the roof, and the front above the sill. It has 40 panes, each with
    /// its scene id, such as <c>pane-nose</c>, <c>pane-port-7</c> and <c>pane-roof-4</c>, named by their edge and their
    /// bay from the nose. Its flight controls also carry the jets of the <see cref="ShuttleJetControl"/>.</item>
    /// <item>Views of the rendezvous, still in the shuttle's frame: <c>window-camera</c>, 1.2 m behind the front's inner face
    /// at eye height, looks out along the nose through <c>pane-nose</c>; <c>bow-camera</c>, 30 m ahead of the nose and 5 m above it, looks aft, 30 degrees to
    /// port of astern, at the shuttle and past it; <c>port-window-camera</c>, 0.6 m inside the port wall at z = 4.2 m, at eye
    /// height, looks out through <c>pane-port-7</c>, 20 degrees aft of abeam, where the station passes
    /// from 33 to 9 degrees aft of abeam in the brake; <c>cockpit-camera</c>, aft of the seat, to starboard of it and above the
    /// head of a person at the seat, looks down past the seat at <c>pane-nose</c>. From starboard it sees the pen in the
    /// person's right hand both when the person floats upside down and when a burn has tipped it with its head toward the
    /// nose.</item>
    /// <item>The <c>station</c> (see <see cref="DemoStation"/>), a wheel 202 m across that turns about its axis, flies in
    /// <c>earth</c> on a circle 400 km up in the launch plane, and passes over the pad at <see cref="DemoStation.OverPadAt"/>.
    /// It spawns before the shuttle, whose <see cref="RendezvousAutopilot"/> flies to it after a <see cref="Rendezvous"/>
    /// order. Its view <c>rim-camera</c> turns with it.</item>
    /// <item>The climb: the shuttle's <see cref="AscentAutopilot"/> reads in <c>earth</c>, through its
    /// <see cref="LaunchPlane"/>. The shuttle leaves earth-surface through the top of its bubble during the climb, 178.4 km
    /// up, where the engine's exit margin ends, and flies on in earth.</item>
    /// <item><c>sky-camera</c>, in earth-surface, stands to starboard of the ramp ahead of the parked nose, and looks aft and up
    /// toward the low Sun and the crescent Moon over the parked shuttle (see <see cref="SkyCamera"/>).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>A character has no sitting pose for now, so the person stands at <see cref="Shuttle.SeatStand"/>, upright on
    /// the cabin floor just aft of the seat. The launch step of the demo (docs/plans/blue-danube-demo.md, "The sequence",
    /// step 2) says that the character leaves its seat: that means that the person floats away from
    /// <see cref="Shuttle.SeatStand"/>.</para>
    /// <para>Under the climb's thrust, the engine turns a character's up against the cabin's gravity, so the belted person
    /// tips toward the nose, and it floats that way in orbit, because a weightless character keeps its last up. Ticket
    /// ter-yrfa will let a weightless character turn its own up, which will right the person in orbit.</para>
    /// </remarks>
    public static class DemoSurface
    {
        /// <summary>
        /// How much higher than the ramp's top the shuttle climbs before its autopilot steers, in meters, so that the
        /// shuttle is clear of the ramp.
        /// </summary>
        public const double ClimbClearance = 30;

        /// <summary>
        /// The ground's turn at the departure, the angle in radians about earth's +Y from earth's axes to the ground's axes:
        /// 142.6 degrees (docs/plans/blue-danube-demo.md, "The station's plane"). At the June solstice the Sun's direction
        /// across the pad's up is earth's -Z. This turn sets the ramp 142.6 degrees from it, so that the Sun stands behind
        /// the launch and 37.4 degrees out of the launch plane on the slot's side. So the station's orbit keeps a β of 33.9
        /// degrees, and the Sun meets the slot's face at 56 degrees from the station's axis.
        /// </summary>
        public const double GroundTurnAtDeparture = 142.6 * Math.PI / 180;

        /// <summary>
        /// The departure, in seconds of world time: 20 s, <see cref="DemoStation.DepartureLead"/> before the station passes
        /// over the pad at <see cref="DemoStation.OverPadAt"/>. At the departure the ground stands turned by
        /// <see cref="GroundTurnAtDeparture"/>, so the ramp lies in the station's plane.
        /// </summary>
        public const double DepartureAt = DemoStation.OverPadAt - DemoStation.DepartureLead;

        /// <summary>The pen's mass, in kilograms.</summary>
        public const float PenMass = 0.02f;

        /// <summary>How far behind the person's eye the <c>follow-camera</c> keeps, in meters (see <see cref="ChaseCamera"/>).</summary>
        public const double FollowBehind = 3.5;

        /// <summary>
        /// How far above the person's eye the <c>follow-camera</c> keeps, in meters: 2.45 m above the floor, below the top of
        /// the terminal's doorways, 3.03 m above the slab.
        /// </summary>
        public const double FollowAbove = 0.8;

        /// <summary>How far ahead of the person's eye the <c>follow-camera</c> looks, in meters.</summary>
        public const double FollowAhead = 2;

        // The thickness of the terminal's walls and roof, in meters.
        private const float TerminalWall = 0.3f;

        // The height of the hall's drawn floor and of the doorways' sills above the slab, in meters. A doorway of a drawn
        // box must lie clear of the face's edges, so each sill stands a little above the drawn floor.
        private const float HallFloor = 0.01f;
        private const float Sill = 0.03f;

        /// <summary>Where the slab's top lies under the terminal's center: 22 m along +X, on the ground.</summary>
        public static Double3 TerminalCenter { get; } = new(22, EarthRadius, 0);

        /// <summary>The terminal's half extents, in meters: 10 m deep along X, 5 m tall and 16 m wide along Z.</summary>
        public static Vector3 TerminalHalf { get; } = new(5, 2.5f, 8);

        /// <summary>
        /// The normal of the launch plane, a unit vector in earth's axes, which do not turn: about (0.795, 0, 0.607), -X of
        /// the departure axes, the ground's axes at <see cref="DepartureAt"/>. The launch plane is the plane through Earth's
        /// center that holds the pad's up, +Y, and the ramp's incline, <see cref="LaunchRamp.Up"/>, at the departure. The
        /// normal is the plane's prograde normal, the pad's up across the incline, so that the normal across the up of a
        /// ship in the plane points the way that the ship flies. The ramp, the climb and the station share it: the ascent
        /// autopilot steers into the plane, and the parking orbit and the station's orbit lie in it. The incline lies in the
        /// ground's axes, so the departure axes' turn takes the normal into earth's.
        /// </summary>
        public static Double3 LaunchPlaneNormal { get; } =
            DQuaternion.FromAxisAngle(Double3.UnitY, GroundTurnAtDeparture)
                .Rotate(Double3.Cross(Double3.UnitY, LaunchRamp.Up)).Normalized();

        /// <summary>
        /// The demo's date: the June solstice, with the ground turned at the world's time 0 so that it stands turned by
        /// <see cref="GroundTurnAtDeparture"/> at <see cref="DepartureAt"/>.
        /// </summary>
        public static EarthDate Date { get; } = EarthDate.JuneSolstice with
        {
            GroundTurn = GroundTurnAtDeparture - SiderealRate * DepartureAt
        };

        /// <summary>The pen's half extents, in meters: 14 cm long along its own Z, and 12 mm thick.</summary>
        public static Vector3 PenHalf { get; } = new(0.006f, 0.006f, 0.07f);

        /// <summary>
        /// Where the clip holds the pen, in the shuttle's axes: on the port wall's pillar at z = -1 m, 1.5 m above the cabin
        /// floor, 2.75 m aft of <see cref="Shuttle.SeatStand"/>, with 1 cm between the pen and the wall. Its length lies along
        /// the cabin, within the pillar's 0.2 m, so the pen never hangs in front of glass.
        /// </summary>
        public static DPose PenClip { get; } = new(new(-Shuttle.Cabin.X + PenHalf.X + 0.01, -Shuttle.Cabin.Y + 1.5, -1),
            DQuaternion.Identity);

        /// <summary>
        /// The push with which the clip lets the pen go, in m/s, in the shuttle's axes: away from the port wall, toward the
        /// middle of the cabin, and a little up.
        /// </summary>
        public static Double3 PenPush { get; } = new(0.2, 0.03, 0);

        /// <summary>
        /// The spin with which the clip lets the pen go, in rad/s, in the shuttle's axes: 0.5 rad/s, a turn in about 13 s,
        /// end over end about an axis across the pen. The pen's two short sides are equal, so every axis across it is a
        /// principal axis, and the spin stays steady. Its ends move at 3.5 cm/s, slower than the push carries the pen from
        /// the wall, so the pen never swings into it.
        /// </summary>
        public static Vector3 PenSpin { get; } = new(0.3f, 0.4f, 0);

        /// <summary>
        /// Where the shuttle stands parked, in <c>earth-surface</c>: over the north pole, on the axis of the ground's turn,
        /// unturned, with its hull's bottom on the launch ramp's bed (<see cref="LaunchRamp.BedTop"/>).
        /// </summary>
        public static DPose ShuttleParked { get; } =
            new(new(0, LaunchRamp.BedTop + Shuttle.Hull.Y, 0), DQuaternion.Identity);

        /// <summary>
        /// The pose of <c>cabin-camera</c> in the shuttle's frame: in the cabin's aft port corner, looking at the seat and the
        /// hatch.
        /// </summary>
        public static DPose CabinCamera { get; } = LookingAt(new(-2.3, 1.2, 6.8), new(1, -1.3, -4.5));

        /// <summary>The eye of the person, relative to its center, in its axes: 1.65 m above its feet.</summary>
        public static Vector3 Eye { get; } = new(0, 0.75f, 0);

        /// <summary>
        /// The half extents of the box round the drawn person, in meters, in the person's axes: 0.6 m wide and deep, 1.8 m
        /// tall, round the person's center. The drawn body and its visor lie inside it at every heading and lift (see
        /// <see cref="BodyHalf"/>). The cameras that keep the person in the picture and the checks of the films take this box.
        /// </summary>
        public static Vector3 PersonHalf { get; } = new(0.3f, 0.9f, 0.3f);

        /// <summary>
        /// The half extents of the person's drawn body, in meters, in the body's axes: 0.5 m wide, 0.28 m deep and 1.76 m
        /// tall. It is wider than it is deep, so a viewer sees which way it faces, and its footprint's corners lie 0.287 m
        /// from its middle, inside <see cref="PersonHalf"/> at every heading.
        /// </summary>
        public static Vector3 BodyHalf { get; } = new(0.25f, 0.88f, 0.14f);

        /// <summary>
        /// Where the drawn body's center rests relative to the person's center, in meters: 2 cm lower, so that its bottom lies
        /// at the capsule's bottom, and its top 4 cm below the top of <see cref="PersonHalf"/>, room for a step's rise.
        /// </summary>
        public static Vector3 BodyCenter { get; } = new(0, -0.02f, 0);

        /// <summary>
        /// The half extents of the visor on the drawn body's front, in meters, in the body's axes: 0.34 m wide, 10 cm tall and
        /// 8 cm deep, so that it shows in the films from 10 m away. Its outer corners lie 0.278 m from the body's middle.
        /// </summary>
        public static Vector3 VisorHalf { get; } = new(0.17f, 0.05f, 0.04f);

        /// <summary>
        /// Where the visor's center lies relative to the drawn body's center, in meters, in the body's axes: at the eye's
        /// height, with its back on the body's front face, -Z.
        /// </summary>
        public static Vector3 VisorCenter { get; } = new(0, 0.77f, -0.18f);

        /// <summary>Where the person starts: the center of its capsule, 9 m outside the entrance, 0.05 m above the slab's top.</summary>
        public static Double3 PersonStart { get; } = new(36, EarthRadius + 0.95, 0);

        // In the terminal's axes, with the origin on the slab's top under its center. The doorways are 2.5 m wide and 3 m
        // tall above their sills.
        private static BoundingBox Entrance { get; } = Doorway(TerminalHalf.X - TerminalWall, TerminalHalf.X, 0);
        private static BoundingBox Gate { get; } = Doorway(-TerminalHalf.X, -TerminalHalf.X + TerminalWall, -4);

        // The furniture in the terminal's axes: two benches along the +Z wall, and the check-in counter on the +Z side of
        // the gate, clear of the way from the entrance to the gate.
        private static readonly BoundingBox[] Furniture =
        [
            new(new(-3, 0, 6), new(-0.5f, 0.45f, 6.8f)),
            new(new(0.5f, 0, 6), new(3, 0.45f, 6.8f)),
            new(new(-3.6f, 0, 1.5f), new(-2.8f, 1.1f, 4.5f))
        ];

        /// <summary>Where <c>sky-camera</c> stands, in earth-surface: to starboard of the ramp, 40 m ahead of the parked nose, 8 m up.</summary>
        public static Double3 SkyEye { get; } = new(6, EarthRadius + 8, -40);

        /// <summary>
        /// The loose <paramref name="pen"/> in <paramref name="cabin"/>, the shuttle's frame, at <see cref="PenClip"/>, with
        /// a <see cref="Clip"/> that holds it there while the cabin has weight and lets it go with <see cref="PenPush"/> and
        /// <see cref="PenSpin"/>.
        /// </summary>
        public static SpawnDesc ClippedPen(CrateSpec pen, FrameHandle cabin) =>
            pen.Spawn(cabin, PenClip, Double3.Zero, new Clip(PenClip, PenPush, PenSpin));

        private static BoundingBox Doorway(float x0, float x1, float z) =>
            new(new(x0, Sill, z - 1.25f), new(x1, Sill + 3, z + 1.25f));

        /// <summary>
        /// The boxes of the terminal's static shape, in its axes, with the origin on the slab's top under
        /// <see cref="TerminalCenter"/>: the roof, the four walls round the two doorways with their jambs and lintels, and
        /// the furniture.
        /// </summary>
        public static IEnumerable<BoundingBox> TerminalShape()
        {
            Vector3 half = TerminalHalf;
            float inner = half.Z - TerminalWall;
            float top = 2 * half.Y - TerminalWall;
            yield return new(new(-half.X, top, -half.Z), new(half.X, 2 * half.Y, half.Z));
            yield return new(new(-half.X, 0, -half.Z), new(half.X, top, -inner));
            yield return new(new(-half.X, 0, inner), new(half.X, top, half.Z));
            foreach (BoundingBox part in AroundDoorway(
                         new(new(half.X - TerminalWall, 0, -inner), new(half.X, top, inner)), Entrance))
                yield return part;
            foreach (BoundingBox part in AroundDoorway(
                         new(new(-half.X, 0, -inner), new(-half.X + TerminalWall, top, inner)), Gate))
                yield return part;
            foreach (BoundingBox box in Furniture)
                yield return box;
        }

        /// <summary>The drawn terminal: the outer box and the hall seen from inside, joined through the doorways, and the furniture.</summary>
        private static MeshData TerminalMesh()
        {
            Vector3 half = TerminalHalf;
            Opening[] doorways = [Opening.ThroughX(Entrance), Opening.ThroughX(Gate)];
            var hall = new BoundingBox(new(-half.X + TerminalWall, HallFloor, -half.Z + TerminalWall),
                new(half.X - TerminalWall, 2 * half.Y - TerminalWall, half.Z - TerminalWall));
            BoxMesh mesh = new BoxMesh()
                .Box(new(new(-half.X, 0, -half.Z), new(half.X, 2 * half.Y, half.Z)), openings: doorways)
                .Box(hall, inward: true, openings: doorways);
            foreach (Opening doorway in doorways)
                mesh.Sides(doorway);
            foreach (BoundingBox box in Furniture)
                mesh.Box(box);
            return mesh.Build();
        }

        /// <summary>
        /// Spawns the drawn body of <paramref name="person"/>, as <c>person-body</c>, and its visor, as <c>person-visor</c>, in
        /// <paramref name="scene"/>. The person's own row draws nothing, since a character does not turn. The body is a box of
        /// <see cref="BodyHalf"/> in the person's color, attached to the person, and its <see cref="Gait"/> turns it as the
        /// person's view and bobs it with each step. The visor is a dark box of <see cref="VisorHalf"/>, attached to the body
        /// at <see cref="VisorCenter"/>, so that a viewer sees the body's front. Both cast shadows.
        /// </summary>
        public static Entity SpawnDrawnBody(World world, SceneInstance scene, Entity person, IRenderResources resources)
        {
            var heading = Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                (float)WalkHeading.Yaw(world.GetRender(person).ViewOffset.Rotation));
            Entity body = world.Spawn(new()
            {
                AttachTo = person,
                AttachOffset = new(Vector3.Transform(BodyCenter, heading), heading),
                Render = Drawn(resources, Primitives.Box(BodyHalf), new(0.2f, 0.45f, 0.8f, 1), boundingRadius: 1),
                Components = [new Gait(BodyCenter)]
            });
            scene.Add("person-body", body);
            scene.Add("person-visor", world.Spawn(new()
            {
                AttachTo = body,
                AttachOffset = new(VisorCenter, Quaternion.Identity),
                Render = Drawn(resources, Primitives.Box(VisorHalf), new(0.06f, 0.06f, 0.08f, 1),
                    boundingRadius: VisorHalf.Length())
            }));
            return body;
        }

        /// <summary>
        /// The walker's path: in <paramref name="surface"/>, just inside the entrance, before the gate and just outside it;
        /// then in <paramref name="cabin"/>, the shuttle's frame, 3 m outside the hatch, just inside it, and the place at the
        /// seat. The waypoints on the ship are in its frame, so the path stays right however the shuttle stands.
        /// </summary>
        public static Waypoint[] Path(FrameHandle surface, FrameHandle cabin) =>
        [
            new(surface, new(TerminalCenter.X + 2, EarthRadius + 0.9, 0)),
            new(surface, new(TerminalCenter.X - 2.5, EarthRadius + 0.9, -4)),
            new(surface, new(TerminalCenter.X - 8, EarthRadius + 0.9, -4)),
            new(cabin, Shuttle.OutsideHatch),
            new(cabin, Shuttle.InsideHatch),
            new(cabin, Shuttle.SeatStand)
        ];

        /// <summary>
        /// The pose of <c>sky-camera</c> in earth-surface at the world's time: at <see cref="SkyEye"/>, looking toward the
        /// middle between the Sun's direction and the Moon's, 10 degrees lower, with no roll. Both directions are turned
        /// from earth's axes into the ground's by the date's ground, as the pad view of the earth-system scene does. Seen
        /// from the pad, the Sun and the Moon stand aft of the parked shuttle, 46 degrees apart, so the view takes in both
        /// and the shuttle below them.
        /// </summary>
        private static DPose SkyCamera(World world)
        {
            KeplerOrbit earthOrbit = EarthOrbit(Date);
            Double3 toSun = earthOrbit.RotationAt(world.Time, SunMu).Conjugate()
                .Rotate(-earthOrbit.StateAt(world.Time, SunMu).Position).Normalized();
            DQuaternion intoGround = Date.Ground.RotationAt(world.Time).Conjugate();
            Double3 sun = intoGround.Rotate(toSun);
            Double3 moon = (intoGround.Rotate(MoonOrbit.StateAt(world.Time, EarthMu).Position) - SkyEye).Normalized();
            Double3 middle = (sun + moon).Normalized();
            double lower = 10 * Math.PI / 180;
            double height = Math.Asin(middle.Y) - lower;
            Double3 across = new Double3(middle.X, 0, middle.Z).Normalized();
            return LookingAt(SkyEye, SkyEye + across * Math.Cos(height) + Double3.UnitY * Math.Sin(height));
        }

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            // The demo's light is the renderer's exposure that every film was judged with: the world's own sunlight and
            // ambient light, not the brighter sun and the dim space of the orbit scene, which SunAndEarth sets.
            (float sunIntensity, AmbientLight ambient) = (world.SunIntensity, world.Ambient);
            FrameHandle earth = SunAndEarth(world, resources, scene, Date);
            (world.SunIntensity, world.Ambient) = (sunIntensity, ambient);
            FrameHandle surface = world.GetOwnedFrame(scene["earth-surface"]);
            Floor(world, resources, scene, surface, new(70, 0.5f, 50));
            scene.Add("ramp",
                LaunchRamp.Spawn(world, surface,
                    Drawn(resources, LaunchRamp.Mesh(), new(0.6f, 0.6f, 0.58f, 1),
                        boundingRadius: LaunchRamp.Length + 6)));

            MeshId lampMesh = resources.CreateMesh(Primitives.Box(new(0.6f, 0.05f, 0.6f)));
            MaterialId lampMaterial =
                resources.CreateMaterial(new(new(0, 0, 0, 1), SpecularIntensity: 0, Emissive: new(6, 5.4f, 4.5f)));

            SpawnDesc Lamp(FrameHandle frame, Double3 position, float range) => new()
            {
                Frame = frame,
                Pose = new(position, DQuaternion.Identity),
                Render = new RenderData
                {
                    Mesh = lampMesh,
                    Material = lampMaterial,
                    BoundingRadius = 0.85f,
                    Flags = RenderFlags.Visible,
                    Light = new PointLightData(new(new(0, -0.1f, 0), Quaternion.Identity), new(1, 0.9f, 0.75f),
                        Intensity: 100, Range: range)
                }
            };

            Vector3 half = TerminalHalf;
            scene.Add("terminal", world.Spawn(new()
            {
                Frame = surface,
                Pose = new(TerminalCenter, DQuaternion.Identity),
                Body = new(
                    new CompoundShapeDesc([
                        .. TerminalShape().Select(box =>
                            new CompoundChild(new BoxShapeDesc(box.Extents), new(box.Center, Quaternion.Identity)))
                    ]), BodyMotion.Static),
                // The terminal's origin lies on the slab, so its farthest corner is a whole height up.
                Render = Drawn(resources, TerminalMesh(), new(0.72f, 0.72f, 0.7f, 1),
                    boundingRadius: MathF.Ceiling((half with { Y = 2 * half.Y }).Length()))
            }));
            double lampHeight = EarthRadius + 2 * half.Y - TerminalWall - 0.05;
            world.Spawn(Lamp(surface, new(TerminalCenter.X + 1, lampHeight, -3), range: 12));
            world.Spawn(Lamp(surface, new(TerminalCenter.X + 1, lampHeight, 3), range: 12));

            // The pad is as thick as the bed's rise, so the parked hull stands on its drawn top.
            var padHalf = new Vector3(10, (float)LaunchRamp.BedRise / 2, 14);
            scene.Add("pad", world.Spawn(new()
            {
                Frame = surface,
                Pose = new(new(0, EarthRadius + padHalf.Y, 0), DQuaternion.Identity),
                Render = Drawn(resources, Primitives.Box(padHalf), new(0.25f, 0.25f, 0.24f, 1),
                    boundingRadius: MathF.Ceiling(padHalf.Length()))
            }));
            var towerHalf = new Vector3(1.5f, 8, 1.5f);
            scene.Add("tower", world.Spawn(new()
            {
                Frame = surface,
                Pose = new(new(-11, EarthRadius + towerHalf.Y, -4), DQuaternion.Identity),
                Body = new(new BoxShapeDesc(towerHalf), BodyMotion.Static),
                Render = Drawn(resources, Primitives.Box(towerHalf), new(0.55f, 0.3f, 0.2f, 1),
                    boundingRadius: towerHalf.Length())
            }));

            // The station spawns before the shuttle, whose rendezvous autopilot takes the station's entity.
            Entity station = DemoStation.Spawn(world, earth, scene, resources);
            Entity shuttle = Shuttle.Park(world, surface, ShuttleParked, Shuttle.DrawnHull(resources),
                Shuttle.FlightControls(LaunchRamp.Height + ClimbClearance, new(earth, LaunchPlaneNormal), station));
            scene.Add("shuttle", shuttle);
            Shuttle.FitActuators(world, shuttle);
            FrameHandle cabin = world.GetOwnedFrame(shuttle);
            Vector3 seatHalf = Shuttle.Seat.Extents;
            scene.Add("seat", world.Spawn(new()
            {
                Frame = cabin,
                Pose = new(Shuttle.Seat.Center, DQuaternion.Identity),
                Render = Drawn(resources, Primitives.Box(seatHalf), new(0.15f, 0.25f, 0.6f, 1),
                    boundingRadius: seatHalf.Length())
            }));
            Shuttle.Panes(world, cabin, scene, resources);
            Shuttle.SpawnLamps(world, cabin, scene, resources);
            Shuttle.SpawnNozzle(world, cabin, scene, resources);

            // The capsule is 1.8 m tall, and its center rests 0.9 m above the slab. Its eye is a view, which the pen in its
            // hand follows.
            var penSpec = new CrateSpec(PenHalf, PenMass, resources.CreateMesh(Primitives.Box(PenHalf)),
                resources.CreateMaterial(MaterialDesc.Color(new(0.85f, 0.1f, 0.08f, 1))));
            Entity pen = world.Spawn(ClippedPen(penSpec, cabin));
            scene.Add("pen", pen);
            // The person starts facing its first waypoint, along -X, toward the terminal's entrance: a quarter turn about +Y.
            var personRender = new RenderData
                { ViewOffset = new(Eye, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)) };
            Entity person = world.Spawn(new()
            {
                Frame = surface,
                Pose = new(PersonStart, DQuaternion.Identity),
                Body = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: YardPersonMass),
                Render = personRender,
                Components =
                [
                    new Walker(Path(surface, cabin)), new Passenger(new(cabin, Shuttle.SeatStand), pen),
                    StationExit.Disembark(cabin, world.GetOwnedFrame(station)),
                    WheelCoda.Play(scene, world.GetOwnedFrame(station)), new WalkHeading()
                ]
            });
            scene.Add("person", person);
            SpawnDrawnBody(world, scene, person, resources);
            // The person starts its walk along -X, toward the entrance, so the camera starts behind it along +X.
            scene.Add("follow-camera", world.Spawn(new()
            {
                AttachTo = person,
                AttachOffset = new(Eye + new Vector3((float)FollowBehind, (float)FollowAbove, 0), Quaternion.Identity),
                Render = new RenderData(),
                Components = [new ChaseCamera(FollowBehind, FollowAbove, FollowAhead, PersonHalf, world.VerticalFov)]
            }));
            scene.Add("slide-camera", StationExit.SpawnSlideCamera(world, person, world.GetOwnedFrame(station)));

            Entity overview = world.Spawn(new()
            {
                Frame = surface,
                Pose = LookingAt(new(48, EarthRadius + 16, 28), new(12, EarthRadius + 1, -1)),
                Render = new RenderData()
            });
            scene.Add("overview", overview);
            scene.Add("terminal-camera", world.Spawn(new()
            {
                Frame = surface,
                Pose = LookingAt(new(TerminalCenter.X - 4.2, EarthRadius + 3.6, 7.2),
                    new(TerminalCenter.X + 1, EarthRadius + 1, -1.5)),
                Render = new RenderData()
            }));
            scene.Add("apron-camera", world.Spawn(new()
            {
                Frame = surface,
                Pose = LookingAt(new(13, EarthRadius + 3, 10), new(5, EarthRadius + 1.2, 0.5)),
                Render = new RenderData()
            }));
            scene.Add("cabin-camera",
                world.Spawn(new() { Frame = cabin, Pose = CabinCamera, Render = new RenderData() }));

            scene.Add("pad-camera", world.Spawn(new()
            {
                Frame = surface,
                Pose = LookingAt(new(32, EarthRadius + 6, 30), new(0, EarthRadius + 12, -30)),
                Render = new RenderData()
            }));
            scene.Add("ramp-camera", world.Spawn(new()
            {
                Frame = surface,
                Pose = LookingAt(new(140, EarthRadius + 25, -95), new(0, EarthRadius + 55, -110)),
                Render = new RenderData()
            }));
            scene.Add("ramp-top-camera", world.Spawn(new()
            {
                Frame = surface,
                Pose = LookingAt(new(90, EarthRadius + 105, -200), new(0, EarthRadius + 145, -262)),
                Render = new RenderData()
            }));
            scene.Add("chase-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(new(19.5, 8.5, 34), Double3.Zero),
                Render = new RenderData()
            }));
            scene.Add("climb-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(new(0, 41, 29), Double3.Zero),
                Render = new RenderData()
            }));
            scene.Add("pen-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(new(-1.5, 1.2, -0.45), new(-1.5, -0.3, -1.6)),
                Render = new RenderData()
            }));
            scene.Add("hand-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(new(0.5, -0.9, -0.7), new(-0.7, 0.05, -0.95)),
                Render = new RenderData()
            }));
            // The views of the rendezvous (docs/plans/blue-danube-demo.md, "The shuttle's windows and the cameras"), still in
            // the shuttle's frame.
            double eye = -Shuttle.Cabin.Y + 1.65;
            scene.Add("window-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = new(new(0, eye, -Shuttle.Cabin.Z + 1.2), DQuaternion.Identity),
                Render = new RenderData()
            }));
            var bow = new Double3(0, 5, -Shuttle.Hull.Z - 30);
            double portOfAstern = 30 * Math.PI / 180;
            scene.Add("bow-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(bow, bow + new Double3(-Math.Sin(portOfAstern), 0, Math.Cos(portOfAstern))),
                Render = new RenderData()
            }));
            var portWindow = new Double3(-Shuttle.Cabin.X + 0.6, eye, 4.2);
            double aftOfAbeam = 20 * Math.PI / 180;
            scene.Add("port-window-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(portWindow, portWindow + new Double3(-Math.Cos(aftOfAbeam), 0, Math.Sin(aftOfAbeam))),
                Render = new RenderData()
            }));
            scene.Add("cockpit-camera", world.Spawn(new()
            {
                Frame = cabin,
                Pose = LookingAt(new(1.9, 0.9, -1.4), new(0.7, -1.3, -5.2)),
                Render = new RenderData()
            }));

            scene.Add("sky-camera",
                world.Spawn(new() { Frame = surface, Pose = SkyCamera(world), Render = new RenderData() }));

            scene.AddView("overview");
            scene.AddView("sky-camera");
            scene.AddView("terminal-camera");
            scene.AddView("apron-camera");
            scene.AddView("cabin-camera");
            scene.AddView("pad-camera");
            scene.AddView("ramp-camera");
            scene.AddView("ramp-top-camera");
            scene.AddView("chase-camera");
            scene.AddView("climb-camera");
            scene.AddView("pen-camera");
            scene.AddView("hand-camera");
            scene.AddView("follow-camera");
            scene.AddView("window-camera");
            scene.AddView("bow-camera");
            scene.AddView("port-window-camera");
            scene.AddView("cockpit-camera");
            scene.AddView("rim-camera");
            scene.AddView("bay-camera");
            scene.AddView("hold-camera");
            world.Camera = overview;
            return scene;
        }
    }
}
