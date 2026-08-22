namespace Menees.Chords.Transformers;

/// <summary>
/// Changes every chord in a document to a specified notation.
/// </summary>
public sealed class NotationTransformer : DocumentTransformer
{
	#region Private Data Members

	private readonly Notation notation;
	private readonly DetectKey detectKey;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified document.
	/// </summary>
	/// <param name="document">The document to transform.</param>
	/// <param name="notation">The notation to use.</param>
	/// <param name="detectKey">How to detect the key if it is not declared in metadata.</param>
	public NotationTransformer(Document document, Notation notation, DetectKey detectKey = DetectKey.FirstChord)
		: base(document)
	{
		if (!Enum.IsDefined(typeof(Notation), notation))
		{
			throw new ArgumentOutOfRangeException(nameof(notation));
		}

		this.notation = notation;
		this.detectKey = detectKey;
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Changes all chords in <see cref="DocumentTransformer.Document"/> to the requested notation.
	/// </summary>
	/// <returns>The current transformer.</returns>
	public override
#if NET
		NotationTransformer
#else
		DocumentTransformer
#endif
		Transform()
	{
		Key key = Key.Find(this.Document, this.detectKey)
			?? throw new InvalidOperationException("The document key is unknown.");
		ChordDocumentTransformer transformer = new(key, (chord, currentKey) => chord.ChangeNotation(this.notation, currentKey));
		this.SetEntries(transformer.Transform(this.Document.Entries));
		return this;
	}

	#endregion
}
