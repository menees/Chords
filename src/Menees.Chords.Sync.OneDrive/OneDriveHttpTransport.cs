#region Using Directives

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Menees.Chords.Sync.OneDrive.OneDriveGraphClient;

#endregion

namespace Menees.Chords.Sync.OneDrive;

/// <summary>Account-bound Microsoft Graph operations against one flat book folder.</summary>
public sealed class OneDriveHttpTransport : IOneDriveTransport, IDisposable
{
	#region Private Data

	private const long SimpleUploadLimit = 250_000_000;
	private readonly OneDriveGraphClient graph;
	private readonly IOneDriveTokenProvider tokens;
	private readonly HashSet<string> knownChildren = new(StringComparer.Ordinal);

	#endregion

	#region Public API

	/// <summary>The optional handler is for testing. Production disables automatic redirects to contain bearer tokens.</summary>
	public OneDriveHttpTransport(OneDriveOptions options, IOneDriveTokenProvider tokens, HttpMessageHandler? handler = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(tokens);
		this.tokens = tokens;
		this.Identity = new("OneDrive", options.AccountId, options.RemoteFolderId);
		this.graph = new(options.AccountId, tokens, handler);
	}

	public CloudReplicaIdentity Identity { get; }

	// Full shallow listings are deliberate until a durable delta-cache adapter is available.
	public CloudReplicaCapabilities Capabilities => CloudReplicaCapabilities.Rename | CloudReplicaCapabilities.ConditionalMutation;

	public bool IsAuthenticated => this.graph.IsAuthenticated;

	public async Task AuthenticateAsync(CancellationToken cancellationToken)
	{
		await this.tokens.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
		this.graph.RequireAccount();
	}

