namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Represents the parsed body of a chord sheet file or text stream
/// as an immutable, ordered collection of <see cref="Section"/>s.
/// </summary>
public sealed class Document
{
	#region Constructors

	private Document(List<Section> sections, string? fileName)
	{
		this.Sections = sections.ToList();
		this.FileName = fileName;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the name of the file the document was loaded from (if any).
	/// </summary>
	public string? FileName { get; }

	/// <summary>
	/// Gets the ordered collection of sections within the document.
	/// </summary>
	public IReadOnlyList<Section> Sections { get; }

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
		parser ??= new();
		using StreamReader reader = new(fileName);
		List<Section> sections = parser.Parse(reader);
		Document result = new(sections, fileName);
		return result;
	}

	/// <summary>
	/// Loads a document from the specified <paramref name="textReader"/>.
	/// </summary>
	/// <param name="textReader">The reader to read lines from.</param>
	/// <param name="parser">An optional custom document parser. If null, then a default <see cref="DocumentParser"/> is used.</param>
	/// <returns>A new document instance.</returns>
	public static Document Load(TextReader textReader, DocumentParser? parser = null)
	{
		parser ??= new();
		List<Section> sections = parser.Parse(textReader);
		Document result = new(sections, null);
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
		parser ??= new();
		using StringReader reader = new(text);
		List<Section> sections = parser.Parse(reader);
		Document result = new(sections, null);
		return result;
	}

	#endregion
}
