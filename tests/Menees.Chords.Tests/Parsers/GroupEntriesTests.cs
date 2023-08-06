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
		IReadOnlyList<Entry> entries = GetEntries(
			"""
			{start_of_verse}
			{comment: D* = D Dsus4 D Dsus2 D}
			It's [D*]all the same, [Cadd9]only the names will [G]change
			[Cadd9]Everyday[G] it seems we're [G]wast[F]ing [D]away
			[D*]Another place where the [Cadd9]faces are so [G]cold
			I'd [Cadd9]drive all [G]night just to [G]get [F]back [D]home
			{end_of_verse}

			{start_of_chorus}
			I'm a [Cadd9]cowboy[G].  On a [F]steel horse I [D]ride.
			I'm [Cadd9]wanted[G]  [F]dead or [D]alive
			[Cadd9]Wanted[G]  [F]dead or [D]alive
			{end_of_chorus}
			""",
			GroupEntries.ByChordProEnvironment);

		entries.Count.ShouldBe(3);
		Section section = entries[0].ShouldBeOfType<Section>();
		section.Entries.Count.ShouldBe(7);
		section.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().ShortName.ShouldBe("sov");

		entries[1].ShouldBeOfType<BlankLine>();
		section = entries[2].ShouldBeOfType<Section>();
		section.Entries.Count.ShouldBe(5);
		section.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().ShortName.ShouldBe("soc");
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