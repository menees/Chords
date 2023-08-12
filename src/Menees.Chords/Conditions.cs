namespace Menees.Chords;

#region Using Directives

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#endregion

internal class Conditions
{
	#region Public Methods

	/// <summary>
	/// Makes sure the argument's state is valid (i.e., true).
	/// </summary>
	/// <param name="argState">The argument's state.</param>
	/// <param name="explanation">The name of the arg to put in the exception.</param>
	/// <exception cref="ArgumentException">If <paramref name="argState"/> is false.</exception>
	public static void RequireArgument([DoesNotReturnIf(false)] bool argState, string explanation)
		=> RequireArgument(argState, explanation, null);

	/// <summary>
	/// Makes sure the named argument's state is valid (i.e., true).
	/// </summary>
	/// <param name="argState">The argument's state.</param>
	/// <param name="explanation">The explanation to put in the exception.</param>
	/// <param name="argName">The name of the arg to put in the exception.</param>
	/// <exception cref="ArgumentException">If <paramref name="argState"/> is false.</exception>
	public static void RequireArgument([DoesNotReturnIf(false)] bool argState, string explanation, string? argName)
	{
		if (!argState)
		{
			throw new ArgumentException(explanation, argName);
		}
	}

	/// <summary>
	/// Makes sure a collection is non-null and non-empty.
	/// </summary>
	/// <typeparam name="T">The type of item in the collection.</typeparam>
	/// <param name="arg">The collection to check.</param>
	/// <param name="argName">The name of the argument to put in the exception.</param>
	/// <exception cref="ArgumentException"><paramref name="arg"/> is null or empty.</exception>
	/// <returns>The non-null <paramref name="arg"/>.</returns>
	public static IEnumerable<T> RequireNonEmpty<T>([NotNull] IEnumerable<T>? arg, [CallerArgumentExpression(nameof(arg))] string? argName = null)
	{
		RequireArgument(arg != null && arg.Any(), "The collection must be non-empty.", argName);
		return arg;
	}

	/// <summary>
	/// Makes sure a reference or nullable value type is non-null.
	/// </summary>
	/// <param name="arg">The reference or nullable value type to check.</param>
	/// <param name="argName">The arg name to put in the exception.</param>
	/// <exception cref="ArgumentNullException">If <paramref name="arg"/> is null.</exception>
	/// <returns>The non-null <paramref name="arg"/>.</returns>
	[return: NotNull]
	public static T RequireNonNull<T>([NotNull] T? arg, [CallerArgumentExpression(nameof(arg))] string? argName = null)
	{
		// Note: This method doesn't use a "where T : class" constraint because we need to allow generic methods
		// and types to call Conditions.RequireReference even if they aren't constrained to just classes.
		if (arg == null)
		{
			throw new ArgumentNullException(argName!);
		}

		return arg;
	}

	/// <summary>
	/// Makes sure the given state is valid (i.e., true).
	/// </summary>
	/// <param name="state">The state to check.</param>
	/// <param name="explanation">The explanation to put in the exception.</param>
	/// <exception cref="InvalidOperationException">If <paramref name="state"/> is false.</exception>
	public static void RequireState([DoesNotReturnIf(false)] bool state, string explanation)
	{
		if (!state)
		{
			throw new InvalidOperationException(explanation);
		}
	}

	/// <summary>
	/// Makes sure a string is non-null, non-empty, and non-whitespace.
	/// </summary>
	/// <param name="arg">The string to check.</param>
	/// <param name="argName">The name of the arg to put in the exception.</param>
	/// <exception cref="ArgumentException"><paramref name="arg"/> is null or empty.</exception>
	/// <returns>The non-null <paramref name="arg"/>.</returns>
	public static string RequireNonEmpty([NotNull] string? arg, [CallerArgumentExpression(nameof(arg))] string? argName = null)
	{
		RequireArgument(arg != null && !string.IsNullOrEmpty(arg), "The string must be non-empty.", argName);
		return arg;
	}

	/// <summary>
	/// Makes sure a string is non-null, non-empty, and non-whitespace.
	/// </summary>
	/// <param name="arg">The string to check.</param>
	/// <param name="argName">The name of the arg to put in the exception.</param>
	/// <exception cref="ArgumentException"><paramref name="arg"/> is null or whitespace.</exception>
	/// <returns>The non-null <paramref name="arg"/>.</returns>
	public static string RequireNonWhiteSpace([NotNull] string? arg, [CallerArgumentExpression(nameof(arg))] string? argName = null)
	{
		RequireArgument(arg != null && !string.IsNullOrWhiteSpace(arg), "The string must be non-whitespace.", argName);
		return arg;
	}

	/// <summary>
	/// Adds a debug-only "no-op" reference to an otherwise unused parameter.
	/// </summary>
	/// <typeparam name="T">The <paramref name="value"/>'s type.</typeparam>
	/// <param name="value">A parameter value from the calling method that was intentionally unused.</param>
	[Conditional("DEBUG")]
	public static void Unused<T>(T value) => value?.GetHashCode();

	#endregion
}
