using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>A Vulkan call returned an error.</summary>
public sealed class VulkanException(string operation, Result result) : Exception($"{operation} failed: {result}")
{
    /// <summary>The result code of the failed call.</summary>
    public Result Result { get; } = result;
}

internal static class VulkanResultExtensions
{
    /// <summary>Throws a <see cref="VulkanException"/> unless <paramref name="result"/> is <see cref="Result.Success"/>.</summary>
    public static void Check(this Result result, string operation)
    {
        if (result != Result.Success)
            throw new VulkanException(operation, result);
    }
}
