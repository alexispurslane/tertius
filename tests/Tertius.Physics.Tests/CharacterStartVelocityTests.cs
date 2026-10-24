using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// The float rule of a walking character whose velocity at the start of its update is an instant velocity
/// (docs/plans/spin-gravity.md, "Characters"). In a frame that does not turn, the character moves by the float rule of
/// before the rotation terms, to the bit: a jump, which is relative to the ground and so to the home's instant velocity,
/// loses half a step of gravity, and a velocity that the game gave, which is relative to the home's frame velocity, also
/// loses the home's instant velocity past its frame velocity where the step starts. That excess is half a step of the
/// home's step acceleration, so it is not zero in a numeric home under a field, or in a following frame whose anchor
/// accelerates.
/// </summary>
public class CharacterStartVelocityTests
{
    /// <summary>A jump of 3.1 m/s along up plus a walk, in m/s.</summary>
    private static readonly Vector3 Jump = new(0.7f, 3.1f, -1.3f);

    /// <summary>Half a step of a linear gravity at 60 Hz, in m/s.</summary>
    private static readonly Vector3 HalfStep = new Vector3(0.013f, -9.81f, 0.002f) * (1f / 120);

    /// <summary>How much a numeric home's instant velocity exceeds its frame velocity where the step starts, in m/s.</summary>
    private static readonly Vector3 Over = new(0.0371f, -0.0817f, 0.0129f);

    [Fact]
    public void AJumpLosesHalfAStepOfGravityToTheBitOfTheRuleBeforeTheRotationTermsInANumericHomeThatAccelerates()
    {
        Vector3 chord = InstantStart.Jump(Jump).FloatChordBehind(HalfStep);

        chord.Should().Be(Jump - HalfStep,
            "a jump keeps the float rule of before, which never added the home's excess");
    }

    [Fact]
    public void
        AVelocityThatTheGameGaveLosesHalfAStepOfGravityAndTheHomesExcessToTheBitOfTheRuleBeforeTheRotationTerms()
    {
        InstantStart start = InstantStart.Given(Jump, Over);

        start.FloatChordBehind(HalfStep).Should().Be(Jump - (HalfStep + Over),
            "the float rule of before took both in one subtraction");
        start.Local.Should().Be(Jump - (Double3)Over,
            "the exact rule takes the velocity relative to the home's instant velocity");
    }

    [Fact]
    public void AJumpIsRelativeToTheHomesInstantVelocity()
    {
        InstantStart.Jump(Jump).Local.Should().Be((Double3)Jump);
    }
}

/// <summary>
/// The gravity that Jolt reads to press a walking character's weight onto a dynamic body that it touches
/// (docs/plans/spin-gravity.md, "Characters"), from its weight input: the effective gravity plus the Coriolis term. The
/// weight here is that input for a person 95 m from a station's axis who moves at 2 m/s across it: 9.32 m/s² outward and
/// 1.25 m/s² of the Coriolis term along the floor.
/// </summary>
public class CharacterPressedWeightTests
{
    private static readonly Vector3 Weight = new(9.32f, 0, 1.25f);
    private static readonly Vector3 Up = -Vector3.UnitX;

    /// <summary>The normal of a face 60 degrees steep, tilted about the station's Y from up toward +Z.</summary>
    private static readonly Vector3 SteepNormal = new(-0.5f, 0, 0.8660254f);

    [Fact]
    public void ACharacterThatDoesNotStandHandsJoltItsWholeWeightWithTheCoriolisTerm()
    {
        JoltPhysicsBackend.PressedWeight(weight: Weight, standing: false, up: Up, normal: SteepNormal).Should().Be(Weight,
            "nothing holds a character that does not stand, so Jolt presses its steep ground with the part of the whole input along the face's normal");
    }

    [Fact]
    public void AStandingCharacterHandsJoltThePartOfItsWeightAlongUpAlongTheGroundsNormal()
    {
        Vector3 normal = Vector3.Normalize(new(-0.94f, 0, 0.342f));

        JoltPhysicsBackend.PressedWeight(weight: Weight, standing: true, up: Up, normal: normal).Should().Be(
            Vector3.Dot(Weight, Up) * normal,
            "the ground holds the rest of a standing character's weight");
    }
}
