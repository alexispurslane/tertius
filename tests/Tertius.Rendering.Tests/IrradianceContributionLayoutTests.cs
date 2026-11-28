namespace Tertius.Rendering.Tests;

/// <summary>
/// The link between <see cref="IrradianceCube.ContributionBytes"/>, which sizes the contribution buffer, and the layout
/// that the two irradiance shaders compile from environment_irradiance_parts.glsl. A header of the wrong length moves
/// every record, and a record of the wrong stride overruns the buffer at the last map. Both would show only as wrong light
/// at some map counts.
/// </summary>
public sealed class IrradianceContributionLayoutTests
{
    private const ushort OpTypeStruct = 30;
    private const ushort OpTypePointer = 32;
    private const ushort OpVariable = 59;
    private const ushort OpMemberDecorate = 72;
    private const uint DecorationArrayStride = 6;
    private const uint DecorationOffset = 35;

    public static TheoryData<string> Shaders => ["environment_irradiance.comp", "environment_irradiance_combine.comp"];

    [Theory]
    [MemberData(nameof(Shaders))]
    public void AShaderFindsTheFirstRecordAfterTheHeaderThatTheBufferSizeCounts(string file)
    {
        ContributionLayout layout = Layout(ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, file));

        layout.RecordsOffset.Should().Be(IrradianceCube.ContributionHeaderBytes);
    }

    [Theory]
    [MemberData(nameof(Shaders))]
    public void AShaderStepsThroughTheRecordsByTheStrideThatTheBufferSizeCounts(string file)
    {
        ContributionLayout layout = Layout(ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, file));

        layout.RecordStride.Should().Be(IrradianceCube.ContributionStride);
    }

    /// <summary>The byte offset of the records and their array stride in the buffer at set 0, binding 2.</summary>
    private readonly record struct ContributionLayout(uint RecordsOffset, uint RecordStride);

    /// <summary>
    /// Follows the variable at set 0, binding 2 of <paramref name="spirv"/> to its block, then reads the offset of the
    /// block's second member, the records, and the array stride of that member's type.
    /// </summary>
    private static ContributionLayout Layout(byte[] spirv)
    {
        HashSet<uint> inSetZero = [];
        HashSet<uint> atBindingTwo = [];
        Dictionary<uint, uint> pointee = [];
        Dictionary<uint, uint> variableType = [];
        Dictionary<uint, uint[]> members = [];
        Dictionary<(uint Struct, uint Member), uint> offsets = [];
        Dictionary<uint, uint> strides = [];
        foreach (Spirv.Instruction instruction in Spirv.Instructions(spirv))
        {
            uint[] operands = instruction.Operands;
            switch (instruction.Opcode)
            {
                case Spirv.OpDecorate when operands[1] == Spirv.DecorationDescriptorSet && operands[2] == 0:
                    inSetZero.Add(operands[0]);
                    break;
                case Spirv.OpDecorate when operands[1] == Spirv.DecorationBinding && operands[2] == 2:
                    atBindingTwo.Add(operands[0]);
                    break;
                case Spirv.OpDecorate when operands[1] == DecorationArrayStride:
                    strides[operands[0]] = operands[2];
                    break;
                case OpMemberDecorate when operands[2] == DecorationOffset:
                    offsets[(operands[0], operands[1])] = operands[3];
                    break;
                case OpTypePointer:
                    pointee[operands[0]] = operands[2];
                    break;
                case OpTypeStruct:
                    members[operands[0]] = operands[1..];
                    break;
                case OpVariable:
                    variableType[operands[1]] = operands[0];
                    break;
                default:
                    break;
            }
        }

        uint buffer = inSetZero.Intersect(atBindingTwo).Should().ContainSingle().Subject;
        uint block = pointee[variableType[buffer]];
        return new(RecordsOffset: offsets[(block, 1)], RecordStride: strides[members[block][1]]);
    }
}
