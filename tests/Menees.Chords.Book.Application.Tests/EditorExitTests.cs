#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class EditorExitTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow(EditorExitChoice.SaveChanges, 1, 0)]
	[DataRow(EditorExitChoice.DiscardChanges, 0, 1)]
	[DataRow(EditorExitChoice.KeepEditing, 0, 0)]
	public async Task PendingEditsFollowOnlyTheChosenExit(EditorExitChoice choice, int expectedSaves, int expectedLeaves)
	{
		int prompts = 0, saves = 0, leaves = 0;
		await EditorExit.RequestAsync(
			true,
			async () =>
			{
				prompts++;
				await Task.Yield();
				return choice;
			},
			() =>
			{
				saves++;
				return Task.CompletedTask;
			},
			() =>
			{
				leaves++;
				return Task.CompletedTask;
			},
			this.TestContext.CancellationToken);
		prompts.ShouldBe(1);
		saves.ShouldBe(expectedSaves);
		leaves.ShouldBe(expectedLeaves);
	}

	[TestMethod]
	public async Task UnchangedEditorLeavesWithoutPromptOrSave()
	{
		int leaves = 0;
		await EditorExit.RequestAsync(
			false,
			() => throw new InvalidOperationException("Unchanged editor prompted"),
			() => throw new InvalidOperationException("Unchanged editor saved"),
			() =>
			{
				leaves++;
				return Task.CompletedTask;
			},
			this.TestContext.CancellationToken);
		leaves.ShouldBe(1);
	}

	[TestMethod]
	public async Task FailedSaveDoesNotFallThroughToDiscard()
	{
		int leaves = 0;
		await Should.ThrowAsync<IOException>(() => EditorExit.RequestAsync(
			true,
			() => Task.FromResult(EditorExitChoice.SaveChanges),
			() => throw new IOException("Disk full"),
			() =>
			{
				leaves++;
				return Task.CompletedTask;
			},
			this.TestContext.CancellationToken));
		leaves.ShouldBe(0);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task CancellationCannotLeaveOrCommitAnEditor(bool cancelDuringPrompt)
	{
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
		if (!cancelDuringPrompt)
		{
			cancellation.Cancel();
		}

		int actions = 0;
		await Should.ThrowAsync<OperationCanceledException>(() => EditorExit.RequestAsync(
			true,
			() =>
			{
				cancellation.Cancel();
				return Task.FromResult(EditorExitChoice.SaveChanges);
			},
			() =>
			{
				actions++;
				return Task.CompletedTask;
			},
			() =>
			{
				actions++;
				return Task.CompletedTask;
			},
			cancellation.Token));
		actions.ShouldBe(0);
	}

	#endregion
}
