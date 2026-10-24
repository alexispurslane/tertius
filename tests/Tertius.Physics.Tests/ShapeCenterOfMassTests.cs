using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// <see cref="IPhysicsBackend.ShapeCenterOfMass"/>: the center of mass of a shape, in the axes of the body that the shape
/// describes. The lever of a part is measured from it, so it must be the center that Jolt gives the body.
/// </summary>
public class ShapeCenterOfMassTests
{
    public static TheoryData<string> OffCenterShapes => ["offset box", "two children", "lopsided hull"];

    /// <summary>A compound of one box whose center sits off the body's origin.</summary>
    private static CompoundShapeDesc OffsetBox() =>
        new([new(new BoxShapeDesc(new(0.5f, 0.25f, 1)), new(new(1.5f, -0.75f, 2.25f), Quaternion.Identity))]);

    /// <summary>A compound of a heavy box and a light sphere, with a turned child, so its center is not any child's center.</summary>
    private static CompoundShapeDesc TwoChildren() =>
        new(
        [
            new(new BoxShapeDesc(new(1, 0.2f, 0.5f)),
                new(new(-0.5f, 0, 0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f))),
            new(new SphereShapeDesc(0.4f), new(new(0.9f, 0.6f, -0.2f), Quaternion.Identity))
        ]);

    /// <summary>A convex hull of a tetrahedron that does not surround the body's origin.</summary>
    private static ConvexHullShapeDesc LopsidedHull() =>
        new([new(1, 0, 0), new(3, 0, 0), new(1, 2, 0), new(1, 0, 1.5f)]);

    private static ShapeDesc ShapeNamed(string name) => name switch
    {
        "offset box" => OffsetBox(),
        "two children" => TwoChildren(),
        "lopsided hull" => LopsidedHull(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    private static void ShouldBeBitForBit(Vector3 actual, Vector3 expected, string because)
    {
        static int[] Bits(Vector3 v) =>
        [
            BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y),
            BitConverter.SingleToInt32Bits(v.Z)
        ];

        Bits(actual).Should().Equal(Bits(expected), because);
    }

    [Fact]
    public void TheCenterOfMassOfABoxASphereAndACapsuleIsTheirOrigin()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);

        backend.ShapeCenterOfMass(new BoxShapeDesc(new(0.5f, 1, 2))).Should().Be(Vector3.Zero);
        backend.ShapeCenterOfMass(new SphereShapeDesc(0.7f)).Should().Be(Vector3.Zero);
        backend.ShapeCenterOfMass(new CapsuleShapeDesc(0.5f, 0.25f)).Should().Be(Vector3.Zero);
    }

    [Theory]
    [MemberData(nameof(OffCenterShapes))]
    public void TheCenterOfMassOfAShapeIsWhereJoltPutsTheCenterOfMassOfABodyAtRestAtTheOrigin(string name)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        ShapeDesc shape = ShapeNamed(name);
        Entity body = world.Spawn(new() { Frame = space, Pose = DPose.Identity, Body = new(Shape: shape, Mass: 3) });
        Tick(world, 1);

        BodyID id = backend.BodiesIn(space).Single(facts =>
            BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, body)).Id;
        backend.SystemOf(space)!.BodyInterface.GetRCenterOfMassPosition(id, out RVector3 joltCenter);
        var jolts = joltCenter.ToDouble3();

        Vector3 center = backend.ShapeCenterOfMass(shape);

        center.Length().Should()
            .BeGreaterThan(0.1f, "the shape is chosen so that its center of mass is off the body's origin");
        new Double3(center.X, center.Y, center.Z).Should().Be(jolts,
            "a body at rest at the origin, unturned, has its center of mass where the shape has it");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(OffCenterShapes))]
    public void TheSameDescriptionGivesTheSameCenterOfMassBitForBit(string name)
    {
        ShapeDesc shape = ShapeNamed(name);
        using World first = NewWorld(backend: out JoltPhysicsBackend backend);
        using World second = NewWorld(backend: out JoltPhysicsBackend other);

        Vector3 answer = backend.ShapeCenterOfMass(shape);

        ShouldBeBitForBit(backend.ShapeCenterOfMass(shape), answer,
            "a second question about the same description gets the same answer");
        ShouldBeBitForBit(other.ShapeCenterOfMass(shape), answer,
            "another backend builds the same native shape from the same description");
    }

    [Fact]
    public void AFirstQuestionWaitsForTheBackendGateAndThenGetsTheAnswerOfAQuietBackend()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        ShapeDesc unbuilt = LopsidedHull();
        Vector3? answer = null;
        var asker = new Thread(() => answer = backend.ShapeCenterOfMass(unbuilt));

        // The test thread holds the gate, as a step holds it while Jolt steps, so the question on the other thread must wait.
        backend.Gate.Enter();
        try
        {
            asker.Start();
            asker.Join(TimeSpan.FromMilliseconds(200)).Should()
                .BeFalse("the first question about a shape builds it, which takes the backend's gate");
        }
        finally
        {
            backend.Gate.Exit();
        }

        asker.Join(TimeSpan.FromSeconds(10)).Should().BeTrue("the question goes on once the gate is free");

        using World other = NewWorld(backend: out JoltPhysicsBackend quiet);
        ShouldBeBitForBit(answer!.Value, quiet.ShapeCenterOfMass(unbuilt),
            "a question that waited for the gate gets the answer of a quiet backend");
    }

    [Fact]
    public void AKeptAnswerNeedsNoBackendGate()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        ShapeDesc shape = TwoChildren();
        Vector3 first = backend.ShapeCenterOfMass(shape);
        Vector3? again = null;
        var asker = new Thread(() => again = backend.ShapeCenterOfMass(shape));

        backend.Gate.Enter();
        try
        {
            asker.Start();
            asker.Join(TimeSpan.FromSeconds(10)).Should()
                .BeTrue("the backend keeps the answer, so a later question takes no lock");
        }
        finally
        {
            backend.Gate.Exit();
        }

        ShouldBeBitForBit(again!.Value, first, "the kept answer is the first answer");
    }

    [Fact]
    public void AKeptAnswerAllocatesNothing()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        ShapeDesc shape = TwoChildren();
        backend.ShapeCenterOfMass(shape);
        backend.ShapeProblem(shape);

        long allocated = Allocations.Fewest(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                backend.ShapeCenterOfMass(shape);
                backend.ShapeProblem(shape);
            }
        });

        allocated.Should().Be(0,
            "the World asks both at every spawn, and the center of mass in every sync point with debug checks once per body");
    }

    [Fact]
    public void AShapeThatJoltCannotBuildHasNoCenterOfMass()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        var flat = new ConvexHullShapeDesc([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)]);

        Action ask = () => backend.ShapeCenterOfMass(flat);

        ask.Should().Throw<ArgumentException>().WithMessage(backend.ShapeProblem(flat));
    }
}
