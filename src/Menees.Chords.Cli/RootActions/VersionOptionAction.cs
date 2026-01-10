namespace Menees.Chords.Cli.RootActions;

#region Using Directives

using System.CommandLine;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

#endregion

internal sealed class VersionOptionAction : RootOptionAction
{
	#region Public Properties

	// See https://github.com/dotnet/dotnet/blob/main/src/command-line-api/src/System.CommandLine/VersionOption.cs
	public override bool ClearsParseErrors => true;

	#endregion

	#region Protected Methods

	protected override RootOptionCommand CreateCommand(ParseResult parseResult)
		=> new VersionCommand(parseResult);

	#endregion

	#region Private Types

	private sealed class VersionCommand : RootOptionCommand
	{
		#region Private Data Members

		private readonly Assembly assembly;
		private readonly List<(string Key, object? Value)> metadata = [];

		#endregion

		#region Constructors

		public VersionCommand(ParseResult parseResult)
			: base(parseResult)
		{
			this.assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
		}

		#endregion

		#region Protected Methods

		protected override async Task<int?> OnExecuteAsync(CancellationToken cancellationToken)
		{
			int? result = await base.OnExecuteAsync(cancellationToken).ConfigureAwait(false);
			this.ReadMetadata();
			this.WriteMetadata();
			return result;
		}

		#endregion

		#region Private Methods

		private bool Add<TAttribute>(string key, Func<TAttribute, object?> valueSelector)
			where TAttribute : Attribute
		{
			bool result = false;

			TAttribute? attribute = this.assembly.GetCustomAttribute<TAttribute>();
			if (attribute is not null)
			{
				result = this.Add(key, valueSelector, attribute);
			}

			return result;
		}

		private bool Add<TAttribute>(string key, Func<TAttribute, object?> valueSelector, TAttribute attribute)
			where TAttribute : Attribute
		{
			bool result = false;

			object? value = valueSelector(attribute);
			if (value is not null)
			{
				this.metadata.Add((key, value));
				result = true;
			}

			return result;
		}

		private void ReadMetadata()
		{
			if (!this.Add<AssemblyInformationalVersionAttribute>("Version", a => Split(a).Version)
				&& !this.Add<AssemblyVersionAttribute>("Assembly Version", a => a.Version))
			{
				this.Add<AssemblyFileVersionAttribute>("File Version", a => a.Version);
			}

			this.Add<AssemblyProductAttribute>("Product", a => a.Product);
			this.Add<AssemblyCompanyAttribute>("Author", a => a.Company);

			// Most terminals won't render the copyright symbol properly, so replace it better than the console's font fallback would.
			// https://devblogs.microsoft.com/commandline/windows-command-line-unicode-and-utf-8-output-text-buffer/#console---built-in-a-pre-unicode-dawn
			this.Add<AssemblyCopyrightAttribute>("Copyright", a => a.Copyright?.Replace("Copyright ", string.Empty).Replace("©", "(c)"));
			this.Add<TargetFrameworkAttribute>("Framework", a => a.FrameworkDisplayName);

			foreach (AssemblyMetadataAttribute metadata in this.assembly.GetCustomAttributes<AssemblyMetadataAttribute>().OrderBy(m => m.Key))
			{
				string key = metadata.Key switch
				{
					"BuildTime" => "Built (UTC)",
					"ProductUrl" => "Product URL",
					"RepositoryUrl" => "Repository URL",
					_ => metadata.Key,
				};
				this.Add(key, m => m.Value, metadata);
			}

			this.Add<AssemblyInformationalVersionAttribute>("Commit ID", a => Split(a).CommitId);

			static (string Version, string? CommitId) Split(AssemblyInformationalVersionAttribute attribute)
			{
				string version = attribute.InformationalVersion;
				int plusIndex = version.IndexOf('+');
				return plusIndex >= 0 && plusIndex < version.Length - 1
					? (version[0..plusIndex], version[(plusIndex + 1)..])
					: (version, null);
			}
		}

		private void WriteMetadata()
		{
			if (this.metadata.Count > 0)
			{
				const int SeparatorWidth = 2; // values can contain single spaces
				int maxKeyWidth = this.metadata.Max(pair => pair.Key.Length) + SeparatorWidth;

				foreach ((string key, object? value) in this.metadata)
				{
					string? textValue = value?.ToString();
					if (!string.IsNullOrWhiteSpace(textValue))
					{
						string pad = new(' ', maxKeyWidth - key.Length);
						this.WriteLine($"{key}:{pad}{textValue}");
					}
				}
			}
		}

		#endregion
	}

	#endregion
}
