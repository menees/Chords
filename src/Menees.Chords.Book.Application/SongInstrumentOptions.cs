using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

public sealed record SongInstrumentOptions(
	int TransposeSemitones = 0,
	int? CapoFret = null,
	CapoBehavior CapoBehavior = CapoBehavior.DisplayOnly,
	AccidentalPreference Spelling = AccidentalPreference.Default,
	Guid? PreferredSongFileId = null);
