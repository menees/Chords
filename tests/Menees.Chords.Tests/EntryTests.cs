namespace Menees.Chords;

[TestClass]
public class EntryTests
{
	#region Public Methods

	[TestMethod]
	public void AnnotationsTest()
	{
		TestEntry testEntry = new(new Comment("Testing"), new LyricLine("Lyrics!"));
		testEntry.Annotations.Count.ShouldBe(2);
		testEntry.Annotations[0].ShouldBeOfType<Comment>().Text.ShouldBe("Testing");
		testEntry.Annotations[1].ShouldBeOfType<LyricLine>().Text.ShouldBe("Lyrics!");
	}

	#endregion

	#region Private Types

	private sealed class TestEntry : Entry
	{
		#region Constructors

		public TestEntry(params Entry[] annotations)
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