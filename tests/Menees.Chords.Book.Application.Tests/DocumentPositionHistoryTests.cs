using System.Text.Json;

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class DocumentPositionHistoryTests
{
	[TestMethod]
	public void PositionsAreIndependentForRepeatedEntriesAndDifferentSheets()
	{
		DocumentPositionHistory history = new();
		Guid song = Guid.NewGuid();
		Guid file = Guid.NewGuid();
		DocumentPositionKey first = new(song, Guid.NewGuid(), file);
		DocumentPositionKey repeat = new(song, Guid.NewGuid(), file);
		DocumentPositionKey otherFile = first with { FileId = Guid.NewGuid() };
		history.Record(first, new(4, 2, 0.5, 0.75));
		history.Record(repeat, new(1));
		history.Record(otherFile, new(9));
		history.Find(first).ShouldBe(new(4, 2, 0.5, 0.75));
		history.Find(repeat)!.Page.ShouldBe(1);
		history.Find(otherFile)!.Page.ShouldBe(9);
		history.MarkSaved();
		history.Record(otherFile, new(9));
		history.IsDirty.ShouldBeFalse();
	}

	[TestMethod]
	public void HistoryIsBoundedAndSurvivesSerialization()
	{
		DocumentPositionHistory history = new();
		DocumentPositionKey first = new(Guid.NewGuid(), null, null);
		history.Record(first, new(1));
		for (int index = 0; index < 100; index++)
		{
			history.Record(new(Guid.NewGuid(), null, null), new(index));
		}

		history.Positions.Count.ShouldBe(100);
		history.Find(first).ShouldBeNull();
		string json = JsonSerializer.Serialize(history.Positions);
		DocumentPositionHistory loaded = new(JsonSerializer.Deserialize<SavedDocumentPosition[]>(json));
		loaded.Positions.ShouldBe(history.Positions);
		loaded.IsDirty.ShouldBeFalse();
	}

	[TestMethod]
	public void CorruptOrDuplicateSavedPositionsAreIgnored()
	{
		DocumentPositionKey key = new(Guid.NewGuid(), null, Guid.NewGuid());
		DocumentPositionHistory history = new([
			null!,
			new(null!, new(0)),
			new(key, null!),
			new(key, new(-1)),
			new(key, new(0, double.NaN)),
			new(key, new(0, 1, double.PositiveInfinity)),
			new(key, new(0, 1, 0, 2)),
			new(key, new(3)),
			new(key, new(4)),
		]);
		history.Positions.Count.ShouldBe(1);
		history.Find(key)!.Page.ShouldBe(3);
	}

	[TestMethod]
	[DataRow("chordbook://position/7/3/1.5/0.25/0.75", true)]
	[DataRow("chordbook://position/6/3/1/0/0", false)]
	[DataRow("https://position/7/3/1/0/0", false)]
	[DataRow("chordbook://position/7/-1/1/0/0", false)]
	[DataRow("chordbook://position/7/3/NaN/0/0", false)]
	[DataRow("chordbook://position/7/3/1/0/Infinity", false)]
	[DataRow("chordbook://position/7/3/1/0/2", false)]
	[DataRow("chordbook://position/7/3/1/0/0/extra", false)]
	public void PositionMessagesMustMatchCurrentGenerationAndValidBounds(string url, bool expected)
	{
		TextViewerBridge.TryReadPosition(url, 7, out DocumentViewerPosition? position).ShouldBe(expected);
		if (expected)
		{
			position.ShouldBe(new(3, 1.5, 0.25, 0.75));
		}
		else
		{
			position.ShouldBeNull();
		}
	}

	[TestMethod]
	public void ReadinessIsStampedAndBoundaryLoadsIgnoreSavedPosition()
	{
		TextViewerBridge.IsReadyMessage("chordbook://ready/7", 7).ShouldBeTrue();
		TextViewerBridge.IsReadyMessage("chordbook://ready/6", 7).ShouldBeFalse();
		string html = TextViewerBridge.Attach("<html><head></head></html>", 7, true, new(8));
		html.ShouldContain("pageCount - 1");
		html.ShouldNotContain("function restore()");
		TextViewerBridge.Attach("<html><head></head></html>", 7, false, new(8)).ShouldContain("function restore()");
	}
}
