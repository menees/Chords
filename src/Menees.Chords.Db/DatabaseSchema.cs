using System.Text.Json;
using System.Text.Json.Nodes;

namespace Menees.Chords.Db;

/// <summary>Applies deterministic upgrades to database JSON documents.</summary>
public static class DatabaseSchema
{
	private static readonly JsonSerializerOptions MigrationOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	/// <summary>Migrates a JSON document to <see cref="ChordDatabase.CurrentSchemaVersion"/>.</summary>
	public static JsonObject Migrate(JsonNode document)
	{
		if (document is not JsonObject root)
		{
			throw new DatabaseFormatException("The database JSON root must be an object.");
		}

		int version = root["schemaVersion"]?.GetValue<int>() ?? 0;
		if (version > ChordDatabase.CurrentSchemaVersion)
		{
			throw new UnsupportedSchemaVersionException(version);
		}

		JsonObject result = (JsonObject)root.DeepClone();
		while (version < ChordDatabase.CurrentSchemaVersion)
		{
			result = version switch
			{
				0 => MigrateVersion0To1(result),
				_ => throw new UnsupportedSchemaVersionException(version),
			};
			version++;
		}

		return result;
	}

	private static JsonObject MigrateVersion0To1(JsonObject root)
	{
		root["schemaVersion"] = 1;
		root["bookSettings"] ??= JsonSerializer.SerializeToNode(new BookSettings(), MigrationOptions);
		root["songs"] ??= new JsonArray();
		root["songFiles"] ??= new JsonArray();
		root["instrumentProfiles"] ??= new JsonArray();
		root["songInstrumentSettings"] ??= new JsonArray();
		root["setlists"] ??= new JsonArray();
		root["customTabs"] ??= new JsonArray();
		root["tombstones"] ??= new JsonArray();
		root["revision"] ??= JsonSerializer.SerializeToNode(new RevisionStamp(), MigrationOptions);
		return root;
	}
}
