namespace Menees.Chords.Db;

/// <summary>An immutable review of external changes, bound to the store and database that produced it.</summary>
public sealed class BookReconcilePreview
{
	internal BookReconcilePreview(
		BookLocation location,
		string expectedJson,
		string proposedJson,
		IReadOnlyList<BookReconcileChange> changes,
		IReadOnlyList<BookMetadataConflict> conflicts,
		IReadOnlyList<ExternalBookProblem> problems,
		IReadOnlyList<ReconciledFileObservation> observations)
	{
		this.Location = location;
		this.ExpectedJson = expectedJson;
		this.ProposedJson = proposedJson;
		this.Changes = changes;
		this.Conflicts = conflicts;
		this.Problems = problems;
		this.Observations = observations;
	}

	/// <summary>Gets renamed or modified sheets proposed for adoption.</summary>
	public IReadOnlyList<BookReconcileChange> Changes { get; }

	/// <summary>Gets independent catalog values retained unless the user selects the source value.</summary>
	public IReadOnlyList<BookMetadataConflict> Conflicts { get; }

	/// <summary>Gets unresolved files that will not be adopted or deleted.</summary>
	public IReadOnlyList<ExternalBookProblem> Problems { get; }

	/// <summary>Gets whether applying the preview would update the catalog or file observations.</summary>
	public bool HasChanges => this.Observations.Count > 0;

	internal BookLocation Location { get; }

	internal string ExpectedJson { get; }

	internal string ProposedJson { get; }

	internal IReadOnlyList<ReconciledFileObservation> Observations { get; }
}
