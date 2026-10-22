using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// <see cref="Push"/>, the pure part of the flow of the parts' pushes (docs/plans/parts.md, "How a part's push converts
/// into the root's push"). A part's force acts at the part's place, so it gives the root's body the same force and the
/// torque of its lever about the center of mass. The lever is the place in the body less the center of mass, turned by
/// the root's rotation. The sum of a root runs in doubles and narrows to floats once. No World runs here.
/// </summary>
public class PushTests
{
    private static Push ForceOnly(Vector3 force) => Push.Of(new() { Force = force });

    [Fact]
    public void AForceAtTheCenterOfMassGivesNoTorque()
    {
        Double3 lever = Push.Lever(DQuaternion.Identity, new(0, 1.5, -2), new(0, 1.5, -2));

        Push atCenter = ForceOnly(new(3, -4, 5)).At(lever);

        atCenter.Force.Should().Be(new Double3(3, -4, 5));
        atCenter.Torque.Should().Be(Double3.Zero);
    }

    [Fact]
    public void TheLeverIsThePlaceInTheBodyLessTheCenterOfMassTurnedByTheRootsRotation()
    {
        // A quarter turn about +Y takes the body's +X to the frame's -Z.
        DQuaternion quarterTurnAboutY = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

        Double3 lever = Push.Lever(quarterTurnAboutY, new(3, 0, 0), new(1, 0, 0));

        lever.X.Should().BeApproximately(0, 1e-12);
        lever.Y.Should().BeApproximately(0, 1e-12);
        lever.Z.Should().BeApproximately(-2, 1e-12);
    }

    [Fact]
    public void AForceAtALeverGivesItsCrossProductAsTorque()
    {
        Push atLever = ForceOnly(new(0, 10, 0)).At(new(2, 0, 0));

        // (2, 0, 0) × (0, 10, 0) = (0, 0, 20).
        atLever.Force.Should().Be(new Double3(0, 10, 0));
        atLever.Torque.Should().Be(new Double3(0, 0, 20));
    }

    [Fact]
    public void AnImpulseAtALeverGivesItsCrossProductAsAngularImpulse()
    {
        Push impulse = Push.Of(new() { Impulse = new(0, 0, 3), AngularImpulse = new(1, 0, 0) });

        Push atLever = impulse.At(new(0, 2, 0));

        // (0, 2, 0) × (0, 0, 3) = (6, 0, 0), added to the part's own angular impulse.
        atLever.Impulse.Should().Be(new Double3(0, 0, 3));
        atLever.AngularImpulse.Should().Be(new Double3(7, 0, 0));
        atLever.Torque.Should().Be(Double3.Zero);
    }

    [Fact]
    public void ATorqueAndAnAngularImpulseIgnoreTheLever()
    {
        Push free = Push.Of(new() { Torque = new(1, 2, 3), AngularImpulse = new(-4, 5, -6) });

        Push atLever = free.At(new(5, -7, 11));

        atLever.Should().Be(new Push(Double3.Zero, new(1, 2, 3), Double3.Zero, new(-4, 5, -6)));
    }

    [Fact]
    public void TwoOppositeForcesAtOppositePlacesSumToExactlyZeroForce()
    {
        var thrust = new Vector3(0.1f, 0.3f, 0.7f);
        Push left = ForceOnly(thrust).At(new(1, 0, 0));
        Push right = ForceOnly(-thrust).At(new(-1, 0, 0));

        Push pair = left.Plus(right);

        pair.Force.Should().Be(Double3.Zero);
        // Each jet gives (1, 0, 0) × thrust, so the pair gives the couple twice that.
        Double3 couple = Double3.Cross(new(1, 0, 0), thrust) * 2;
        pair.Torque.Should().Be(couple);
    }

    [Fact]
    public void TheRootsOwnForceSurvivesAPairOfOppositeJetsExactly()
    {
        var own = new PhysicsIntent
            { Force = new(1e5f, 3.3f, 0.1f), Velocity = new(7, 8, 9), Flags = IntentFlags.SetVelocity };
        var thrust = new Vector3(0.123f, 4567.8f, 0.9f);

        Push sum = Push.Of(own).Plus(ForceOnly(thrust).At(new(0, 0, 4))).Plus(ForceOnly(-thrust).At(new(0, 0, -4)));
        PhysicsIntent written = own;
        sum.WriteInto(ref written);

        written.Force.Should().Be(new Vector3(1e5f, 3.3f, 0.1f));
        // The other fields of the intent stay as they were.
        written.Velocity.Should().Be(new Double3(7, 8, 9));
        written.Flags.Should().Be(IntentFlags.SetVelocity);
    }

    [Fact]
    public void TheSumNarrowsToFloatsOnceAndNotOncePerPart()
    {
        // Each part's 4e-8 N is less than half a float step at 1 N (6e-8 N), so a narrowing after each part would lose it,
        // and the three together are more than one step.
        var own = new PhysicsIntent { Force = new(1, 0, 0), Torque = new(0, 1, 0) };
        Push sum = Push.Of(own);
        for (int part = 0; part < 3; part++)
            sum = sum.Plus(Push.Of(new() { Force = new(4e-8f, 0, 0), Torque = new(0, 4e-8f, 0) }));

        PhysicsIntent written = own;
        sum.WriteInto(ref written);

        written.Force.X.Should().Be(MathF.BitIncrement(1f));
        written.Torque.Y.Should().Be(MathF.BitIncrement(1f));
    }

    [Fact]
    public void TheImpulsesOfASumNarrowOnceToo()
    {
        var own = new PhysicsIntent { Impulse = new(0, 0, 1), AngularImpulse = new(1, 0, 0) };
        Push sum = Push.Of(own);
        for (int part = 0; part < 3; part++)
            sum = sum.Plus(Push.Of(new() { Impulse = new(0, 0, 4e-8f), AngularImpulse = new(4e-8f, 0, 0) }));

        PhysicsIntent written = own;
        sum.WriteInto(ref written);

        written.Impulse.Z.Should().Be(MathF.BitIncrement(1f));
        written.AngularImpulse.X.Should().Be(MathF.BitIncrement(1f));
    }
}
