using System.Buffers.Binary;

namespace Menees.Chords.Db;

/// <summary>Catalog dates that do not depend on filesystem copy or synchronization timestamps.</summary>
public static class SongDates
{
	/// <summary>Gets the creation time encoded in a version-seven song ID, or null for older IDs.</summary>
	public static DateTimeOffset? CreatedUtc(Guid songId)
	{
		DateTimeOffset? result = null;
		const int Version = 7;
		const int GuidSize = 16;
		const int TimestampShift = 16;
		if (songId.Version == Version)
		{
			Span<byte> bytes = stackalloc byte[GuidSize];
			_ = songId.TryWriteBytes(bytes, bigEndian: true, out _);
			long milliseconds = (long)(BinaryPrimitives.ReadUInt64BigEndian(bytes) >> TimestampShift);
			if (milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
			{
				result = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
			}
		}

		return result;
	}
}
