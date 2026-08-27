using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>Reads and writes the canonical, stable schema-v1 JSON representation.</summary>
public static class DatabaseJson
{
	private static readonly JsonSerializerOptions Options = CreateOptions();

	/// <summary>Serializes and validates a database using stable property and enum names.</summary>
	public static string Serialize(ChordDatabase database)
	{
		DatabaseValidation.ThrowIfInvalid(database);
		return JsonSerializer.Serialize(database, Options) + "\n";
	}

	/// <summary>Migrates, deserializes, and validates database JSON.</summary>
	public static ChordDatabase Deserialize(string json)
	{
		JsonNode document;
		try
		{
			document = JsonNode.Parse(json) ?? throw new DatabaseFormatException("The database JSON is empty.");
		}
		catch (JsonException exception)
		{
			throw new DatabaseFormatException("The database JSON is invalid.", exception);
		}

		JsonObject migrated = DatabaseSchema.Migrate(document);
		ChordDatabase database;
		try
		{
			database = migrated.Deserialize<ChordDatabase>(Options)
				?? throw new DatabaseFormatException("The database JSON did not contain an object.");
		}
		catch (JsonException exception)
		{
			throw new DatabaseFormatException("The database JSON does not match the schema.", exception);
		}

		DatabaseValidation.ThrowIfInvalid(database);
		return database;
	}

	private static JsonSerializerOptions CreateOptions()
	{
		JsonSerializerOptions result = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = false,
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.Never,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		};
		result.Converters.Add(new JsonStringEnumConverter());
		return result;
	}
}
