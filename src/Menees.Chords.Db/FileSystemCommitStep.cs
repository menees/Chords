namespace Menees.Chords.Db;

/// <summary>Identifies filesystem commit boundaries exposed to fault-injection tests.</summary>
internal enum FileSystemCommitStep
{
	RecoveryBackupCreated,
	RollbackSnapshotCreated,
	ManagedAssetReplaced,
	DatabaseReplaced,
}
