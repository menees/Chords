namespace Menees.Chords.Web;

using System.Reflection;

public partial class MainLayout
{
	public static string? DisplayVersion
	{
		get
		{
			Assembly assembly = typeof(Document).Assembly;
			AssemblyName name = assembly.GetName();
			Version? version = name.Version;

			string? result = null;
			if (version is not null)
			{
				const int MaxFieldCount = 4;
				int fieldCount = MaxFieldCount;
				if (version.Revision == 0)
				{
					fieldCount--;
					if (version.Build == 0)
					{
						fieldCount--;
					}
				}

				result = name.Version?.ToString(fieldCount);
			}

			return result;
		}
	}
}