namespace Menees.Chords.Db;

/// <summary>Contains the result of validating one complete native chord book.</summary>
public sealed class BookValidationReport
{
	/// <summary>Initializes a new validation report.</summary>
	public BookValidationReport(ChordDatabase? database, IReadOnlyList<BookValidationIssue> issues)
	{
		this.Database = database;
		this.Issues = issues;
	}

	/// <summary>Gets the validated database, or null when its JSON was invalid.</summary>
	public ChordDatabase? Database { get; }

	/// <summary>Gets all detected failures.</summary>
	public IReadOnlyList<BookValidationIssue> Issues { get; }

	/// <summary>Gets whether the database and all managed assets are valid.</summary>
	public bool IsValid => this.Database is not null && this.Issues.Count == 0;
}
