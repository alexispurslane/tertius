namespace Tertius.Assets;

/// <summary>
/// Why a model did not load: its cooked file is missing, the file is broken, or the file has no piece of the name that
/// the reference gives. Each case names the cooked file. A missing file is the usual case on a checkout whose assets were
/// never converted, so it is a result, not an exception.
/// </summary>
public closed record ModelProblem(string Path);

/// <summary>No cooked file at <paramref name="Path"/>: the model was never converted on this machine.</summary>
public sealed record MissingModel(string Path) : ModelProblem(Path);

/// <summary>The cooked file at <paramref name="Path"/> is not a model that the reader takes, for <paramref name="Reason"/>.</summary>
public sealed record MalformedModel(string Path, string Reason) : ModelProblem(Path);

/// <summary>The cooked file at <paramref name="Path"/> has no piece <paramref name="Piece"/>; it has <paramref name="Pieces"/>.</summary>
public sealed record MissingPiece(string Path, string Piece, IReadOnlyList<string> Pieces) : ModelProblem(Path);
