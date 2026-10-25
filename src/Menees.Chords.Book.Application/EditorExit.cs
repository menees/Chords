#region Using Directives

using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Coordinates a user-directed exit without losing pending edits or leaving after a failed save.</summary>
public static class EditorExit
{
	#region Public Methods

	public static async Task RequestAsync(
		bool changed, Func<Task<EditorExitChoice>> choose, Func<Task> saveAndLeave, Func<Task> leave, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(choose);
		ArgumentNullException.ThrowIfNull(saveAndLeave);
		ArgumentNullException.ThrowIfNull(leave);
		cancellationToken.ThrowIfCancellationRequested();
		EditorExitChoice choice = changed ? await choose().ConfigureAwait(true) : EditorExitChoice.DiscardChanges;
		cancellationToken.ThrowIfCancellationRequested();
		if (choice == EditorExitChoice.SaveChanges)
		{
			await saveAndLeave().ConfigureAwait(true);
		}
		else if (choice == EditorExitChoice.DiscardChanges)
		{
			await leave().ConfigureAwait(true);
		}
	}

	#endregion
}
