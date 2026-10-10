namespace Menees.Chords.Book.Application;

/// <summary>One aligned display row; missing sides are padding, never source text.</summary>
public sealed record SongDifferenceLine(SongDifferenceKind Kind, int? LeftNumber, string? LeftText, int? RightNumber, string? RightText);
