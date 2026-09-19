using System.Net;

namespace Menees.Chords.Sync;

/// <summary>A sanitized provider failure. Tokens, upload URLs and response bodies are never included.</summary>
public sealed class CloudRequestException(int statusCode, TimeSpan? retryAfter = null)
	: Exception($"Cloud request failed (HTTP {statusCode}).")
{
	public int StatusCode { get; } = statusCode;

	public TimeSpan? RetryAfter { get; } = retryAfter;

	public bool IsConcurrencyFailure => (HttpStatusCode)this.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed;

	public bool IsTransient => (HttpStatusCode)this.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
		or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
