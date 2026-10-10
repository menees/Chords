namespace Menees.Chords.Sync;

public sealed record BookSyncResult(int UploadedSheets, int DownloadedSheets, int RecoveredSheets, int ReportedConflicts, DateTimeOffset CompletedUtc);
