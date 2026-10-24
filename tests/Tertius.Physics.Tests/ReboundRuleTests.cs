namespace Tertius.Physics.Tests;

/// <summary>
/// The rule by which a contact keeps its combined restitution. Jolt's rebound is the restitution times the approach
/// speed less the speed that the pull toward the surface adds in the step. The contact keeps its restitution when that
/// rebound carries the bodies at least <see cref="FrameWorld.MinReboundHeight"/> (4 cm, twice Jolt's speculative contact
/// distance) apart before the pull brings them back, or when nothing pulls them together. Otherwise the contact does not
/// bounce. Until the review of ter-ejx9 the rule asked for a rebound of 1 m/s at any gravity, and ignored the pull.
/// </summary>
public sealed class ReboundRuleTests
{
    private const float G = 9.81f;
    private const float Step = 1f / 60;

    /// <summary>0.5 × (2 − 0.16) m/s = 0.92 m/s, which rises 4.3 cm at 1 g. (Before: a rebound of 1 m/s.)</summary>
    [Fact]
    public void ABounceThatRisesFourCentimetersKeepsItsRestitution() =>
        FrameWorld
            .KeptRestitution(restitution: 0.5f, approachSpeed: 2f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(0.5f);

    [Fact]
    public void AFasterBounceKeepsItsRestitution() =>
        FrameWorld
            .KeptRestitution(restitution: 0.8f, approachSpeed: 5f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(0.8f);

    /// <summary>0.5 × (1.9 − 0.16) m/s = 0.87 m/s, which rises 3.8 cm at 1 g. (Before: a rebound slower than 1 m/s.)</summary>
    [Fact]
    public void ABounceThatRisesLessThanFourCentimetersDoesNotBounce() =>
        FrameWorld
            .KeptRestitution(restitution: 0.5f, approachSpeed: 1.9f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(0f, "0.5 · (1.9 − 0.16) m/s rises 3.8 cm");

    [Fact]
    public void AnApproachFasterThanOneMeterPerSecondWithASlowBounceDoesNotBounce() =>
        FrameWorld
            .KeptRestitution(restitution: 0.1f, approachSpeed: 6f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(0f, "Jolt would bounce it, at 0.58 m/s");

    /// <summary>
    /// Jolt takes the speed that the pull adds in the step off the rebound. 0.5 × 1.85 m/s = 0.93 m/s would rise 4.4 cm,
    /// but Jolt rebounds at 0.5 × (1.85 − 0.16) m/s = 0.84 m/s, which rises 3.6 cm.
    /// </summary>
    [Fact]
    public void ThePullOfTheStepComesOffTheRebound() =>
        FrameWorld
            .KeptRestitution(restitution: 0.5f, approachSpeed: 1.85f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(0f);

    /// <summary>
    /// A stronger pull brings a bounce back sooner, so it needs a faster rebound. At 1 g, 1 × (2 − 0.16) m/s rises 17 cm.
    /// At 3 g, 1 × (2 − 0.49) m/s = 1.51 m/s rises only 3.9 cm.
    /// </summary>
    [Fact]
    public void AStrongerPullNeedsAFasterRebound()
    {
        FrameWorld
            .KeptRestitution(restitution: 1f, approachSpeed: 2f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(1f);
        FrameWorld
            .KeptRestitution(restitution: 1f, approachSpeed: 2f, approachAcceleration: 3 * G, deltaTime: Step)
            .Should()
            .Be(0f);
    }

    /// <summary>In zero gravity a bounce never comes back, so it leaves Jolt's 2 cm however slowly it goes.</summary>
    [Fact]
    public void WithoutAPullEveryBounceKeepsItsRestitution()
    {
        FrameWorld
            .KeptRestitution(restitution: 0.5f, approachSpeed: 1.5f, approachAcceleration: 0f, deltaTime: Step)
            .Should()
            .Be(0.5f, "0.75 m/s keeps going");
        FrameWorld
            .KeptRestitution(restitution: 0.1f, approachSpeed: 1.01f, approachAcceleration: 0f, deltaTime: Step)
            .Should()
            .Be(0.1f, "0.1 m/s keeps going");
    }

    /// <summary>Jolt takes off only a pull toward the surface. A push apart takes nothing off, and the bounce never comes back.</summary>
    [Fact]
    public void APushApartKeepsEveryBounce() =>
        FrameWorld
            .KeptRestitution(restitution: 0.5f, approachSpeed: 1.5f, approachAcceleration: -G, deltaTime: Step)
            .Should()
            .Be(0.5f);

    /// <summary>A push apart adds nothing to the rebound, so bodies that move apart slowly while it pushes them do not bounce either.</summary>
    [Fact]
    public void BodiesThatMoveApartDoNotBounce()
    {
        FrameWorld
            .KeptRestitution(restitution: 1f, approachSpeed: -3f, approachAcceleration: G, deltaTime: Step)
            .Should()
            .Be(0f);
        FrameWorld
            .KeptRestitution(restitution: 1f, approachSpeed: -3f, approachAcceleration: 0f, deltaTime: Step)
            .Should()
            .Be(0f);
        FrameWorld
            .KeptRestitution(restitution: 1f, approachSpeed: -0.05f, approachAcceleration: -G, deltaTime: Step)
            .Should()
            .Be(0f);
    }
}
