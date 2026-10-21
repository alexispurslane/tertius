using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics reads no medium (docs/plans/particles.md, "Settled forks", fork 1): a frame's medium acts only on puffs, for
/// now, and each body keeps its own damping. So a body moves bit for bit the same in a frame with a medium as in the same
/// frame without one. The scene is a ground of 9.81 m/s² along −Y with a floor, a box that falls onto it while it slides,
/// and a character that walks. The medium is thick air with a strong drag, which a reader of the medium could not miss.
/// </summary>
public class FrameMediumPhysicsTests
{
    private static readonly MediumField ThickAir = MediumField.Uniform(5, 8);

    private sealed record Trace(List<DPose> Poses, List<Vector3> Velocities);

    /// <summary>Runs the scene for 120 ticks in a ground with <paramref name="medium"/>, and records each body after each tick.</summary>
    private static (Trace Box, Trace Person) Run(MediumField medium)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0)), medium));
        world.Frames.Medium(ground).Should().Be(medium);
        Floor(world, ground);
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 3),
            LinearVelocity = new Double3(4, 0, 1),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Dynamic, Mass: 10),
        });
        var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new Vector3(2, 0, -1)) };
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(-5, 1.2, 0),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            Components = [pilot],
        });
        var boxTrace = new Trace([], []);
        var personTrace = new Trace([], []);
        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            boxTrace.Poses.Add(world.PoseInOwnFrame(box));
            boxTrace.Velocities.Add(world.VelocityInOwnFrame(box));
            personTrace.Poses.Add(world.PoseInOwnFrame(person));
            personTrace.Velocities.Add(world.VelocityInOwnFrame(person));
        }
        world.Errors.Should().BeEmpty();
        return (boxTrace, personTrace);
    }

    [Fact]
    public void ABodyMovesTheSameWithAndWithoutAMediumInItsFrame()
    {
        (Trace vacuumBox, Trace vacuumPerson) = Run(MediumField.None);
        (Trace airBox, Trace airPerson) = Run(ThickAir);

        airBox.Poses.Should().Equal(vacuumBox.Poses, "a dynamic body reads no medium");
        airBox.Velocities.Should().Equal(vacuumBox.Velocities);
        airPerson.Poses.Should().Equal(vacuumPerson.Poses, "a character reads no medium");
        airPerson.Velocities.Should().Equal(vacuumPerson.Velocities);
        vacuumBox.Poses[^1].Position.X.Should().BeGreaterThan(1, "the box slid");
        vacuumPerson.Poses[^1].Position.X.Should().BeGreaterThan(-4, "the person walked");
    }
}
