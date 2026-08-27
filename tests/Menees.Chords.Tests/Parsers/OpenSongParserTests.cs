namespace Menees.Chords.Parsers;

#region Using Directives

using System.IO;
using System.Text;
using System.Xml.Linq;
using Menees.Chords.Transformers;

#endregion

[TestClass]
public sealed class OpenSongParserTests
{
	#region Public Methods

	[TestMethod]
	[DataRow("blessed assurance i6975", "Blessed Assurance", "D", 4, 16, 4)]
	[DataRow("swing low i42430", "Swing Low", "D", 4, 16, 4)]
	[DataRow("battle belongs i101115", "Battle belongs", "Db", 11, 35, 12)]
	public void LoadSamplesTest(
		string fileName,
		string title,
		string key,
		int sectionCount,
		int pairCount,
		int headerCount)
	{
		string path = GetSampleFileName(fileName);
		Document document = Document.Load(path);
		IReadOnlyList<Entry> entries = EnumerateEntries(document.Entries);

		document.FileName.ShouldBe(path);
		entries.OfType<Section>().Count().ShouldBe(sectionCount);
		entries.OfType<ChordLyricPair>().Count().ShouldBe(pairCount);
		entries.OfType<HeaderLine>().Count().ShouldBe(headerCount);
		document.Entries.OfType<MetadataEntry>().Single(metadata => metadata.Name == "title").Argument.ShouldBe(title);
		document.Entries.OfType<MetadataEntry>().Single(metadata => metadata.Name == "key").Argument.ShouldBe(key);
	}

	[TestMethod]
	public void MetadataOrderTest()
	{
		Document document = Document.Load(GetSampleFileName("battle belongs i101115"));
		string[] entryDescriptions = [.. document.Entries.Select(entry => entry is MetadataEntry metadata ? metadata.Name : entry.GetType().Name)];

		entryDescriptions.Take(3).ShouldBe(["tempo", "title", "author"]);
		entryDescriptions[^1].ShouldBe("key");
	}

	[TestMethod]
	public void ParseStringAndReaderTest()
	{
		string text = File.ReadAllText(GetSampleFileName("swing low i42430"));
		Document parsed = Document.Parse(text);
		using StringReader reader = new(text);
		Document loaded = Document.Load(reader);

		GetTitle(parsed).ShouldBe("Swing Low");
		GetTitle(loaded).ShouldBe("Swing Low");
		parsed.Entries.Count.ShouldBe(loaded.Entries.Count);
	}

	[TestMethod]
	public void UsesOpenSongLineParsersTest()
	{
		string text = File.ReadAllText(GetSampleFileName("swing low i42430"));
		DocumentParser parser = new(DocumentParser.ChordProLineParsers);
		Document document = Document.Parse(text, parser);
		IReadOnlyList<Entry> entries = EnumerateEntries(document.Entries);

		entries.OfType<HeaderLine>().Select(header => header.Text).ShouldBe(["C1", "V1", "V2", "V3"]);
		entries.OfType<ChordLyricPair>().Count().ShouldBe(16);
		entries.OfType<LyricLine>().All(line => !line.Text.Contains("OpenSong Section", StringComparison.Ordinal)).ShouldBeTrue();
	}

	[TestMethod]
	public void StreamEncodingTest()
	{
		Encoding[] encodings =
		[
			new UTF8Encoding(true),
			Encoding.Unicode,
			Encoding.BigEndianUnicode,
			Encoding.UTF32,
			new UTF32Encoding(true, true),
		];

		foreach (Encoding encoding in encodings)
		{
			string xml = $"<?xml version=\"1.0\" encoding=\"{encoding.WebName}\"?><song><title>Café</title><lyrics>[V1]\n.C\n Café</lyrics></song>";
			byte[] content = [.. encoding.GetPreamble(), .. encoding.GetBytes(xml)];
			using MemoryStream stream = new(content);
			Document document = Document.Load(stream);

			GetTitle(document).ShouldBe("Café", encoding.WebName);
			EnumerateEntries(document.Entries).OfType<ChordLyricPair>().Count().ShouldBe(1, encoding.WebName);
			stream.CanRead.ShouldBeTrue();
		}
	}

	[TestMethod]
	public void NonSeekableStreamTest()
	{
		byte[] content = File.ReadAllBytes(GetSampleFileName("blessed assurance i6975"));
		using NonSeekableStream stream = new(content);
		Document document = Document.Load(stream);

		GetTitle(document).ShouldBe("Blessed Assurance");
		stream.WasDisposed.ShouldBeFalse();
	}

