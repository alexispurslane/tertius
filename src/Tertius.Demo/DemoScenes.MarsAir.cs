using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// A Mars-like planet with a painted atmosphere (docs/plans/atmosphere.md), for the screenshots of its look.
    /// <list type="bullet">
    /// <item>The Sun of <see cref="SunRoot"/>, and <c>mars</c>, a frame 1.524 AU from it along +X whose origin is the
    /// planet's center. Its air is <see cref="Air"/>: a butterscotch sky, an ochre haze and a warm tint on the sunlight. The
    /// frame's bubble reaches 300 km above the ground, 27 scale heights, so a view above the air keeps the planet's
    /// record. Past that edge the record goes, and the ground's haze and tint go with it in one frame
    /// (docs/plans/atmosphere.md, "Not in this ticket").</item>
    /// <item>Two sites on the ground, each a flat plate 30 km wide with pillars along its +Z at 15 m to 12 km, which grow
    /// with their distance and stand side by side in the view: <c>day</c>, 50° from the point under the Sun, where the Sun stands 40° up, and <c>dusk</c>,
    /// 89° from it, where the Sun stands 1° up.</item>
    /// <item>The views, each a camera without a body: <c>day-view</c> and <c>dusk-view</c>, 1.7 m above their sites,
    /// looking along the pillars 4° down, and <c>above-view</c>, 120 km above the day site, looking along the same line 20°
    /// down, where the air is gone and the sky is black. <c>terminator-view</c>, 60 km above the dusk site, looks 12° down
    /// away from the Sun, across the terminator. <c>edge-inside-view</c> and <c>edge-outside-view</c> look 60° down at the
    /// day site from 5 km inside and outside the bubble's edge, the second in the Sun's frame, where no planet's air is on
    /// the camera's chain.</item>
    /// </list>
    /// </summary>
    public static class MarsAir
    {
        /// <summary>Mars's mean radius, in m.</summary>
        public const double Radius = 3_389_500;

        /// <summary>Mars's G·M, in m³/s².</summary>
        public const double Mu = 4.282837e13;

        /// <summary>The height over which the air thins by the factor e, in m: about 11 km.</summary>
        public const float ScaleHeight = 11_100;

        /// <summary>
        /// The air: Mars's surface density, 0.020 kg/m³, and the painted look. The Sun gives 586 W/m² at 1.524 AU, so the
        /// ochre ground with the Sun 40° up sends about 54 W/(m²·sr) of red, and about 80 with the sky's light. The sky is a
        /// little brighter, as a dusty sky by day is, and the haze a little paler. The haze lets through 1/e of the light per
        /// 15 km at the surface, so a column of the air straight up lets through about half. The plate's edge, 15 km away,
        /// still shows through about 37% of its light, so the day view keeps a sharp horizon.
        /// The tint keeps 88% of the green and 75% of the blue of the sunlight.
        /// </summary>
        public static MediumField Air =>
            MediumField.Planet(
                radius: Radius,
                surfaceDensity: 0.020f,
                scaleHeight: ScaleHeight,
                drag: DemoScenes.Air.Drag,
                skyRadiance: new(90, 60, 36),
                haze: new(new(90, 66, 46), Distance: 15_000),
                sunTint: new(1, 0.88f, 0.75f)
            );

        /// <summary>The angle of the day site from the point under the Sun, in rad: 50°, so the Sun stands 40° up.</summary>
        private const double DayAngle = 50 * Math.PI / 180;

        /// <summary>The angle of the dusk site from the point under the Sun, in rad: 89°, so the Sun stands 1° up.</summary>
        private const double DuskAngle = 89 * Math.PI / 180;

        /// <summary>The distances of the pillars from a site's center, in m.</summary>
        private static readonly double[] PillarDistances = [15, 60, 250, 1_000, 4_000, 12_000];

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            FrameHandle sun = SunRoot(world, resources);
            scene.Add("sun", sun.Entity);
            world.Ambient = SpaceAmbient;

            var planetPlace = new Double3(1.524 * AstronomicalUnit, 0, 0);
            Entity planet = world.Spawn(
                new()
                {
                    Frame = sun,
                    Pose = new(planetPlace, DQuaternion.Identity),
                    AsFrame = new(FrameBounds.Sphere((float)(Radius + 300e3)), GravityField.Point(Mu), Air),
                    Render = new RenderData
                    {
                        Mesh = resources.CreateMesh(Primitives.Sphere((float)Radius, 256, 128)),
                        Material = resources.CreateMaterial(
                            MaterialDesc.Color(new(x: 0.45f, y: 0.26f, z: 0.15f, w: 1), roughness: 0.9f)
                        ),
                        BoundingRadius = (float)Radius,
                        Flags = RenderFlags.Visible | RenderFlags.Celestial | RenderFlags.CastsShadow,
                    },
                }
            );
            scene.Add("mars", planet);
            FrameHandle mars = world.GetOwnedFrame(planet);

            SpawnSite(world: world, resources: resources, scene: scene, mars: mars, angle: DayAngle, name: "day");
            SpawnSite(world: world, resources: resources, scene: scene, mars: mars, angle: DuskAngle, name: "dusk");

            SpawnViews(world: world, scene: scene, sun: sun, mars: mars, planetPlace: planetPlace);
            world.Camera = scene["day-view"];
            return scene;
        }

        /// <summary>
        /// The views of the scene, each a camera without a body in <paramref name="mars"/>, or in <paramref name="sun"/>,
        /// whose axes are mars's, for the view outside mars's bubble. <paramref name="planetPlace"/> is mars's origin in the
        /// Sun's frame.
        /// </summary>
        private static void SpawnViews(
            World world,
            SceneInstance scene,
            FrameHandle sun,
            FrameHandle mars,
            Double3 planetPlace
        )
        {
            // A view at `eye` in the axes of the site at `angle`, looking along the site's `heading`, `down` rad below the
            // horizon there. It lies in mars, or, if `outsideMars`, at the same place in the Sun's frame, whose axes are
            // mars's.
            void View(string id, double angle, Double3 eye, double down, Double3 heading, bool outsideMars = false)
            {
                DQuaternion site = SiteRotation(angle);
                Double3 up = site.Rotate(Double3.UnitY),
                    along = site.Rotate(heading);
                DQuaternion facing = AttitudeAutopilot.Facing(along * Math.Cos(down) - up * Math.Sin(down), up);
                SpawnDesc view = outsideMars
                    ? ViewIn(sun, new(planetPlace + SitePlace(angle, eye), facing))
                    : ViewIn(mars, new(SitePlace(angle, eye), facing));
                scene.Add(id, world.Spawn(view));
                scene.AddView(id);
            }

            double gentle = 4 * Math.PI / 180;
            View(id: "day-view", angle: DayAngle, eye: new(0, 1.7, 0), down: gentle, heading: Double3.UnitZ);
            View(id: "dusk-view", angle: DuskAngle, eye: new(0, 1.7, 0), down: gentle, heading: Double3.UnitZ);
            View(
                id: "above-view",
                angle: DayAngle,
                eye: new(0, 120e3, 0),
                down: 20 * Math.PI / 180,
                heading: Double3.UnitZ
            );
            // The site's +X points away from the Sun, so this view looks across the terminator, 59 km ahead.
            View(
                id: "terminator-view",
                angle: DuskAngle,
                eye: new(0, 60e3, 0),
                down: 12 * Math.PI / 180,
                heading: Double3.UnitX
            );
            double steep = 60 * Math.PI / 180;
            View(id: "edge-inside-view", angle: DayAngle, eye: new(0, 295e3, 0), down: steep, heading: Double3.UnitZ);
            View(
                id: "edge-outside-view",
                angle: DayAngle,
                eye: new(0, 305e3, 0),
                down: steep,
                heading: Double3.UnitZ,
                outsideMars: true
            );
        }

        /// <summary>
        /// The axes of the site at <paramref name="angle"/> rad from the point under the Sun, in mars's axes: its +Y points
        /// up, away from the planet's center, and its +Z is mars's +Z, across the line to the Sun.
        /// </summary>
        private static DQuaternion SiteRotation(double angle) =>
            // The point under the Sun lies on −X, toward the Sun at the root's origin. A turn about Z by π/2 − angle takes +Y
            // to (−cos angle, sin angle, 0).
            DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2 - angle);

        /// <summary>A place in mars's axes from <paramref name="local"/> in the site's axes, whose y is the height above the ground.</summary>
        private static Double3 SitePlace(double angle, Double3 local)
        {
            DQuaternion site = SiteRotation(angle);
            return site.Rotate(local) + site.Rotate(Double3.UnitY) * Radius;
        }

        /// <summary>
        /// The site <paramref name="name"/> at <paramref name="angle"/> rad from the point under the Sun: a flat plate 30 km
        /// wide whose top is the ground, and the pillars along its +Z, the nearest on the right of a view along +Z.
        /// </summary>
        private static void SpawnSite(
            World world,
            IRenderResources resources,
            SceneInstance scene,
            FrameHandle mars,
            double angle,
            string name
        )
        {
            DQuaternion site = SiteRotation(angle);
            var plateHalf = new Vector3(15_000, 1, 15_000);
            scene.Add(
                $"{name}-ground",
                world.Spawn(
                    new()
                    {
                        Frame = mars,
                        Pose = new(SitePlace(angle, new(0, -plateHalf.Y, 0)), site),
                        Render = new RenderData
                        {
                            Mesh = resources.CreateMesh(Primitives.Box(plateHalf)),
                            Material = resources.CreateMaterial(
                                MaterialDesc.Color(new(x: 0.45f, y: 0.26f, z: 0.15f, w: 1), roughness: 0.9f)
                            ),
                            BoundingRadius = plateHalf.Length(),
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                        },
                    }
                )
            );
            for (int i = 0; i < PillarDistances.Length; i++)
            {
                double distance = PillarDistances[i];
                // Each pillar spans about the same angle from the site's center: 6% of its distance across, 12% tall.
                var half = new Vector3((float)(0.03 * distance), (float)(0.06 * distance), (float)(0.03 * distance));
                // From −0.25 to +0.25 of the distance across, so that no pillar hides another.
                double side = (i - 2.5) * 0.1;
                scene.Add(
                    $"{name}-pillar-{i}",
                    world.Spawn(
                        new()
                        {
                            Frame = mars,
                            Pose = new(SitePlace(angle, new(side * distance, half.Y, distance)), site),
                            Render = Drawn(
                                resources: resources,
                                mesh: Primitives.Box(half),
                                color: new(x: 0.3f, y: 0.22f, z: 0.18f, w: 1),
                                boundingRadius: half.Length()
                            ),
                        }
                    )
                );
            }
        }
    }
}
