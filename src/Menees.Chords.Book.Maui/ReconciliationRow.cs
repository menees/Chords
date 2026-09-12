namespace Menees.Chords.Book.Maui;

public sealed class ReconciliationRow
{
	public required string Title { get; init; }

	public required string Description { get; init; }

	public string? ConflictKey { get; init; }

	public bool CanChoose => this.ConflictKey is not null;

	public bool UseSourceValue { get; set; }
}
