namespace Menees.Chords.Book.Application;

public sealed record SetlistEntrySettings(
	Guid SetlistId, Guid EntryId, Guid SongId, long SetlistRevision, Guid? PreferredSongFileId, int? TransposeSemitones, Guid? InstrumentProfileId = null);
