namespace Menees.Chords.Db;

/// <summary>Summarizes safe external changes adopted into a book database.</summary>
public sealed record BookReconcileResult(
	int RenamedFileCount,
	int ChangedFileCount,
	IReadOnlyList<ExternalBookProblem> Problems);
