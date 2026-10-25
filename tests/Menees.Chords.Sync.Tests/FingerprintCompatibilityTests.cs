#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class FingerprintCompatibilityTests
{
	#region Public Methods

	[TestMethod]
	public void StreamingFingerprintsMatchExistingMergeBases()
	{
		ChordDatabase book = ChordDatabase.Create("Book <\u00e9>", Guid.NewGuid());
		Song song = new()
		{
			Id = Guid.NewGuid(), Title = "Title \U0001f3b8", Artists = ["Artist"], Tags = ["Tag"],
			SourceMetadata = new(StringComparer.OrdinalIgnoreCase)
			{
				["z"] = [new() { Value = "Last", SourceName = "Z" }],
				["A"] = [new() { Value = "First" }, new() { Value = "Second" }],
			},
			MetadataOverrides = new(StringComparer.OrdinalIgnoreCase) { ["z"] = ["Last"], ["A"] = ["First"] },
			DisplayOverride = new(), MetronomeOverride = new(), DurationSeconds = 123,
		};
		book.Songs.Add(song);
		book.Songs.Add(new() { Id = Guid.NewGuid() });
		book.SongFiles.Add(new()
		{
			Id = Guid.NewGuid(), SongId = song.Id, ContentHash = new string('A', 64),
			ObservedLength = 123, ObservedWriteUtc = DateTimeOffset.UtcNow, ContentRevision = 42, AnalysisVersion = 7,
			RecoveryVersion = new(), SavedVersion = new(),
		});
		book.SongFiles.Add(new() { Id = Guid.NewGuid(), SongId = song.Id });
		book.Setlists.Add(new() { Id = Guid.NewGuid(), Entries = [new() { Id = Guid.NewGuid(), SongId = song.Id }] });
		book.InstrumentProfiles.Add(new() { Id = Guid.NewGuid() });
		book.SongInstrumentSettings.Add(new() { Id = Guid.NewGuid(), SongId = song.Id, CapoFret = 3 });
		book.CustomTabs.Add(new()
		{
			Id = Guid.NewGuid(), GroupBy = "artist", Sort = [new() { Field = "title" }],
			Filter = new() { Operator = "and", Children = [new() { Operator = "eq", Field = "key", Value = "C" }] },
		});
		BookMergeBase baseline = BookSyncMerger.CreateBase(book);
		foreach (Song value in book.Songs)
		{
			Check(baseline, value.Id, value);
		}

		foreach (SongFile value in book.SongFiles)
		{
			Check(baseline, value.Id, value);
		}

		foreach (Setlist value in book.Setlists)
		{
			Check(baseline, value.Id, value);
		}

		foreach (InstrumentProfile value in book.InstrumentProfiles)
		{
			Check(baseline, value.Id, value);
		}

		foreach (SongInstrumentSetting value in book.SongInstrumentSettings)
		{
			Check(baseline, value.Id, value);
		}

		foreach (CustomTab value in book.CustomTabs)
		{
			Check(baseline, value.Id, value);
		}

		Check(baseline, book.Id, book.BookSettings);
		baseline.Entities["BookName:" + book.Id.ToString("D")].ShouldBe(Legacy(new { book.Name, book.Revision }));
	}

	[TestMethod]
	public void TimestampFingerprintsPreserveOffsetsAndFractionPrecision()
	{
		ChordDatabase book = ChordDatabase.Create("Dates", Guid.NewGuid());
		Song song = new() { Id = Guid.NewGuid() };
		book.Songs.Add(song);
		foreach (int ticks in new[] { 0, 1, 10, 1000, 1_234_567 })
		{
			foreach (int offset in new[] { 0, 330, -300 })
			{
				song.LastAccessedUtc = new DateTimeOffset(2026, 10, 24, 12, 34, 56, TimeSpan.FromMinutes(offset)).AddTicks(ticks);
				Check(BookSyncMerger.CreateBase(book), song.Id, song);
			}
		}
	}

	#endregion

	#region Private Methods

	private static void Check<T>(BookMergeBase baseline, Guid id, T value)
		=> baseline.Entities[typeof(T).Name + ":" + id.ToString("D")].ShouldBe(Legacy(value));

	// Preserve the prior algorithm here: these hashes are a persisted cross-device protocol.
	private static string Legacy<T>(T entity)
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
