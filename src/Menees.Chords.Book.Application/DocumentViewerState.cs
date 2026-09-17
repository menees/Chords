namespace Menees.Chords.Book.Application;

public sealed record DocumentViewerState(
	int Page, int PageCount, bool AtStart, bool AtEnd, double Zoom = 1, double HorizontalProgress = 0, double VerticalProgress = 0);
