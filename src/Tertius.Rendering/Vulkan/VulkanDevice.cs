using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Tertius.Rendering;

/// <summary>
/// The Vulkan instance, the chosen physical device, the logical device and its one graphics and compute queue. Optionally a
/// validation messenger and a window surface. Requires Vulkan 1.3 with dynamic rendering, synchronization2,
/// descriptor indexing, multi-draw indirect, depth clamp, dual-source blending and anisotropic filtering
/// (ARCHITECTURE.md, "Required device features").
/// </summary>
internal sealed unsafe class VulkanDevice : IDisposable
{
    private const string ValidationLayer = "VK_LAYER_KHRONOS_validation";

    private readonly GCHandle logHandle;
    private readonly ExtDebugUtils? debugUtils;
    private readonly DebugUtilsMessengerEXT messenger;
    private bool disposed;

    private VulkanDevice(
        in DeviceHandles handles,
        ValidationLog log,
        PhysicalDevice physicalDevice,
        uint queueFamily,
        KhrSwapchain? swapchainApi,
        bool validation
    )
    {
        Vk vk = handles.Vk;
        Vk = vk;
        Instance = handles.Instance;
        Log = log;
        logHandle = handles.LogHandle;
        debugUtils = handles.DebugUtils;
        messenger = handles.Messenger;
        SurfaceApi = handles.SurfaceApi;
        Surface = handles.Surface;
        PhysicalDevice = physicalDevice;
        QueueFamily = queueFamily;
        Device device = handles.Device;
        Device = device;
        SwapchainApi = swapchainApi;
        Validation = validation;
        vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties properties);
        Properties = properties;
        DeviceName = Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? "unknown";
        var driver = new PhysicalDeviceDriverProperties { SType = StructureType.PhysicalDeviceDriverProperties };
        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &driver,
        };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        Driver = driver.DriverID;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out PhysicalDeviceMemoryProperties memory);
        MemoryProperties = memory;
        vk.GetDeviceQueue(device: device, queueFamilyIndex: queueFamily, queueIndex: 0, pQueue: out Queue queue);
        Queue = queue;
        Timestamps = new(
            QueueFamilies(vk, physicalDevice)[queueFamily].TimestampValidBits,
            properties.Limits.TimestampPeriod
        );
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

    /// <summary>The driver of <see cref="PhysicalDevice"/>, by its Vulkan 1.2 driver id.</summary>
    public DriverId Driver { get; }

    /// <summary>The clock of the timestamps that <see cref="Queue"/> writes.</summary>
    public TimestampClock Timestamps { get; }

    /// <summary>The window surface, or a null handle for a headless device.</summary>
    public SurfaceKHR Surface { get; }

    public KhrSurface? SurfaceApi { get; }
    public KhrSwapchain? SwapchainApi { get; }

    /// <summary>
    /// Creates the instance and device. <paramref name="surfaceExtensions"/> and <paramref name="createSurface"/> are
    /// given for a window; the chosen queue family must then be able to present to that surface.
    /// </summary>
    public static VulkanDevice Create(
        bool validation,
        IReadOnlyList<string> surfaceExtensions,
        Func<Instance, SurfaceKHR>? createSurface
    )
    {
        var vk = Vk.GetApi();
        var log = new ValidationLog();
        var handles = new DeviceHandles { Vk = vk, LogHandle = GCHandle.Alloc(log) };
        var strings = new NativeStrings();
        try
        {
            CreateInstanceAndMessenger(
                validation: validation,
                surfaceExtensions: surfaceExtensions,
                strings: strings,
                handles: ref handles
            );
            if (createSurface != null)
            {
                if (!vk.TryGetInstanceExtension(handles.Instance, out handles.SurfaceApi))
                    throw new NotSupportedException("VK_KHR_surface is not available.");
                handles.Surface = createSurface(handles.Instance);
            }

            (PhysicalDevice physicalDevice, uint queueFamily) = ChoosePhysicalDevice(
                vk: vk,
                instance: handles.Instance,
                surfaceApi: handles.SurfaceApi,
                surface: handles.Surface
            );
            handles.Device = CreateLogicalDevice(
                vk: vk,
                physicalDevice: physicalDevice,
                queueFamily: queueFamily,
                swapchain: handles.Surface.Handle != 0,
                strings: strings
            );
            return new(
                handles: handles,
                log: log,
                physicalDevice: physicalDevice,
                queueFamily: queueFamily,
                swapchainApi: SwapchainApiOf(handles),
                validation: validation
            );
        }
        catch
        {
            handles.Destroy();
            throw;
        }
        finally
        {
            strings.Dispose();
        }
    }

    /// <summary>
    /// Creates the instance into <paramref name="handles"/>, and with validation the messenger of the validation log, which
    /// also reports problems inside the creation of the instance.
    /// </summary>
    private static void CreateInstanceAndMessenger(
        bool validation,
        IReadOnlyList<string> surfaceExtensions,
        NativeStrings strings,
        ref DeviceHandles handles
    )
    {
        Vk vk = handles.Vk;
        (List<string> extensions, bool portability) = InstanceExtensionNames(
            vk: vk,
            validation: validation,
            surfaceExtensions: surfaceExtensions
        );
        DebugUtilsMessengerCreateInfoEXT messengerInfo = ValidationLog.CreateInfo(handles.LogHandle);
        CreateInstance(
            vk: vk,
            validation: validation,
            extensions: extensions,
            portability: portability,
            strings: strings,
            messengerInfo: &messengerInfo,
            instance: out handles.Instance
        );
        if (!validation)
            return;
        if (!vk.TryGetInstanceExtension(handles.Instance, out handles.DebugUtils) || handles.DebugUtils is null)
            throw new NotSupportedException("VK_EXT_debug_utils is not available.");
        handles
            .DebugUtils.CreateDebugUtilsMessenger(
                instance: handles.Instance,
                pCreateInfo: &messengerInfo,
                pAllocator: null,
                pMessenger: out handles.Messenger
            )
            .Check("vkCreateDebugUtilsMessengerEXT");
    }

    /// <summary>The swapchain extension of the device in <paramref name="handles"/> when it has a surface, or null.</summary>
    private static KhrSwapchain? SwapchainApiOf(in DeviceHandles handles)
    {
        KhrSwapchain? swapchainApi = null;
        return
            handles.Surface.Handle != 0
            && !handles.Vk.TryGetDeviceExtension(
                instance: handles.Instance,
                device: handles.Device,
                ext: out swapchainApi
            )
            ? throw new NotSupportedException("VK_KHR_swapchain is not available.")
            : swapchainApi;
    }

    /// <summary>
    /// The instance extensions to enable: <paramref name="surfaceExtensions"/>, portability enumeration where the loader
    /// offers it, and the debug and layer-settings extensions with validation. Throws when validation is asked for and its
    /// layer is not installed.
    /// </summary>
    private static (List<string> Extensions, bool Portability) InstanceExtensionNames(
        Vk vk,
        bool validation,
        IReadOnlyList<string> surfaceExtensions
    )
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

        return (extensions, portability);
    }

    /// <summary>
    /// Creates the instance with <paramref name="extensions"/>, and with validation the validation layer, its
    /// synchronization checks and the messenger of <paramref name="messengerInfo"/>, into <paramref name="instance"/>.
    /// </summary>
    private static void CreateInstance(
        Vk vk,
        bool validation,
        List<string> extensions,
        bool portability,
        NativeStrings strings,
        DebugUtilsMessengerCreateInfoEXT* messengerInfo,
        out Instance instance
    )
    {
        byte* appName = strings.Get("Tertius");
        var app = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = appName,
            ApplicationVersion = Vk.MakeVersion(0, 1, 0),
            PEngineName = appName,
            EngineVersion = Vk.MakeVersion(0, 1, 0),
            ApiVersion = Vk.Version13,
        };

        byte* layer = strings.Get(ValidationLayer);
        // Synchronization validation checks barriers, semaphores and fences against the actual accesses. The two
        // extra checks are off by default in the layer: attachment loads after a store in an earlier rendering
        // instance (the G-buffer and HDR targets are loaded across passes), and shader accesses through
        // descriptors (the instance, material and light buffers and the textures).
        uint enabled = Vk.True;
        ReadOnlySpan<string> syncSettings =
        [
            "validate_sync",
            "syncval_load_op_after_store_op_validation",
            "syncval_shader_accesses_heuristic",
        ];
        LayerSettingEXT* settings = stackalloc LayerSettingEXT[syncSettings.Length];
        for (int i = 0; i < syncSettings.Length; i++)
            settings[i] = new()
            {
                PLayerName = layer,
                PSettingName = strings.Get(syncSettings[i]),
                Type = LayerSettingTypeEXT.Bool32Ext,
                ValueCount = 1,
                PValues = &enabled,
            };
        var layerSettings = new LayerSettingsCreateInfoEXT
        {
            SType = StructureType.LayerSettingsCreateInfoExt,
            PNext = messengerInfo,
            SettingCount = (uint)syncSettings.Length,
            PSettings = settings,
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
            PpEnabledExtensionNames = extensionNames,
        };
        vk.CreateInstance(&instanceInfo, null, out instance).Check("vkCreateInstance");
    }

    private static (PhysicalDevice Device, uint QueueFamily) ChoosePhysicalDevice(
        Vk vk,
        Instance instance,
        KhrSurface? surfaceApi,
        SurfaceKHR surface
    )
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

            string? missing = MissingFeature(vk, candidate) ?? MissingSubgroupSupport(vk, candidate);
            if (missing != null)
            {
                reasons.Append($"{name}: missing {missing}. ");
                continue;
            }

            QueueFamilyProperties[] families = QueueFamilies(vk, candidate);
            for (uint i = 0; i < families.Length; i++)
            {
                // One queue runs the graphics passes and the exposure's compute pass, in one command buffer.
                const QueueFlags needed = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
                if ((families[i].QueueFlags & needed) != needed)
                    continue;
                if (surfaceApi != null)
                {
                    surfaceApi
                        .GetPhysicalDeviceSurfaceSupport(
                            physicalDevice: candidate,
                            queueFamilyIndex: i,
                            surface: surface,
                            pSupported: out Bool32 supported
                        )
                        .Check("vkGetPhysicalDeviceSurfaceSupportKHR");
                    if (!supported)
                        continue;
                }

                return (candidate, i);
            }

            reasons.Append($"{name}: no graphics and compute queue that can present. ");
        }

        throw new NotSupportedException($"No usable Vulkan device. {reasons}");
    }

    private static QueueFamilyProperties[] QueueFamilies(Vk vk, PhysicalDevice device)
    {
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, ref count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* p = families)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(device, ref count, p);
        }

        return families;
    }

    private static string? MissingFeature(Vk vk, PhysicalDevice device)
    {
        var f13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features };
        var f12 = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &f13,
        };
        var f2 = new PhysicalDeviceFeatures2 { SType = StructureType.PhysicalDeviceFeatures2, PNext = &f12 };
        vk.GetPhysicalDeviceFeatures2(device, &f2);
        return MissingFeature(f2.Features, f12, f13);
    }

    /// <summary>
    /// The first feature that the renderer requires and the device lacks, by the features that it reports, or null when it
    /// has every one (ARCHITECTURE.md, "Required device features").
    /// </summary>
    internal static string? MissingFeature(
        in PhysicalDeviceFeatures f,
        in PhysicalDeviceVulkan12Features f12,
        in PhysicalDeviceVulkan13Features f13
    ) =>
        // The order only picks which of several missing features the refusal names.
        MissingCoreFeature(f) ?? MissingVulkan12Feature(f12) ?? MissingVulkan13Feature(f13);

    private static string? MissingSubgroupSupport(Vk vk, PhysicalDevice device)
    {
        var p11 = new PhysicalDeviceVulkan11Properties { SType = StructureType.PhysicalDeviceVulkan11Properties };
        var p2 = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &p11 };
        vk.GetPhysicalDeviceProperties2(device, &p2);
        return MissingSubgroupSupport(p11.SubgroupSupportedStages, p11.SubgroupSupportedOperations);
    }

    /// <summary>
    /// The first subgroup support that the renderer requires and the device lacks, by its subgroup properties, or null
    /// when it has all (ARCHITECTURE.md, "Required device features"): `subgroupBroadcastFirst` in the fragment and compute
    /// shaders that read the map's ambient light (environment.glsl).
    /// </summary>
    internal static string? MissingSubgroupSupport(ShaderStageFlags stages, SubgroupFeatureFlags operations)
    {
        if ((operations & SubgroupFeatureFlags.BallotBit) == 0)
            return "subgroupSupportedOperations BALLOT";
        if ((stages & ShaderStageFlags.FragmentBit) == 0)
            return "subgroupSupportedStages FRAGMENT";
        if ((stages & ShaderStageFlags.ComputeBit) == 0)
            return "subgroupSupportedStages COMPUTE";
        return null;
    }

    private static string? MissingCoreFeature(in PhysicalDeviceFeatures f)
    {
        if (!f.MultiDrawIndirect)
            return "multiDrawIndirect";
        if (!f.DrawIndirectFirstInstance)
            return "drawIndirectFirstInstance";
        if (!f.DepthClamp)
            return "depthClamp";
        if (!f.DualSrcBlend)
            return "dualSrcBlend";
        if (!f.FragmentStoresAndAtomics)
            return "fragmentStoresAndAtomics";
        if (!f.SamplerAnisotropy)
            return "samplerAnisotropy";
        if (!f.ShaderStorageImageExtendedFormats)
            return "shaderStorageImageExtendedFormats";
        if (!f.ImageCubeArray)
            return "imageCubeArray";
        return null;
    }

    private static string? MissingVulkan12Feature(in PhysicalDeviceVulkan12Features f12)
    {
        // The feature itself, beside its parts, guarantees the limits of 500,000 descriptors after bind.
        if (!f12.DescriptorIndexing)
            return "descriptorIndexing";
        if (!f12.RuntimeDescriptorArray)
            return "runtimeDescriptorArray";
        if (!f12.DescriptorBindingPartiallyBound)
            return "descriptorBindingPartiallyBound";
        if (!f12.DescriptorBindingVariableDescriptorCount)
            return "descriptorBindingVariableDescriptorCount";
        if (!f12.DescriptorBindingSampledImageUpdateAfterBind)
            return "descriptorBindingSampledImageUpdateAfterBind";
        if (!f12.ShaderSampledImageArrayNonUniformIndexing)
            return "shaderSampledImageArrayNonUniformIndexing";
        return null;
    }

    private static string? MissingVulkan13Feature(in PhysicalDeviceVulkan13Features f13)
    {
        if (!f13.DynamicRendering)
            return "dynamicRendering";
        if (!f13.Synchronization2)
            return "synchronization2";
        if (!f13.ShaderDemoteToHelperInvocation)
            return "shaderDemoteToHelperInvocation";
        return null;
    }

    /// <summary>The Vulkan 1.3 features that the renderer enables, with no next structure.</summary>
    private static PhysicalDeviceVulkan13Features EnabledVulkan13Features() =>
        new()
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            DynamicRendering = true,
            Synchronization2 = true,
            // glslc maps `discard` to OpDemoteToHelperInvocation for SPIR-V 1.6 (the Vulkan 1.3 target).
            ShaderDemoteToHelperInvocation = true,
        };

    /// <summary>The Vulkan 1.2 features that the renderer enables, for the bindless textures, with no next structure.</summary>
    private static PhysicalDeviceVulkan12Features EnabledVulkan12Features() =>
        new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            DescriptorIndexing = true,
            RuntimeDescriptorArray = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingVariableDescriptorCount = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            ShaderSampledImageArrayNonUniformIndexing = true,
        };

    /// <summary>The Vulkan 1.0 features that the renderer enables.</summary>
    private static PhysicalDeviceFeatures EnabledCoreFeatures() =>
        new()
        {
            MultiDrawIndirect = true,
            DrawIndirectFirstInstance = true,
            DepthClamp = true,
            // The transparent pass blends with a second output: what a pane lets through.
            DualSrcBlend = true,
            // The ambient pass adds D's ambient part to the diffuse image with a storage write at skin pixels
            // (docs/plans/skin-and-hair.md, decision 14).
            FragmentStoresAndAtomics = true,
            SamplerAnisotropy = true,
            // The occlusion passes store R8 and R16F images (docs/plans/ambient-occlusion.md, decision 35).
            ShaderStorageImageExtendedFormats = true,
            // The environment maps of the chain's frames are cube arrays (docs/plans/environment-maps-per-frame.md,
            // decision 18).
            ImageCubeArray = true,
        };

    private static Device CreateLogicalDevice(
        Vk vk,
        PhysicalDevice physicalDevice,
        uint queueFamily,
        bool swapchain,
        NativeStrings strings
    )
    {
        HashSet<string> available = DeviceExtensions(vk, physicalDevice);
        var extensions = new List<string>();
        if (swapchain)
            extensions.Add(KhrSwapchain.ExtensionName);

        PhysicalDeviceVulkan13Features f13 = EnabledVulkan13Features();
        PhysicalDeviceVulkan12Features f12 = EnabledVulkan12Features();
        f12.PNext = &f13;
        var features = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &f12,
            Features = EnabledCoreFeatures(),
        };

        // MoltenVK is a portability implementation: it must be enabled, and its subset features are enabled as
        // reported so that validation knows exactly what the device can do.
        PhysicalDevicePortabilitySubsetFeaturesKHR portabilityFeatures;
        if (available.Contains("VK_KHR_portability_subset"))
        {
            extensions.Add("VK_KHR_portability_subset");
            portabilityFeatures = PortabilitySubset(vk, physicalDevice);
            f13.PNext = &portabilityFeatures;
        }

        return CreateDevice(
            vk: vk,
            physicalDevice: physicalDevice,
            queueFamily: queueFamily,
            features: &features,
            extensions: extensions,
            strings: strings
        );
    }

    /// <summary>The portability subset features that the device reports, unlinked from any chain.</summary>
    private static PhysicalDevicePortabilitySubsetFeaturesKHR PortabilitySubset(Vk vk, PhysicalDevice physicalDevice)
    {
        var portability = new PhysicalDevicePortabilitySubsetFeaturesKHR
        {
            SType = StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr,
        };
        var query = new PhysicalDeviceFeatures2 { SType = StructureType.PhysicalDeviceFeatures2, PNext = &portability };
        vk.GetPhysicalDeviceFeatures2(physicalDevice, &query);
        portability.PNext = null;
        return portability;
    }

    /// <summary>Creates the logical device with one queue of <paramref name="queueFamily"/>, the feature chain that
    /// starts at <paramref name="features"/>, and <paramref name="extensions"/>.</summary>
    private static Device CreateDevice(
        Vk vk,
        PhysicalDevice physicalDevice,
        uint queueFamily,
        PhysicalDeviceFeatures2* features,
        List<string> extensions,
        NativeStrings strings
    )
    {
        float priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = features,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = (uint)extensions.Count,
            PpEnabledExtensionNames = strings.Array(extensions),
        };
        vk.CreateDevice(
                physicalDevice: physicalDevice,
                pCreateInfo: &deviceInfo,
                pAllocator: null,
                pDevice: out Device device
            )
            .Check("vkCreateDevice");
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
        vk.EnumerateDeviceExtensionProperties(
                physicalDevice: device,
                pLayerName: (byte*)null,
                pPropertyCount: ref count,
                pProperties: null
            )
            .Check("vkEnumerateDeviceExtensionProperties");
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = properties)
        {
            vk.EnumerateDeviceExtensionProperties(
                    physicalDevice: device,
                    pLayerName: (byte*)null,
                    pPropertyCount: ref count,
                    pProperties: p
                )
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
                PObjectName = p,
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

    public void EndLabel(CommandBuffer commandBuffer) => debugUtils?.CmdEndDebugUtilsLabel(commandBuffer);

    public void WaitIdle() => Vk.DeviceWaitIdle(Device).Check("vkDeviceWaitIdle");

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        // The handles' own teardown is the one copy of the order, for a failed Create and a disposed device alike.
        new DeviceHandles
        {
            Vk = Vk,
            LogHandle = logHandle,
            Instance = Instance,
            DebugUtils = debugUtils,
            Messenger = messenger,
            SurfaceApi = SurfaceApi,
            Surface = Surface,
            Device = Device,
        }.Destroy();
    }

    /// <summary>
    /// The handles that <see cref="Create"/> has made so far, null until made. A failure destroys them, and the new device
    /// takes them over and gives them back to <see cref="Destroy"/> when it is disposed.
    /// </summary>
    private struct DeviceHandles
    {
        public Vk Vk;
        public GCHandle LogHandle;
        public Instance Instance;
        public ExtDebugUtils? DebugUtils;
        public DebugUtilsMessengerEXT Messenger;
        public KhrSurface? SurfaceApi;
        public SurfaceKHR Surface;
        public Device Device;

        /// <summary>Destroys every handle made so far, in the reverse order of creation, then frees the log and the API.</summary>
        public void Destroy()
        {
            if (Device.Handle != 0)
                Vk.DestroyDevice(Device, null);
            // A surface comes only from the surface extension, and a messenger only from the debug extension.
            if (Surface.Handle != 0)
                SurfaceApi?.DestroySurface(Instance, Surface, null);
            if (Messenger.Handle != 0)
                DebugUtils?.DestroyDebugUtilsMessenger(Instance, Messenger, null);
            if (Instance.Handle != 0)
                Vk.DestroyInstance(Instance, null);
            // The messenger chained into vkCreateInstance stays active until vkDestroyInstance returns.
            LogHandle.Free();
            Vk.Dispose();
        }
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
