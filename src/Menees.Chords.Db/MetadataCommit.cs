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

		Dictionary<Guid, JsonElement> files = previous.RootElement.TryGetProperty("songFiles", out JsonElement previousFiles)
			? previousFiles.EnumerateArray().ToDictionary(file => file.GetProperty("id").GetGuid()) : [];
		if (files.Count != next.SongFiles.Count || next.SongFiles.Any(file => !files.TryGetValue(file.Id, out JsonElement old)
			|| old.GetProperty("songId").GetGuid() != file.SongId || old.GetProperty("relativePath").GetString() != file.RelativePath
			|| (old.TryGetProperty("contentHash", out JsonElement hash) ? hash.GetString() : string.Empty) != file.ContentHash
			|| (old.TryGetProperty("contentRevision", out JsonElement revision) ? revision.GetInt64() : 0) != file.ContentRevision
			|| (old.TryGetProperty("observedLength", out JsonElement length) && length.ValueKind != JsonValueKind.Null ? length.GetInt64() : (long?)null)
				!= file.ObservedLength))
		{
			throw new BookStoreValidationException("Song file identity or content changes require an asset transaction.");
		}
	}
}
