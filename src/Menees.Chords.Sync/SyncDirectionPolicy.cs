namespace Menees.Chords.Sync;

public static class SyncDirectionPolicy
{
	public static bool AllowsUpload(SyncDirection direction) => direction != SyncDirection.UpdateThisDevice;

	public static bool AllowsDownload(SyncDirection direction) => direction != SyncDirection.UpdateCloud;

	public static SyncSide ChooseWinner(SyncDirection direction, DateTimeOffset localEdited, DateTimeOffset cloudEdited)
	{
		SyncSide result = SyncSide.Local;
		if (direction == SyncDirection.UpdateThisDevice || (direction == SyncDirection.TwoWay && cloudEdited > localEdited))
		{
			result = SyncSide.Cloud;
		}

		return result;
	}
}
