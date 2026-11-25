using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The model handle of the render seam: like the mesh handle, it stores its value plus one, so that a zeroed handle is
/// none and the first model is 0.
/// </summary>
public sealed class ModelIdTests
{
    [Fact]
    public void TheDefaultModelIdIsNone()
    {
        default(ModelId).IsNone.Should().BeTrue();
        default(ModelId).Should().Be(ModelId.None);
    }

    [Fact]
    public void TheFirstModelIdKeepsTheValueZeroAndIsNotNone()
    {
        var first = new ModelId(0);

        first.Value.Should().Be(0);
        first.IsNone.Should().BeFalse();
    }

    [Fact]
    public void AModelIdNamesItsValue()
    {
        new ModelId(7).ToString().Should().Be("ModelId(7)");
    }

    [Fact]
    public void TheNoneModelIdNamesItselfNone()
    {
        ModelId.None.ToString().Should().Be("ModelId(none)");
    }
}
