namespace Menees.Chords.Sync;

public sealed class SyncOperation
{
	public SyncOperation(string id, SyncOperationKind kind, string relativePath, long estimatedBytes = 0)
	{
		this.Id = !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("An operation ID is required.", nameof(id));
		this.Kind = kind;
		this.RelativePath = relativePath;
		this.EstimatedBytes = estimatedBytes;
	}

	public string Id { get; }

	public SyncOperationKind Kind { get; }

	public string RelativePath { get; }

	public long EstimatedBytes { get; }

	internal int SafeOrder => this.Kind switch
	{
		SyncOperationKind.DownloadAsset => 0,
		SyncOperationKind.UploadAsset => 0,
		SyncOperationKind.CommitLocalDatabase => 1,
		SyncOperationKind.CommitCloudDatabase => 1,
		SyncOperationKind.DeleteLocalAsset => 2,
		SyncOperationKind.DeleteCloudAsset => 2,
		_ => throw new InvalidOperationException($"Unknown operation kind {this.Kind}."),
	};
}
