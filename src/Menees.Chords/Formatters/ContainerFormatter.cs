namespace Menees.Chords.Formatters;

/// <summary>
/// Supports recursively formatting an <see cref="IEntryContainer"/> (e.g., a <see cref="Document"/>).
/// </summary>
public abstract class ContainerFormatter
{
	#region Private Data Members

	private readonly IEntryContainer rootContainer;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="container">The container to format.</param>
	protected ContainerFormatter(IEntryContainer container)
	{
		Conditions.RequireNonNull(container);
		this.rootContainer = container;
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns the formatted text for the current <see cref="IEntryContainer"/>.
	/// </summary>
	public abstract override string ToString();

	#endregion

	#region Protected Methods

	/// <summary>
	/// Recursively formats all the <see cref="Entry"/>s in the current <see cref="IEntryContainer"/>.
	/// </summary>
	protected void Format()
	{
		Stack<IEntryContainer> stack = new();
		this.Format(this.rootContainer, stack);
	}

	/// <summary>
	/// Called during depth-first recursion to format each entry in an <see cref="IEntryContainer"/>.
	/// </summary>
	/// <param name="entry">The current entry to format.</param>
	/// <param name="hierarchy">The hierarchy of containers above this <paramref name="entry"/>.</param>
	protected abstract void Format(Entry entry, IReadOnlyCollection<IEntryContainer> hierarchy);

	/// <summary>
	/// Called when a <paramref name="container"/> is about to be pushed onto the <paramref name="hierarchy"/> stack.
	/// </summary>
	/// <param name="container">The container being started.</param>
	/// <param name="hierarchy">The stack of containers up to but not including <paramref name="container"/>.</param>
	protected virtual void BeginContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireNonNull(container);
		Conditions.RequireNonNull(hierarchy);
	}

	/// <summary>
	/// Called when a <paramref name="container"/> has just been popped from the <paramref name="hierarchy"/> stack.
	/// </summary>
	/// <param name="container">The container being finished.</param>
	/// <param name="hierarchy">The stack of containers up to but not including <paramref name="container"/>.</param>
	protected virtual void EndContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireNonNull(container);
		Conditions.RequireNonNull(hierarchy);
	}

	#endregion

	#region Private Methods

	private void Format(IEntryContainer container, Stack<IEntryContainer> stack)
	{
		Conditions.RequireState(!stack.Contains(container), "A container should not recursively contain itself.");
		this.BeginContainer(container, stack);
		stack.Push(container);
		try
		{
			foreach (Entry entry in container.Entries)
			{
				if (entry is IEntryContainer childContainer)
				{
					this.Format(childContainer, stack);
				}
				else
				{
					this.Format(entry, stack);
				}
			}
		}
		finally
		{
			stack.Pop();
			this.EndContainer(container, stack);
		}
	}

	#endregion
}
