namespace Menees.Chords.Parsers;

[TestClass]
public class GroupEntriesTests
{
	#region Public Methods

	[TestMethod]
	public void ByChordLinePair()
	{
		IReadOnlyList<Entry> entries = GetEntries(
			"""
			A       E    D       E
			 Oh yeah life goes on
			A               E         D             A
			 Long after the thrill of livin' is gone
			""",
			GroupEntries.ByChordLinePair);
		entries.Count.ShouldBe(2);
		entries[0].ShouldBeOfType<ChordLyricPair>();
		entries[1].ShouldBeOfType<ChordLyricPair>();
	}

	[TestMethod]
	public void ByChordProEnvironment()
	{
		Assert.Fail();
	}

	[TestMethod]
	public void ByHeaderLine()
	{
		IReadOnlyList<Entry> entries = GetEntries(
			"""
			[Verse 2]
			A                   E      D                E
			 Suckin' on a chili dog outside Tastee Freez

			[Chorus]
			A        E D           E
			 Oh yeah,  life goes on
			""",
			GroupEntries.ByHeaderLine);
		entries.Count.ShouldBe(2);
		Section section = entries[0].ShouldBeOfType<Section>();
		section.Entries.Count.ShouldBe(4);
		section.Entries[0].ShouldBeOfType<HeaderLine>();

		section = entries[1].ShouldBeOfType<Section>();
		section.Entries.Count.ShouldBe(3);
		section.Entries[0].ShouldBeOfType<HeaderLine>();
	}

	[TestMethod]
	public void ByBlankLine()
	{
		IReadOnlyList<Entry> entries = GetEntries(
			"""
			A       E    D       E
			 Oh yeah life goes on

			A               E         D             A
			 Long after the thrill of livin' is gone
			""",
			GroupEntries.ByBlankLine);
		entries.Count.ShouldBe(3);
		entries[0].ShouldBeOfType<Section>().Entries.Count.ShouldBe(2);
		entries[1].ShouldBeOfType<BlankLine>();
		entries[2].ShouldBeOfType<Section>().Entries.Count.ShouldBe(2);
	}

	#endregion

	#region Private Methods

	private static IReadOnlyList<Entry> GetEntries(string text, Func<GroupContext, IReadOnlyList<Entry>> grouper)
	{
		DocumentParser parser = new(groupers: new[] { grouper });
		Document document = Document.Parse(text, parser);
		IReadOnlyList<Entry> result = document.Entries;
		return result;
	}

	#endregion
}