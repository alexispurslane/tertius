using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The link between <see cref="ShaderConstants"/> and the shaders that the build embeds: each constant's id in C# is
/// the constant_id that its shaders declare, and the shaders' defaults are the values of the default constants. A driver
/// ignores a specialization entry whose id no shader declares (the Vulkan spec, VkSpecializationMapEntry), and a
/// constant such as <see cref="ShaderConstants.DropsFibers"/> changes only speed, so no render test sees a broken link.
/// </summary>
public sealed class ShaderConstantsTests
{
    private static readonly Declared False = new(Type: "bool", Default: 0);

    /// <summary>
    /// The specialization constants that each shader declares, by constant_id. The table covers every constant, and for
    /// each two constants a shader that declares only one of them, so a swap of two ids on either side shows.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<uint, Declared>> DeclaredByShader = new()
    {
        ["composite.frag"] = new()
        {
            [ShaderConstants.EnvironmentMapId] = False,
            [ShaderConstants.ScreenTraceOnId] = False,
            [ShaderConstants.ReflectionSharingPrototypeId] = False,
        },
        ["screen_trace_block_rays.comp"] = new()
        {
            [ShaderConstants.EnvironmentMapId] = False,
            [ShaderConstants.RayKindId] = new(Type: "uint", Default: 0),
        },
        // A face's surfaces only sample their material: the light pass of the face holds the lights' constants.
        ["environment_face.frag"] = new()
        {
            [ShaderConstants.CutoutAlphaId] = False,
            [ShaderConstants.DropsFibersId] = False,
            [ShaderConstants.MainViewId] = False,
        },
        ["environment_face_light.frag"] = new()
        {
            [ShaderConstants.DropsFibersId] = False,
            [ShaderConstants.TileCornerMeansId] = False,
        },
        ["skin_scatter_rows.comp"] = new() { [ShaderConstants.ScatterTapsPerSideId] = new(Type: "int", Default: 1) },
        ["direct.frag"] = new()
        {
            [ShaderConstants.DropsFibersId] = False,
            [ShaderConstants.TileCornerMeansId] = False,
        },
        ["puff.vert"] = new()
        {
            [ShaderConstants.EnvironmentMapId] = False,
            [ShaderConstants.TileCornerMeansId] = False,
        },
        ["gbuffer.frag"] = new()
        {
            [ShaderConstants.EnvironmentMapId] = False,
            [ShaderConstants.CutoutAlphaId] = False,
            [ShaderConstants.DropsFibersId] = False,
            [ShaderConstants.MainViewId] = False,
        },
        ["transparent.frag"] = new()
        {
            [ShaderConstants.EnvironmentMapId] = False,
            [ShaderConstants.CutoutAlphaId] = False,
            [ShaderConstants.DropsFibersId] = False,
            [ShaderConstants.MainViewId] = False,
            [ShaderConstants.TileCornerMeansId] = False,
        },
    };

    public static TheoryData<string> Shaders => [with(DeclaredByShader.Keys)];

    [Theory]
    [MemberData(nameof(Shaders))]
    public void AShaderDeclaresItsSpecializationConstantsAtTheIdsOfShaderConstants(string file)
    {
        IReadOnlyDictionary<uint, Declared> declared = SpecializationConstants(
            ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, file)
        );

        declared.Should().BeEquivalentTo(DeclaredByShader[file]);
    }

    [Theory]
    [MemberData(nameof(Shaders))]
    public void TheDefaultConstantsSpecializeAShaderToItsOwnDefaults(string file)
    {
        IReadOnlyDictionary<uint, Declared> declared = SpecializationConstants(
            ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, file)
        );
        Dictionary<uint, uint> given = Given(new ShaderConstants(EnvironmentMap: false));

        foreach ((uint id, Declared constant) in declared)
            given[id].Should().Be(constant.Default, $"a pipeline without constants takes constant_id {id}'s default");
    }

    [Theory]
    [InlineData(true, 1u)]
    [InlineData(false, 0u)]
    public void TheWayToReadAShadowTileReachesItsConstant(bool cornerMeans, uint value)
    {
        // Both ways to read a tile give the same image, so no render test sees a constant that never arrives.
        Dictionary<uint, uint> given = Given(
            new ShaderConstants(EnvironmentMap: false) { TileCornerMeans = cornerMeans }
        );

        given[ShaderConstants.TileCornerMeansId].Should().Be(value);
    }

    /// <summary>The value that a pipeline with <paramref name="constants"/> gives each constant_id.</summary>
    private static unsafe Dictionary<uint, uint> Given(ShaderConstants constants)
    {
        SpecializationStorage storage = default;
        SpecializationInfo* info = ShaderConstants.Specialization(constants, &storage);

        Dictionary<uint, uint> given = [];
        for (int i = 0; i < info->MapEntryCount; i++)
        {
            SpecializationMapEntry entry = info->PMapEntries[i];
            given[entry.ConstantID] = *(uint*)((byte*)info->PData + entry.Offset);
        }

        return given;
    }

    /// <summary>A specialization constant that SPIR-V declares: its type and its default value as one word.</summary>
    private readonly record struct Declared(string Type, uint Default);

    /// <summary>The specialization constants that <paramref name="spirv"/> declares, by their SpecId decoration.</summary>
    private static Dictionary<uint, Declared> SpecializationConstants(byte[] spirv)
    {
        const ushort OpTypeBool = 20,
            OpTypeInt = 21,
            OpSpecConstantTrue = 48,
            OpSpecConstantFalse = 49;
        const ushort OpSpecConstant = 50;
        const uint SpecId = 1;

        Dictionary<uint, string> types = [];
        Dictionary<uint, uint> specIds = [];
        Dictionary<uint, Declared> constants = [];
        foreach (Spirv.Instruction instruction in Spirv.Instructions(spirv))
        {
            uint[] operands = instruction.Operands;
            ushort opcode = instruction.Opcode;
            switch (opcode)
            {
                case OpTypeBool:
                    types[operands[0]] = "bool";
                    break;
                case OpTypeInt:
                    types[operands[0]] = operands[2] == 1 ? "int" : "uint";
                    break;
                case OpSpecConstantTrue or OpSpecConstantFalse or OpSpecConstant:
                    uint value = opcode switch
                    {
                        OpSpecConstantTrue => 1,
                        OpSpecConstantFalse => 0,
                        _ => operands[2],
                    };
                    constants[operands[1]] = new(Type: types[operands[0]], Default: value);
                    break;
                case Spirv.OpDecorate when operands[1] == SpecId:
                    specIds[operands[0]] = operands[2];
                    break;
                default:
                    break;
            }
        }

        return specIds.ToDictionary(pair => pair.Value, pair => constants[pair.Key]);
    }
}
