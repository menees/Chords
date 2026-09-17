#region Using Directives

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

public sealed class PerformanceInputService
{
	#region Private Data

	private const int MaximumBindings = 32;
	private const int MaximumKeyLength = 64;
	private const int MaximumGestureJsonLength = 256;
	private readonly BookApplicationSession session;

	#endregion

	#region Constructors

	public PerformanceInputService(BookApplicationSession session) => this.session = session;

	#endregion

	#region Public Methods

	public static void Validate(IReadOnlyList<PerformanceKeyBinding> bindings)
	{
		if (bindings.Count > MaximumBindings || bindings.Any(binding => binding?.Gesture is null || !IsValidGesture(binding.Gesture)
			|| !Enum.IsDefined(binding.Command)) || bindings.DistinctBy(binding => binding.Gesture).Count() != bindings.Count)
		{
			throw new ArgumentException("Use at most 32 distinct key combinations and supported commands.", nameof(bindings));
		}
	}

	public static bool TryReadLearnedGesture(string url, out PerformanceKeyGesture? gesture)
	{
		gesture = null;
		if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == "chordbook" && uri.Host == "learn")
		{
			try
			{
				string json = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
				PerformanceKeyGesture? parsed = json.Length <= MaximumGestureJsonLength
					? JsonSerializer.Deserialize<PerformanceKeyGesture>(json, JsonSerializerOptions.Web) : null;
				if (parsed is not null && IsValidGesture(parsed))
				{
					gesture = parsed;
				}
			}
			catch (JsonException)
			{
				// Malformed browser messages are ignored.
			}
		}

		return gesture is not null;
	}

	public static bool TryReadCommand(string url, int generation, out PerformanceCommand command)
	{
		command = default;
		bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == "chordbook" && uri.Host == "command"
			&& uri.AbsolutePath.StartsWith("/" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/", StringComparison.Ordinal)
			&& Enum.TryParse(uri.Segments.Last(), out command) && Enum.IsDefined(command)
			&& uri.AbsolutePath == $"/{generation}/{command}" && uri.Query.Length == 0 && uri.Fragment.Length == 0;
		return result;
	}

	public PerformanceBindingsSnapshot GetBindings()
	{
		ChordDatabase database = this.session.Database ?? throw new InvalidOperationException("No book is open.");
		return new(database.Id, database.BookSettings.Revision.Revision, [.. database.BookSettings.InputBindings]);
	}

	public Task SaveBindingsAsync(
		PerformanceBindingsSnapshot original, IReadOnlyList<PerformanceKeyBinding> bindings, Guid deviceId, CancellationToken cancellationToken = default)
	{
		List<PerformanceKeyBinding> next = [.. bindings];
		Validate(next);
		return this.session.MutateMetadataAsync(
			(database, now) =>
			{
				if (database.Id != original.BookId || database.BookSettings.Revision.Revision != original.Revision)
				{
					throw new InvalidOperationException("Book settings changed. Reopen the input settings before saving.");
				}

				database.BookSettings.InputBindings = next;
				database.BookSettings.Revision = BookApplicationSession.NextRevision(database.BookSettings.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);
	}

	#endregion

	#region Private Methods

	private static bool IsValidGesture(PerformanceKeyGesture gesture)
		=> !string.IsNullOrEmpty(gesture.Key) && gesture.Key.Length <= MaximumKeyLength
			&& gesture.Key is not ("Shift" or "Control" or "Alt" or "Meta" or "Unidentified" or "Escape" or "Tab")
			&& !gesture.Key.Any(char.IsControl);

	#endregion
}
