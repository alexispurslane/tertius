using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

internal sealed partial class RenderSnapshotBuilder
{
    /// <summary>
    /// Gives <paramref name="snapshot"/> the air that the camera looks through (docs/plans/atmosphere.md, "The snapshot
    /// build"): the planet medium of the atmosphere's frame (see <see cref="AtmosphereFrame"/>), with the frame's origin
    /// placed as an instance is, and the planet's sun among the snapshot's lights. A world without a planet medium leaves
    /// the snapshot without an atmosphere. It runs after the frame poses, which it reads, and after the lights.
    /// </summary>
    private void BuildAtmosphere(RenderSnapshot snapshot, FrameHandle cameraFrame)
    {
        if (AtmosphereFrame(cameraFrame) is not var (frame, planet))
            return;
        int dense = denseOfFrame[frame.Index];
        var center = (relCurrent[dense].Position - renderOrigin).ToVector3();
        // The previous place follows the rule of an instance's: the build before, only when this build interpolates.
        Vector3 previous = renderInterpolate ? (relPrevious[dense].Position - renderOrigin).ToVector3() : center;
        snapshot.Atmosphere = new(
            Center: center,
            PreviousCenter: previous,
            Medium: planet,
            Sun: SunOf(CollectionsMarshal.AsSpan(snapshot.Lights), center, planet.Radius)
        );
    }

    /// <summary>
    /// The frame whose planet medium the camera looks through, and that medium (User, ter-uu2y): the nearest frame of the
    /// chain of <paramref name="cameraFrame"/> that has a planet medium. Only when no chain frame has one, the frame of the
    /// world that is densest at the eye: the one whose planet medium puts the eye the fewest scale heights above its
    /// surface, max(0, |c| − R) / H, with c the frame's origin relative to the eye. The heights compare directly, not their
    /// densities, so far planets do not all tie at a density of 0. Of two that tie, the lower frame slot wins, so every run
    /// picks the same air. Null when the world holds no planet medium. Reads the frame poses of this build.
    /// </summary>
    private (FrameHandle Frame, PlanetMedium Medium)? AtmosphereFrame(FrameHandle cameraFrame)
    {
        for (FrameHandle frame = cameraFrame; !frame.IsNull; frame = frames.Parent(frame))
            if (frames.Medium(frame).TryGetValue(out PlanetMedium planet))
                return (frame, planet);

        (FrameHandle Frame, PlanetMedium Medium)? densest = null;
        double fewest = 0;
        // Every frame of the tree has a pose of this build. A numeric frame has no medium.
        for (int i = 0; i < frames.Count; i++)
        {
            FrameHandle frame = denseFrames[i];
            if (!frames.Medium(frame).TryGetValue(out PlanetMedium planet))
                continue;
            double scaleHeights = planet.HeightAt((relCurrent[i].Position - renderOrigin).Length) / planet.ScaleHeight;
            bool denser =
                densest is not { } best
                || scaleHeights < fewest
                || (scaleHeights == fewest && frame.Index < best.Frame.Index);
            if (denser)
                (densest, fewest) = ((frame, planet), scaleHeights);
        }

        return densest;
    }

    /// <summary>
    /// The index in <paramref name="lights"/> of the planet's sun: the point light that gives the planet's center the most
    /// light, P (r + g + b) / max(d², R²), above 0. The floor at the planet's radius keeps a light near the center from
    /// winning by its nearness, and a spot light lights only its cone, so it is never a sun. Of two lights that give the
    /// same light, the earlier one wins, so the choice follows the slot order of the entities, which every run shares. Null
    /// when no light gives the center any light.
    /// </summary>
    /// <param name="lights">The snapshot's lights.</param>
    /// <param name="center">The planet's center, relative to the render origin, in m.</param>
    /// <param name="radius">The planet's radius, in m.</param>
    private static int? SunOf(ReadOnlySpan<RenderLight> lights, Vector3 center, double radius)
    {
        int? sun = null;
        // A light that is not a number, or 0, never passes the comparison.
        double most = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (!lights[i].TryGetValue(out PointRenderLight point))
                continue;
            double dx = (double)point.Position.X - center.X,
                dy = (double)point.Position.Y - center.Y,
                dz = (double)point.Position.Z - center.Z;
            double distanceSquared = Math.Max(dx * dx + dy * dy + dz * dz, radius * radius);
            double light =
                (double)point.Power * ((double)point.Color.X + point.Color.Y + point.Color.Z) / distanceSquared;
            if (light > most)
                (sun, most) = (i, light);
        }

        return sun;
    }
}
