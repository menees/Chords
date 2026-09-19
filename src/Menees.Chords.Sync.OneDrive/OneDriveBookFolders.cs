#region Using Directives

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Menees.Chords.Sync.OneDrive.OneDriveGraphClient;

#endregion

namespace Menees.Chords.Sync.OneDrive;

/// <summary>Discovers and explicitly creates book folders beneath the caller's OneDrive application folder.</summary>
/// <remarks>Call only from a user-directed connection flow. Graph can create the application folder on first access.</remarks>
public sealed class OneDriveBookFolders : IDisposable
{
	#region Private Data

	private readonly OneDriveGraphClient graph;

	#endregion

	#region Public API

	public OneDriveBookFolders(string accountId, IOneDriveTokenProvider tokens, HttpMessageHandler? handler = null)
		=> this.graph = new(accountId, tokens, handler);

	public async Task<IReadOnlyDictionary<Guid, string>> ListAsync(CancellationToken cancellationToken = default)
	{
		string root = await this.GetRootAsync(cancellationToken).ConfigureAwait(false);
		Uri? next = Graph("me/drive/items/" + Escape(root) + "/children?$select=id,name,folder");
		Dictionary<Guid, string> result = [];
		HashSet<string> pages = new(StringComparer.Ordinal);
		while (next is not null)
		{
			if (!pages.Add(next.AbsoluteUri))
			{
				throw new InvalidDataException("OneDrive returned a repeated folder listing page.");
			}

			using HttpRequestMessage request = new(HttpMethod.Get, next);
			using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
			using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
			foreach (JsonElement item in json.RootElement.GetProperty("value").EnumerateArray())
			{
				if (item.TryGetProperty("folder", out _) && Guid.TryParseExact(item.GetProperty("name").GetString(), "D", out Guid bookId)
					&& bookId != Guid.Empty && !result.TryAdd(bookId, ReadFolderId(item)))
				{
					throw new InvalidDataException("OneDrive returned duplicate book identities in the application folder.");
				}
			}

			next = json.RootElement.TryGetProperty("@odata.nextLink", out JsonElement link) ? ValidateGraphUri(link.GetString()!) : null;
		}

		return result;
	}

	/// <summary>Creates only the GUID-named folder; it does not initialize or replace database.json.</summary>
	public async Task<string> EnsureAsync(Guid bookId, CancellationToken cancellationToken = default)
	{
		if (bookId == Guid.Empty)
		{
			throw new ArgumentException("A book identity is required.", nameof(bookId));
		}

		string root = await this.GetRootAsync(cancellationToken).ConfigureAwait(false);
		string name = bookId.ToString("D");
		Uri folder = Graph("me/drive/items/" + Escape(root) + ":/" + name + "?$select=id,name,folder");
		string result;
		try
		{
			result = await this.ReadFolderAsync(folder, cancellationToken).ConfigureAwait(false);
		}
		catch (CloudRequestException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
		{
			using HttpRequestMessage create = new(HttpMethod.Post, Graph("me/drive/items/" + Escape(root) + "/children"))
			{
				Content = JsonContent.Create(new Dictionary<string, object>
				{
					["name"] = name,
					["folder"] = new { },
					["@microsoft.graph.conflictBehavior"] = "fail",
				}),
			};
			try
			{
				using HttpResponseMessage response = await this.graph.SendAsync(create, cancellationToken).ConfigureAwait(false);
				using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
				result = ReadFolderId(json.RootElement);
			}
			catch (CloudRequestException conflict) when (conflict.StatusCode == (int)HttpStatusCode.Conflict)
			{
				// Another installation can create the same folder after our initial lookup.
				result = await this.ReadFolderAsync(folder, cancellationToken).ConfigureAwait(false);
			}
		}

		return result;
	}

	public void Dispose() => this.graph.Dispose();

	#endregion

	#region Private Methods

	private static string ReadFolderId(JsonElement item)
	{
		if (!item.TryGetProperty("folder", out _) || !item.TryGetProperty("id", out JsonElement value)
			|| string.IsNullOrWhiteSpace(value.GetString()))
		{
			throw new InvalidDataException("The OneDrive book location is not a folder.");
		}

		return value.GetString()!;
	}

	private Task<string> GetRootAsync(CancellationToken cancellationToken)
		=> this.ReadFolderAsync(Graph("me/drive/special/approot?$select=id,folder"), cancellationToken);

	private async Task<string> ReadFolderAsync(Uri uri, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		using HttpResponseMessage response = await this.graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
		using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
		return ReadFolderId(json.RootElement);
	}

	#endregion
}
