using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The layout of an own-ray list of the screen tracer, one per ray kind (docs/plans/temporal-renderer.md, section 6, "The
/// ray budget"), which own_rays.glsl declares: a header of four uints, the own-ray dispatch's
/// VkDispatchIndirectCommand (x, y, z) and the count, then one pixel per entry, x in the low and y in the high 16 bits.
/// </summary>
internal static class OwnRayList
{
    /// <summary>The bytes before the entries: the indirect command and the count.</summary>
    public const int HeaderBytes = 16;

    /// <summary>The header of an empty list, whose dispatch has no workgroup: (0, 1, 1) and a count of 0.</summary>
    public static ReadOnlySpan<uint> EmptyHeader => [0, 1, 1, 0];

    /// <summary>The bytes of the list of a <paramref name="screen"/> of W × H: the header and W · H entries, because every pixel can trace its own ray.</summary>
    public static ulong Bytes(Extent2D screen) => HeaderBytes + sizeof(uint) * (ulong)screen.Width * screen.Height;

    /// <summary>The listed pixels of a list whose words are <paramref name="words"/>, from its header on, in the order of the list.</summary>
    public static (int X, int Y)[] Pixels(ReadOnlySpan<uint> words)
    {
        const int header = HeaderBytes / sizeof(uint);
        // The count follows the indirect command's three words.
        var pixels = new (int X, int Y)[words[header - 1]];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (X: (int)(words[header + i] & 0xffff), Y: (int)(words[header + i] >> 16));
        return pixels;
    }

    /// <summary>
    /// Why the descriptor of the list of a <paramref name="screen"/> exceeds a device's
    /// <paramref name="maxStorageBufferRange"/>, or null when it fits. Vulkan guarantees 2^27 bytes, 33.5 million pixels.
    /// </summary>
    internal static string? RangeProblem(Extent2D screen, uint maxStorageBufferRange) =>
        Bytes(screen) > maxStorageBufferRange
            ? $"The reflections' own-ray list of a screen of {screen.Width} × {screen.Height} takes {Bytes(screen)} bytes, above the device's maxStorageBufferRange of {maxStorageBufferRange}."
            : null;
}
