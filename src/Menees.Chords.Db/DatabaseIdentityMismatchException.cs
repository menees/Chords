namespace Menees.Chords.Db;

/// <summary>Indicates that a replacement restore targets a different book identity.</summary>
public sealed class DatabaseIdentityMismatchException : InvalidOperationException
{
	/// <summary>Initializes a new instance of the <see cref="DatabaseIdentityMismatchException"/> class.</summary>
	public DatabaseIdentityMismatchException(Guid currentId, Guid replacementId)
		: base($"Replacement book {replacementId:D} does not match current book {currentId:D}.")
	{
		this.CurrentId = currentId;
		this.ReplacementId = replacementId;
	}

	/// <summary>Gets the current book identity.</summary>
	public Guid CurrentId { get; }

	/// <summary>Gets the attempted replacement identity.</summary>
	public Guid ReplacementId { get; }
}
