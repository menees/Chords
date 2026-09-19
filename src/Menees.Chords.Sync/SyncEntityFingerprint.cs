#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

internal static class SyncEntityFingerprint
{
	#region Public Methods

	public static string Create<T>(T entity)
	{
		JsonObject node = JsonSerializer.SerializeToNode(entity)!.AsObject();
		node.Remove(nameof(Song.Revision));
		if (entity is SongFile file)
		{
			node[nameof(SongFile.ContentHash)] = file.ContentHash.ToLowerInvariant();
			node.Remove(nameof(SongFile.ObservedWriteUtc));
			node.Remove(nameof(SongFile.ObservedLength));
			node.Remove(nameof(SongFile.ContentRevision));
			node.Remove(nameof(SongFile.AnalysisVersion));
		}

		using MemoryStream bytes = new();
		using (Utf8JsonWriter writer = new(bytes))
		{
			WriteCanonical(writer, node);
		}

		return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
	}

	public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

	#endregion

	#region Private Methods

	private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
	{
		if (node is JsonObject properties)
		{
			writer.WriteStartObject();
			foreach ((string name, JsonNode? value) in properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
			{
				writer.WritePropertyName(name);
				WriteCanonical(writer, value);
			}

			writer.WriteEndObject();
		}
		else if (node is JsonArray array)
		{
			writer.WriteStartArray();
			foreach (JsonNode? item in array)
			{
				WriteCanonical(writer, item);
			}

			writer.WriteEndArray();
		}
		else if (node is null)
		{
			writer.WriteNullValue();
		}
		else
		{
			node.WriteTo(writer);
		}
	}

	#endregion
}
