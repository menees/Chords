namespace Menees.Chords.Db;

/// <summary>A managed sheet whose external rename or content change can be adopted.</summary>
public sealed record BookReconcileChange(Guid FileId, string PreviousName, string CurrentName, bool ContentChanged);
