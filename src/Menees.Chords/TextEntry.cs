namespace Menees.Chords;

#region Using Directives

using System.IO;

#endregion

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
	/// <param name="allowWhitespace">Whether it's ok for <paramref name="text"/> to be empty or whitespace.</param>
	/// <param name="annotations">A collection of optional end-of-line annotations.</param>
	protected TextEntry(string text, IEnumerable<Entry>? annotations = null, bool allowWhitespace = false)
		: base(annotations)
	{
		if (!allowWhitespace)
		{
			Conditions.RequireNonWhiteSpace(text);
		}

		this.Text = text ?? string.Empty;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the entry's text line.
	/// </summary>
	public string Text { get; }

	#endregion

	#region Protected Methods

	/// <summary>
	/// Writes <see cref="Text"/>.
	/// </summary>
	/// <param name="writer">Used to write output.</param>
	protected override void WriteWithoutAnnotations(TextWriter writer)
	{
		Conditions.RequireNonNull(writer);
		writer.Write(this.Text);
	}

	#endregion
}
