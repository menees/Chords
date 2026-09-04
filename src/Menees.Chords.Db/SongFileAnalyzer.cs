#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Menees.Chords.Parsers;

#endregion

namespace Menees.Chords.Db;

/// <summary>Detects supported song formats, encodings, and metadata while preserving source bytes.</summary>
public static class SongFileAnalyzer
{
	#region Public Data

	/// <summary>Gets the current persisted metadata-analysis version.</summary>
	public const int CurrentAnalysisVersion = 1;

	#endregion

	#region Private Data

	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private static readonly byte[] Utf32BigEndianPreamble = [0x00, 0x00, 0xFE, 0xFF];
	private static readonly byte[] Utf32LittleEndianPreamble = [0xFF, 0xFE, 0x00, 0x00];
	private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];
	private static readonly byte[] Utf16BigEndianPreamble = [0xFE, 0xFF];
	private static readonly byte[] Utf16LittleEndianPreamble = [0xFF, 0xFE];

	#endregion

	#region Public API

	/// <summary>Computes a lowercase SHA-256 content hash.</summary>
	public static string Hash(ReadOnlySpan<byte> content)
		=> Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

	/// <summary>Analyzes a complete source file held in memory.</summary>
	public static SongFileAnalysis Analyze(ReadOnlyMemory<byte> content, string sourceName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
		SongFileAnalysis result;
		if (content.Span.StartsWith("%PDF-"u8))
		{
			result = new()
			{
				MediaKind = MediaKind.Pdf,
				SourceFormat = SourceFormat.Unknown,
				Title = GetFallbackTitle(sourceName),
			};
		}
		else
		{
			(Encoding encoding, ByteOrderMarkKind bom) = DetectEncoding(content.Span);
			int preambleLength = GetPreambleLength(bom);
			string text = encoding.GetString(content.Span[preambleLength..]);
			using StringReader reader = new(text);
			Document document = Document.Load(reader);
			bool openSong = OpenSongParser.LooksLikeOpenSong(text);
			List<MetadataEntry> entries = [.. GetMetadata(document)];
			SortedDictionary<string, IReadOnlyList<SourceMetadataValue>> metadata = new(StringComparer.Ordinal);
			foreach (IGrouping<string, MetadataEntry> group in entries.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
			{
				metadata[group.Key.ToLowerInvariant()] =
				[
					.. group.Select(entry => new SourceMetadataValue { Value = entry.Argument, SourceName = entry.Name }),
				];
			}

			string title = entries.FirstOrDefault(entry => entry.Name is "title" or "t")?.Argument
				?? GetFallbackTitle(sourceName);
			IReadOnlyList<string> artists =
			[
				.. entries
					.Where(entry => entry.Name is "artist" or "author")
					.Select(entry => entry.Argument)
					.Where(value => !string.IsNullOrWhiteSpace(value))
					.Distinct(StringComparer.OrdinalIgnoreCase),
			];
			result = new()
			{
				MediaKind = MediaKind.Text,
				SourceFormat = openSong ? SourceFormat.OpenSongXml : DetectTextFormat(document),
				TextEncoding = encoding.WebName,
				ByteOrderMark = bom,
				Title = title,
				Artists = artists,
				Metadata = metadata,
			};
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static (Encoding Encoding, ByteOrderMarkKind Bom) DetectEncoding(ReadOnlySpan<byte> content)
	{
		(Encoding Encoding, ByteOrderMarkKind Bom) result;
		if (content.StartsWith(Utf32BigEndianPreamble))
		{
			result = (new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true), ByteOrderMarkKind.Utf32BigEndian);
		}
		else if (content.StartsWith(Utf32LittleEndianPreamble))
		{
			result = (new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true), ByteOrderMarkKind.Utf32LittleEndian);
		}
		else if (content.StartsWith(Utf8Preamble))
		{
			result = (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), ByteOrderMarkKind.Utf8);
		}
		else if (content.StartsWith(Utf16BigEndianPreamble))
		{
			result = (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true), ByteOrderMarkKind.Utf16BigEndian);
		}
		else if (content.StartsWith(Utf16LittleEndianPreamble))
		{
			result = (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true), ByteOrderMarkKind.Utf16LittleEndian);
		}
		else
		{
			try
			{
				_ = StrictUtf8.GetString(content);
				result = (StrictUtf8, ByteOrderMarkKind.None);
			}
			catch (DecoderFallbackException)
			{
				result = (Encoding.Latin1, ByteOrderMarkKind.None);
			}
		}

		return result;
	}

	private static SourceFormat DetectTextFormat(Document document)
	{
		bool chordPro = document.Entries.Any(entry => entry is ChordProDirectiveLine or ChordProLyricLine);
		bool chordOverText = document.Entries.Any(entry => entry is ChordLine);
		return (chordPro, chordOverText) switch
		{
			(true, true) => SourceFormat.Mixed,
			(true, false) => SourceFormat.ChordPro,
			(false, true) => SourceFormat.ChordOverText,
			_ => SourceFormat.Unknown,
		};
	}

	private static string GetFallbackTitle(string sourceName)
	{
		string fileName = Path.GetFileName(sourceName);
		string result = Path.HasExtension(fileName) ? Path.GetFileNameWithoutExtension(fileName) : fileName;
		return string.IsNullOrWhiteSpace(result) ? "Untitled" : result;
	}

	private static int GetPreambleLength(ByteOrderMarkKind bom) => bom switch
	{
		ByteOrderMarkKind.Utf8 => Utf8Preamble.Length,
		ByteOrderMarkKind.Utf16LittleEndian => Utf16LittleEndianPreamble.Length,
		ByteOrderMarkKind.Utf16BigEndian => Utf16BigEndianPreamble.Length,
		ByteOrderMarkKind.Utf32LittleEndian => Utf32LittleEndianPreamble.Length,
		ByteOrderMarkKind.Utf32BigEndian => Utf32BigEndianPreamble.Length,
		_ => 0,
	};

	private static IEnumerable<MetadataEntry> GetMetadata(IEntryContainer container)
	{
		foreach (Entry entry in container.Entries)
		{
			if (entry is MetadataEntry metadata)
			{
				yield return metadata;
			}
			else if (entry is TitleLine title)
			{
				foreach (MetadataEntry item in title.Metadata)
				{
					yield return item;
				}
			}
			else if (entry is ChordProDirectiveLine directive && MetadataEntry.TryParse(directive) is MetadataEntry parsed)
			{
				yield return parsed;
			}

			if (entry is IEntryContainer child)
			{
				foreach (MetadataEntry descendant in GetMetadata(child))
				{
					yield return descendant;
				}
			}
		}
	}

	#endregion
}
