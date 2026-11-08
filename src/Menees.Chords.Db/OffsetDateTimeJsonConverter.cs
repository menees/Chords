using System.Text.Json;
using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>Reads ISO timestamps, interpreting legacy wall times in the system time zone.</summary>
public sealed class OffsetDateTimeJsonConverter : JsonConverter<DateTimeOffset>
{
	private readonly Action? reportLegacy;

	/// <summary>Initializes a converter with optional notification of a timestamp requiring migration.</summary>
	public OffsetDateTimeJsonConverter(Action? reportLegacy = null) => this.reportLegacy = reportLegacy;

	/// <inheritdoc/>
	public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		const int DateLength = 10;
		bool offsetless = reader.TokenType == JsonTokenType.String
			&& (reader.HasValueSequence || reader.ValueIsEscaped
				? reader.GetString() is string text && text.Length >= DateLength && text.AsSpan(DateLength).IndexOfAny('+', '-', 'Z') < 0
				: reader.ValueSpan.Length >= DateLength && reader.ValueSpan[DateLength..].IndexOfAny((byte)'+', (byte)'-', (byte)'Z') < 0);
		DateTimeOffset result;
		if (offsetless && reader.TryGetDateTime(out DateTime wallTime))
		{
			this.reportLegacy?.Invoke();

			result = new(wallTime, TimeZoneInfo.Local.GetUtcOffset(wallTime));
		}
		else
		{
			result = reader.GetDateTimeOffset();
		}

		return result;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
		=> writer.WriteStringValue(value);
}
