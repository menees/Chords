namespace Menees.Chords;

/// <summary>
/// A comment line or a comment segment on the same line as another entry.
/// </summary>
public sealed class Comment : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified comment.
	/// </summary>
	/// <param name="comment"></param>
	public Comment(string comment)
	{
		this.Text = comment;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the comment text.
	/// </summary>
	public string Text { get; }

	#endregion

	// Starts with # or *. Or entire line in parentheses
	// TODO: Parse [Bill, 7/21/2023]
}
