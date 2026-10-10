using System.Numerics;
using Tertius;

namespace Tertius.Core.Tests;

public class MathTests
{
    private const double Eps = 1e-9;

    [Fact]
    public void DQuaternion_RotateMatchesFloatQuaternion()
    {
        var q = Quaternion.CreateFromYawPitchRoll(0.3f, -1.1f, 2.0f);
        var dq = DQuaternion.FromQuaternion(q);
        var v = new Vector3(1.5f, -2f, 0.25f);
        Vector3 expected = Vector3.Transform(v, q);
        Vector3 actual = dq.Rotate(v);
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"{expected} vs {actual}");
    }

    [Fact]
    public void DQuaternion_ProductOrderMatchesSystemNumerics()
    {
        var a = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f);
        var b = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -1.3f);
        var v = new Vector3(0.2f, 1f, -3f);
        Vector3 expected = Vector3.Transform(v, a * b);
        Vector3 actual = (DQuaternion.FromQuaternion(a) * DQuaternion.FromQuaternion(b)).Rotate(v);
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f);
        // (a * b) rotates by b first, then a.
        Vector3 stepwise = Vector3.Transform(Vector3.Transform(v, b), a);
        Assert.True(Vector3.Distance(stepwise, actual) < 1e-5f);
    }

    [Fact]
    public void DPose_ComposeWithInverseIsIdentity()
    {
        var p = new DPose(new Double3(6e6, -2.5, 1e3), DQuaternion.FromAxisAngle(new Double3(1, 2, 3), 0.9));
        DPose id = DPose.Compose(p, p.Inverse());
        Assert.True(id.Position.Length < 1e-8);
        Assert.True(Math.Abs(Math.Abs(id.Rotation.W) - 1) < Eps);
    }

    [Fact]
    public void Pose_TransformPointRoundTrips()
    {
        var p = new Pose(new Vector3(1, 2, 3), Quaternion.CreateFromYawPitchRoll(1, 0.5f, -0.2f));
        var point = new Vector3(-4, 5, 0.5f);
        Vector3 back = p.InverseTransformPoint(p.TransformPoint(point));
        Assert.True(Vector3.Distance(point, back) < 1e-5f);
    }

    [Fact]
    public void Pose_ComposeMatchesSequentialTransforms()
    {
        var parent = new Pose(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        var local = new Pose(new Vector3(1, 0, 0), Quaternion.Identity);
        Pose world = Pose.Compose(parent, local);
        // Rotating +X by +90 degrees about Y gives -Z.
        Assert.True(Vector3.Distance(world.Position, new Vector3(10, 0, -1)) < 1e-5f, world.Position.ToString());
    }

    [Fact]
    public void Double3_KeepsMillimetersAtSolarSystemScale()
    {
        var a = new Double3(5.9e12, 0, 0);
        var b = a + new Double3(0.001, 0, 0);
        Assert.Equal(0.001, (b - a).X, 3);
    }
}
