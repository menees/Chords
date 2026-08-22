namespace Menees.Chords;

#region Using Directives

using System.Diagnostics.CodeAnalysis;
using System.Text;

#endregion

/// <summary>
/// A named major or minor musical key.
/// </summary>
#if NET8_0_OR_GREATER
public sealed class Key : IEquatable<Key>, IParsable<Key>, ISpanParsable<Key>, IUtf8SpanParsable<Key>
#else
public sealed class Key : IEquatable<Key>
#endif
{
	#region Private Data Members

	private static readonly UTF8Encoding StrictUtf8 = new(false, true);

	#endregion

	#region Constructors

	private Key(string name, string root, bool minor)
	{
		this.Name = name;
		this.Root = root;
		this.IsMinor = minor;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the full name of the key.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the key's tonic note.
	/// </summary>
	public string Root { get; }

	/// <summary>
	/// Gets whether this is a minor key.
	/// </summary>
	public bool IsMinor { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Parses <paramref name="text"/> as a major or minor key.
	/// </summary>
	/// <param name="text">The key name to parse.</param>
	/// <returns>A new key instance.</returns>
	/// <exception cref="FormatException"><paramref name="text"/> is not a valid key.</exception>
	public static Key Parse(string text)
	{
		Conditions.RequireNonWhiteSpace(text);
		if (!TryParse(text, out Key? result))
		{
			throw new FormatException($"Cannot parse \"{text}\" as a key.");
		}

		return result;
	}

	/// <summary>
	/// Tries to parse <paramref name="text"/> as a major or minor key.
	/// </summary>
	/// <param name="text">The key name to parse.</param>
	/// <param name="key">Returns a new key instance if <paramref name="text"/> was parsed.</param>
	/// <returns>True if <paramref name="text"/> was parsed; otherwise false.</returns>
	public static bool TryParse([NotNullWhen(true)] string? text, [MaybeNullWhen(false)] out Key key)
	{
		key = null;
		bool minor = false;
		bool result = Chord.TryParse(text?.Trim(), out Chord? chord)
			&& chord.Notation == Notation.Name
			&& chord.Bass is null
			&& chord.Annotation is null
			&& TryGetMode(chord.Modifiers, out minor);
		if (result)
		{
			key = new(chord!.Name, chord.Root, minor);
		}

		return result;
	}

	/// <summary>
	/// Finds the key declared by a document's metadata, or optionally detects it from a named chord.
	/// </summary>
	/// <param name="document">The document to inspect.</param>
	/// <param name="detectKey">How to detect a key when the document contains no key metadata.</param>
	/// <returns>The declared or detected key, or null if no key can be determined.</returns>
	public static Key? Find(Document document, DetectKey detectKey = DetectKey.FirstChord)
	{
		Conditions.RequireNonNull(document);
		if (!Enum.IsDefined(typeof(DetectKey), detectKey))
		{
			throw new ArgumentOutOfRangeException(nameof(detectKey));
		}

		return DocumentKeyFinder.Find(document, detectKey);
	}

#if NET8_0_OR_GREATER
	/// <inheritdoc/>
	public static Key Parse(string s, IFormatProvider? provider) => Parse(s);

	/// <inheritdoc/>
	public static Key Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s.ToString());

	/// <inheritdoc/>
	public static Key Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
		=> Parse(StrictUtf8.GetString(utf8Text));

	/// <inheritdoc/>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Key result)
		=> TryParse(s, out result);

	/// <inheritdoc/>
	public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Key result)
		=> TryParse(s.ToString(), out result);

	/// <inheritdoc/>
	public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out Key result)
	{
		bool success;
		try
		{
			success = TryParse(StrictUtf8.GetString(utf8Text), out result);
		}
		catch (DecoderFallbackException)
		{
			result = null;
			success = false;
		}

		return success;
	}
#endif

	/// <inheritdoc/>
	public bool Equals(Key? other)
		=> other is not null
			&& this.IsMinor == other.IsMinor
			&& string.Equals(this.Root, other.Root, StringComparison.OrdinalIgnoreCase);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => this.Equals(obj as Key);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> StringComparer.OrdinalIgnoreCase.GetHashCode(this.Root) ^ this.IsMinor.GetHashCode();

	/// <summary>
	/// Returns the key <see cref="Name"/>.
	/// </summary>
	public override string ToString() => this.Name;

	#endregion

	#region Internal Methods

	internal static Key FromChord(Chord chord)
	{
		bool minor = MusicTheory.IsMinor(chord.Modifiers);
		string name = chord.Root + (minor ? "m" : string.Empty);
		return new(name, chord.Root, minor);
	}

	internal Key Transpose(sbyte halfSteps, AccidentalPreference accidentalPreference)
	{
		halfSteps = MusicTheory.NormalizeTranspose(halfSteps);
		Key result = this;
		if (halfSteps != 0)
		{
			string root = MusicTheory.TransposeNamedNote(this.Root, halfSteps, accidentalPreference);
			string name = root + (this.IsMinor ? "m" : string.Empty);
			result = new(name, root, this.IsMinor);
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static bool TryGetMode(IReadOnlyList<string> modifiers, out bool minor)
	{
		minor = false;
		bool result = modifiers.Count == 0;
		if (modifiers.Count == 1)
		{
			string modifier = modifiers[0];
			minor = modifier.Equals("m", StringComparison.Ordinal)
				|| modifier.Equals("min", StringComparison.OrdinalIgnoreCase)
				|| modifier.Equals("-", StringComparison.Ordinal);
			result = minor || modifier.Equals("maj", StringComparison.OrdinalIgnoreCase);
		}

		return result;
	}

	#endregion
}
