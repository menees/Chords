#region Using Directives

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SettingsTransferTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task ExportContainsOnlyPortableSettingsAndRejectsMissingOrForeignFields()
	{
		var token = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		Guid device = Guid.NewGuid();
		BookLocation location = await store.CreateBookAsync("Private book name", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		SettingsTransferService transfer = new(session);
		string json = transfer.Export();
		json.ShouldNotContain(device.ToString());
		json.ShouldNotContain(session.Database!.Id.ToString());
		json.ShouldNotContain("Private book name");
		json.ShouldNotContain("revision", Case.Insensitive);
		json.ShouldNotContain("songs", Case.Insensitive);
		SettingsTransferService.Read(json).Metronome.Sound.ShouldBe("KickHiHat");
		Should.Throw<JsonException>(() => SettingsTransferService.Read("{}"));
		Should.Throw<JsonException>(() => SettingsTransferService.Read(json.Insert(1, "\"deviceId\":\"not-portable\",")));
		Should.Throw<ArgumentException>(() => SettingsTransferService.Read(json.Replace("\"formatVersion\": 1", "\"formatVersion\": 2")));
		Should.Throw<ArgumentException>(() => SettingsTransferService.Read(json.Replace("KickHiHat", "MissingSound")));
	}

	[TestMethod]
	public async Task ApplyUsesOnlyMetadataPreservesSongOverridesAndRejectsStaleReview()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(SettingsTransferTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Keep book", device, token);
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes("{title: Keep source}\r\n[C]Hello\r\n");
			await BookImportService.ImportAsync(store, location, "chart.cho", new MemoryStream(bytes), device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			Guid songId = session.Database!.Songs.Single().Id;
			await session.SaveSongMetronomeAsync(songId, new() { BeatsPerMinute = 97 }, device, token);
			SongMetronomeOverride songOverride = session.Database.Songs.Single().MetronomeOverride!;
			SongCatalogItem row = session.Search(null).Single();
			SettingsTransferService transfer = new(session);
			PortableBookSettings settings = SettingsTransferService.Read(transfer.Export());
			settings.Display.FontSize = 26;
			settings.Metronome.BeatsPerMinute = 83;
			settings.InputBindings.Clear();
			Guid bookId = session.Database.Id;
			long revision = session.Database.BookSettings.Revision.Revision;
			string assetPath = Path.Combine(store.GetDirectory(location), session.Database.SongFiles.Single().RelativePath);
			using (FileStream locked = new(assetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
				await transfer.ApplyAsync(settings, bookId, revision, device, token);
				ReferenceEquals(row, session.Search(null).Single()).ShouldBeTrue();
				ReferenceEquals(songOverride, session.Database.Songs.Single().MetronomeOverride).ShouldBeTrue();
				await Should.ThrowAsync<InvalidOperationException>(() => transfer.ApplyAsync(settings, bookId, revision, device, token));
			}

			settings.Display.FontSize = 40;
			session.Database.BookSettings.DefaultDisplayProfile.FontSize.ShouldBe(26);
			session.Database.Id.ShouldBe(bookId);
			session.Database.Name.ShouldBe("Keep book");
			session.GetMetronomeSettings(songId).BeatsPerMinute.ShouldBe(97);
			(await File.ReadAllBytesAsync(assetPath, token)).ShouldBe(bytes);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task UnicodeSettingsAtFieldLimitsRoundTripThroughFile()
	{
		var token = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Unicode", Guid.NewGuid(), token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		BookSettings settings = session.Database!.BookSettings;
		settings.TitleTemplate = new string('\u4e00', 4096);
		settings.SubtitleTemplate = new string('\u4e01', 4096);
		settings.InputBindings = [.. Enumerable.Range(0, 32).Select(index =>
			new PerformanceKeyBinding(new(new string((char)(0x4e00 + index), 64)), PerformanceCommand.NextSong))];
		SettingsTransferService transfer = new(session);
		string json = transfer.Export();
		json.Length.ShouldBeGreaterThan(65536);
		SettingsTransferService.Read(json).TitleTemplate.ShouldBe(settings.TitleTemplate);
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mcbsettings");
		try
		{
			await transfer.ExportFileAsync(path, token);
			PortableBookSettings restored = await SettingsTransferService.ReadFileAsync(path, token);
			restored.TitleTemplate.ShouldBe(settings.TitleTemplate);
			restored.SubtitleTemplate.ShouldBe(settings.SubtitleTemplate);
			restored.InputBindings.ShouldBe(settings.InputBindings);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[TestMethod]
	public async Task CanceledSettingsExportRetainsExistingFileAndOversizedInputIsRejected()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(SettingsTransferTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			InMemoryBookStore store = new();
			BookLocation location = await store.CreateBookAsync("Book", Guid.NewGuid(), token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			SettingsTransferService transfer = new(session);
			string path = Path.Combine(root, "book.mcbsettings");
			await File.WriteAllTextAsync(path, "previous", token);
			using CancellationTokenSource cancellation = new();
			cancellation.Cancel();
			await Should.ThrowAsync<OperationCanceledException>(() => transfer.ExportFileAsync(path, cancellation.Token));
			(await File.ReadAllTextAsync(path, token)).ShouldBe("previous");
			Directory.GetFiles(root).ShouldBe([path]);
			await File.WriteAllBytesAsync(path, new byte[(128 * 1024) + 1], token);
			await Should.ThrowAsync<ArgumentException>(() => SettingsTransferService.ReadFileAsync(path, token));
			Should.Throw<ArgumentException>(() => SettingsTransferService.Read(new string(' ', (128 * 1024) + 1)));
			Should.Throw<ArgumentException>(() => SettingsTransferService.Read(new string('\u4e00', 50000)));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	#endregion
}
