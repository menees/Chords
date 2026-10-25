#region Using Directives

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Streams the existing canonical fingerprint contract without constructing a JSON object graph.</summary>
internal static class SyncEntityFingerprint
{
	#region Public Methods

	public static string Create<T>(T entity)
	{
		using MemoryStream bytes = new();
		JsonSerializer.Serialize(bytes, entity, Contract<T>.Options);
		return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
	}

	public static T Clone<T>(T value)
	{
		// Pooled serializer storage avoids a new byte array for every detached entity.
		using JsonDocument document = JsonSerializer.SerializeToDocument(value);
		return document.RootElement.Deserialize<T>()!;
	}

	#endregion

	#region Private Methods

	private static JsonSerializerOptions CreateOptions(Type root)
	{
		DefaultJsonTypeInfoResolver resolver = new();
		resolver.Modifiers.Add(type =>
		{
			if (type.Kind == JsonTypeInfoKind.Object)
			{
				JsonPropertyInfo[] properties = [.. type.Properties.OrderBy(property => property.Name, StringComparer.Ordinal)];
				type.Properties.Clear();
				foreach (JsonPropertyInfo property in properties)
				{
					bool ignored = type.Type == root && (property.Name == nameof(Song.Revision)
						|| (root == typeof(SongFile) && property.Name is nameof(SongFile.ObservedWriteUtc) or nameof(SongFile.ObservedLength)
							or nameof(SongFile.ContentRevision) or nameof(SongFile.AnalysisVersion)));
					if (!ignored)
					{
						if (type.Type == typeof(SongFile) && property.Name == nameof(SongFile.ContentHash))
						{
							property.Get = value => ((SongFile)value).ContentHash.ToLowerInvariant();
						}

						type.Properties.Add(property);
					}
				}
			}
		});
		JsonSerializerOptions result = new() { TypeInfoResolver = resolver };
		result.Converters.Add(new CanonicalTimestamp());
		result.Converters.Add(new CanonicalMetadata<List<string>>());
		result.Converters.Add(new CanonicalMetadata<List<SourceMetadataValue>>());
		return result;
	}

	#endregion

	#region Private Types

	private static class Contract<T>
	{
		public static readonly JsonSerializerOptions Options = CreateOptions(typeof(T));
	}

	private sealed class CanonicalTimestamp : JsonConverter<DateTimeOffset>
	{
		public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			=> throw new NotSupportedException("Fingerprint contracts are write-only.");

		public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
		{
			// The prior JSON-node writer escaped '+' in offsets as ordinary string content.
			writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture));
		}
	}

	private sealed class CanonicalMetadata<T> : JsonConverter<SortedDictionary<string, T>>
	{
		public override SortedDictionary<string, T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			=> throw new NotSupportedException("Fingerprint contracts are write-only.");

		public override void Write(Utf8JsonWriter writer, SortedDictionary<string, T> value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			foreach ((string key, T item) in value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
			{
				writer.WritePropertyName(key);
				JsonSerializer.Serialize(writer, item, options);
			}

			writer.WriteEndObject();
		}
	}

	#endregion
}
