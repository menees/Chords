#region Using Directives

using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Sync.OneDrive;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class OneDriveConcurrencyTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task ProductionAdapterSupportsConcurrentUploadsDownloadsAndInventoryRefresh()
	{
		const int Workers = 4;
		const int SheetsPerWorker = 2500;
		const int RefreshInterval = 503;
		var token = this.TestContext.CancellationToken;
		using ConcurrentHandler handler = new();
		using OneDriveHttpTransport transport = new(new("client", "account", "folder"), new Tokens(), handler);
		await Parallel.ForEachAsync(
			Enumerable.Range(0, Workers),
			new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = token },
			async (worker, cancellation) =>
			{
				for (int index = 0; index < SheetsPerWorker; index++)
				{
					using MemoryStream source = new([1]);
					CloudReplicaItem uploaded = await transport.CreateAsync($"sheet-{worker}-{index}.txt", source, cancellation);
					if (index % RefreshInterval == 0)
					{
						await transport.ListOrGetChangesAsync(null, cancellation);
					}

					await using Stream downloaded = await transport.DownloadAsync(uploaded.Id, cancellation);
					downloaded.ReadByte().ShouldBe(1);
				}
			});
		CloudChangeSet inventory = await transport.ListOrGetChangesAsync(null, token);
		inventory.Items.Count.ShouldBe(Workers * SheetsPerWorker);
		inventory.Items.Select(item => item.Id).Distinct().Count().ShouldBe(inventory.Items.Count);
	}

	#endregion

	#region Private Types

	private sealed class Tokens : IOneDriveTokenProvider
	{
		public string? AccountId => "account";

		public Task AuthenticateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("fake-token");
	}

	private sealed class ConcurrentHandler : HttpMessageHandler
	{
		private readonly ConcurrentDictionary<string, object> items = new(StringComparer.Ordinal);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await Task.Yield();
			string path = request.RequestUri!.AbsolutePath;
			HttpContent content;
			if (request.Method == HttpMethod.Put)
			{
				string name = Uri.UnescapeDataString(path.Split(':')[1].Trim('/'));
				object item = new { id = name, name, eTag = "\"v1\"", size = 1, file = new { }, parentReference = new { id = "folder" } };
				this.items[name] = item;
				content = new StringContent(JsonSerializer.Serialize(item));
			}
			else if (path.EndsWith("/children", StringComparison.Ordinal))
			{
				content = new StringContent(JsonSerializer.Serialize(new { value = this.items.Values.ToArray() }));
			}
			else if (path.EndsWith("/content", StringComparison.Ordinal))
			{
				content = new ByteArrayContent([1]);
			}
			else
			{
				content = new StringContent(JsonSerializer.Serialize(this.items[Uri.UnescapeDataString(path.Split('/')[^1])]));
			}

			return new(HttpStatusCode.OK) { Content = content };
		}
	}

	#endregion
}
