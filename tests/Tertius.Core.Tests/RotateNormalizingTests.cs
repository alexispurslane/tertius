using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="DQuaternion.RotateNormalizing"/> rotates a vector by a quaternion as if it were normalized first, without the
/// square root. <see cref="DPose.Compose"/> uses it to move the position of a child by the normalized rotation of its parent,
/// in the hot loop of the render snapshot. These tests compare it with the plain way: normalize, then rotate.
/// </summary>
public class RotateNormalizingTests
{
    // A vector 6.77E6 m long, as the offset of a ship in a low orbit round Earth. A relative error of 1E-16 is 1E-9 m.
    private static readonly Double3 Far = new(4.2e6, -3.9e6, 3.6e6);

    public static TheoryData<string, DQuaternion> Rotations => new()
    {
        { "a rotation about an axis, of unit length", DQuaternion.FromAxisAngle(new Double3(1, 2, 3), 0.9) },
        { "a rotation that came out of a float quaternion", DQuaternion.FromQuaternion(new Quaternion(0.18257418f, 0.36514837f, 0.5477226f, 0.73029685f)) },
        { "a rotation that is too short", new DQuaternion(0, 0.1, 0, 0.1) },
        { "a rotation that is too long", new DQuaternion(2, -3, 1, 5) },
        { "a rotation by half a turn", new DQuaternion(0, 1, 0, 0) },
        { "a rotation that is very short", new DQuaternion(0, 1e-100, 0, 1e-100) },
    };

    [Theory]
    [MemberData(nameof(Rotations))]
    public void ItRotatesAVectorAsTheNormalizedQuaternionDoes(string why, DQuaternion rotation)
    {
        Double3 actual = rotation.RotateNormalizing(Far);

        Double3.Distance(actual, rotation.Normalized().Rotate(Far)).Should().BeLessThan(1e-8, why);
    }

    [Fact]
    public void TheZeroQuaternionLeavesAVectorAsItIs()
    {
        default(DQuaternion).RotateNormalizing(Far).Should().Be(Far);
    }

    [Fact]
    public void AQuaternionThatIsNotANumberLeavesAVectorAsItIs()
    {
        new DQuaternion(double.NaN, 0, 0, 1).RotateNormalizing(Far).Should().Be(Far);
    }
}
