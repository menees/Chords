﻿namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

[TestClass]
public class ChordProDirectiveLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseValidTest()
	{
		Test("{name}", "name");
		Test("{name: argument}", "name", "argument");
		Test("{name:argument}", "name", "argument");
		Test(" { name : argument } ", "name", "argument");
		Test("{name argument}", "name", "argument");
		Test(" { name  argument } ", "name", "argument");

		Test("{start_of_tab: Solo}", "start_of_tab", "Solo", "sot");
		Test("{eot}", "eot", null, "end_of_tab");

		var directive = Test(" { name-tenor : argument } ", "name", "argument");
		directive.QualifiedName.ToString().ShouldBe("name-tenor");
		directive.Args.Attributes.Count.ShouldBe(0);

		directive = Test(" { name-!bass : key1='value1' key2 = 'value2' } ", "name", "key1='value1' key2 = 'value2'");
		directive.QualifiedName.ToString().ShouldBe("name-!bass");
		directive.Args.Attributes.Count.ShouldBe(2);
		directive.Args.Attributes["key1"].ShouldBe("value1");
		directive.Args.Attributes["key2"].ShouldBe("value2");

		static ChordProDirectiveLine Test(string text, string? expectedName = null, string? expectedArgument = null, string? expectedAlternateName = null)
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

			return line!;
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("{}");
		Test("Directive: Nope");
		Test("{[name]}");
		Test("{weird~ness value}");
		Test("{Joe's}");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine.TryParse(context).ShouldBeNull();
		}
	}

	[TestMethod]
	public void ToStringTest()
	{
		Test("{name}");
		Test("{name: argument}");
		Test("{name:argument}", "{name: argument}");
		Test(" { name : argument } ", "{name: argument}");
		Test(" { name-tenor : argument } ", "{name-tenor: argument}");
		Test(" { name-!bass : key1='value1' key2 = 'value2' } ", "{name-!bass: key1='value1' key2 = 'value2'}");

		static void Test(string text, string? expectedText = null)
		{
			ChordProDirectiveLine line = Parse(text);
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
		Test("{soc: label=\"Main Chorus\" part = 'bass'}", "{start_of_chorus: label=\"Main Chorus\" part = 'bass'}");

		static void Test(string text, string? expectedText = null)
		{
			ChordProDirectiveLine line = Parse(text);
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
		Test("{start_of_chorus: label=\"Main Chorus\" part = 'alto'}", "{soc: label=\"Main Chorus\" part = 'alto'}");

		static void Test(string text, string? expectedText = null)
		{
			ChordProDirectiveLine line = Parse(text);
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
			ChordProDirectiveLine define = ChordProDirectiveLine.Convert(chordDefinition, inline: false);
			define.ToString().ShouldBe(expected);
			ChordProDirectiveLine chord = ChordProDirectiveLine.Convert(chordDefinition, inline: true);
			chord.ToString().ShouldBe(expected.Replace("{define:", "{chord:"));
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

		Test("[Intro] (+ Hook)", null, "{start_of_bridge: Intro} (+ Hook)");
		Test("[Intro (with open \"D D\" string)]", true, "{start_of_bridge: Intro (with open \"D D\" string)}");
		Test("[Verse 1]", false, "{sov: Verse 1}");
		Test("[Interlude (hammering chords)]", null, "{start_of_bridge: Interlude (hammering chords)}");
		Test("[Bridge (N.C.)]", true, "{start_of_bridge: Bridge (N.C.)}");
		Test("[Outro (heavy chords with open \"D D\" string)]", false, "{sob: Outro (heavy chords with open \"D D\" string)}");
		Test("[Verse] (+ Hook)", null, "{start_of_verse} (+ Hook)");
		Test("[Solo Lead – Relative to capo]", true, "{start_of_bridge: Solo Lead – Relative to capo}");
		Test("[Chorus] (a cappella with hand claps)", false, "{soc} (a cappella with hand claps)");

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

	[TestMethod]
	public void ConvertMetadataEntryTest()
	{
		Test("Title: Test Song", "title", "Test Song");
		Test(" key : C ", "key", "C");

		static void Test(string text, string name, string argument)
		{
			LineContext context = LineContextTests.Create(text);
			MetadataEntry metadata = MetadataEntry.TryParse(context).ShouldNotBeNull();
			metadata.Name.ShouldBe(name);
			metadata.Argument.ShouldBe(argument);

			ChordProDirectiveLine directive = ChordProDirectiveLine.Convert(metadata);
			directive.Name.ShouldBe(metadata.Name);
			directive.Argument.ShouldBe(metadata.Argument);
		}
	}

	#endregion

	#region Internal Methods

	internal static ChordProDirectiveLine Parse(string text)
	{
		LineContext context = LineContextTests.Create(text);
		ChordProDirectiveLine result = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
		return result;
	}

	#endregion
}