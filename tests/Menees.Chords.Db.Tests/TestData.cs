using System.Security.Cryptography;
using System.Text;

namespace Menees.Chords.Db.Tests;

internal static class TestData
{
	public static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 34, 56, TimeSpan.Zero);

	private const int SetlistTimestampOffset = 3;
	private const int SetlistEntryTimestampOffset = 4;

	public static ChordDatabase CreateDatabase(bool includeFile = true)
	{
		Guid deviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		ChordDatabase database = ChordDatabase.Create("Test Book", deviceId, Now);
		Song song = new()
		{
			Id = Guid.CreateVersion7(Now.AddMilliseconds(1)),
			Title = "Blessed Assurance",
			Artists = ["Fanny Crosby"],
			Revision = RevisionStamp.Initial(deviceId, Now),
		};
		song.SourceMetadata["artist"] = [new SourceMetadataValue { Value = "Fanny Crosby", SourceName = "artist" }];
		database.Songs.Add(song);

		if (includeFile)
		{
			byte[] content = Encoding.UTF8.GetBytes("<song><title>Blessed Assurance</title></song>");
			Guid fileId = Guid.CreateVersion7(Now.AddMilliseconds(2));
			database.SongFiles.Add(new SongFile
			{
				Id = fileId,
				SongId = song.Id,
				RelativePath = PortableManagedFileName.Create(song.Title, fileId, extension: null),
				MediaKind = MediaKind.Text,
				SourceFormat = SourceFormat.OpenSongXml,
				ContentHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
				ObservedLength = content.Length,
				ContentRevision = 1,
				Revision = RevisionStamp.Initial(deviceId, Now),
			});
		}

		database.Setlists.Add(new Setlist
		{
			Id = Guid.CreateVersion7(Now.AddMilliseconds(SetlistTimestampOffset)),
			Name = "Sunday",
			Entries =
			[
				new SetlistEntry
				{
					Id = Guid.CreateVersion7(Now.AddMilliseconds(SetlistEntryTimestampOffset)),
					SongId = song.Id,
					PreferredSongFileId = database.SongFiles.FirstOrDefault()?.Id,
				},
			],
			Revision = RevisionStamp.Initial(deviceId, Now),
		});
		return database;
	}

	public static byte[] OpenSongBytes() => Encoding.UTF8.GetBytes("<song><title>Blessed Assurance</title></song>");
}
