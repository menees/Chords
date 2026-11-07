namespace Menees.Chords.Db;

/// <summary>Summarizes a versioned catalog-metadata refresh.</summary>
public sealed record BookMetadataRefreshResult(int AnalyzedFileCount, int UpdatedSongCount)
{
	/// <summary>Gets recoverable source problems whose existing catalog values and analysis versions were retained.</summary>
	public IReadOnlyList<ExternalBookProblem> Problems { get; init; } = [];
}
