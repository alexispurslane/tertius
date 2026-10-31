using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The sizes of the unions of Core that the union generator writes (docs/plans/union-tooling.md, "Conversion"). Each
/// size is the size of the hand-written union before the conversion, or less, so that no table, event or intent that
/// holds one grows. The unions whose own test files pin their size (HemisphereUp, PuffShading, RenderLight and
/// FrameOutline) are not repeated here.
/// </summary>
public sealed class UnionSizeTests
{
    [Fact]
    public void ATargetTakesSixteenBytesBecauseEveryEventCarriesOne()
    {
        // The entity's index and generation share their two uint slots with the topic's generation and id, and the
        // broadcast has no value: 12 bytes and the kind, padded to 16.
        Unsafe.SizeOf<Target>().Should().Be(16);
        Unsafe.SizeOf<Event>().Should().Be(88, "the target, the sender, the type, the payload and the extra");
    }

    [Fact]
    public void AGravityFieldTakesFiftySixBytes() =>
        // The G·M, the acceleration and the optional region, each in its own slot, and the kind.
        Unsafe.SizeOf<GravityField>().Should().Be(56);

    [Fact]
    public void AMediumFieldTakesOneHundredAndFourBytes() =>
        // The two cases share their float slots, and a planet's look adds its sky, its optional haze and its optional tint
        // (docs/plans/atmosphere.md, "The types"). The frame tree holds one medium per frame, so the size matters little.
        Unsafe.SizeOf<MediumField>().Should().Be(104);

    [Fact]
    public void AQueryShapeTakesTwentyFourBytes() =>
        // The sphere's radius shares a float slot with the capsule's half height.
        Unsafe.SizeOf<QueryShape>().Should().Be(24);

    [Fact]
    public void AMotionIntentTakesOneSampleAndItsKind() =>
        // The three cases share the pose slot, and only the full sample fills the five velocity slots. Every entity's
        // intent holds one.
        Unsafe.SizeOf<MotionIntent>().Should().Be(184);

    [Fact]
    public void AnInputEventTakesSixteenBytes() =>
        // The key and the button share the code and the flag, and the move and the scroll share the delta.
        Unsafe.SizeOf<InputEvent>().Should().Be(16);
}
