namespace Menees.Chords;

/// <summary>
/// The base class for entries that consist of a single line of text.
/// </summary>
public abstract class TextEntry : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The text line for the current entry.</param>
	protected TextEntry(string text)
	{
		if (this is not BlankLine)
		{
			Conditions.RequireNonWhiteSpace(text);
		}

		this.Text = text;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the entry's text line.
	/// </summary>
	public string Text { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion
}
