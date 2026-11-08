namespace Menees.Chords.Sync;

/// <summary>Identifies an existing cloud lock and whether an explicit takeover is available.</summary>
public sealed class CloudBookLockedException(string deviceName, DateTimeOffset lastRenewal, bool canTakeOver)
	: InvalidOperationException($"{deviceName} holds the cloud book lock. Last renewed {lastRenewal.ToLocalTime():g}.")
{
	public bool CanTakeOver { get; } = canTakeOver;
}
