#region Using Directives

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync.OneDrive;

internal sealed class OneDriveGraphClient : IDisposable
{
	#region Private Data

	private const string GraphRoot = "https://graph.microsoft.com/v1.0/";
	private readonly HttpClient client;
	private readonly IOneDriveTokenProvider tokens;
	private readonly string accountId;
	private bool disposed;

	#endregion

	#region Public API

	public OneDriveGraphClient(string accountId, IOneDriveTokenProvider tokens, HttpMessageHandler? handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
		ArgumentNullException.ThrowIfNull(tokens);
		this.accountId = accountId;
		this.tokens = tokens;
		this.client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true);
	}

	public bool IsAuthenticated => this.tokens.AccountId == this.accountId;

	public static Uri Graph(string path) => new(GraphRoot + path);

	public static string Escape(string value) => Uri.EscapeDataString(value);

	public static Uri ValidateGraphUri(string value)
	{
		Uri uri = new(value, UriKind.Absolute);
		if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "graph.microsoft.com" || !uri.IsDefaultPort
			|| uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal))
		{
			throw new InvalidDataException("OneDrive returned an invalid Graph continuation URL.");
		}

		return uri;
	}

	public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		return await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	public static void ThrowIfFailed(HttpResponseMessage response)
	{
		if (!response.IsSuccessStatusCode)
		{
			throw new CloudRequestException((int)response.StatusCode, response.Headers.RetryAfter?.Delta);
		}
	}

	public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool allowRedirect = false)
	{
		this.RequireAccount();
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await this.tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
		this.RequireAccount();
		HttpResponseMessage response = await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode && !(allowRedirect && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect))
		{
			try
			{
				ThrowIfFailed(response);
			}
			finally
			{
				response.Dispose();
			}
		}

		return response;
	}

	public Task<HttpResponseMessage> SendDownloadAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		=> this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

	public void RequireAccount()
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		if (!this.IsAuthenticated)
		{
			throw new InvalidOperationException("Sign in to the OneDrive account configured for this replica.");
		}
	}

	public void Dispose()
	{
		this.disposed = true;
		this.client.Dispose();
	}

	#endregion
}
