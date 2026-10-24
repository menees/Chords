namespace Menees.Chords.Sync;

/// <summary>A sync phase with optional completed and total sheet counts, not a byte/time estimate.</summary>
public sealed record BookSyncProgress(string Message, int? CompletedSheets = null, int? TotalSheets = null);
