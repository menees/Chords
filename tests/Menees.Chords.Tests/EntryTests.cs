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

	[TestMethod]
	public void CloneTest()
	{
		TestEntry testEntry = new(new Comment("Testing"), new LyricLine("Lyrics!"));
		testEntry.Annotations.Count.ShouldBe(2);

		TestEntry entry2 = testEntry.Clone(testEntry.Annotations);
		entry2.Annotations.Count.ShouldBe(2);

		TestEntry entry1 = testEntry.Clone(testEntry.Annotations.Take(1));
		entry1.Annotations.Count.ShouldBe(1);
		entry1.Annotations[0].ShouldBeOfType<Comment>().ToString().ShouldBe("Testing");

		TestEntry entry0 = testEntry.Clone(Array.Empty<Entry>());
		entry0.Annotations.Count.ShouldBe(0);

		TestEntry entryNull = testEntry.Clone(null);
		entryNull.Annotations.Count.ShouldBe(0);
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

		public new TestEntry Clone(IEnumerable<Entry>? annotations) => (TestEntry)base.Clone(annotations);

		#endregion
	}

	#endregion
}