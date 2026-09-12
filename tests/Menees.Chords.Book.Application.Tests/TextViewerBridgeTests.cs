namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class TextViewerBridgeTests
{
	[TestMethod]
	[DataRow("chordbook://viewport/7/1", true, 1)]
	[DataRow("chordbook://viewport/7/-1", true, -1)]
	[DataRow("chordbook://viewport/6/1", false, 0)]
	[DataRow("https://viewport/7/1", false, 0)]
	[DataRow("chordbook://other/7/1", false, 0)]
	[DataRow("chordbook://viewport/7/1/extra", false, 0)]
	[DataRow("not a uri", false, 0)]
	public void OnlyCurrentViewerBoundaryCommandsAreAccepted(string url, bool expected, int direction)
	{
		TextViewerBridge.TryReadBoundary(url, 7, out int actual).ShouldBe(expected);
		actual.ShouldBe(direction);
	}
}
