namespace Menees.Chords;

/// <summary>
/// The base class for entries that consist of a single line of text.
/// </summary>
public abstract class TextEntry : Entry
{
	#region Private Data Members

	private List<Entry>? annotations;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The text line for the current entry.</param>
	protected TextEntry(string text)
	{
		this.Text = text ?? string.Empty;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the entry's text line.
	/// </summary>
	public string Text { get; }

	/// <summary>
	/// Gets the ordered sub-entries contained on the same line with this entry.
	/// </summary>
	/// <remarks>
	/// This is useful for entries like a <see cref="HeaderLine"/> that also contains a <see cref="Comment"/>,
	/// and for when <see cref="ChordDefinitions"/> are at the end of <see cref="ChordLine"/>.
	/// </remarks>
	public IReadOnlyList<Entry> Annotations => this.annotations ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns <see cref="Text"/>.
	/// </summary>
	public override string ToString() => this.Text;

	#endregion

	#region Protected Methods

	/// <summary>
	/// Adds a sub-entry that's on the same line as the current entry.
	/// </summary>
	/// <param name="annotation">The sub-entry to add.</param>
	protected void AddAnnotation(Entry annotation)
	{
		this.annotations ??= new();
		this.annotations.Add(annotation);
	}

	#endregion
}
