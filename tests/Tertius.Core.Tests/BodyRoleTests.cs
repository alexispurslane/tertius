using AwesomeAssertions;

namespace Tertius.Core.Tests;

public sealed class BodyRoleTests
{
    [Fact]
    public void TheCheckOfABodyDescriptionRefusesARoleThatBodyRoleDoesNotDefine()
    {
        var desc = new BodyDesc(Shape: new SphereShapeDesc(1));

        Action check = () => desc.Problem((BodyRole)99);

        check.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("role");
    }
}
