using System.Reflection;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Loads the SPIR-V modules that the build compiled from Shaders/*.vert, Shaders/*.frag and Shaders/*.comp and embedded as
/// resources named <c>Tertius.Rendering.Shaders.&lt;file&gt;.spv</c>. Modules are created once and cached.
/// </summary>
internal sealed unsafe class ShaderLibrary(VulkanDevice device) : IDisposable
{
    private readonly VulkanDevice device = device;
    private readonly Dictionary<string, ShaderModule> modules = [];

    /// <summary>The module for a shader file name such as <c>mesh.vert</c>.</summary>
    public ShaderModule Get(string file)
    {
        if (modules.TryGetValue(file, out ShaderModule module))
            return module;
        byte[] code = LoadSpirv(file);
        fixed (byte* p = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)p,
            };
            device
                .Vk.CreateShaderModule(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pShaderModule: out module
                )
                .Check($"vkCreateShaderModule({file})");
        }

        device.SetName(ObjectType.ShaderModule, module.Handle, file);
        modules.Add(file, module);
        return module;
    }

    public static byte[] LoadSpirv(string file)
    {
        string resource = $"Tertius.Rendering.Shaders.{file}.spv";
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"The shader {resource} is not embedded. Was it compiled by the build?");
        byte[] code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code.Length < 20 || BitConverter.ToUInt32(code, 0) != 0x07230203
            ? throw new InvalidDataException($"{resource} is not SPIR-V.")
            : code;
    }

    public void Dispose()
    {
        foreach (ShaderModule module in modules.Values)
            device.Vk.DestroyShaderModule(device.Device, module, null);
        modules.Clear();
    }
}
