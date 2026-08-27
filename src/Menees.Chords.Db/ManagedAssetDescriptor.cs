namespace Menees.Chords.Db;

/// <summary>Describes an asset that is explicitly managed by a chord database.</summary>
/// <param name="SongFileId">The song-file identity.</param>
/// <param name="RelativePath">The portable top-level filename.</param>
/// <param name="Length">The byte length.</param>
/// <param name="ContentHash">The lowercase SHA-256 hash.</param>
public sealed record ManagedAssetDescriptor(Guid SongFileId, string RelativePath, long Length, string ContentHash);
