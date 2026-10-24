using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Tertius.Rendering;

/// <summary>
/// The Vulkan instance, the chosen physical device, the logical device and its one graphics queue. Optionally a
/// validation messenger and a window surface. Requires Vulkan 1.3 with dynamic rendering, synchronization2,
/// descriptor indexing, multi-draw indirect and depth clamp.
/// </summary>
internal sealed unsafe class VulkanDevice : IDisposable
{
    private const string ValidationLayer = "VK_LAYER_KHRONOS_validation";

    private readonly GCHandle logHandle;
    private readonly ExtDebugUtils? debugUtils;
    private readonly DebugUtilsMessengerEXT messenger;
    private bool disposed;

    private VulkanDevice(
        Vk vk, Instance instance, ValidationLog log, GCHandle logHandle, ExtDebugUtils? debugUtils,
        DebugUtilsMessengerEXT messenger, KhrSurface? surfaceApi, SurfaceKHR surface, PhysicalDevice physicalDevice,
        uint queueFamily, Device device, KhrSwapchain? swapchainApi, bool validation)
    {
        Vk = vk;
        Instance = instance;
        Log = log;
        this.logHandle = logHandle;
        this.debugUtils = debugUtils;
        this.messenger = messenger;
        SurfaceApi = surfaceApi;
        Surface = surface;
        PhysicalDevice = physicalDevice;
        QueueFamily = queueFamily;
        Device = device;
        SwapchainApi = swapchainApi;
        Validation = validation;
        vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties properties);
        Properties = properties;
        DeviceName = Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? "unknown";
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out PhysicalDeviceMemoryProperties memory);
        MemoryProperties = memory;
        vk.GetDeviceQueue(device, queueFamily, 0, out Queue queue);
        Queue = queue;
    }

    public Vk Vk { get; }
    public Instance Instance { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public Device Device { get; }
    public Queue Queue { get; }
    public uint QueueFamily { get; }
    public PhysicalDeviceProperties Properties { get; }
    public PhysicalDeviceMemoryProperties MemoryProperties { get; }
    public ValidationLog Log { get; }
    public bool Validation { get; }
    public string DeviceName { get; }

    /// <summary>The window surface, or a null handle for a headless device.</summary>
    public SurfaceKHR Surface { get; }

    public KhrSurface? SurfaceApi { get; }
    public KhrSwapchain? SwapchainApi { get; }

    /// <summary>
    /// Creates the instance and device. <paramref name="surfaceExtensions"/> and <paramref name="createSurface"/> are
    /// given for a window; the chosen queue family must then be able to present to that surface.
    /// </summary>
    public static VulkanDevice Create(bool validation, IReadOnlyList<string> surfaceExtensions,
        Func<Instance, SurfaceKHR>? createSurface)
    {
        Vk vk = Vk.GetApi();
        var log = new ValidationLog();
        GCHandle logHandle = GCHandle.Alloc(log);
        var strings = new NativeStrings();
        Instance instance = default;
        DebugUtilsMessengerEXT messenger = default;
        ExtDebugUtils? debugUtils = null;
        KhrSurface? surfaceApi = null;
        SurfaceKHR surface = default;
        Device device = default;
        try
        {
            HashSet<string> available = InstanceExtensions(vk, null);
            if (validation && !HasLayer(vk, ValidationLayer))
                throw new NotSupportedException($"{ValidationLayer} is not installed. Check VK_LAYER_PATH.");

            var extensions = new List<string>(surfaceExtensions);
            bool portability = available.Contains("VK_KHR_portability_enumeration");
            if (portability)
                extensions.Add("VK_KHR_portability_enumeration");
            if (validation)
            {
                extensions.Add(ExtDebugUtils.ExtensionName);
                // The validation layer provides VK_EXT_layer_settings itself.
                extensions.Add("VK_EXT_layer_settings");
            }

            byte* appName = strings.Get("Tertius");
            var app = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                ApplicationVersion = Vk.MakeVersion(0, 1, 0),
                PEngineName = appName,
                EngineVersion = Vk.MakeVersion(0, 1, 0),
                ApiVersion = Vk.Version13
            };

            DebugUtilsMessengerCreateInfoEXT messengerInfo = ValidationLog.CreateInfo(logHandle);
            byte* layer = strings.Get(ValidationLayer);
            // Synchronization validation checks barriers, semaphores and fences against the actual accesses. The two
            // extra checks are off by default in the layer: attachment loads after a store in an earlier rendering
            // instance (the G-buffer and HDR targets are loaded across passes), and shader accesses through
            // descriptors (the instance, material and light buffers and the textures).
            uint enabled = Vk.True;
            ReadOnlySpan<string> syncSettings =
                ["validate_sync", "syncval_load_op_after_store_op_validation", "syncval_shader_accesses_heuristic"];
            LayerSettingEXT* settings = stackalloc LayerSettingEXT[syncSettings.Length];
            for (int i = 0; i < syncSettings.Length; i++)
                settings[i] = new()
                {
                    PLayerName = layer,
                    PSettingName = strings.Get(syncSettings[i]),
                    Type = LayerSettingTypeEXT.Bool32Ext,
                    ValueCount = 1,
                    PValues = &enabled
                };
            var layerSettings = new LayerSettingsCreateInfoEXT
            {
                SType = StructureType.LayerSettingsCreateInfoExt,
                PNext = &messengerInfo,
                SettingCount = (uint)syncSettings.Length,
                PSettings = settings
            };

            byte** extensionNames = strings.Array(extensions);
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                // Chaining the messenger here also reports problems inside vkCreateInstance and vkDestroyInstance.
                PNext = validation ? &layerSettings : null,
                Flags = portability ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0,
                PApplicationInfo = &app,
                EnabledLayerCount = validation ? 1u : 0u,
                PpEnabledLayerNames = validation ? &layer : null,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = extensionNames
            };
            vk.CreateInstance(&instanceInfo, null, out instance).Check("vkCreateInstance");

            if (validation)
            {
                if (!vk.TryGetInstanceExtension(instance, out debugUtils))
                    throw new NotSupportedException("VK_EXT_debug_utils is not available.");
                debugUtils!.CreateDebugUtilsMessenger(instance, &messengerInfo, null, out messenger)
                    .Check("vkCreateDebugUtilsMessengerEXT");
            }

            if (createSurface != null)
            {
                if (!vk.TryGetInstanceExtension(instance, out surfaceApi))
                    throw new NotSupportedException("VK_KHR_surface is not available.");
                surface = createSurface(instance);
            }

            (PhysicalDevice physicalDevice, uint queueFamily) = ChoosePhysicalDevice(vk, instance, surfaceApi, surface);
            device = CreateLogicalDevice(vk, physicalDevice, queueFamily, surface.Handle != 0, strings);

            KhrSwapchain? swapchainApi = null;
            return surface.Handle != 0 && !vk.TryGetDeviceExtension(instance, device, out swapchainApi)
                ? throw new NotSupportedException("VK_KHR_swapchain is not available.")
                : new(vk, instance, log, logHandle, debugUtils, messenger, surfaceApi, surface,
                    physicalDevice, queueFamily, device, swapchainApi, validation);
        }
        catch
        {
            if (device.Handle != 0)
                vk.DestroyDevice(device, null);
            if (surface.Handle != 0)
                surfaceApi!.DestroySurface(instance, surface, null);
            if (messenger.Handle != 0)
                debugUtils!.DestroyDebugUtilsMessenger(instance, messenger, null);
            if (instance.Handle != 0)
                vk.DestroyInstance(instance, null);
            logHandle.Free();
            vk.Dispose();
            throw;
        }
        finally
        {
            strings.Dispose();
        }
    }

    private static (PhysicalDevice Device, uint QueueFamily) ChoosePhysicalDevice(Vk vk, Instance instance,
        KhrSurface? surfaceApi, SurfaceKHR surface)
    {
        uint count = 0;
        vk.EnumeratePhysicalDevices(instance, ref count, null).Check("vkEnumeratePhysicalDevices");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
        {
            vk.EnumeratePhysicalDevices(instance, ref count, p).Check("vkEnumeratePhysicalDevices");
        }

        var reasons = new StringBuilder();
        foreach (PhysicalDevice candidate in devices)
        {
            vk.GetPhysicalDeviceProperties(candidate, out PhysicalDeviceProperties properties);
            string name = Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? "?";
            if (properties.ApiVersion < Vk.Version13)
            {
                reasons.Append($"{name}: Vulkan 1.3 is required. ");
                continue;
            }

            string? missing = MissingFeature(vk, candidate);
            if (missing != null)
            {
                reasons.Append($"{name}: missing {missing}. ");
                continue;
            }

            uint familyCount = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref familyCount, null);
            var families = new QueueFamilyProperties[familyCount];
            fixed (QueueFamilyProperties* p = families)
            {
                vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref familyCount, p);
            }

            for (uint i = 0; i < familyCount; i++)
            {
                if ((families[i].QueueFlags & QueueFlags.GraphicsBit) == 0)
                    continue;
                if (surfaceApi != null)
                {
                    surfaceApi.GetPhysicalDeviceSurfaceSupport(candidate, i, surface, out Bool32 supported)
                        .Check("vkGetPhysicalDeviceSurfaceSupportKHR");
                    if (!supported)
                        continue;
                }

                return (candidate, i);
            }

            reasons.Append($"{name}: no graphics queue that can present. ");
        }

        throw new NotSupportedException($"No usable Vulkan device. {reasons}");
    }

    private static string? MissingFeature(Vk vk, PhysicalDevice device)
    {
        var f13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features };
        var f12 = new PhysicalDeviceVulkan12Features
            { SType = StructureType.PhysicalDeviceVulkan12Features, PNext = &f13 };
        var f2 = new PhysicalDeviceFeatures2 { SType = StructureType.PhysicalDeviceFeatures2, PNext = &f12 };
        vk.GetPhysicalDeviceFeatures2(device, &f2);
        PhysicalDeviceFeatures f = f2.Features;
        if (!f.MultiDrawIndirect) return "multiDrawIndirect";
        if (!f.DrawIndirectFirstInstance) return "drawIndirectFirstInstance";
        if (!f.DepthClamp) return "depthClamp";
        if (!f12.RuntimeDescriptorArray) return "runtimeDescriptorArray";
        if (!f12.DescriptorBindingPartiallyBound) return "descriptorBindingPartiallyBound";
        if (!f12.DescriptorBindingVariableDescriptorCount) return "descriptorBindingVariableDescriptorCount";
        if (!f12.DescriptorBindingSampledImageUpdateAfterBind) return "descriptorBindingSampledImageUpdateAfterBind";
        if (!f12.ShaderSampledImageArrayNonUniformIndexing) return "shaderSampledImageArrayNonUniformIndexing";
        return !f13.DynamicRendering
            ? "dynamicRendering"
            : !f13.Synchronization2
                ? "synchronization2"
                : !f13.ShaderDemoteToHelperInvocation
                    ? "shaderDemoteToHelperInvocation"
                    : null;
    }

    private static Device CreateLogicalDevice(Vk vk, PhysicalDevice physicalDevice, uint queueFamily, bool swapchain,
        NativeStrings strings)
    {
        HashSet<string> available = DeviceExtensions(vk, physicalDevice);
        var extensions = new List<string>();
        if (swapchain)
            extensions.Add(KhrSwapchain.ExtensionName);

        var f13 = new PhysicalDeviceVulkan13Features
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            DynamicRendering = true,
            Synchronization2 = true,
            // glslc maps `discard` to OpDemoteToHelperInvocation for SPIR-V 1.6 (the Vulkan 1.3 target).
            ShaderDemoteToHelperInvocation = true
        };
        var f12 = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &f13,
            DescriptorIndexing = true,
            RuntimeDescriptorArray = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingVariableDescriptorCount = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            ShaderSampledImageArrayNonUniformIndexing = true
        };
        var features = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &f12,
            Features = new()
            {
                MultiDrawIndirect = true,
                DrawIndirectFirstInstance = true,
                DepthClamp = true,
                SamplerAnisotropy = true
            }
        };

        // MoltenVK is a portability implementation: it must be enabled, and its subset features are enabled as
        // reported so that validation knows exactly what the device can do.
        var portabilityFeatures = new PhysicalDevicePortabilitySubsetFeaturesKHR
            { SType = StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr };
        if (available.Contains("VK_KHR_portability_subset"))
        {
            extensions.Add("VK_KHR_portability_subset");
            var query = new PhysicalDeviceFeatures2
                { SType = StructureType.PhysicalDeviceFeatures2, PNext = &portabilityFeatures };
            vk.GetPhysicalDeviceFeatures2(physicalDevice, &query);
            portabilityFeatures.PNext = null;
            f13.PNext = &portabilityFeatures;
        }

        float priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority
        };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &features,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = (uint)extensions.Count,
            PpEnabledExtensionNames = strings.Array(extensions)
        };
        vk.CreateDevice(physicalDevice, &deviceInfo, null, out Device device).Check("vkCreateDevice");
        return device;
    }

    private static HashSet<string> InstanceExtensions(Vk vk, byte* layer)
    {
        uint count = 0;
        vk.EnumerateInstanceExtensionProperties(layer, ref count, null).Check("vkEnumerateInstanceExtensionProperties");
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = properties)
        {
            vk.EnumerateInstanceExtensionProperties(layer, ref count, p)
                .Check("vkEnumerateInstanceExtensionProperties");
        }

        var names = new HashSet<string>();
        foreach (ExtensionProperties property in properties)
            names.Add(Marshal.PtrToStringUTF8((nint)property.ExtensionName) ?? "");
        return names;
    }

    private static HashSet<string> DeviceExtensions(Vk vk, PhysicalDevice device)
    {
        uint count = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, null)
            .Check("vkEnumerateDeviceExtensionProperties");
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = properties)
        {
            vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, p)
                .Check("vkEnumerateDeviceExtensionProperties");
        }

        var names = new HashSet<string>();
        foreach (ExtensionProperties property in properties)
            names.Add(Marshal.PtrToStringUTF8((nint)property.ExtensionName) ?? "");
        return names;
    }

    private static bool HasLayer(Vk vk, string name)
    {
        uint count = 0;
        vk.EnumerateInstanceLayerProperties(ref count, null).Check("vkEnumerateInstanceLayerProperties");
        var layers = new LayerProperties[count];
        fixed (LayerProperties* p = layers)
        {
            vk.EnumerateInstanceLayerProperties(ref count, p).Check("vkEnumerateInstanceLayerProperties");
        }

        foreach (LayerProperties layer in layers)
            if (Marshal.PtrToStringUTF8((nint)layer.LayerName) == name)
                return true;
        return false;
    }

    /// <summary>Finds a memory type allowed by <paramref name="typeBits"/> with all <paramref name="required"/> flags, preferring the most
    /// <paramref name="preferred"/> flags.</summary>
    public int FindMemoryType(uint typeBits, MemoryPropertyFlags required, MemoryPropertyFlags preferred)
    {
        int best = -1;
        int bestScore = -1;
        PhysicalDeviceMemoryProperties memory = MemoryProperties;
        for (int i = 0; i < memory.MemoryTypeCount; i++)
        {
            if ((typeBits & (1u << i)) == 0)
                continue;
            MemoryPropertyFlags flags = memory.MemoryTypes[i].PropertyFlags;
            if ((flags & required) != required)
                continue;
            int score = BitOperations.PopCount((uint)(flags & preferred));
            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        return best;
    }

    public FormatProperties FormatProperties(Format format)
    {
        Vk.GetPhysicalDeviceFormatProperties(PhysicalDevice, format, out FormatProperties properties);
        return properties;
    }

    /// <summary>Gives a Vulkan object a name that validation messages and GPU debuggers show. Only active with validation.</summary>
    public void SetName(ObjectType type, ulong handle, string name)
    {
        if (debugUtils == null)
            return;
        int length = Encoding.UTF8.GetByteCount(name);
        Span<byte> bytes = length < 256 ? stackalloc byte[length + 1] : new byte[length + 1];
        Encoding.UTF8.GetBytes(name, bytes);
        bytes[length] = 0;
        fixed (byte* p = bytes)
        {
            var info = new DebugUtilsObjectNameInfoEXT
            {
                SType = StructureType.DebugUtilsObjectNameInfoExt,
                ObjectType = type,
                ObjectHandle = handle,
                PObjectName = p
            };
            debugUtils.SetDebugUtilsObjectName(Device, &info);
        }
    }

    /// <summary>Opens a named region in a command buffer for validation messages and GPU captures.</summary>
    public void BeginLabel(CommandBuffer commandBuffer, string name)
    {
        if (debugUtils == null)
            return;
        int length = Encoding.UTF8.GetByteCount(name);
        Span<byte> bytes = length < 256 ? stackalloc byte[length + 1] : new byte[length + 1];
        Encoding.UTF8.GetBytes(name, bytes);
        bytes[length] = 0;
        fixed (byte* p = bytes)
        {
            var label = new DebugUtilsLabelEXT { SType = StructureType.DebugUtilsLabelExt, PLabelName = p };
            debugUtils.CmdBeginDebugUtilsLabel(commandBuffer, &label);
        }
    }

    public void EndLabel(CommandBuffer commandBuffer)
    {
        debugUtils?.CmdEndDebugUtilsLabel(commandBuffer);
    }

    public void WaitIdle() => Vk.DeviceWaitIdle(Device).Check("vkDeviceWaitIdle");

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Vk.DestroyDevice(Device, null);
        if (Surface.Handle != 0)
            SurfaceApi!.DestroySurface(Instance, Surface, null);
        if (messenger.Handle != 0)
            debugUtils!.DestroyDebugUtilsMessenger(Instance, messenger, null);
        Vk.DestroyInstance(Instance, null);
        // The messenger chained into vkCreateInstance stays active until vkDestroyInstance returns.
        logHandle.Free();
        Vk.Dispose();
    }

    /// <summary>Null-terminated UTF-8 copies of strings for Vulkan create infos, freed together.</summary>
    private sealed class NativeStrings : IDisposable
    {
        private readonly List<nint> allocations = [];

        public byte* Get(string value)
        {
            int length = Encoding.UTF8.GetByteCount(value);
            nint p = Marshal.AllocHGlobal(length + 1);
            allocations.Add(p);
            var bytes = new Span<byte>((void*)p, length + 1);
            Encoding.UTF8.GetBytes(value, bytes);
            bytes[length] = 0;
            return (byte*)p;
        }

        public byte** Array(List<string> values)
        {
            nint array = Marshal.AllocHGlobal(Math.Max(1, values.Count) * sizeof(nint));
            allocations.Add(array);
            for (int i = 0; i < values.Count; i++)
                ((byte**)array)[i] = Get(values[i]);
            return (byte**)array;
        }

        public void Dispose()
        {
            foreach (nint p in allocations)
                Marshal.FreeHGlobal(p);
            allocations.Clear();
        }
    }
}
