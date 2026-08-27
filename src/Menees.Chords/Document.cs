namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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
	private const int BitsPerByte = 8;

	private static readonly byte[] Utf8Preamble = new UTF8Encoding(true).GetPreamble();
	private static readonly byte[] Utf16LittleEndianPreamble = Encoding.Unicode.GetPreamble();
	private static readonly byte[] Utf16BigEndianPreamble = Encoding.BigEndianUnicode.GetPreamble();
	private static readonly byte[] Utf32LittleEndianPreamble = Encoding.UTF32.GetPreamble();
	private static readonly byte[] Utf32BigEndianPreamble = new UTF32Encoding(true, true).GetPreamble();
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
	private static readonly Encoding StrictUtf16LittleEndian = new UnicodeEncoding(false, false, true);
	private static readonly Encoding StrictUtf16BigEndian = new UnicodeEncoding(true, false, true);
	private static readonly Encoding StrictUtf32LittleEndian = new UTF32Encoding(false, false, true);
	private static readonly Encoding StrictUtf32BigEndian = new UTF32Encoding(true, false, true);

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
		using FileStream stream = File.OpenRead(fileName);
		Document loaded = Load(stream, parser);

		Document result = new(loaded.Entries, fileName);
		return result;
	}

	/// <summary>
	/// Loads a document from the specified <paramref name="stream"/>.
	/// </summary>
	/// <param name="stream">The stream to load from. The caller retains ownership of the stream.</param>
	/// <param name="parser">An optional custom document parser. If null, then a default <see cref="DocumentParser"/> is used.</param>
	/// <returns>A new document instance.</returns>
	public static Document Load(Stream stream, DocumentParser? parser = null)
	{
		Conditions.RequireNonNull(stream);

		parser ??= new();
		Stream input = stream;
		MemoryStream? bufferedInput = null;
		if (!input.CanSeek)
		{
			bufferedInput = new();
			input.CopyTo(bufferedInput);
			bufferedInput.Position = 0;
			input = bufferedInput;
		}

		try
		{
			long initialPosition = input.Position;
			IReadOnlyList<Entry>? entries = null;
			if (parser.HasStructuredParsers && LooksLikeXml(input))
			{
				input.Position = initialPosition;
				try
				{
					XDocument structuredDocument = XDocument.Load(input, LoadOptions.PreserveWhitespace);
					entries = parser.Parse(structuredDocument);
				}
				catch (XmlException)
				{
					// The inexpensive XML probe produced a false positive. Fall back to text parsing.
				}
			}

			if (entries == null)
			{
				input.Position = initialPosition;
				entries = ParseTextStream(input, parser);
			}

			Document result = new(entries, null);
			return result;
		}
		finally
		{
			bufferedInput?.Dispose();
		}
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
		IReadOnlyList<Entry> entries = parser.HasStructuredParsers
			? ParseContent(reader.ReadToEnd(), parser)
			: parser.Parse(reader);
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
		IReadOnlyList<Entry> entries = ParseContent(text, parser);
		Document result = new(entries, null);
		return result;
	}

	#endregion

	#region Private Methods

	private static bool IsXmlWhitespace(int value)
		=> value is ' ' or '\t' or '\r' or '\n';

	private static bool LooksLikeXml(string text)
	{
		int start = 0;
		if (text.Length > 0 && text[0] == '\uFEFF')
		{
			start++;
		}

		while (start < text.Length && IsXmlWhitespace(text[start]))
		{
			start++;
		}

		int end = text.Length - 1;
		while (end >= start && IsXmlWhitespace(text[end]))
		{
			end--;
		}

		return start <= end && text[start] == '<' && text[end] == '>';
	}

	private static bool LooksLikeXml(Stream stream)
	{
		long originalPosition = stream.Position;
		bool result = false;
		try
		{
			long endPosition = stream.Length;
			byte[] prefix = new byte[sizeof(int)];
			int prefixCount = stream.Read(prefix, 0, prefix.Length);
			int codeUnitSize = 1;
			bool bigEndian = false;
			long startPosition = originalPosition;

			if (HasPrefix(prefix, prefixCount, Utf32BigEndianPreamble))
			{
				codeUnitSize = sizeof(int);
				bigEndian = true;
				startPosition += Utf32BigEndianPreamble.Length;
			}
			else if (HasPrefix(prefix, prefixCount, Utf32LittleEndianPreamble))
			{
				codeUnitSize = sizeof(int);
				startPosition += Utf32LittleEndianPreamble.Length;
			}
			else if (HasPrefix(prefix, prefixCount, Utf8Preamble))
			{
				startPosition += Utf8Preamble.Length;
			}
			else if (HasPrefix(prefix, prefixCount, Utf16BigEndianPreamble))
			{
				codeUnitSize = sizeof(char);
				bigEndian = true;
				startPosition += Utf16BigEndianPreamble.Length;
			}
			else if (HasPrefix(prefix, prefixCount, Utf16LittleEndianPreamble))
			{
				codeUnitSize = sizeof(char);
				startPosition += Utf16LittleEndianPreamble.Length;
			}
			else if (prefixCount >= prefix.Length && prefix[1] == 0 && prefix[2] == 0 && prefix[3] == 0)
			{
				codeUnitSize = sizeof(int);
			}
			else if (prefixCount >= prefix.Length && prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 0)
			{
				codeUnitSize = sizeof(int);
				bigEndian = true;
			}
			else if (prefixCount >= prefix.Length && prefix[1] == 0 && prefix[3] == 0)
			{
				codeUnitSize = sizeof(char);
			}
			else if (prefixCount >= prefix.Length && prefix[0] == 0 && prefix[2] == 0)
			{
				codeUnitSize = sizeof(char);
				bigEndian = true;
			}

			if (startPosition < endPosition && (endPosition - startPosition) % codeUnitSize == 0)
			{
				long firstPosition = startPosition;
				int first;
				do
				{
					first = ReadCodeUnit(stream, firstPosition, codeUnitSize, bigEndian);
					firstPosition += codeUnitSize;
				}
				while (firstPosition < endPosition && IsXmlWhitespace(first));

				long lastPosition = endPosition - codeUnitSize;
				int last;
				do
				{
					last = ReadCodeUnit(stream, lastPosition, codeUnitSize, bigEndian);
					lastPosition -= codeUnitSize;
				}
				while (lastPosition >= startPosition && IsXmlWhitespace(last));

				result = first == '<' && last == '>';
			}
		}
		finally
		{
			stream.Position = originalPosition;
		}

		return result;
	}

	private static IReadOnlyList<Entry> ParseContent(string text, DocumentParser parser)
	{
		IReadOnlyList<Entry>? entries = null;
		if (parser.HasStructuredParsers && LooksLikeXml(text))
		{
			try
			{
				XDocument structuredDocument = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
				entries = parser.Parse(structuredDocument);
			}
			catch (XmlException)
			{
				// The inexpensive XML probe produced a false positive. Fall back to text parsing.
			}
		}

		if (entries == null)
		{
			using StringReader reader = new(text);
			entries = parser.Parse(reader);
		}

		return entries;
	}

	private static IReadOnlyList<Entry> ParseTextStream(Stream stream, DocumentParser parser)
	{
		using MemoryStream contentStream = new();
		stream.CopyTo(contentStream);
		byte[] content = contentStream.ToArray();
		Encoding encoding = StrictUtf8;
		int preambleLength = 0;
		bool allowLatin1Fallback = true;
		if (HasPrefix(content, content.Length, Utf32BigEndianPreamble))
		{
			encoding = StrictUtf32BigEndian;
			preambleLength = Utf32BigEndianPreamble.Length;
			allowLatin1Fallback = false;
		}
		else if (HasPrefix(content, content.Length, Utf32LittleEndianPreamble))
		{
			encoding = StrictUtf32LittleEndian;
			preambleLength = Utf32LittleEndianPreamble.Length;
			allowLatin1Fallback = false;
		}
		else if (HasPrefix(content, content.Length, Utf8Preamble))
		{
			preambleLength = Utf8Preamble.Length;
			allowLatin1Fallback = false;
		}
		else if (HasPrefix(content, content.Length, Utf16BigEndianPreamble))
		{
			encoding = StrictUtf16BigEndian;
			preambleLength = Utf16BigEndianPreamble.Length;
			allowLatin1Fallback = false;
		}
		else if (HasPrefix(content, content.Length, Utf16LittleEndianPreamble))
		{
			encoding = StrictUtf16LittleEndian;
			preambleLength = Utf16LittleEndianPreamble.Length;
			allowLatin1Fallback = false;
		}

		string text;
		try
		{
			// ChordPro permits UTF encodings and ISO-8859-1. Try strict UTF-8 first so
			// invalid byte sequences can fall back to Latin-1 instead of becoming U+FFFD.
			text = encoding.GetString(content, preambleLength, content.Length - preambleLength);
		}
		catch (DecoderFallbackException) when (allowLatin1Fallback)
		{
			text = Encoding.GetEncoding(Latin1CodePage).GetString(content);
		}

		using StringReader reader = new(text);
		IReadOnlyList<Entry> result = parser.Parse(reader);
		return result;
	}

	private static int ReadCodeUnit(Stream stream, long position, int codeUnitSize, bool bigEndian)
	{
		stream.Position = position;
		int result = 0;
		for (int index = 0; index < codeUnitSize; index++)
		{
			int value = stream.ReadByte();
			if (value < 0)
			{
				result = -1;
				break;
			}

			int shift = (bigEndian ? codeUnitSize - index - 1 : index) * BitsPerByte;
			result |= value << shift;
		}

		return result;
	}

	private static bool HasPrefix(byte[] input, int inputCount, byte[] prefix)
	{
		bool result = inputCount >= prefix.Length;
		for (int index = 0; result && index < prefix.Length; index++)
		{
			result = input[index] == prefix[index];
		}

		return result;
	}

	#endregion
}
