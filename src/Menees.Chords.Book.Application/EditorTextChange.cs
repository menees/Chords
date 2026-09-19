namespace Menees.Chords.Book.Application;

/// <summary>An exact source span replacement for a reviewable editor-buffer edit.</summary>
public sealed record EditorTextChange(int Start, int Length, string Before, string After, int LineNumber);
