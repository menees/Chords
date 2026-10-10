#region Using Directives

using System.Threading;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongTextDifferenceTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void ReplacementInsertionAndDeletionPreserveEverySourceLine()
	{
		IReadOnlyList<SongDifferenceLine> replacement = SongTextDifference.Compare("A\nold\nZ", "A\nnew\nZ", this.TestContext.CancellationToken);
		replacement.Select(row => row.Kind).ShouldBe([SongDifferenceKind.Unchanged, SongDifferenceKind.Changed, SongDifferenceKind.Unchanged]);
		replacement[1].LeftText.ShouldBe("old");
		replacement[1].RightText.ShouldBe("new");
		IReadOnlyList<SongDifferenceLine> inserted = SongTextDifference.Compare("A\nZ", "A\nnew\nZ", this.TestContext.CancellationToken);
		inserted[1].Kind.ShouldBe(SongDifferenceKind.Added);
		inserted[1].LeftNumber.ShouldBeNull();
		inserted[1].RightNumber.ShouldBe(2);
		IReadOnlyList<SongDifferenceLine> deleted = SongTextDifference.Compare("A\nold\nZ", "A\nZ", this.TestContext.CancellationToken);
		deleted[1].Kind.ShouldBe(SongDifferenceKind.Removed);
		deleted[1].RightNumber.ShouldBeNull();
	}

	[TestMethod]
	public void WhitespaceCaseRepeatedLinesAndTrailingNewlineRemainSignificant()
	{
		IReadOnlyList<SongDifferenceLine> rows = SongTextDifference.Compare("same\n A\nsame\na\n", "same\na\nsame\nA", this.TestContext.CancellationToken);
		rows.Any(row => row.Kind != SongDifferenceKind.Unchanged).ShouldBeTrue();
		rows.Where(row => row.LeftNumber is not null).Select(row => row.LeftText).ShouldBe(["same", " A", "same", "a", string.Empty]);
		rows.Where(row => row.RightNumber is not null).Select(row => row.RightText).ShouldBe(["same", "a", "same", "A"]);
	}

	[TestMethod]
	public void LargeSharedPrefixDoesNotBecomeQuadraticDifferenceWork()
	{
		const int LineCount = 10_000;
		string common = string.Join("\n", Enumerable.Range(0, LineCount).Select(index => "Line " + index)) + "\n";
		IReadOnlyList<SongDifferenceLine> rows = SongTextDifference.Compare(common + "left", common + "right", this.TestContext.CancellationToken);
		rows.Count.ShouldBe(LineCount + 1);
		rows.Count(row => row.Kind == SongDifferenceKind.Changed).ShouldBe(1);
	}

	[TestMethod]
	public void HugeDivergenceAndCancellationStopBoundedWork()
	{
		const int DivergentLines = 6_000;
		string left = string.Join("\n", Enumerable.Range(0, DivergentLines).Select(index => "Left " + index));
		string right = string.Join("\n", Enumerable.Range(0, DivergentLines).Select(index => "Right " + index));
		Should.Throw<InvalidOperationException>(() => SongTextDifference.Compare(left, right, this.TestContext.CancellationToken));
		using CancellationTokenSource canceled = new();
		canceled.Cancel();
		Should.Throw<OperationCanceledException>(() => SongTextDifference.Compare("left", "right", canceled.Token));
	}

	#endregion
}
