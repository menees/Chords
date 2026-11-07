namespace Menees.Chords.Db;

/// <summary>One immutable source snapshot and its review classification.</summary>
public sealed class BookImportItem
{
	internal BookImportItem(
		Guid id,
		string name,
		string hash,
		SongFileAnalysis analysis,
		BookImportConflict conflict,
		IReadOnlyList<BookImportCandidate> candidates)
	{
		this.Id = id;
		this.Name = name;
		this.ContentHash = hash;
		this.Analysis = analysis;
		this.Conflict = conflict;
		this.Candidates = candidates;
	}

	/// <summary>Gets the source identity, also used for its planned new song.</summary>
	public Guid Id { get; }

	/// <summary>Gets the original filename.</summary>
	public string Name { get; }

	/// <summary>Gets the captured content hash.</summary>
	public string ContentHash { get; }

	/// <summary>Gets the detected source metadata.</summary>
	public SongFileAnalysis Analysis { get; }

	/// <summary>Gets the classification used for the safe default.</summary>
	public BookImportConflict Conflict { get; }

	/// <summary>Gets related sheets, including archived sheets and earlier incoming sources.</summary>
	public IReadOnlyList<BookImportCandidate> Candidates { get; }

	/// <summary>Gets a safe initial comparison sheet for one unambiguous related song, without choosing an import action.</summary>
	public BookImportCandidate? DefaultCandidate
	{
		get
		{
			BookImportCandidate? result = null;
			if (this.Candidates.Select(candidate => candidate.SongId).Distinct().Take(2).Count() == 1)
			{
				result = this.Candidates.FirstOrDefault(candidate => candidate.IsDefault)
					?? this.Candidates.FirstOrDefault(candidate => candidate.CanCompare) ?? this.Candidates[0];
			}

			return result;
		}
	}

	/// <summary>Gets a default that never silently combines or duplicates related songs.</summary>
	public BookImportDecision DefaultDecision => new(
		this.Id,
		this.Conflict == BookImportConflict.NewSong ? BookImportAction.ImportNewSong : BookImportAction.Skip);
}