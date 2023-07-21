namespace Menees.Chords;

/// <summary>
/// A document entry with an optional user-visible text label.
/// </summary>
public class LabeledEntry : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance with the specified label.
	/// </summary>
	/// <param name="label">An optional label to display</param>
	public LabeledEntry(string? label)
	{
		this.Label = label;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the user-visible text label for this entry.
	/// </summary>
	public string? Label { get; }

	#endregion
}
