namespace Menees.Chords.Db;

/// <summary>Identifies an entity revision and its originating device.</summary>
public sealed class RevisionStamp
{
	/// <summary>Gets or sets the monotonically increasing revision number.</summary>
	public long Revision { get; set; }

	/// <summary>Gets or sets the modification time.</summary>
	public DateTimeOffset ModifiedUtc { get; set; }

	/// <summary>Gets or sets the modifying device identifier.</summary>
	public Guid DeviceId { get; set; }

	/// <summary>Creates an initial revision stamp.</summary>
	/// <param name="deviceId">The creating device identifier.</param>
	/// <param name="now">The optional creation time.</param>
	/// <returns>An initialized revision stamp.</returns>
	public static RevisionStamp Initial(Guid deviceId, DateTimeOffset? now = null) => new()
	{
		Revision = 1,
		ModifiedUtc = now ?? DateTimeOffset.UtcNow,
		DeviceId = deviceId,
	};
}
