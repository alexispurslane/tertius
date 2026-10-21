using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The Earth system at its true sizes, at the June solstice, with the demo's station and a pad at the north pole, and
    /// views that show each part.
    /// <list type="bullet">
    /// <item>The Sun, earth, earth-surface and the Moon of <see cref="SunAndEarth"/>, at <see cref="EarthDate.JuneSolstice"/>.
    /// The Sun stands 23.44 degrees above the horizon of the north pole all day, and goes round it once a day.</item>
    /// <item>The demo's <c>station</c> (see <see cref="DemoStation"/>) on the elements of <see cref="DemoStation.Orbit"/>,
    /// but in <c>earth</c>, which does not turn: a polar circle 400 km up, over the north pole at
    /// <see cref="DemoStation.OverPadAt"/>. Its bubble lies outside earth-surface's, which ends 50 km up.</item>
    /// <item>The <c>pad</c>, a static box 12 by 1 by 12 m, lies on the ground at the north pole in earth-surface, with its
    /// top at <see cref="PadTop"/>, and the red <c>mast</c>, 10 m tall, stands on its corner at x = 5, z = -5. Both turn
    /// with the ground.</item>
    /// <item>The views, each a camera without a body, at the start:
    /// <c>sun-near</c>, in the Sun's frame 0.05 AU from the Sun's center on Earth's side, where the disc is 10.6 degrees
    /// across; <c>sun-from-earth</c>, in earth three Earth radii from its center toward the Sun, looking at the Sun, whose
    /// disc there is 0.53 degrees across, as from the ground; <c>earth-moon</c>, in earth, 50,000 km from the Moon on its
    /// sunward side, which shows the Moon and Earth together; <c>station-view</c>, in earth, 300 m from the station's center
    /// along its axis on the slot's side, looking 11 degrees below the station, so that the station stands over Earth's
    /// limb; <c>pad-view</c>, in earth-surface 22 m from the pole and 6 m up, looking past the pad toward the Sun; and
    /// <c>earth-far</c>, the camera at the start, in earth 50,000 km from Earth's center on its sunward side.</item>
    /// </list>
    /// </summary>
    public static class EarthSystem
    {
        /// <summary>The pad's half extents, in meters: 12 by 1 by 12 m.</summary>
        public static Vector3 PadHalf { get; } = new(6, 0.5f, 6);

        /// <summary>The height of the pad's top face above Earth's center, in meters, along earth-surface's +Y.</summary>
        public const double PadTop = EarthRadius + 0.5;

        public static SceneInstance Build(World world, IRenderResources resources)
        {
            var scene = new SceneInstance();
            EarthDate date = EarthDate.JuneSolstice;
            FrameHandle earth = SunAndEarth(world, resources, scene, date);
            FrameHandle sun = world.GetOwnedFrame(scene["sun"]);
            FrameHandle surface = world.GetOwnedFrame(scene["earth-surface"]);

            DemoStation.Spawn(world, earth, scene, resources);

            // The pad lies on the ground at the pole, the vertex of Earth's sphere on +Y.
            scene.Add("pad", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(new Double3(0, PadTop - PadHalf.Y, 0), DQuaternion.Identity),
                Body = new BodyDesc(new BoxShapeDesc(PadHalf), BodyMotion.Static),
                Render = Drawn(resources, Primitives.Box(PadHalf), new Vector4(0.75f, 0.74f, 0.7f, 1), PadHalf.Length()) with
                {
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                },
            }));
            var mastHalf = new Vector3(0.3f, 5, 0.3f);
            scene.Add("mast", world.Spawn(new SpawnDesc
            {
                Frame = surface,
                Pose = new DPose(new Double3(5, PadTop + mastHalf.Y, -5), DQuaternion.Identity),
                Render = Drawn(resources, Primitives.Box(mastHalf), new Vector4(0.8f, 0.15f, 0.1f, 1), mastHalf.Length()) with
                {
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                },
            }));

            // Where the Sun stands from Earth at the world's time, in earth's axes.
            KeplerOrbit earthOrbit = EarthOrbit(date);
            Double3 earthPlace = earthOrbit.StateAt(world.Time, SunMu).Position;
            Double3 toSun = earthOrbit.RotationAt(world.Time, SunMu).Conjugate().Rotate(-earthPlace).Normalized();

            void View(string id, FrameHandle frame, DPose pose)
            {
                scene.Add(id, world.Spawn(new SpawnDesc { Frame = frame, Pose = pose, Render = new RenderData() }));
                scene.AddView(id);
            }

            Double3 sunNear = earthPlace.Normalized() * (0.05 * AstronomicalUnit);
            View("sun-near", sun, LookingAt(sunNear, Double3.Zero));
            Double3 sunWatch = toSun * (3 * EarthRadius);
            View("sun-from-earth", earth, LookingAt(sunWatch, sunWatch + toSun));

            // The Moon and Earth: 50,000 km from the Moon, on its sunward side and beyond it, looking between the two.
            Double3 moon = MoonOrbit.StateAt(world.Time, EarthMu).Position, outward = moon.Normalized();
            Double3 sunward = (toSun - outward * Double3.Dot(toSun, outward)).Normalized();
            Double3 moonWatch = moon + outward * 30e6 + sunward * 40e6;
            View("earth-moon", earth, LookingAt(moonWatch, moonWatch + (moon - moonWatch).Normalized() + (-moonWatch).Normalized()));

            // The station over Earth's limb: 300 m out along its axis, looking 11 degrees below it, with the roof away from Earth.
            KeplerOrbit stationOrbit = DemoStation.Orbit;
            Double3 station = stationOrbit.StateAt(world.Time, EarthMu).Position, up = station.Normalized();
            Double3 axis = stationOrbit.RotationAt(world.Time, EarthMu).Rotate(Double3.UnitY);
            double down = 11 * Math.PI / 180;
            View("station-view", earth, new DPose(station + axis * 300, AttitudeAutopilot.Facing(-axis * Math.Cos(down) - up * Math.Sin(down), up)));

            // The pad from the ground, looking past it toward the Sun, which stands 23.44 degrees up: the eye stands 22 m
            // from the pole on the side away from the Sun. At the world's time, earth-surface has turned from earth's axes by
            // the date's ground, so the Sun's direction turns back by that spin into earth-surface's axes.
            Double3 toSunInSurface = date.Ground.RotationAt(world.Time).Conjugate().Rotate(toSun);
            Double3 sunAcross = new Double3(toSunInSurface.X, 0, toSunInSurface.Z).Normalized();
            Double3 padEye = -sunAcross * 22 + new Double3(0, EarthRadius + 6, 0);
            View("pad-view", surface, LookingAt(padEye, new Double3(0, EarthRadius + 8, 0)));

            Double3 farEye = (toSun + Double3.UnitX * 0.6).Normalized() * 5e7;
            View("earth-far", earth, LookingAt(farEye, Double3.Zero));
            world.Camera = scene["earth-far"];
            return scene;
        }
    }
}
