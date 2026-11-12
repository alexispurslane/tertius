using System.Runtime.InteropServices;
using System.Text;

namespace Tertius.Rendering.Tests;

/// <summary>A reader of the instructions of a SPIR-V module, for the tests that compare a host with its shaders.</summary>
internal static class Spirv
{
    public const ushort OpName = 5;
    public const ushort OpDecorate = 71;
    public const uint DecorationBinding = 33;

    /// <summary>One instruction: its opcode and the words after its first.</summary>
    public readonly record struct Instruction(ushort Opcode, uint[] Operands);

    /// <summary>The instructions of <paramref name="spirv"/>, in the order of the module.</summary>
    public static IEnumerable<Instruction> Instructions(byte[] spirv)
    {
        uint[] words = MemoryMarshal.Cast<byte, uint>(spirv).ToArray();
        // The header takes 5 words. Each instruction's first word holds its word count above its opcode.
        for (int at = 5; at < words.Length; at += (int)(words[at] >> 16))
            yield return new((ushort)words[at], words[(at + 1)..(at + (int)(words[at] >> 16))]);
    }

    /// <summary>The binding number and the name of each variable that <paramref name="spirv"/> decorates with a binding.</summary>
    public static IReadOnlyList<(uint Binding, string Name)> Bindings(byte[] spirv)
    {
        Dictionary<uint, string> names = [];
        List<(uint Id, uint Binding)> bindings = [];
        foreach (Instruction instruction in Instructions(spirv))
        {
            if (instruction.Opcode == OpName)
                names[instruction.Operands[0]] = LiteralString(instruction.Operands.AsSpan(1));
            else if (instruction.Opcode == OpDecorate && instruction.Operands[1] == DecorationBinding)
                bindings.Add((Id: instruction.Operands[0], Binding: instruction.Operands[2]));
        }

        return [.. bindings.Select(binding => (binding.Binding, Name: names[binding.Id]))];
    }

    /// <summary>
    /// The binding number of each variable that <paramref name="spirv"/> decorates with a binding, in the order of the
    /// module, for a module that glslc's optimizer stripped of its names.
    /// </summary>
    public static IReadOnlyList<uint> BindingNumbers(byte[] spirv) =>
        [
            .. Instructions(spirv)
                .Where(instruction => instruction.Opcode == OpDecorate && instruction.Operands[1] == DecorationBinding)
                .Select(instruction => instruction.Operands[2]),
        ];

    // A literal string is UTF-8, packed four bytes to a word and ended by a zero byte.
    private static string LiteralString(ReadOnlySpan<uint> words)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(words);
        return Encoding.UTF8.GetString(bytes[..bytes.IndexOf((byte)0)]);
    }
}
