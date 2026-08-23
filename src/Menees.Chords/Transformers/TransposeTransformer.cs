namespace Menees.Chords.Transformers;

/// <summary>
/// Transposes chord usages, bracketed chord-diagram lookups, and key declarations in a document.
/// </summary>
public sealed class TransposeTransformer : DocumentTransformer
{
	#region Private Data Members

	private readonly AccidentalPreference accidentalPreference;
	private readonly DetectKey detectKey;
	private readonly sbyte halfSteps;
	private readonly Key? key;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified document.
	/// </summary>
	/// <param name="document">The document to transform.</param>
	/// <param name="halfSteps">The signed number of half steps. Values outside one octave wrap around.</param>
	/// <param name="accidentalPreference">Which accidental names should be used.</param>
	/// <param name="detectKey">How to detect the key if it is not declared in metadata.</param>
	public TransposeTransformer(
		Document document,
		sbyte halfSteps,
		AccidentalPreference accidentalPreference = AccidentalPreference.Default,
		DetectKey detectKey = DetectKey.FirstChord)
		: base(document)
	{
		if (!Enum.IsDefined(typeof(AccidentalPreference), accidentalPreference))
		{
			throw new ArgumentOutOfRangeException(nameof(accidentalPreference));
		}

		this.halfSteps = halfSteps;
		this.accidentalPreference = accidentalPreference;
		this.detectKey = detectKey;
	}

	/// <summary>
	/// Creates a new instance using an explicitly supplied key.
	/// </summary>
	/// <param name="document">The document to transform.</param>
	/// <param name="halfSteps">The signed number of half steps. Values outside one octave wrap around.</param>
	/// <param name="key">The song key.</param>
	/// <param name="accidentalPreference">Which accidental names should be used.</param>
	public TransposeTransformer(
		Document document,
		sbyte halfSteps,
		Key key,
		AccidentalPreference accidentalPreference = AccidentalPreference.Default)
		: this(document, halfSteps, accidentalPreference, DetectKey.MetadataOnly)
	{
		Conditions.RequireNonNull(key);
		this.key = key;
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Transposes chord usages, bracketed chord-diagram lookups, and key declarations
	/// in <see cref="DocumentTransformer.Document"/>.
	/// </summary>
	/// <returns>The current transformer.</returns>
	public override
#if NET
		TransposeTransformer
#else
		DocumentTransformer
#endif
		Transform()
	{
		Key key = this.key ?? Key.Find(this.Document, this.detectKey)
			?? throw new InvalidOperationException("The document key is unknown.");
		ChordDocumentTransformer transformer = new(
			key,
			(chord, _) => chord.Transpose(this.halfSteps, this.accidentalPreference),
			value => value.Transpose(this.halfSteps, this.accidentalPreference));
		this.SetEntries(transformer.Transform(this.Document.Entries));
		return this;
	}

	#endregion
}
