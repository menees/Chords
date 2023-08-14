namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

[TestClass]
public class ChordProDirectiveLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseTest()
	{
		Test("{name}", "name");
		Test("{name: argument}", "name", "argument");
		Test("{name:argument}", "name", "argument");
		Test(" { name : argument } ", "name", "argument");

		Test("{start_of_tab: Solo}", "start_of_tab", "Solo", "sot");
		Test("{eot}", "eot", null, "end_of_tab");

		static void Test(string text, string? expectedName = null, string? expectedArgument = null, string? expectedAlternateName = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine? line = ChordProDirectiveLine.TryParse(context);

			if (expectedName is null)
			{
				line.ShouldBeNull();
			}
			else
			{
				line.ShouldNotBeNull();
				line.Name.ShouldBe(expectedName);
				line.Argument.ShouldBe(expectedArgument);

				if (expectedAlternateName is not null)
				{
					if (line.Name == line.LongName)
					{
						line.ShortName.ShouldBe(expectedAlternateName);
					}
					else
					{
						line.LongName.ShouldBe(expectedAlternateName);
					}
				}
			}
		}
	}

	[TestMethod]
	public void ToStringTest()
	{
		Test("{name}");
		Test("{name: argument}");
		Test("{name:argument}", "{name: argument}");
		Test(" { name : argument } ", "{name: argument}");

		static void Test(string text, string? expectedText = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine line = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
			line.ToString().ShouldBe(expectedText ?? text);
		}
	}

	[TestMethod]
	public void ToLongStringTest()
	{
		Test("{name}");
		Test("{name: argument}");
		Test("{soc}", "{start_of_chorus}");
		Test("{soc: argument}", "{start_of_chorus: argument}");
		Test("{sot:argument}", "{start_of_tab: argument}");
		Test(" { sov : argument } ", "{start_of_verse: argument}");

		static void Test(string text, string? expectedText = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine line = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
			line.ToLongString().ShouldBe(expectedText ?? text);
		}
	}

	[TestMethod]
	public void ToShortStringTest()
	{
		Test("{name}");
		Test("{name: argument}");
		Test("{start_of_chorus}", "{soc}");
		Test("{start_of_chorus: argument}", "{soc: argument}");
		Test("{start_of_tab:argument}", "{sot: argument}");
		Test(" { start_of_verse : argument } ", "{sov: argument}");

		static void Test(string text, string? expectedText = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine line = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
			line.ToShortString().ShouldBe(expectedText ?? text);
		}
	}

	[TestMethod]
	public void ConvertChordDefinitionTest()
	{
		Test("Am", "x02210", "{define: Am base-fret 1 frets x 0 2 2 1 0}");
		Test("A/C#", "x42220", "{define: A/C# base-fret 1 frets x 4 2 2 2 0}");
		Test("A/C#", "_4222_", "{define: A/C# base-fret 2 frets x 4 2 2 2 x}");
		Test("G7", "320001", "{define: G7 base-fret 1 frets 3 2 0 0 0 1}");
		Test("C", "x-3-2-0-1-0", "{define: C base-fret 1 frets x 3 2 0 1 0}");
		Test("D/F#", "200232", "{define: D/F# base-fret 1 frets 2 0 0 2 3 2}");
		Test("A/C#", "_4222_", "{define: A/C# base-fret 2 frets x 4 2 2 2 x}");
		Test("Em", "12-14-14-13-12-12", "{define: Em base-fret 12 frets 12 14 14 13 12 12}");

		static void Test(string name, string defintion, string expected)
		{
			ChordDefinition chordDefinition = ChordDefinition.TryParse(name, defintion).ShouldNotBeNull();
			ChordProDirectiveLine directive = ChordProDirectiveLine.Convert(chordDefinition);
			directive.ToString().ShouldBe(expected);
		}
	}

	[TestMethod]
	public void ConvertHeaderLineTest()
	{
		Test("[Verse]", false, "{sov}");
		Test("[Chorus]", false, "{soc}");
		Test("[Bridge]", false, "{sob}");
		Test("[Verse]", true, "{start_of_verse}");
		Test("[Chorus]", true, "{start_of_chorus}");
		Test("[Bridge]", true, "{start_of_bridge}");

		Test("[Intro] (+ Hook)", null, "{start_of_bridge: Intro}(+ Hook)");
		Test("[Intro (with open \"D D\" string)]", true, "{start_of_bridge: Intro (with open \"D D\" string)}");
		Test("[Verse 1]", false, "{sov: Verse 1}");
		Test("[Interlude (hammering chords)]", null, "{start_of_bridge: Interlude (hammering chords)}");
		Test("[Bridge (N.C.)]", true, "{start_of_bridge: Bridge (N.C.)}");
		Test("[Outro (heavy chords with open \"D D\" string)]", false, "{sob: Outro (heavy chords with open \"D D\" string)}");
		Test("[Verse] (+ Hook)", null, "{start_of_verse}(+ Hook)");
		Test("[Solo Lead – Relative to capo]", true, "{start_of_bridge: Solo Lead – Relative to capo}");
		Test("[Chorus] (a cappella with hand claps)", false, "{soc}(a cappella with hand claps)");

		static void Test(string text, bool? preferLongNames, string expectedStart)
		{
			LineContext context = LineContextTests.Create(text);
			HeaderLine header = HeaderLine.TryParse(context).ShouldNotBeNull();
			(ChordProDirectiveLine start, ChordProDirectiveLine end) = ChordProDirectiveLine.Convert(header, preferLongNames);
			start.ToString().ShouldBe(expectedStart);

			string suffix = start.LongName.Substring("start_of_".Length);
			string expectedEnd = preferLongNames ?? true ? $"{{end_of_{suffix}}}" : $"{{eo{suffix[0]}}}";
			end.ToString().ShouldBe(expectedEnd);
		}
	}

	#endregion
}