namespace Menees.Chords.Book.Application;

public sealed record DocumentViewerPosition(int Page, double Zoom = 1, double HorizontalProgress = 0, double VerticalProgress = 0);
