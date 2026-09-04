using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>Provides client-neutral content for the selected song and file.</summary>
public sealed record BookSongPresentation(string Title, Guid? SongFileId, MediaKind? MediaKind, string? Html);
