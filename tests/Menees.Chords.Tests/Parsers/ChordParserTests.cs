namespace Menees.Chords.Parsers;

[TestClass]
public class ChordParserTests
{
	[TestMethod]
	public void GetNoteLength()
	{
		ChordParser.GetNoteLength(string.Empty).ShouldBe(0);
		ChordParser.GetNoteLength("X").ShouldBe(0);
		ChordParser.GetNoteLength("L#").ShouldBe(0);
		ChordParser.GetNoteLength("A#", 1).ShouldBe(0);

		foreach (char upper in new[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G' })
		{
			foreach (char ch in new[] { upper, char.ToLower(upper) })
			{
				ChordParser.GetNoteLength($"{ch}").ShouldBe(1);
				ChordParser.GetNoteLength($"{ch}#").ShouldBe(2);
				ChordParser.GetNoteLength($"{ch}b").ShouldBe(2);

				const int PrefixLength = 3;
				string prefix = new('_', PrefixLength);
				ChordParser.GetNoteLength($"{prefix}{ch}xxxx", PrefixLength).ShouldBe(1);
				ChordParser.GetNoteLength($"{prefix}{ch}#xxxx", PrefixLength).ShouldBe(2);
				ChordParser.GetNoteLength($"{prefix}{ch}bxxxx", PrefixLength).ShouldBe(2);
			}
		}
	}
}