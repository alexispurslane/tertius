using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Collects the warnings and errors that the validation layer and the loader report through VK_EXT_debug_utils.
/// The layer calls back on whatever thread made the Vulkan call, so access is locked.
/// </summary>
internal sealed unsafe class ValidationLog
{
    // MoltenVK 1.4.2 reports this for every pipeline that draws into a color attachment of a format that cannot blend,
    // whether or not the pipeline blends. GraphicsPipelineDesc.Create refuses a pipeline that blends into an integer
    // format, so every such report is false.
    private const string IntegerTargetBlendPrefix =
        "VK_ERROR_FEATURE_NOT_PRESENT: Blending is enabled for attachment with format VK_FORMAT_";

    private readonly Lock gate = new();
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Snapshot()
    {
        lock (gate)
        {
            return [.. messages];
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> is MoltenVK's false report of blending on an attachment of an unsigned integer
    /// format, which the log leaves out.
    /// </summary>
    public static bool IsFalseIntegerBlendReport(string text) =>
        text.StartsWith(IntegerTargetBlendPrefix, StringComparison.Ordinal)
        && text.Contains("_UINT, which does not support it.", StringComparison.Ordinal);

    public void Add(string message)
    {
        lock (gate)
        {
            messages.Add(message);
        }
    }

    /// <summary>
    /// The messenger records warnings and errors of every type: general (loader), validation and performance.
    /// Info and verbose messages are not requested, because they describe normal operation.
    /// </summary>
    public static DebugUtilsMessengerCreateInfoEXT CreateInfo(GCHandle self) =>
        new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity =
                DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType =
                DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new(&OnMessage),
            PUserData = (void*)GCHandle.ToIntPtr(self),
        };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static Bool32 OnMessage(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData
    )
    {
        if (GCHandle.FromIntPtr((nint)userData).Target is ValidationLog log)
        {
            string id = data->PMessageIdName != null ? Marshal.PtrToStringUTF8((nint)data->PMessageIdName) ?? "" : "";
            string text = data->PMessage != null ? Marshal.PtrToStringUTF8((nint)data->PMessage) ?? "" : "";
            if (IsFalseIntegerBlendReport(text))
                return Vk.False;
            string level = "info";
            if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
                level = "error";
            else if ((severity & DebugUtilsMessageSeverityFlagsEXT.WarningBitExt) != 0)
                level = "warning";
            log.Add($"[{level}, {types}] {id}: {text}");
        }

        return Vk.False;
    }
}
