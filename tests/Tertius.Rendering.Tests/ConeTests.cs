using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="Cone.Of"/>: the clamped angles and the two cosines of a spot light's cone, whose edges the shader's
/// smoothstep fades between (docs/plans/local-light-shadows.md, "Portable Vulkan").
/// </summary>
public sealed class ConeTests
{
    /// <summary>GLSL's smoothstep in floats, as the shader fades a spot light from its outer to its inner edge.</summary>
    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    public static TheoryData<float> OuterAngles =>
        [LightUnits.MinConeAngle, 0.00873f, 0.0141f, 0.02f, 0.5f, 2f, MathF.PI];

    [Theory]
    [MemberData(nameof(OuterAngles))]
    public void TheAxisOfEveryConeGetsTheWholeIntensity(float outerAngle)
    {
        Cone cone = Cone.Of(axis: Vector3.UnitZ, innerAngle: outerAngle, outerAngle: outerAngle);

        Smoothstep(cone.CosOuter, cone.CosInner, 1f)
            .Should()
            .Be(1, "the intensity counts the whole power through the cone, so the axis must show all of it");
    }

    [Theory]
    [MemberData(nameof(OuterAngles))]
    public void TheInnerEdgeLiesStrictlyInsideTheOuterEdge(float outerAngle)
    {
        Cone cone = Cone.Of(axis: Vector3.UnitZ, innerAngle: outerAngle + 0.1f, outerAngle: outerAngle);

        cone.CosInner.Should()
            .BeGreaterThan(cone.CosOuter, "smoothstep is undefined in SPIR-V for equal or reversed edges");
    }

    [Fact]
    public void AnInnerEdgeInsideTheOuterEdgeStaysWhereItIs() =>
        Cone.Of(axis: Vector3.UnitZ, innerAngle: 0.3f, outerAngle: 0.5f).CosInner.Should().Be(MathF.Cos(0.3f));

    [Fact]
    public void AWideConeWithEqualEdgesFadesOverATenThousandthOfACosine()
    {
        Cone cone = Cone.Of(axis: Vector3.UnitZ, innerAngle: 0.5f, outerAngle: 0.5f);

        (cone.CosInner - cone.CosOuter).Should().BeApproximately(1e-4f, 1e-7f);
    }

    [Fact]
    public void AConeNarrowerThanTheSmallestCountsAsTheSmallest()
    {
        Cone cone = Cone.Of(axis: Vector3.UnitZ, innerAngle: 0, outerAngle: 1e-5f);

        cone.OuterAngle.Should().Be(LightUnits.MinConeAngle);
        cone.CosOuter.Should().Be(MathF.Cos(LightUnits.MinConeAngle));
    }

    [Fact]
    public void AConeWiderThanAHalfTurnCountsAsAHalfTurn()
    {
        Cone cone = Cone.Of(axis: Vector3.UnitZ, innerAngle: 4, outerAngle: 4);

        cone.OuterAngle.Should().Be(MathF.PI);
        cone.CosOuter.Should().Be(MathF.Cos(MathF.PI));
    }

    [Fact]
    public void TheConeKeepsItsAxis() =>
        Cone.Of(axis: Vector3.UnitX, innerAngle: 0.3f, outerAngle: 0.5f).Axis.Should().Be(Vector3.UnitX);
}