	[TestMethod]
	public void Latin1StreamFallbackTest()
	{
		byte[] content = Encoding.GetEncoding(28591).GetBytes("title: Café");
		using MemoryStream stream = new(content);
		Document document = Document.Load(stream);

		document.Entries.Single().ShouldBeOfType<MetadataEntry>().Argument.ShouldBe("Café");
		stream.CanRead.ShouldBeTrue();
	}

	[TestMethod]
	public void StructuredParsersCanBeCustomizedTest()
	{
		const string Xml = "<song><title>Original</title><lyrics> Lyrics</lyrics></song>";
		bool receivedTypedContext = false;
		DocumentParser customParser = new(
			structuredParsers:
			[
				context =>
				{
					receivedTypedContext = context is StructuredContext<XDocument>;
					return receivedTypedContext ? [new LyricLine("custom")] : null;
				},
			]);
		Document custom = Document.Parse(Xml, customParser);
		DocumentParser textParser = new(structuredParsers: DocumentParser.Unstructured);
		Document text = Document.Parse(Xml, textParser);

		receivedTypedContext.ShouldBeTrue();
		custom.Entries.Single().ShouldBeOfType<LyricLine>().Text.ShouldBe("custom");
		text.Entries.OfType<MetadataEntry>().ShouldBeEmpty();
	}

	[TestMethod]
	public void UnrecognizedOrInvalidXmlFallsBackToTextTest()
	{
		Document unrecognized = Document.Parse("<root>text</root>");
		Document invalid = Document.Parse("<song><title>Broken</song>");

		unrecognized.Entries.OfType<MetadataEntry>().ShouldBeEmpty();
		invalid.Entries.OfType<MetadataEntry>().ShouldBeEmpty();
	}

	[TestMethod]
	public void SlideBreakTest()
	{
		Document document = Document.Parse("<song><title>Break</title><lyrics>[V1]\n.C || G\n Line</lyrics></song>");
		MetadataEntry slideBreak = document.Entries.OfType<MetadataEntry>()
			.Single(metadata => metadata.Name == OpenSongParser.SlideBreakMetadataName);

		slideBreak.Argument.ShouldBe("true");
	}

	[TestMethod]
	public void ChordGridLineTest()
	{
		const string Xml = "<song><title>Grid</title><lyrics>[P1]\n.C / / (G) //\n  (Intro)</lyrics></song>";
		Document document = Document.Parse(Xml);
		IReadOnlyList<Entry> entries = EnumerateEntries(document.Entries);

		entries.OfType<ChordLyricPair>().ShouldBeEmpty();
		entries.OfType<ChordProLyricLine>().Single().ToString().ShouldBe("[C] [*/] [*/] [*(][G][*)] [*//]");
		entries.OfType<Comment>().Single().Text.ShouldBe("Intro");
	}

	#endregion

	#region Private Methods

	private static List<Entry> EnumerateEntries(IReadOnlyList<Entry> entries)
	{
		List<Entry> result = [];
		foreach (Entry entry in entries)
		{
			result.Add(entry);
			if (entry is IEntryContainer container)
			{
				result.AddRange(EnumerateEntries(container.Entries));
			}
		}

		return result;
	}

	private static string GetSampleFileName(string fileName)
		=> TestUtility.GetSampleFileName(Path.Combine("OpenSong", fileName));

	private static string GetTitle(Document document)
		=> document.Entries.OfType<MetadataEntry>().Single(metadata => metadata.Name == "title").Argument;

	#endregion

	#region Private Types

	private sealed class NonSeekableStream : Stream
	{
		#region Private Data Members

		private readonly MemoryStream stream;

		#endregion

		#region Constructors

		public NonSeekableStream(byte[] content)
		{
			this.stream = new(content);
		}

		#endregion

		#region Public Properties

		public override bool CanRead => this.stream.CanRead;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public bool WasDisposed { get; private set; }

		#endregion

		#region Public Methods

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
			=> this.stream.Read(buffer, offset, count);

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		#endregion

		#region Protected Methods

		protected override void Dispose(bool disposing)
		{
			this.WasDisposed = true;
			if (disposing)
			{
				this.stream.Dispose();
			}

			base.Dispose(disposing);
		}

		#endregion
	}

	#endregion
}
