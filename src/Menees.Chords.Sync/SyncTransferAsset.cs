namespace Menees.Chords.Sync;

internal sealed record SyncTransferAsset(
	Guid FileId, string Name, string ContentHash, long Length, Guid LocalSourceId,
	SyncItemReceipt? SourceItem, bool Stage, bool Upload);
