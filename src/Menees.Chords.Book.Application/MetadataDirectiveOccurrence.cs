namespace Menees.Chords.Book.Application;

/// <summary>An existing directive value that can be reviewed without changing its surrounding syntax.</summary>
public sealed record MetadataDirectiveOccurrence(int LineNumber, int Start, int Length, string Value, string SourceLine, bool CanReplace);
