namespace Menees.Chords.Parsers;

#region Using Directives

using System.Diagnostics;

#endregion

[TestClass]
public class ChordParserTests
{
	#region Public Methods

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

	[TestMethod]
	public void Valid()
	{
		Test("A");
		Test("D/F#", root: "D", bass: "F#");
		Test("Am", root: "A", modifiers: new[] { "m" });
		Test("Asus2", root: "A", modifiers: new[] { "sus", "2" });
		Test("Dadd9add11", root: "D", modifiers: new[] { "add", "9", "add", "11" }); // x54030
		Test("[C#7b5/D]", start: 1, length: 7, root: "C#", modifiers: new[] { "7", "b", "5" }, bass: "D");
		Test("C/Ab", root: "C", bass: "Ab");
		Test("Caugmaj13", root: "C", modifiers: new[] { "aug", "maj", "13" });
		Test("C#min7dim5", root: "C#", modifiers: new[] { "min", "7", "dim", "5" });
		Test("  Ebm7  ", name: "Ebm7", root: "Eb", modifiers: new[] { "m", "7" });
		Test("CM7", root: "C", modifiers: new[] { "M", "7" });

		Test("1/3", root: "1", bass: "3", notation: Notation.Nashville);
		Test("3#7b9", root: "3", modifiers: new[] { "#", "7", "b", "9" }, notation: Notation.Nashville);

		Test("I/IV", root: "I", bass: "IV", notation: Notation.Roman);
		Test("viiadd3sus4", root: "vii", modifiers: new[] { "add", "3", "sus", "4" }, notation: Notation.Roman);

		static void Test(
			string text,
			string? name = null,
			string? root = null,
			string[]? modifiers = null,
			string? bass = null,
			Notation notation = Notation.Name,
			int start = 0,
			int? length = null)
		{
			length ??= text.Length - start;
			name ??= text.Substring(start, length.Value).Trim();
			root ??= name;
			ChordParser parser = new(text, start, length.Value);
			parser.Text.ShouldBe(name);
			parser.Errors.Count.ShouldBe(0);
			Chord chord = parser.Chord.ShouldNotBeNull();
			chord.Name.ShouldBe(name);
			chord.Root.ShouldBe(root);
			chord.Modifiers.ShouldBe(modifiers ?? Array.Empty<string>());
			chord.Bass.ShouldBe(bass);
			chord.Notation.ShouldBe(notation);
		}
	}

	[TestMethod]
	public void Invalid()
	{
		Test("Not");
		Test("A/");
		Test("A!");
		Test("C#mod4");
		Test("A|B");
		Test("Coda");
		Test("D/F#q");
		Test("Viii");
		Test("1/8");
		Test("C#/4");
		Test("iv/D");

		static void Test(string text)
		{
			ChordParser parser = new(text);
			Debug.WriteLine($"{text}: {string.Join("|", parser.Errors)}");
			parser.Text.ShouldBe(text.Trim());
			parser.Chord.ShouldBeNull();
			parser.Errors.Count.ShouldBeGreaterThanOrEqualTo(0);
		}
	}

	#endregion
}