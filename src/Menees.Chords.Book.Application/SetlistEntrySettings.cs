using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

public sealed record SetlistEntrySettings(
	Guid SetlistId, Guid EntryId, Guid SongId, long SetlistRevision, Guid? PreferredSongFileId, int? TransposeSemitones, Guid? InstrumentProfileId = null)
{
	internal static void Validate(ChordDatabase database, Guid songId, Guid? fileId, int? transpose, Guid? instrumentId)
	{
		const int MaximumTranspose = 24;
		if (transpose is < -MaximumTranspose or > MaximumTranspose)
		{
			throw new ArgumentOutOfRangeException(nameof(transpose), "Use a transposition from -24 to +24 semitones.");
		}

		if (fileId is Guid selected && !database.SongFiles.Any(file => file.Id == selected && file.SongId == songId
			&& !file.IsArchived && file.RecoveryVersion is null))
		{
			throw new ArgumentException("Choose an active sheet belonging to this song.", nameof(fileId));
		}

		if (instrumentId is Guid profileId && !database.InstrumentProfiles.Any(profile => profile.Id == profileId))
		{
			throw new ArgumentException("Choose an instrument from this book.", nameof(instrumentId));
		}
	}
}