	public async Task DisconnectAsync(CancellationToken cancellationToken)
	{
		this.knownChildren.Clear();
		await this.tokens.DisconnectAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
	{
		List<CloudReplicaItem> items = [];
		Uri? next = Graph("me/drive/items/" + Escape(this.Identity.RemoteFolderId) + "/children?$select=id,name,eTag,size,file,folder,parentReference");
		HashSet<string> pages = new(StringComparer.Ordinal);
		while (next is not null)
		{
			if (!pages.Add(next.AbsoluteUri))
			{
				throw new InvalidDataException("OneDrive returned a repeated listing page.");
			}

			using HttpRequestMessage request = new(HttpMethod.Get, next);
			using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
			using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
			foreach (JsonElement item in json.RootElement.GetProperty("value").EnumerateArray())
			{
				if (item.TryGetProperty("file", out _) && !item.TryGetProperty("deleted", out _))
				{
					CloudReplicaItem parsed = ParseItem(item);
					items.Add(parsed);
				}
			}

			next = json.RootElement.TryGetProperty("@odata.nextLink", out JsonElement link) ? ValidateGraphUri(link.GetString()!) : null;
		}

		this.knownChildren.Clear();
		this.knownChildren.UnionWith(items.Select(item => item.Id.Value));
		return new(items, null);
	}

	public async Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken)
	{
		await this.RequireChildAsync(itemId, cancellationToken).ConfigureAwait(false);
		using HttpRequestMessage request = new(HttpMethod.Get, ItemUri(itemId, "/content"));
		HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken, allowRedirect: true).ConfigureAwait(false);
		try
		{
			if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect)
			{
				Uri location = ValidateDownloadUri(response.Headers.Location);
				response.Dispose();
				using HttpRequestMessage download = new(HttpMethod.Get, location);
				response = await this.graph.SendDownloadAsync(download, cancellationToken).ConfigureAwait(false);
				ThrowIfFailed(response);
			}

			Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			return new OneDriveResponseStream(stream, response);
		}
		catch
		{
			response.Dispose();
			throw;
		}
	}

	public Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
	{
		ValidateName(name);
		Uri uri = Graph("me/drive/items/" + Escape(this.Identity.RemoteFolderId) + ":/" + Escape(name)
			+ ":/content?@microsoft.graph.conflictBehavior=fail");
		return this.UploadAsync(uri, null, content, cancellationToken);
	}

	public async Task<CloudReplicaItem> ReplaceAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken)
	{
		ValidateVersion(expectedVersion);
		await this.RequireChildAsync(itemId, cancellationToken).ConfigureAwait(false);
		return await this.UploadAsync(ItemUri(itemId, "/content"), expectedVersion, content, cancellationToken).ConfigureAwait(false);
	}

	public async Task<CloudReplicaItem> RenameAsync(
		ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken)
	{
		ValidateName(name);
		ValidateVersion(expectedVersion);
		await this.RequireChildAsync(itemId, cancellationToken).ConfigureAwait(false);
		using HttpRequestMessage request = new(HttpMethod.Patch, ItemUri(itemId)) { Content = JsonContent.Create(new { name }) };
		request.Headers.TryAddWithoutValidation("If-Match", expectedVersion.Value);
		using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
		using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
		return ParseItem(json.RootElement);
	}

	public async Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
	{
		ValidateVersion(expectedVersion);
		await this.RequireChildAsync(itemId, cancellationToken).ConfigureAwait(false);
		using HttpRequestMessage request = new(HttpMethod.Delete, ItemUri(itemId));
		request.Headers.TryAddWithoutValidation("If-Match", expectedVersion.Value);
		using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
		this.knownChildren.Remove(itemId.Value);
	}

	public void Dispose() => this.graph.Dispose();

	#endregion

	#region Private Methods

	private static Uri ItemUri(ProviderItemId id, string suffix = "") => Graph("me/drive/items/" + Escape(id.Value) + suffix);

	private static Uri ValidateDownloadUri(Uri? uri)
	{
		if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
		{
			throw new InvalidDataException("OneDrive returned an invalid download URL.");
		}

		return uri;
	}

	private static void ValidateVersion(ProviderItemVersion version)
	{
		if (!EntityTagHeaderValue.TryParse(version.Value, out EntityTagHeaderValue? parsed) || parsed.Tag == "*")
		{
			throw new ArgumentException("An explicit OneDrive item version is required for every replacement, rename or deletion.", nameof(version));
		}
	}

	private static void ValidateName(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (name is "." or ".." || name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
		{
			throw new ArgumentException("OneDrive book items must have a flat filename.", nameof(name));
		}
	}

	private static CloudReplicaItem ParseItem(JsonElement item) => new(
		new(item.GetProperty("id").GetString()!),
		item.GetProperty("name").GetString()!,
		new(item.GetProperty("eTag").GetString()!),
		item.GetProperty("size").GetInt64());

	private async Task RequireChildAsync(ProviderItemId id, CancellationToken cancellationToken)
	{
		this.graph.RequireAccount();
		if (!this.knownChildren.Contains(id.Value))
		{
			using HttpRequestMessage request = new(HttpMethod.Get, ItemUri(id, "?$select=id,parentReference,file"));
			using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
			using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
			if (!json.RootElement.TryGetProperty("file", out _)
				|| json.RootElement.GetProperty("parentReference").GetProperty("id").GetString() != this.Identity.RemoteFolderId)
			{
				throw new InvalidOperationException("The OneDrive item is outside the selected book folder.");
			}

			this.knownChildren.Add(id.Value);
		}
	}

	private async Task<CloudReplicaItem> UploadAsync(Uri uri, ProviderItemVersion? version, Stream content, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(content);
		if (!content.CanRead || !content.CanSeek || content.Position > content.Length || content.Length - content.Position > SimpleUploadLimit)
		{
			throw new ArgumentException("Uploads require a readable, seekable stream of at most 250 MB.", nameof(content));
		}

		using HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = new OneDriveStreamContent(content, content.Length - content.Position) };
		request.Content.Headers.ContentType = new("application/octet-stream");
		request.Headers.TryAddWithoutValidation(version is null ? "If-None-Match" : "If-Match", version?.Value ?? "*");
		using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
		using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
		CloudReplicaItem item = ParseItem(json.RootElement);
		this.knownChildren.Add(item.Id.Value);
		return item;
	}

	#endregion
}
