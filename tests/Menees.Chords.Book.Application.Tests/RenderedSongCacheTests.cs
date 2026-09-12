#region Using Directives

using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class RenderedSongCacheTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RevisitAvoidsAssetReadsAndDisplayChangesInvalidateTheCachedRender()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(RenderedSongCacheTests), Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Cache", device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			Guid song = await session.CreateSongAsync("Song", [], [], "[C]Words", device, token);
			string? first = (await session.GetPresentationAsync(song, cancellationToken: token)).Html;
			SongFile file = session.Database!.SongFiles.Single();
			using (FileStream held = File.Open(Path.Combine(store.GetDirectory(location), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None))
			{
				(await session.GetPresentationAsync(song, cancellationToken: token)).Html.ShouldBeSameAs(first);
				await session.SaveDisplaySettingsAsync(song, new() { FontSize = 24 }, device, token);
				await Should.ThrowAsync<IOException>(() => session.GetPresentationAsync(song, cancellationToken: token));
			}

			(await session.GetPresentationAsync(song, cancellationToken: token)).Html!.ShouldContain("font-size: 24px");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	#endregion
}
