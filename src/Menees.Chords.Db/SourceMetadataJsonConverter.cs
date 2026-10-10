#region Using Directives

using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace Menees.Chords.Db;

/// <summary>Streams compact observations while retaining repeated values and original names.</summary>
internal sealed class SourceMetadataJsonConverter : JsonConverter<SortedDictionary<string, List<SourceMetadataValue>>>
{
	#region Public Methods

	public override SortedDictionary<string, List<SourceMetadataValue>> Read(
		ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		Require(reader.TokenType == JsonTokenType.StartObject);
		SortedDictionary<string, List<SourceMetadataValue>> result = new(StringComparer.Ordinal);
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			Require(reader.TokenType == JsonTokenType.PropertyName);
			string key = reader.GetString()!;
			Require(reader.Read());
			List<SourceMetadataValue> values = [];
			if (reader.TokenType == JsonTokenType.String)
			{
				values.Add(new() { Value = reader.GetString()!, SourceName = key });
			}
			else
			{
				Require(reader.TokenType == JsonTokenType.StartArray);
				while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
				{
					values.Add(ReadObservation(ref reader, key));
				}

				Require(reader.TokenType == JsonTokenType.EndArray);
			}

			Require(result.TryAdd(key, values));
		}

		Require(reader.TokenType == JsonTokenType.EndObject);
		return result;
	}

	public override void Write(
		Utf8JsonWriter writer,
		SortedDictionary<string, List<SourceMetadataValue>> value,
		JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		foreach ((string key, List<SourceMetadataValue> values) in value)
		{
			writer.WritePropertyName(key);
			if (values.Count == 1 && string.Equals(values[0].SourceName, key, StringComparison.Ordinal))
			{
				writer.WriteStringValue(values[0].Value);
			}
			else
			{
				writer.WriteStartArray();
				foreach (SourceMetadataValue observation in values)
				{
					writer.WriteStartObject();
					writer.WriteString("value", observation.Value);
					if (!string.Equals(observation.SourceName, key, StringComparison.Ordinal))
					{
						// Explicit null distinguishes an unknown original name from an inferred key.
						writer.WriteString("sourceName", observation.SourceName);
					}

					writer.WriteEndObject();
				}

				writer.WriteEndArray();
			}
		}

		writer.WriteEndObject();
	}

	#endregion

	#region Private Methods

	private static SourceMetadataValue ReadObservation(ref Utf8JsonReader reader, string key)
	{
		Require(reader.TokenType == JsonTokenType.StartObject);
		SourceMetadataValue result = new() { SourceName = key };
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			Require(reader.TokenType == JsonTokenType.PropertyName);
			string property = reader.GetString()!;
			Require(reader.Read());
			switch (property)
			{
				case "value":
					Require(reader.TokenType == JsonTokenType.String);
					result.Value = reader.GetString()!;
					break;
				case "sourceName":
					Require(reader.TokenType is JsonTokenType.String or JsonTokenType.Null);
					result.SourceName = reader.GetString();
					break;
				default:
					throw new JsonException("Unknown source metadata property.");
			}
		}

		Require(reader.TokenType == JsonTokenType.EndObject);
		return result;
	}

	private static void Require(bool condition)
	{
		if (!condition)
		{
			throw new JsonException("Source metadata must contain strings or arrays of observations.");
		}
	}
	#endregion
}
