namespace Menees.Chords;

[TestClass]
public class TextEntryTests
{
	#region Public Methods

	[TestMethod]
	public void Text()
	{
		const string Expected = " Testing\t";
		LyricLine line = new(Expected);
		line.Text.ShouldBe(Expected);
		line.ToString().ShouldBe(Expected);
	}

	[TestMethod]
	public void Annotations()
	{
		TestEntry testEntry = new(new Comment("Testing"), new LyricLine("Lyrics!"));
		testEntry.Annotations.Count.ShouldBe(2);
		testEntry.Annotations[0].ShouldBeOfType<Comment>().Text.ShouldBe("Testing");
		testEntry.Annotations[1].ShouldBeOfType<LyricLine>().Text.ShouldBe("Lyrics!");
	}

	#endregion

	#region Private Types

	private sealed class TestEntry : TextEntry
	{
		#region Constructors

		public TestEntry(params Entry[] annotations)
			: base(string.Empty)
		{
			foreach (Entry annotation in annotations)
			{
				this.AddAnnotation(annotation);
			}
		}

		#endregion

		#region Public Methods

		public override string ToString() => string.Join(Environment.NewLine, this.Annotations);

		#endregion
	}

	#endregion
}