#region Using Directives

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Sync.OneDrive;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class OneDriveTransportTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task ListsAllPagesWithoutRecursingIntoUnrelatedFolders()
	{
		using ScriptedHandler handler = new(
			request =>
			{
				request.Headers.Authorization!.Parameter.ShouldBe("token");
				request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/me/drive/items/folder/children");
				return Json("""
					{"value":[{"id":"sub","name":".git","folder":{}},
					{"id":"one","name":"database.json","eTag":"\"v1\"","size":2,"file":{}}],
					"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/drive/items/folder/children?next=2"}
					""");
			},
			request =>
			{
				request.RequestUri!.Query.ShouldBe("?next=2");
				return Json("""{"value":[{"id":"two","name":"sheet.txt","eTag":"\"v2\"","size":5,"file":{}}]}""");
			});
		using OneDriveHttpTransport transport = Create(handler);
		CloudChangeSet changes = await transport.ListOrGetChangesAsync(null, this.TestContext.CancellationToken);
		changes.Items.Count.ShouldBe(2);
		changes.NextChangeToken.ShouldBeNull();
		handler.Remaining.ShouldBe(0);
	}

	[TestMethod]
	public async Task NeverSendsBearerTokensToDownloadHostAndReturnsAnOwnedStream()
	{
		MemoryStream bytes = new(Encoding.UTF8.GetBytes("body"));
		using ScriptedHandler handler = new(
			_ => Json("""{"id":"file","parentReference":{"id":"folder"},"file":{}}"""),
			request =>
			{
				request.Headers.Authorization!.Parameter.ShouldBe("token");
				HttpResponseMessage redirect = new(HttpStatusCode.Redirect);
				redirect.Headers.Location = new("https://download.example.test/file?secret=hidden");
				return redirect;
			},
			request =>
			{
				request.Headers.Authorization.ShouldBeNull();
				return new(HttpStatusCode.OK) { Content = new StreamContent(bytes) };
			});
		using OneDriveHttpTransport transport = Create(handler);
		using (Stream content = await transport.DownloadAsync(new("file"), this.TestContext.CancellationToken))
		using (StreamReader reader = new(content))
		{
			(await reader.ReadToEndAsync(this.TestContext.CancellationToken)).ShouldBe("body");
		}

		bytes.CanRead.ShouldBeFalse();
	}

	[TestMethod]
	public async Task CreateAndReplaceAreConditionalAndDoNotDisposeCallerContent()
	{
		using ScriptedHandler handler = new(
			request =>
			{
				request.Headers.GetValues("If-None-Match").Single().ShouldBe("*");
				request.RequestUri!.Query.ShouldContain("conflictBehavior=fail");
				request.Content!.ReadAsStringAsync(this.TestContext.CancellationToken).GetAwaiter().GetResult().ShouldBe("first");
				return Json("""{"id":"file","name":"song.txt","eTag":"\"v1\"","size":5}""");
			},
			request =>
			{
				request.Headers.GetValues("If-Match").Single().ShouldBe("\"v1\"");
				return new(HttpStatusCode.PreconditionFailed) { Content = new StringContent("access_token=do-not-log") };
			});
		using OneDriveHttpTransport transport = Create(handler);
		using MemoryStream content = new(Encoding.UTF8.GetBytes("first"));
		CloudReplicaItem created = await transport.CreateAsync("song.txt", content, this.TestContext.CancellationToken);
		content.CanRead.ShouldBeTrue();
		content.Position = 0;
		CloudRequestException error = await Should.ThrowAsync<CloudRequestException>(
			() => transport.ReplaceAsync(created.Id, created.Version, content, this.TestContext.CancellationToken));
		error.IsConcurrencyFailure.ShouldBeTrue();
		error.ToString().ShouldNotContain("do-not-log");
		content.CanRead.ShouldBeTrue();
	}

	[TestMethod]
	public async Task RejectsForeignAccountAndForeignItemBeforeMutation()
	{
		using ScriptedHandler handler = new(_ => Json("""{"id":"file","parentReference":{"id":"outside"},"file":{}}"""));
		using OneDriveHttpTransport transport = Create(handler);
		await Should.ThrowAsync<InvalidOperationException>(() => transport.DeleteAsync(new("file"), new("\"v1\""), this.TestContext.CancellationToken));
		handler.Remaining.ShouldBe(0);
		using ScriptedHandler unused = new();
		using OneDriveHttpTransport wrong = new(new("client", "different", "folder"), new Tokens(), unused);
		await Should.ThrowAsync<InvalidOperationException>(() => wrong.ListOrGetChangesAsync(null, this.TestContext.CancellationToken));
	}

	[TestMethod]
	[DataRow("https://evil.example/v1.0/me/drive")]
	[DataRow("http://graph.microsoft.com/v1.0/me/drive")]
	public async Task RejectsContinuationUrlsThatCouldLeakCredentials(string next)
	{
		using ScriptedHandler handler = new(_ => Json("{\"value\":[],\"@odata.nextLink\":\"" + next + "\"}"));
		using OneDriveHttpTransport transport = Create(handler);
		await Should.ThrowAsync<InvalidDataException>(() => transport.ListOrGetChangesAsync(null, this.TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task CancellationStopsBeforeNetworkOrStreamConsumption()
	{
		using CancellationTokenSource canceled = new();
		await canceled.CancelAsync();
		using ScriptedHandler handler = new();
		using OneDriveHttpTransport transport = Create(handler);
		await Should.ThrowAsync<OperationCanceledException>(() => transport.ListOrGetChangesAsync(null, canceled.Token));
	}

	[TestMethod]
	public async Task MissingAndWildcardVersionsCannotMakeUnguardedMutations()
	{
		using ScriptedHandler handler = new();
		using OneDriveHttpTransport transport = Create(handler);
		using MemoryStream content = new();
		await Should.ThrowAsync<ArgumentException>(() => transport.ReplaceAsync(new("file"), default, content, this.TestContext.CancellationToken));
		await Should.ThrowAsync<ArgumentException>(() => transport.DeleteAsync(new("file"), new("*"), this.TestContext.CancellationToken));
		await Should.ThrowAsync<ArgumentException>(() => transport.RenameAsync(new("file"), default, "new.txt", this.TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task AppFolderListingReturnsOnlyGuidNamedBookFolders()
	{
		Guid id = Guid.NewGuid();
		using ScriptedHandler handler = new(
			request =>
			{
				request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/me/drive/special/approot");
				return Json("""{"id":"root","folder":{}}""");
			},
			_ => Json("{\"value\":[{\"id\":\"book\",\"name\":\"" + id + "\",\"folder\":{}},"
				+ "{\"id\":\"other\",\"name\":\"Unmanaged\",\"folder\":{}}]}"));
		using OneDriveBookFolders folders = new("account", new Tokens(), handler);
		IReadOnlyDictionary<Guid, string> result = await folders.ListAsync(this.TestContext.CancellationToken);
		result.Count.ShouldBe(1);
		result[id].ShouldBe("book");
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task AppFolderCreationDoesNotOverwriteAndHandlesAnotherDeviceCreatingIt(bool concurrent)
	{
		Guid id = Guid.NewGuid();
		List<Func<HttpRequestMessage, HttpResponseMessage>> steps =
		[
			_ => Json("""{"id":"root","folder":{}}"""),
			_ => new(HttpStatusCode.NotFound),
			request =>
			{
				request.Method.ShouldBe(HttpMethod.Post);
				string body = request.Content!.ReadAsStringAsync(this.TestContext.CancellationToken).GetAwaiter().GetResult();
				body.ShouldContain("conflictBehavior");
				body.ShouldContain("fail");
				body.ShouldContain(id.ToString("D"));
				return concurrent ? new(HttpStatusCode.Conflict) : Json("""{"id":"book","folder":{}}""");
			},
		];
		if (concurrent)
		{
			steps.Add(_ => Json("""{"id":"book","folder":{}}"""));
		}

		using ScriptedHandler handler = new([.. steps]);
		using OneDriveBookFolders folders = new("account", new Tokens(), handler);
		(await folders.EnsureAsync(id, this.TestContext.CancellationToken)).ShouldBe("book");
		handler.Remaining.ShouldBe(0);
	}

	[TestMethod]
	public async Task AppFolderSetupRejectsAFileAtTheBookLocation()
	{
		using ScriptedHandler handler = new(
			_ => Json("""{"id":"root","folder":{}}"""),
			_ => Json("""{"id":"file","file":{}}"""));
		using OneDriveBookFolders folders = new("account", new Tokens(), handler);
		await Should.ThrowAsync<InvalidDataException>(() => folders.EnsureAsync(Guid.NewGuid(), this.TestContext.CancellationToken));
	}

	[TestMethod]
	public void ReplicaWrapperCannotDisplayOneTargetWhileUsingAnother()
	{
		using ScriptedHandler handler = new();
		using OneDriveHttpTransport transport = Create(handler);
		Should.Throw<ArgumentException>(() => new OneDriveCloudReplica(new("client", "other-account", "folder"), transport));
		Should.Throw<ArgumentException>(() => new OneDriveCloudReplica(new("client", "account", "other-folder"), transport));
		new OneDriveCloudReplica(new("client", "account", "folder"), transport).IsAuthenticated.ShouldBeTrue();
	}

	#endregion

	#region Private Methods

	private static OneDriveHttpTransport Create(ScriptedHandler handler) => new(new("client", "account", "folder"), new Tokens(), handler);

	private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	#endregion

	#region Private Types

	private sealed class Tokens : IOneDriveTokenProvider
	{
		public string? AccountId => "account";

		public Task AuthenticateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult("token");
		}
	}

	private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
	{
		private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> remaining = new(steps);

		public int Remaining => this.remaining.Count;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(this.remaining.Dequeue()(request));
		}
	}

	#endregion
}
