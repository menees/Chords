namespace Menees.Chords.Sync;

public enum SyncOperationKind
{
	DownloadAsset,
	UploadAsset,
	CommitLocalDatabase,
	CommitCloudDatabase,
	DeleteLocalAsset,
	DeleteCloudAsset,
}
