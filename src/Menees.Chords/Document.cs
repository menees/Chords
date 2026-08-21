namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.IO;
using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// The parsed body of a chord sheet file or text stream as an
/// immutable, ordered collection of <see cref="Entry"/>s.
/// </summary>
public sealed class Document : IEntryContainer
{
	#region Private Data Members

	private const int Latin1CodePage = 28591;

	#endregion

	#region Constructors

	internal Document(IReadOnlyList<Entry> entries, string? fileName)
	{
		this.Entries = entries;
		this.FileName = fileName;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the name of the file that the document was loaded from (if any).
	/// </summary>
	/// <remarks>
	/// This may help with inferring the song name (e.g., using <see cref="Path.GetFileNameWithoutExtension(string?)"/>).
	/// </remarks>
	public string? FileName { get; }

	/// <summary>
	/// Gets the ordered collection of entries within the document.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Loads a document from the specified <paramref name="fileName"/>.
	/// </summary>
	/// <param name="fileName">The full name of the file to load from.</param>
	/// <param name="parser">An optional custom document parser. If null, then a default <see cref="DocumentParser"/> is used.</param>
	/// <returns>A new document instance.</returns>
	public static Document Load(string fileName, DocumentParser? parser = null)
	{
		Conditions.RequireNonWhiteSpace(fileName);
		parser ??= new();
		IReadOnlyList<Entry> entries;
		try
		{
			// ChordPro permits UTF encodings and ISO-8859-1. Try strict UTF-8 first so
			// invalid byte sequences can fall back to Latin-1 instead of becoming U+FFFD.
			using StreamReader reader = new(fileName, new UTF8Encoding(false, true), true);
			entries = parser.Parse(reader);
		}
		catch (DecoderFallbackException)
		{
			using StreamReader reader = new(fileName, Encoding.GetEncoding(Latin1CodePage), false);
			entries = parser.Parse(reader);
		}

		Document result = new(entries, fileName);
		return result;
	}

	/// <summary>
	/// Loads a document from the specified <paramref name="reader"/>.
	/// </summary>
	/// <param name="reader">The reader to read lines from.</param>
	/// <param name="parser">An optional custom document parser. If null, then a default <see cref="DocumentParser"/> is used.</param>
	/// <returns>A new document instance.</returns>
	public static Document Load(TextReader reader, DocumentParser? parser = null)
	{
		Conditions.RequireNonNull(reader);

		parser ??= new();
		IReadOnlyList<Entry> entries = parser.Parse(reader);
		Document result = new(entries, null);
		return result;
	}

	/// <summary>
	/// Parses the <paramref name="text"/> as the body of a chord sheet.
	/// </summary>
	/// <param name="text">The text to read lines from.</param>
	/// <param name="parser">An optional custom document parser. If null, then a default <see cref="DocumentParser"/> is used.</param>
	/// <returns>A new document instance.</returns>
	public static Document Parse(string text, DocumentParser? parser = null)
	{
		Conditions.RequireNonWhiteSpace(text);

		parser ??= new();
		using StringReader reader = new(text);
		IReadOnlyList<Entry> entries = parser.Parse(reader);
		Document result = new(entries, null);
		return result;
	}

	#endregion
}
