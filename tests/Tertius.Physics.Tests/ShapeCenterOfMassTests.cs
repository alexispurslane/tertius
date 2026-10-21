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
    /// <summary>A compound of one box whose center sits off the body's origin.</summary>
    private static CompoundShapeDesc OffsetBox() =>
        new([new CompoundChild(new BoxShapeDesc(new Vector3(0.5f, 0.25f, 1)), new Pose(new Vector3(1.5f, -0.75f, 2.25f), Quaternion.Identity))]);

    /// <summary>A compound of a heavy box and a light sphere, with a turned child, so its center is not any child's center.</summary>
    private static CompoundShapeDesc TwoChildren() =>
        new(
        [
            new CompoundChild(new BoxShapeDesc(new Vector3(1, 0.2f, 0.5f)),
                new Pose(new Vector3(-0.5f, 0, 0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f))),
            new CompoundChild(new SphereShapeDesc(0.4f), new Pose(new Vector3(0.9f, 0.6f, -0.2f), Quaternion.Identity)),
        ]);

    /// <summary>A convex hull of a tetrahedron that does not surround the body's origin.</summary>
    private static ConvexHullShapeDesc LopsidedHull() =>
        new([new(1, 0, 0), new(3, 0, 0), new(1, 2, 0), new(1, 0, 1.5f)]);

    public static TheoryData<string> OffCenterShapes => ["offset box", "two children", "lopsided hull"];

    private static ShapeDesc ShapeNamed(string name) => name switch
    {
        "offset box" => OffsetBox(),
        "two children" => TwoChildren(),
        "lopsided hull" => LopsidedHull(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static void ShouldBeBitForBit(Vector3 actual, Vector3 expected, string because)
    {
        int[] Bits(Vector3 v) => [BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z)];
        Bits(actual).Should().Equal(Bits(expected), because);
    }

    [Fact]
    public void TheCenterOfMassOfABoxASphereAndACapsuleIsTheirOrigin()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);

        backend.ShapeCenterOfMass(new BoxShapeDesc(new Vector3(0.5f, 1, 2))).Should().Be(Vector3.Zero);
        backend.ShapeCenterOfMass(new SphereShapeDesc(0.7f)).Should().Be(Vector3.Zero);
        backend.ShapeCenterOfMass(new CapsuleShapeDesc(0.5f, 0.25f)).Should().Be(Vector3.Zero);
    }

    [Theory]
    [MemberData(nameof(OffCenterShapes))]
    public void TheCenterOfMassOfAShapeIsWhereJoltPutsTheCenterOfMassOfABodyAtRestAtTheOrigin(string name)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        ShapeDesc shape = ShapeNamed(name);
        Entity body = world.Spawn(new SpawnDesc { Frame = space, Pose = DPose.Identity, Body = new BodyDesc(shape, Mass: 3) });
        Tick(world, 1);

        BodyID id = backend.BodiesIn(space).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, body)).Id;
        backend.SystemOf(space)!.BodyInterface.GetRCenterOfMassPosition(id, out RVector3 joltCenter);
        Double3 jolts = joltCenter.ToDouble3();

        Vector3 center = backend.ShapeCenterOfMass(shape);

        center.Length().Should().BeGreaterThan(0.1f, "the shape is chosen so that its center of mass is off the body's origin");
        new Double3(center.X, center.Y, center.Z).Should().Be(jolts, "a body at rest at the origin, unturned, has its center of mass where the shape has it");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(OffCenterShapes))]
    public void TheSameDescriptionGivesTheSameCenterOfMassBitForBit(string name)
    {
        ShapeDesc shape = ShapeNamed(name);
        using World first = NewWorld(out JoltPhysicsBackend backend);
        using World second = NewWorld(out JoltPhysicsBackend other);

        Vector3 answer = backend.ShapeCenterOfMass(shape);

        ShouldBeBitForBit(backend.ShapeCenterOfMass(shape), answer, "a second question about the same description gets the same answer");
        ShouldBeBitForBit(other.ShapeCenterOfMass(shape), answer, "another backend builds the same native shape from the same description");
    }

    [Fact]
    public async Task AQuestionFromAnotherThreadDuringAStepGetsTheSameAnswer()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        ShapeDesc built = TwoChildren();
        ShapeDesc unbuilt = LopsidedHull();
        world.Spawn(new SpawnDesc { Frame = space, Pose = DPose.Identity, Body = new BodyDesc(built, Mass: 3) });
        Tick(world, 1);
        Vector3 builtAnswer = faults.ShapeCenterOfMass(built);

        // The edit runs inside the step, before Jolt steps. It starts a question on another thread that waits until it
        // finds the native gate taken, which the step holds while Jolt steps, and only then asks. A question that misses
        // the step gives up, and the next step tries again.
        (Vector3 Built, Vector3 Unbuilt)? during = null;
        for (int attempt = 0; attempt < 20 && during is null; attempt++)
        {
            Task<(Vector3 Built, Vector3 Unbuilt)?>? asked = null;
            faults.EditNextIntents = _ => asked = Task.Run(() => AskWhileTheGateIsTaken(faults, built, unbuilt));
            Tick(world, 1);
            asked.Should().NotBeNull("the step ran the edit");
            during = await asked!;
        }

        during.Should().NotBeNull("one of the questions asked while a step held the native gate");
        (Vector3 duringBuilt, Vector3 duringUnbuilt) = during!.Value;
        ShouldBeBitForBit(duringBuilt, builtAnswer, "a shape that a body already uses gives the same answer during a step");
        using World other = NewWorld(out JoltPhysicsBackend fresh);
        ShouldBeBitForBit(duringUnbuilt, fresh.ShapeCenterOfMass(unbuilt), "a shape first built during a step gives the answer of a quiet backend");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Waits up to 1 s for the native gate to be taken, then asks for the center of mass of both shapes. Null when the
    /// gate stayed free.
    /// </summary>
    private static (Vector3 Built, Vector3 Unbuilt)? AskWhileTheGateIsTaken(IPhysicsBackend backend, ShapeDesc built, ShapeDesc unbuilt)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (JoltRuntime.NativeGate.TryEnter())
        {
            JoltRuntime.NativeGate.Exit();
            if (clock.ElapsedMilliseconds > 1000)
                return null;
        }
        // The unbuilt shape goes first, so that its question needs the gate while the step still holds it.
        Vector3 unbuiltAnswer = backend.ShapeCenterOfMass(unbuilt);
        return (backend.ShapeCenterOfMass(built), unbuiltAnswer);
    }

    [Fact]
    public void AShapeThatJoltCannotBuildHasNoCenterOfMass()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var flat = new ConvexHullShapeDesc([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)]);

        Action ask = () => backend.ShapeCenterOfMass(flat);

        ask.Should().Throw<ArgumentException>().WithMessage(backend.ShapeProblem(flat));
    }
}
