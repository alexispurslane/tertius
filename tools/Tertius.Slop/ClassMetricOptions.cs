using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Tertius.Slop;

/// <summary>Optional explicit metadata inputs; no project output or build is discovered.</summary>
public sealed record ClassMetricOptions
{
    public IReadOnlyList<string> ReferencePaths { get; init; } = [];

    // Shared immutable metadata avoids rereading the runtime for each historical snapshot.
    private static readonly Lazy<(MetadataReference[] References, string Fingerprint)> Platform = new(() =>
        ReadReferences(
            Directory.GetFiles(
                Path.GetDirectoryName(typeof(object).Assembly.Location)
                    ?? throw new InvalidOperationException("Runtime directory unavailable."),
                "*.dll"
            )
        )
    );

    internal (MetadataReference[] References, string Fingerprint) References()
    {
        var platform = Platform.Value;
        if (ReferencePaths.Count == 0)
            return platform;
        var additional = ReadReferences(ReferencePaths);
        return ([.. platform.References, .. additional.References], $"{platform.Fingerprint}:{additional.Fingerprint}");
    }

    public static IReadOnlyList<string> ReadReferenceList(string path) =>
        JsonSerializer.Deserialize(File.ReadAllText(path), SlopJson.Default.StringArray)
        ?? throw new ArgumentException("The reference list must be a JSON array of assembly paths.");

    internal static (string[] Arguments, ClassMetricOptions Options) FromArguments(string[] arguments)
    {
        var options = new ClassMetricOptions();
        if (Environment.GetEnvironmentVariable("TERTIUS_SLOP_REFERENCE_LIST") is { Length: > 0 } referenceList)
            options = options with { ReferencePaths = ReadReferenceList(referenceList) };
        var remaining = new List<string>();
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument != "--reference-list")
            {
                remaining.Add(argument);
                continue;
            }
            if (++index == arguments.Length)
                throw new ArgumentException($"{argument} needs a value.");
            options = options with { ReferencePaths = ReadReferenceList(arguments[index]) };
        }
        return ([.. remaining], options);
    }

    private static (MetadataReference[] References, string Fingerprint) ReadReferences(IEnumerable<string> paths)
    {
        var references = new List<MetadataReference>();
        var fingerprints = new List<string>();
        foreach (string path in paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            references.Add(MetadataReference.CreateFromFile(path));
            fingerprints.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fingerprints.Order(StringComparer.Ordinal))))
        );
        return ([.. references], fingerprint);
    }
}
