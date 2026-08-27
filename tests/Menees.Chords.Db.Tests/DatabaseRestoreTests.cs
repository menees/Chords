namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class DatabaseRestoreTests
{
	[TestMethod]
	public void CloneGetsNewBookIdentityButRetainsContentIdentity()
	{
		ChordDatabase original = TestData.CreateDatabase();
		original.Tombstones.Add(new Tombstone
		{
			EntityId = Guid.NewGuid(),
			EntityType = "Song",
			Revision = RevisionStamp.Initial(Guid.NewGuid(), TestData.Now),
		});
		Guid deviceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

		ChordDatabase clone = DatabaseRestore.CloneAsNew(original, deviceId, TestData.Now.AddDays(1));

		clone.Id.ShouldNotBe(original.Id);
		clone.Id.Version.ShouldBe(7);
		clone.Songs.Select(song => song.Id).ShouldBe(original.Songs.Select(song => song.Id));
		clone.SongFiles.Select(file => file.Id).ShouldBe(original.SongFiles.Select(file => file.Id));
		clone.Setlists.SelectMany(setlist => setlist.Entries).Select(entry => entry.Id)
			.ShouldBe(original.Setlists.SelectMany(setlist => setlist.Entries).Select(entry => entry.Id));
		clone.Tombstones.ShouldBeEmpty();
		clone.Revision.DeviceId.ShouldBe(deviceId);
		clone.Revision.Revision.ShouldBe(1);
	}

	[TestMethod]
	public void ReplaceRequiresMatchingBookIdentity()
	{
		ChordDatabase current = TestData.CreateDatabase();
		ChordDatabase wrong = TestData.CreateDatabase();

		Should.Throw<DatabaseIdentityMismatchException>(() => DatabaseRestore.RequireReplacementIdentity(current, wrong));

		ChordDatabase matching = DatabaseJson.Deserialize(DatabaseJson.Serialize(current));
		Should.NotThrow(() => DatabaseRestore.RequireReplacementIdentity(current, matching));
	}
}
