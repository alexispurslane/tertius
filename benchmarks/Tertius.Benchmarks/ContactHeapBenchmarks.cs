using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of Jolt's reports of contacts that persist (ter-wu6v, ter-pnco). One tick of a world in which 300 boxes of
/// 1 m ride a kinematic floor that moves at 1 m/s, so that every box stays awake. The boxes stand in 15 rows of 20,
/// each box against its neighbors in the row, and the rows 5 cm apart, beyond Jolt's speculative contact distance of
/// 2 cm. So Jolt reports 585 contacts again in every step: 300 with the floor and 285 between neighbors.
/// <list type="bullet">
/// <item><see cref="PendingPair"/> false: every pair that Jolt found touches, so no pair waits for its first touch.</item>
/// <item><see cref="PendingPair"/> true: the first box of the first row stands 1 cm from its neighbor, so Jolt finds
/// their pair without a touch, and the pair waits as a pending pair the whole time. The backend must then look at every
/// report of a persisting contact, to see whether it is the touch of the pending pair.</item>
/// <item><see cref="Restitution"/> is the restitution of every box, and the floor has the default of 0. At 0, the report
/// of a persisting contact ends at once while no pair is pending. At 0.5, the backend reads the approach speed of every
/// contact in every step, to apply the minimum rebound speed (ter-4kaa).</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class ContactHeapBenchmarks
{
    private const int Columns = 20;
    private const int Rows = 15;

    [Params(false, true)]
    public bool PendingPair;

    [Params(0f, 0.5f)]
    public float Restitution;

    private World world = null!;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new() { WorkerThreads = 2 });
        world = new(new() { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle ground = world.CreateRootFrame(
            new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -9.81f, 0)))
        );
        // A kinematic body moves only by its motion intents, so the floor's Conveyor writes its end pose in every tick, and
        // its first motion intent moves it in its first step.
        DPose floor = At(0, -0.5, 0);
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = floor,
                FirstMove = new(new EndPose(Conveyor.After(floor, world.DeltaTime))),
                Body = new(Shape: new BoxShapeDesc(new(60, 0.5f, 60)), Motion: BodyMotion.Kinematic),
                Components = [new Conveyor()],
            }
        );
        var boxes = new List<Entity>();
        for (int i = 0; i < Columns * Rows; i++)
        {
            double x = i % Columns - Columns / 2;
            if (PendingPair && i == 0)
                x -= 0.01;
            boxes.Add(
                world.Spawn(
                    new()
                    {
                        Frame = ground,
                        Pose = At(x, 0.5, (i / Columns - Rows / 2) * 1.05),
                        Body = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 10, Restitution: Restitution),
                    }
                )
            );
        }

        // Two seconds: every box has the speed of the floor.
        for (int i = 0; i < 120; i++)
            world.Tick();

        int pending = backend.PendingPairCount(ground);
        if (pending != (PendingPair ? 1 : 0))
            throw new InvalidOperationException($"The scene has {pending} pending pairs.");
        List<JoltPhysicsBackend.BodyFacts> facts = backend.BodiesIn(ground);
        int awake = facts.Count(body => body.Active && body.Motion == MotionType.Dynamic);
        if (awake != boxes.Count)
            throw new InvalidOperationException($"Only {awake} of {boxes.Count} boxes are awake.");
    }

    private static DPose At(double x, double y, double z) => new(new(x, y, z), DQuaternion.Identity);

    /// <summary>Moves the kinematic floor along +X at 1 m/s: in every tick, the end pose one step on from where it starts the next step.</summary>
    private sealed class Conveyor : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        /// <summary>The pose one step of <paramref name="step"/> seconds along +X at 1 m/s from <paramref name="start"/>.</summary>
        public static DPose After(DPose start, double step) =>
            new(start.Position + new Double3(step, 0, 0), start.Rotation);

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveKinematic(After(self.PoseAtNextStepStartIn(self.Frame), self.DeltaTime));
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}
