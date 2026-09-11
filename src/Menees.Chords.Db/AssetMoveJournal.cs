#region Using Directives

using System.IO;
using System.Text;
using System.Text.Json;

#endregion

namespace Menees.Chords.Db;

/// <summary>Records only affected file moves so an interrupted incremental commit can be recovered.</summary>
internal sealed class AssetMoveJournal : IDisposable
{
	#region Private Data

	internal const string FileName = ".moves";
	private readonly string book;
	private readonly string stage;
	private readonly FileStream stream;
	private readonly List<string[]> moves = [];

	#endregion

	#region Constructors

	internal AssetMoveJournal(string book, string stage, string before, string after)
	{
		this.book = book;
		this.stage = stage;
		this.stream = new(Path.Combine(stage, FileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
		this.Append([before, after]);
	}

	#endregion

	#region Public Methods

	public void Dispose() => this.stream.Dispose();

	#endregion

	#region Internal Methods

	internal static void Recover(string book, string stage)
	{
		string journal = Path.Combine(stage, FileName);
		if (File.Exists(journal))
		{
			// Opening exclusively refuses recovery while another process is committing.
			string text;
			using (FileStream input = new(journal, FileMode.Open, FileAccess.Read, FileShare.None))
			using (StreamReader reader = new(input))
			{
				text = reader.ReadToEnd();
			}

			// An unterminated final intent was never flushed and could not have executed.
			string[] lines = text.Split('\n');
			if (lines.Length > 1)
			{
				string[] header = JsonSerializer.Deserialize<string[]>(lines[0])!;
				string current = File.ReadAllText(Path.Combine(book, "database.json"));
				bool committed = File.Exists(Path.Combine(stage, ".committed")) || (current == header[1] && header[0] != header[1]);
				if (!committed)
				{
					if (current != header[0])
					{
						throw new BookStoreConcurrencyException();
					}

					List<string[]> recorded = [.. lines.Skip(1).Take(lines.Length - 2).Select(line => JsonSerializer.Deserialize<string[]>(line)!)];
					Rollback(book, stage, recorded);
				}
			}

			Directory.Delete(stage, recursive: true);
		}
	}

	internal void Move(string source, string target)
	{
		string[] move = [this.Encode(source), this.Encode(target)];
		this.Append(move);
		File.Move(source, target);
		this.moves.Add(move);
	}

	internal void Rollback() => Rollback(this.book, this.stage, this.moves);

	internal void Complete()
	{
		using FileStream marker = new(Path.Combine(this.stage, ".committed"), FileMode.Create, FileAccess.Write, FileShare.None);
		marker.Flush(flushToDisk: true);
	}

	internal void Discard()
	{
		this.stream.Dispose();
		File.Delete(Path.Combine(this.stage, FileName));
	}

	#endregion

	#region Private Methods

	private static void Rollback(string book, string stage, List<string[]> moves)
	{
		string progress = Path.Combine(stage, ".undo");
		int remaining = File.Exists(progress) ? int.Parse(File.ReadAllText(progress), System.Globalization.CultureInfo.InvariantCulture) : moves.Count - 1;
		for (int index = remaining; index >= 0; index--)
		{
			Undo(book, stage, moves[index]);
			string temporary = progress + ".tmp";
			using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				output.Write(Encoding.UTF8.GetBytes((index - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
				output.Flush(flushToDisk: true);
			}

			File.Move(temporary, progress, overwrite: true);
		}
	}

	private static void Undo(string book, string stage, string[] move)
	{
		string source = Decode(book, stage, move[0]);
		string target = Decode(book, stage, move[1]);
		if (!File.Exists(source) && File.Exists(target))
		{
			File.Move(target, source);
		}
	}

	private static string Decode(string book, string stage, string encoded)
	{
		int slash = encoded.IndexOf('/');
		string name = encoded[(slash + 1)..];
		if (slash < 0 || PortableManagedFileName.Validate(name).Count != 0)
		{
			throw new BookStoreValidationException("The interrupted transaction contains an unsafe path.");
		}

		string root = encoded[..slash] switch
		{
			"book" => book,
			"stage" => stage,
			"rollback" => Path.Combine(stage, ".rollback"),
			_ => throw new BookStoreValidationException("The interrupted transaction contains an unknown path root."),
		};
		return Path.Combine(root, name);
	}

	private void Append(string[] record)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
		this.stream.Write(bytes);
		this.stream.Flush(flushToDisk: true);
	}

	private string Encode(string path)
	{
		string parent = Path.GetDirectoryName(path)!;
		string prefix = parent == this.book ? "book" : parent == this.stage ? "stage" : "rollback";
		return prefix + "/" + Path.GetFileName(path);
	}

	#endregion
}
