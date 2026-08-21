namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

[TestClass]
public class ChordProEnvironmentTests
{
	#region Public Methods

	[TestMethod]
	public void DelegatedEnvironmentTest()
	{
		const string Text = """
			{start_of_svg label="Alert"}
			<svg viewBox="0 0 10 10">
			{preserved as svg source}
			</svg>
			{end_of_svg}
			{start_of_abc}
			X:1
			[Am] This is ABC, not a chord line.
			{end_of_abc}
			{start_of_ly}
			{ c' d' e' }
			{end_of_ly}
			{start_of_textblock align=right flush=left}
			Plain text
			{end_of_textblock}
			""";
		DocumentParser parser = new(
			DocumentParser.ChordProLineParsers,
			[GroupEntries.ByChordProEnvironment],
			tabWidth: null);
		Document document = Document.Parse(Text, parser);
		Section[] sections = [.. document.Entries.Cast<Section>()];

		sections.Select(section => section.Environment!.Kind).ShouldBe(
			[
				ChordProEnvironmentKind.Svg,
				ChordProEnvironmentKind.Abc,
				ChordProEnvironmentKind.LilyPond,
				ChordProEnvironmentKind.TextBlock,
			]);
		sections.All(section => section.Environment!.IsDelegated).ShouldBeTrue();
		sections[0].Environment!.Label.ShouldBe("Alert");
		sections[0].Entries[2].ShouldBeOfType<ChordProDelegateLine>().Text.ShouldBe("{preserved as svg source}");
		sections[1].Entries[2].ShouldBeOfType<ChordProDelegateLine>().Text.ShouldStartWith("[Am]");
		sections[2].Entries[1].ShouldBeOfType<ChordProDelegateLine>().Text.ShouldBe("{ c' d' e' }");
		sections[3].Environment!.Start.Args.Attributes["align"].ShouldBe("right");
	}

	[TestMethod]
	public void OrdinaryEnvironmentTest()
	{
		Document document = Document.Parse("{start_of_solo}\n[G]Play\n{end_of_solo}");
		Section section = document.Entries.Single().ShouldBeOfType<Section>();

		section.Environment.ShouldNotBeNull().Kind.ShouldBe(ChordProEnvironmentKind.Generic);
		section.Environment.IsDelegated.ShouldBeFalse();
		section.Entries[1].ShouldBeOfType<ChordProLyricLine>();
	}

	#endregion
}
