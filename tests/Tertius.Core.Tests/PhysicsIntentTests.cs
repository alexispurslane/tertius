using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// What a front intent keeps in a World without a physics backend (<see cref="PhysicsIntent.KeepOnlyMotion"/>): nothing applies
/// its forces, impulses, velocities, teleport, walk or collision groups, so only its frame, its motion intent and its change
/// of a hull's motion stay, which the sync point applies itself (docs/plans/rails-components.md, "Where the motion intent
/// lives").
/// </summary>
public class PhysicsIntentTests
{
    private static readonly FieldInfo[] Fields = typeof(PhysicsIntent).GetFields(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>A value of <paramref name="type"/> that is not its default, for each type of a field of the intent.</summary>
    private static object NotDefault(Type type) => type switch
    {
        _ when type == typeof(FrameHandle) => new FrameHandle(new Entity(3, 1), 2, 1),
        _ when type == typeof(Vector3) => new Vector3(1, 2, 3),
        _ when type == typeof(Double3) => new Double3(4, 5, 6),
        _ when type == typeof(DPose) => new DPose(new Double3(7, 8, 9), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.5)),
        _ when type == typeof(float) => 2.5f,
        _ when type == typeof(CollisionGroups) => CollisionGroups.Of(CollisionGroup.FromIndex(3)),
        _ when type == typeof(CollisionMask) => CollisionMask.Empty.InAny(CollisionGroup.FromIndex(4)),
        _ when type == typeof(BodyMotion) => BodyMotion.Kinematic,
        _ when type == typeof(MotionIntent) => new MotionIntent(new EndPose(new DPose(new Double3(1, 0, 0), DQuaternion.Identity))),
        _ when type == typeof(IntentFlags) => Enum.GetValues<IntentFlags>().Aggregate((all, flag) => all | flag),
        _ => throw new InvalidOperationException($"Give the new field type {type} of PhysicsIntent a value here."),
    };

    /// <summary>An intent in which every field holds a value that is not its default, every flag included.</summary>
    private static PhysicsIntent Full()
    {
        object boxed = new PhysicsIntent();
        foreach (FieldInfo field in Fields)
            field.SetValue(boxed, NotDefault(field.FieldType));
        return (PhysicsIntent)boxed;
    }

    [Fact]
    public void AnIntentWithoutABackendKeepsOnlyItsFrameItsMotionIntentAndItsChangeOfMotion()
    {
        PhysicsIntent full = Full();
        PhysicsIntent kept = full;

        kept.KeepOnlyMotion();

        kept.Should().Be(new PhysicsIntent { Frame = full.Frame, Move = full.Move, Motion = full.Motion, Flags = IntentFlags.SetMotion });
    }

    [Fact]
    public void EveryOtherFieldOfAnIntentWithoutABackendIsCleared()
    {
        PhysicsIntent kept = Full();

        kept.KeepOnlyMotion();

        object boxed = kept;
        foreach (FieldInfo field in Fields)
        {
            if (field.Name is nameof(PhysicsIntent.Frame) or nameof(PhysicsIntent.Move) or nameof(PhysicsIntent.Motion) or nameof(PhysicsIntent.Flags))
                continue;
            // The default of the field's type, all zero, which a parameterless constructor such as DPose's would not give.
            field.GetValue(boxed).Should().Be(RuntimeHelpers.GetUninitializedObject(field.FieldType), $"nothing applies {field.Name} without a backend");
        }
    }

    [Fact]
    public void AnIntentWithoutAChangeOfMotionKeepsNoFlag()
    {
        PhysicsIntent kept = Full();
        kept.Flags &= ~IntentFlags.SetMotion;

        kept.KeepOnlyMotion();

        kept.Flags.Should().Be(IntentFlags.None);
        kept.IsEmpty.Should().BeFalse("its motion intent stays");
    }
}
