#region Using Directives

using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class DatabaseJsonTests
{
	#region Public Methods

	[TestMethod]
	public void TabsAndConstructorDefaultsProduceMinimalRoundTripWithoutHidingSchema()
	{
		ChordDatabase original = ChordDatabase.Create("Defaults", Guid.NewGuid(), TestData.Now);
		string json = DatabaseJson.Serialize(original);
		json.ShouldContain("\n\t\"id\"");
		json.ShouldNotContain("\n  ");
		using JsonDocument document = JsonDocument.Parse(json);
		document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(ChordDatabase.CurrentSchemaVersion);
		document.RootElement.TryGetProperty("bookSettings", out _).ShouldBeFalse();
		document.RootElement.TryGetProperty("songs", out _).ShouldBeFalse();
		ChordDatabase restored = DatabaseJson.Deserialize(json);
		restored.BookSettings.DefaultDisplayProfile.AutoColumns.ShouldBeTrue();
		restored.BookSettings.DefaultDisplayProfile.ShowChords.ShouldBeTrue();
		restored.BookSettings.DefaultMetronome.Volume.ShouldBe(0.8);
		restored.BookSettings.InputBindings.ShouldBe(PerformanceKeyBinding.CreateDefaults());
		DatabaseJson.Serialize(restored).ShouldBe(json);
	}

	[TestMethod]
	public void ExplicitFalseZeroEmptyBindingsAndEmptyMetadataOverridesSurviveOldAndNewJson()
	{
		ChordDatabase original = TestData.CreateDatabase();
		original.BookSettings.StopMetronomeOnSetlistTransition = false;
		original.BookSettings.DefaultDisplayProfile.AutoColumns = false;
		original.BookSettings.DefaultDisplayProfile.ShowChords = false;
		original.BookSettings.DefaultMetronome.Volume = 0;
		original.BookSettings.DefaultMetronome.AudioEnabled = false;
		original.BookSettings.InputBindings.Clear();
		original.BookSettings.TitleTemplate = string.Empty;
		original.Songs[0].SourceMetadata["album"] = [new() { Value = "Source album" }];
		original.Songs[0].MetadataOverrides["album"] = [];
		original.Setlists[0].Entries[0].TransposeSemitones = 0;
		original.SongFiles[0].ObservedLength = 0;
		string compact = DatabaseJson.Serialize(original);
		compact.ShouldContain("\"autoColumns\": false");
		compact.ShouldContain("\"volume\": 0");
		compact.ShouldContain("\"inputBindings\": []");
		compact.ShouldNotContain("\"isArchived\": false");
		compact.ShouldNotContain("\"preferredSongFileId\": null");
		JsonSerializerOptions oldOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
		oldOptions.Converters.Add(new JsonStringEnumConverter());
		string oldJson = JsonSerializer.Serialize(original, oldOptions);
		foreach (string input in new[] { compact, oldJson })
		{
			ChordDatabase restored = DatabaseJson.Deserialize(input);
			restored.BookSettings.DefaultDisplayProfile.AutoColumns.ShouldBeFalse();
			restored.BookSettings.DefaultDisplayProfile.ShowChords.ShouldBeFalse();
			restored.BookSettings.DefaultMetronome.AudioEnabled.ShouldBeFalse();
			restored.BookSettings.DefaultMetronome.Volume.ShouldBe(0);
			restored.BookSettings.StopMetronomeOnSetlistTransition.ShouldBeFalse();
			restored.BookSettings.InputBindings.ShouldBeEmpty();
			restored.BookSettings.TitleTemplate.ShouldBeEmpty();
			SongMetadata.GetValues(restored.Songs[0], "album").ShouldBeEmpty();
			restored.Setlists[0].Entries[0].TransposeSemitones.ShouldBe(0);
			restored.SongFiles[0].ObservedLength.ShouldBe(0);
			DatabaseJson.Serialize(restored).ShouldBe(compact);
		}
	}

	[TestMethod]
	public void SparseFileMetadataStillProtectsTheAssetTransactionBoundary()
	{
		ChordDatabase database = TestData.CreateDatabase();
		database.SongFiles[0].ContentHash = string.Empty;
		database.SongFiles[0].ContentRevision = 0;
		database.SongFiles[0].ObservedLength = null;
		string before = DatabaseJson.Serialize(database);
		BookLocation location = new(Guid.NewGuid(), database.Id);
		database.Name = "Metadata only";
		MetadataCommit.Validate(location, before, database);
		database.SongFiles[0].ContentRevision = 1;
		Should.Throw<BookStoreValidationException>(() => MetadataCommit.Validate(location, before, database));
		database.SongFiles[0].ContentRevision = 0;
		database.SongFiles[0].ObservedLength = 0;
		Should.Throw<BookStoreValidationException>(() => MetadataCommit.Validate(location, before, database));
		database.SongFiles[0].ObservedLength = null;
		const int HashLength = 64;
		database.SongFiles[0].ContentHash = new string('a', HashLength);
		Should.Throw<BookStoreValidationException>(() => MetadataCommit.Validate(location, before, database));
	}

	[TestMethod]
	public void RoundTripIsDeterministic()
	{
		ChordDatabase original = TestData.CreateDatabase();

		string first = DatabaseJson.Serialize(original);
		ChordDatabase deserialized = DatabaseJson.Deserialize(first);
		string second = DatabaseJson.Serialize(deserialized);

		second.ShouldBe(first);
		first.IndexOf("\"id\"", StringComparison.Ordinal).ShouldBeLessThan(first.IndexOf("\"name\"", StringComparison.Ordinal));
		first.ShouldContain("\"sourceFormat\": \"OpenSongXml\"");
	}

	[TestMethod]
	public void VersionZeroDocumentMigratesDeterministically()
	{
		Guid id = Guid.CreateVersion7(TestData.Now);
		string legacy = $$"""
			{
			  "id": "{{id:D}}",
			  "name": "Legacy"
			}
			""";

		ChordDatabase migrated = DatabaseJson.Deserialize(legacy);

		migrated.SchemaVersion.ShouldBe(1);
		migrated.Id.ShouldBe(id);
		migrated.Songs.ShouldBeEmpty();
		DatabaseJson.Serialize(migrated).ShouldContain("\"schemaVersion\": 1");
	}

	[TestMethod]
	public void FutureSchemaIsRejected()
	{
		string json = "{\"schemaVersion\": 99}";

		UnsupportedSchemaVersionException exception = Should.Throw<UnsupportedSchemaVersionException>(() => DatabaseJson.Deserialize(json));

		exception.Version.ShouldBe(99);
	}

	[TestMethod]
	public void UnknownPropertiesAreRejected()
	{
		string json = DatabaseJson.Serialize(TestData.CreateDatabase(includeFile: false));
		json = json.Replace("\"name\": \"Test Book\",", "\"name\": \"Test Book\",\n  \"surprise\": true,", StringComparison.Ordinal);

		Should.Throw<DatabaseFormatException>(() => DatabaseJson.Deserialize(json));
	}

	[TestMethod]
	public void CompactSourceMetadataPreservesRepeatedValuesAndOriginalNames()
	{
		ChordDatabase original = TestData.CreateDatabase();
		SortedDictionary<string, List<SourceMetadataValue>> metadata = original.Songs[0].SourceMetadata;
		metadata.Clear();
		metadata["artist"] = [new() { Value = "Bob Seger", SourceName = "artist" }];
		metadata["key"] = [new() { Value = "G", SourceName = "key" }, new() { Value = "C", SourceName = "k" }];
		metadata["empty"] = [new() { Value = string.Empty, SourceName = "empty" }];
		metadata["unknown"] = [new() { Value = "Unattributed" }];
		metadata["case"] = [new() { Value = "Case matters", SourceName = "Case" }];
		metadata["none"] = [];
		string json = DatabaseJson.Serialize(original);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement stored = document.RootElement.GetProperty("songs")[0].GetProperty("sourceMetadata");
		stored.GetProperty("artist").GetString().ShouldBe("Bob Seger");
		stored.GetProperty("empty").GetString().ShouldBeEmpty();
		stored.GetProperty("key")[0].TryGetProperty("sourceName", out _).ShouldBeFalse();
		stored.GetProperty("key")[1].GetProperty("sourceName").GetString().ShouldBe("k");
		stored.GetProperty("unknown")[0].GetProperty("sourceName").ValueKind.ShouldBe(JsonValueKind.Null);
		stored.GetProperty("case")[0].GetProperty("sourceName").GetString().ShouldBe("Case");
		stored.GetProperty("none").GetArrayLength().ShouldBe(0);
		ChordDatabase restored = DatabaseJson.Deserialize(json);
		foreach ((string key, List<SourceMetadataValue> values) in metadata)
		{
			restored.Songs[0].SourceMetadata[key].Select(item => (item.Value, item.SourceName))
				.ShouldBe(values.Select(item => (item.Value, item.SourceName)));
		}

		DatabaseJson.Serialize(restored).ShouldBe(json);
		JsonSerializerOptions oldOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		oldOptions.Converters.Add(new JsonStringEnumConverter());
		DatabaseJson.Serialize(DatabaseJson.Deserialize(JsonSerializer.Serialize(original, oldOptions))).ShouldBe(json);
	}

	[TestMethod]
	public void SourceMetadataInfersOmittedNamesAndRejectsMalformedValues()
	{
		string json = DatabaseJson.Serialize(TestData.CreateDatabase());
		json = json.Replace("\"artist\": \"Fanny Crosby\"", "\"artist\": [{ \"value\": \"Fanny Crosby\" }]", StringComparison.Ordinal);
		DatabaseJson.Deserialize(json).Songs[0].SourceMetadata["artist"][0].SourceName.ShouldBe("artist");
		foreach (string invalid in new[] { "42", "null", "[\"wrong\"]", "[{\"value\":42}]", "[{\"surprise\":true}]" })
		{
			string malformed = json.Replace("[{ \"value\": \"Fanny Crosby\" }]", invalid, StringComparison.Ordinal);
			Should.Throw<DatabaseFormatException>(() => DatabaseJson.Deserialize(malformed));
		}
	}

	#endregion
}
