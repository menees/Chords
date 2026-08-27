namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class DatabaseJsonTests
{
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
}
