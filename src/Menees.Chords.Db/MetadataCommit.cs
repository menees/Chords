using System.Text.Json;

namespace Menees.Chords.Db;

/// <summary>Validates the boundary between metadata-only writes and asset transactions.</summary>
internal static class MetadataCommit
{
	internal static void Validate(BookLocation location, string expectedJson, string updatedJson)
		=> Validate(location, expectedJson, DatabaseJson.Deserialize(updatedJson));

	internal static void Validate(BookLocation location, string expectedJson, ChordDatabase next)
	{
		using JsonDocument previous = JsonDocument.Parse(expectedJson);
		if (previous.RootElement.GetProperty("id").GetGuid() != location.Token || next.Id != location.Token)
		{
			throw new BookStoreValidationException("A metadata commit cannot change the book identity.");
		}

		Dictionary<Guid, JsonElement> files = previous.RootElement.GetProperty("songFiles").EnumerateArray()
			.ToDictionary(file => file.GetProperty("id").GetGuid());
		if (files.Count != next.SongFiles.Count || next.SongFiles.Any(file => !files.TryGetValue(file.Id, out JsonElement old)
			|| old.GetProperty("songId").GetGuid() != file.SongId || old.GetProperty("relativePath").GetString() != file.RelativePath
			|| old.GetProperty("contentHash").GetString() != file.ContentHash || old.GetProperty("contentRevision").GetInt64() != file.ContentRevision
			|| (old.GetProperty("observedLength").ValueKind == JsonValueKind.Null ? (long?)null : old.GetProperty("observedLength").GetInt64())
				!= file.ObservedLength))
		{
			throw new BookStoreValidationException("Song file identity or content changes require an asset transaction.");
		}
	}
}
