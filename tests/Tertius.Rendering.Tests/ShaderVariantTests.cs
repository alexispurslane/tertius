using System.Text.RegularExpressions;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The link between the shader variants of <see cref="HistoryInstrumentation"/> and the shaders that the build embeds:
/// every variant that the table names is embedded, and every embedded variant of an instrumentation is in its table. A
/// variant missing on either side would otherwise fail only at run time, and only under its instrumentation.
/// </summary>
public sealed partial class ShaderVariantTests
{
    private const string ResourcePrefix = "Tertius.Rendering.Shaders.";

    private static readonly HistoryInstrumentation[] Instrumented =
    [
        HistoryInstrumentation.LightAgeDiagnostic,
        HistoryInstrumentation.ControlTrace,
    ];

    private static readonly HashSet<string> Embedded =
    [
        .. typeof(TemporalSignal)
            .Assembly.GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".spv", StringComparison.Ordinal)
            )
            .Select(name => name[ResourcePrefix.Length..^".spv".Length]),
    ];

    [GeneratedRegex(@"^(?<name>.+)(?<suffix>_lightagediag_precise|_trace_precise)(?<extension>\.(comp|frag|vert))$")]
    private static partial Regex InstrumentedFile();

    [Fact]
    public void EveryShaderOfAnInstrumentationsVariantIsEmbeddedInThatVariantAndAsPrecise()
    {
        List<string> missing = [];
        foreach (HistoryInstrumentation instrumentation in Instrumented)
        {
            ShaderVariant variant = instrumentation.Variant!.Value;
            foreach (string shader in variant.Shaders)
            foreach (
                string file in new[]
                {
                    ShaderVariant.Of(shader, variant.Suffix),
                    ShaderVariant.Of(shader, ShaderVariant.PreciseSuffix),
                }
            )
                if (!Embedded.Contains(file))
                    missing.Add(file);
        }

        missing.Should().BeEmpty("the build compiles every variant that a table names");
    }

    [Fact]
    public void EveryEmbeddedInstrumentedVariantIsInItsInstrumentationsTable()
    {
        List<string> unlisted = [];
        foreach (string file in Embedded)
        {
            if (InstrumentedFile().Match(file) is not { Success: true } match)
                continue;
            string shader = match.Groups["name"].Value + match.Groups["extension"].Value;
            bool listed = Instrumented.Any(instrumentation =>
                instrumentation.Variant is { } variant
                && variant.Suffix == match.Groups["suffix"].Value
                && variant.Shaders.Contains(shader)
            );
            if (!listed)
                unlisted.Add(file);
        }

        unlisted.Should().BeEmpty("a shader that the build compiles in an instrumentation's variant writes its image");
    }

    [Fact]
    public void TheBuildEmbedsAVariantOfEachInstrumentation()
    {
        foreach (HistoryInstrumentation instrumentation in Instrumented)
        {
            string suffix = instrumentation.Variant!.Value.Suffix;
            Embedded
                .Should()
                .Contain(
                    file => file.Contains(suffix, StringComparison.Ordinal),
                    $"the build compiles {instrumentation}'s variant, so an empty table misses it"
                );
        }
    }

    [Fact]
    public void AnInstrumentationWithoutAnImageHasNoVariant() => HistoryInstrumentation.None.Variant.Should().BeNull();

    [Fact]
    public void EveryShaderOfTheLightAgeVariantIsEmbeddedInIt() =>
        ShaderVariant
            .LightAge.Shaders.Select(shader => ShaderVariant.Of(shader, ShaderVariant.LightAge.Suffix))
            .Should()
            .OnlyContain(file => Embedded.Contains(file), "the diffuse signal beside the light age takes this variant");

    [Fact]
    public void EveryEmbeddedLightAgeVariantIsInItsTable() =>
        Embedded
            .Where(file =>
                Path.GetFileNameWithoutExtension(file).EndsWith(ShaderVariant.LightAge.Suffix, StringComparison.Ordinal)
            )
            .Select(file => file.Replace(ShaderVariant.LightAge.Suffix, "", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo(
                ShaderVariant.LightAge.Shaders,
                "a shader that the build compiles in the variant reads the age"
            );

    [Fact]
    public void AVariantsFileNameAddsItsSuffixBeforeTheStage() =>
        ShaderVariant
            .Of("screen_trace_resolve.comp", "_lightagediag_precise")
            .Should()
            .Be("screen_trace_resolve_lightagediag_precise.comp");
}

/// <summary>The variant that <see cref="TemporalSignal.HistoryShader(string, SpecularHistoryMode, HistoryInstrumentation)"/> names for each history.</summary>
public sealed class HistoryShaderTests
{
    [Fact]
    public void TheOriginalHistoryTakesEachShaderAsItIs() =>
        TemporalSignal
            .HistoryShader(
                "screen_trace_resolve.comp",
                SpecularHistoryMode.Original,
                HistoryInstrumentation.LightAgeDiagnostic
            )
            .Should()
            .Be("screen_trace_resolve.comp");

    [Fact]
    public void APreciseHistoryWithoutInstrumentationTakesThePreciseVariant() =>
        TemporalSignal
            .HistoryShader("composite.frag", SpecularHistoryMode.PreciseUncapped, HistoryInstrumentation.None)
            .Should()
            .Be("composite_precise.frag");

    [Theory]
    [InlineData("accumulate_temporal.comp", "accumulate_temporal_lightagediag_precise.comp")]
    [InlineData("screen_trace_plan.comp", "screen_trace_plan_lightagediag_precise.comp")]
    [InlineData("screen_trace_block_rays.comp", "screen_trace_block_rays_lightagediag_precise.comp")]
    [InlineData("screen_trace_own_rays.comp", "screen_trace_own_rays_lightagediag_precise.comp")]
    [InlineData("screen_trace_resolve.comp", "screen_trace_resolve_lightagediag_precise.comp")]
    [InlineData("accumulate_spatial.comp", "accumulate_spatial_precise.comp")]
    [InlineData("composite.frag", "composite_precise.frag")]
    public void TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(
        string shader,
        string expected
    ) =>
        TemporalSignal
            .HistoryShader(shader, SpecularHistoryMode.PreciseCapped, HistoryInstrumentation.LightAgeDiagnostic)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("accumulate_temporal.comp", "accumulate_temporal_trace_precise.comp")]
    [InlineData("accumulate_spatial.comp", "accumulate_spatial_trace_precise.comp")]
    [InlineData("screen_trace_resolve.comp", "screen_trace_resolve_precise.comp")]
    public void TheTraceTakesItsVariantForBothDispatchesAndThePreciseOneElsewhere(string shader, string expected) =>
        TemporalSignal
            .HistoryShader(shader, SpecularHistoryMode.PreciseUncapped, HistoryInstrumentation.ControlTrace)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("accumulate_temporal.comp", "accumulate_temporal_lightage.comp")]
    [InlineData("accumulate_spatial.comp", "accumulate_spatial_lightage.comp")]
    [InlineData("accumulate_diffuse_single_temporal.comp", "accumulate_diffuse_single_temporal.comp")]
    public void AHalfPrecisionHistoryBesideTheLightAgeTakesTheLightAgeVariantOfBothDispatches(
        string shader,
        string expected
    ) => TemporalSignal.LightAgeShader(shader).Should().Be(expected);

    [Fact]
    public void AShaderOutsideTheTablesTakesThePreciseVariantUnderAnInstrumentation() =>
        TemporalSignal
            .HistoryShader(
                "reflection_sharing_pulse.frag",
                SpecularHistoryMode.PreciseUncapped,
                HistoryInstrumentation.LightAgeDiagnostic
            )
            .Should()
            .Be("reflection_sharing_pulse_precise.frag");
}
