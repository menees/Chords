#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

/// <summary>Generates a copyright-free library shaped like the current MobileSheets export.</summary>
internal sealed class RepresentativeBookFixture
{
	#region Private Data

	private const int SongCount = 500;
	private const int PdfCount = 17;
	private const int TextBytesPerSong = 2_444;
	private const int PdfBytesPerFile = 379_606;
	private const int ArtistCount = 37;
	private const int TagCycle = 3;
	private const int MinimumDurationSeconds = 150;
	private const int DurationRangeSeconds = 240;
	private const int MinimumTempo = 70;
	private const int TempoRange = 80;
	private const int FormatCycle = 20;
	private const int ChordOverTextFirstIndex = 3;
	private const int ChordOverTextLastIndex = 5;
	private static readonly DateTimeOffset Timestamp = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

	#endregion

	#region Constructors

	private RepresentativeBookFixture(ChordDatabase database, IReadOnlyList<NativeBookAsset> assets, long totalAssetBytes)
	{
		this.Database = database;
		this.Assets = assets;
		this.TotalAssetBytes = totalAssetBytes;
	}

	#endregion

	#region Public API

	public ChordDatabase Database { get; }

	public IReadOnlyList<NativeBookAsset> Assets { get; }

	public long TotalAssetBytes { get; }

	public static RepresentativeBookFixture Create()
	{
		Guid deviceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		ChordDatabase database = ChordDatabase.Create("Representative 500", deviceId, Timestamp);
		List<NativeBookAsset> assets = [];
		long totalBytes = 0;
		for (int index = 0; index < SongCount; index++)
		{
			DateTimeOffset itemTime = Timestamp.AddMilliseconds(index + 1);
			Song song = CreateSong(index, deviceId, itemTime);
			database.Songs.Add(song);
			SourceFormat format = GetFormat(index);
			byte[] text = CreateTextBytes(format, index, TextBytesPerSong);
			AddFile(database, assets, song, format, MediaKind.Text, text, deviceId, itemTime, extension: GetExtension(format));
			totalBytes += text.Length;
			if (index < PdfCount)
			{
				byte[] pdf = CreatePaddedBytes("%PDF-1.4\n% Synthetic ChordBook fixture\n%%EOF\n", PdfBytesPerFile, (byte)('%' + (index % 2)));
				AddFile(database, assets, song, SourceFormat.Unknown, MediaKind.Pdf, pdf, deviceId, itemTime, ".pdf");
				totalBytes += pdf.Length;
			}
		}

		return new RepresentativeBookFixture(database, assets, totalBytes);
	}

	#endregion

	#region Private Methods

	private static void AddFile(
		ChordDatabase database,
		List<NativeBookAsset> assets,
		Song song,
		SourceFormat format,
		MediaKind mediaKind,
		byte[] content,
		Guid deviceId,
		DateTimeOffset itemTime,
		string? extension)
	{
		Guid fileId = Guid.CreateVersion7(itemTime.AddTicks(database.SongFiles.Count + 1));
		database.SongFiles.Add(new SongFile
		{
			Id = fileId,
			SongId = song.Id,
			RelativePath = PortableManagedFileName.Create(song.Title, fileId, extension),
			MediaKind = mediaKind,
			SourceFormat = format,
			ContentHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
			ObservedLength = content.Length,
			ContentRevision = 1,
			Revision = RevisionStamp.Initial(deviceId, itemTime),
		});
		assets.Add(new NativeBookAsset(fileId, cancellationToken =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
		}));
	}

	private static Song CreateSong(int index, Guid deviceId, DateTimeOffset itemTime)
	{
		Song song = new()
		{
			Id = Guid.CreateVersion7(itemTime),
			Title = $"Fixture Song {index + 1:000}",
			Artists = [$"Fixture Artist {(index % ArtistCount) + 1:00}"],
			Tags = [index % TagCycle == 0 ? "Practice" : "Performance"],
			DurationSeconds = MinimumDurationSeconds + (index % DurationRangeSeconds),
			Revision = RevisionStamp.Initial(deviceId, itemTime),
		};
		song.SourceMetadata["keys"] = [new SourceMetadataValue { Value = GetKey(index), SourceName = "key" }];
		song.SourceMetadata["tempos"] =
		[
			new SourceMetadataValue
			{
				Value = (MinimumTempo + (index % TempoRange)).ToString(System.Globalization.CultureInfo.InvariantCulture),
				SourceName = "tempo",
			},
		];
		return song;
	}

	private static byte[] CreateTextBytes(SourceFormat format, int index, int length)
	{
		string source = format switch
		{
			SourceFormat.ChordPro =>
				$"{{title:Fixture Song {index + 1:000}}}\n{{artist:Fixture Artist {(index % ArtistCount) + 1:00}}}\n[C]Synthetic [G]lyrics\n",
			SourceFormat.ChordOverText => $"Fixture Song {index + 1:000}\nC             G\nSynthetic lyrics\n",
			SourceFormat.Mixed => $"{{title:Fixture Song {index + 1:000}}}\nC             G\nSynthetic lyrics\n",
			SourceFormat.OpenSongXml => $"<song><title>Fixture Song {index + 1:000}</title><lyrics>V1\n.C Synthetic lyrics</lyrics></song>",
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
		return CreatePaddedBytes(source, length, (byte)' ');
	}

	private static byte[] CreatePaddedBytes(string source, int length, byte padding)
	{
		byte[] prefix = Encoding.UTF8.GetBytes(source);
		byte[] result = new byte[length];
		prefix.CopyTo(result, 0);
		result.AsSpan(prefix.Length).Fill(padding);
		return result;
	}

	private static string? GetExtension(SourceFormat format) => format == SourceFormat.OpenSongXml ? null : ".cho";

	private static SourceFormat GetFormat(int index) => (index % FormatCycle) switch
	{
		0 => SourceFormat.OpenSongXml,
		1 or 2 => SourceFormat.Mixed,
		>= ChordOverTextFirstIndex and <= ChordOverTextLastIndex => SourceFormat.ChordOverText,
		_ => SourceFormat.ChordPro,
	};

	private static string GetKey(int index)
	{
		string[] keys = ["C", "D", "E", "F", "G", "A", "Bb"];
		return keys[index % keys.Length];
	}

	#endregion
}
