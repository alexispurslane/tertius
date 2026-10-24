using System.Reflection;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// A pose tree's stable ids and generations are its private storage. With both, code could build a live
/// <see cref="PoseNode"/> for any index and skip the generation check. Only the frame tree, in the same module, reads
/// them, for its walks over every frame.
/// </summary>
public sealed class PoseTreeStorageAccessTests
{
    [Theory]
    [InlineData(nameof(PoseTree<>.IdsDense))]
    [InlineData(nameof(PoseTree<>.Generations))]
    public void APoseTreeShowsItsStorageOnlyInsideTheCoreModule(string member)
    {
        PropertyInfo? property = typeof(PoseTree<Pose>).GetProperty(member,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        property.Should().NotBeNull();
        MethodInfo getter = property!.GetGetMethod(nonPublic: true)!;
        getter.IsAssembly.Should().BeTrue($"{member} must be internal, so that no code outside Tertius.Core reads it");
    }
}
