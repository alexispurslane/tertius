namespace Tertius.Rendering.Tests;

/// <summary>The portable walk's compiled predicates avoid scalar short-circuit flow and facing normalization.</summary>
public sealed class ScreenWalkExecutionTests
{
    private const ushort OpExtInst = 12,
        OpAll = 155,
        OpBranchConditional = 250;
    private const uint Normalize = 69;

    [Fact]
    public void CheckingTheScreenBoundsNeedsNoConditionalBranch() =>
        Spirv
            .Instructions(
                ShaderLibrary.LoadSpirv(typeof(ScreenWalkExecutionTests).Assembly, "screen_walk_bounds_probe.comp")
            )
            .Where(instruction => instruction.Opcode == OpBranchConditional)
            .Should()
            .BeEmpty("the two axes are combined as vector Booleans, not short-circuit branches");

    [Fact]
    public void TestingSurfaceFacingNeedsNoNormalNormalization() =>
        Spirv
            .Instructions(
                ShaderLibrary.LoadSpirv(typeof(ScreenWalkExecutionTests).Assembly, "screen_walk_facing_probe.comp")
            )
            .Where(instruction => instruction.Opcode == OpExtInst && instruction.Operands[3] == Normalize)
            .Should()
            .BeEmpty("facing consumes only the sign of the dot product, not a unit normal");

    [Fact]
    public void TheWalkBranchesOnTheCombinedStepCapAndRayEndPredicates()
    {
        Spirv.Instruction[] instructions =
        [
            .. Spirv.Instructions(
                ShaderLibrary.LoadSpirv(typeof(ScreenWalkExecutionTests).Assembly, "screen_walk_loop_probe.comp")
            ),
        ];
        uint[] combined =
        [
            .. instructions
                .Where(instruction => instruction.Opcode == OpAll)
                .Select(instruction => instruction.Operands[1]),
        ];

        instructions
            .Where(instruction => instruction.Opcode == OpBranchConditional)
            .Select(instruction => instruction.Operands[0])
            .Intersect(combined)
            .Should()
            .NotBeEmpty("the loop combines its step cap and ray end with a vector Boolean all");
    }
}
