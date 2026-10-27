using System.Reflection;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Loads the SPIR-V modules that a build compiled and embedded in <paramref name="source"/> as resources named
/// <c>Tertius.Rendering.Shaders.&lt;file&gt;.spv</c>: the engine's from Shaders/*.vert, Shaders/*.frag and Shaders/*.comp
/// in <see cref="EngineShaders"/>. Modules are created once and cached.
/// </summary>
internal sealed unsafe class ShaderLibrary(VulkanDevice device, Assembly source) : IDisposable
{
    /// <summary>The assembly that holds the engine's shaders.</summary>
    public static Assembly EngineShaders => typeof(ShaderLibrary).Assembly;

    private readonly VulkanDevice device = device;
    private readonly Assembly source = source;
    private readonly Dictionary<string, ShaderModule> modules = [];

    /// <summary>The module for a shader file name such as <c>mesh.vert</c>.</summary>
    public ShaderModule Get(string file)
    {
        if (modules.TryGetValue(file, out ShaderModule module))
            return module;
        byte[] code = LoadSpirv(source, file);
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

    /// <summary>
    /// The create info of a <paramref name="stage"/> that runs <paramref name="entry"/> of <paramref name="file"/>, with
    /// the specialization <paramref name="constants"/> or none. Both pointers must outlive the creation of the pipeline.
    /// </summary>
    public PipelineShaderStageCreateInfo Stage(
        ShaderStageFlags stage,
        string file,
        byte* entry,
        SpecializationInfo* constants
    ) =>
        new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = stage,
            Module = Get(file),
            PName = entry,
            PSpecializationInfo = constants,
        };

    /// <summary>The SPIR-V of a shader <paramref name="file"/> that <paramref name="source"/> embeds.</summary>
    public static byte[] LoadSpirv(Assembly source, string file)
    {
        string resource = $"Tertius.Rendering.Shaders.{file}.spv";
        using Stream stream =
            source.GetManifestResourceStream(resource)
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
