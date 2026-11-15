namespace Tertius.Rendering.Tests;

/// <summary>
/// The shaders of the diffuse filter's wavelet kernels (ticket t0-89zi, <see cref="DiffuseFilterKernel"/>): only the
/// feedback dispatch's shader binds the slow history and the fast history, so the plain dispatches neither compile the
/// feedback's sums nor read a history.
/// </summary>
public sealed class DiffuseFilterKernelTests
{
    /// <summary>The filter set's number in the filter's pipeline layout.</summary>
    private const uint FilterSet = 3;

    /// <summary>The filter set's bindings of the light that a wavelet dispatch reads and writes.</summary>
    private static readonly uint[] LightBindings = [0, 1];

    /// <summary>The filter set's bindings of the light and of the slow and the fast history.</summary>
    private static readonly uint[] LightAndHistoryBindings = [0, 1, 2, 3];

    public static TheoryData<string> Kernels => ["thin", "wide"];

    [Theory]
    [MemberData(nameof(Kernels))]
    public void APlainWaveletDispatchBindsOnlyTheLightThatItReadsAndWrites(string kernel) =>
        Spirv
            .BindingNumbersOfSet(
                spirv: ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, KernelNamed(kernel).WaveletShader),
                set: FilterSet
            )
            .Should()
            .BeEquivalentTo(LightBindings, "a plain dispatch reads and writes no history");

    [Theory]
    [MemberData(nameof(Kernels))]
    public void TheFeedbackDispatchBindsTheLightAndBothHistories(string kernel) =>
        Spirv
            .BindingNumbersOfSet(
                spirv: ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, KernelNamed(kernel).FeedbackShader),
                set: FilterSet
            )
            .Should()
            .BeEquivalentTo(
                LightAndHistoryBindings,
                "the feedback dispatch writes the slow history's light and the fast history's retained deviation"
            );

    private static DiffuseFilterKernel KernelNamed(string kernel) =>
        kernel == "thin" ? DiffuseFilterKernel.Thin : DiffuseFilterKernel.Wide;
}
