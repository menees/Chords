# Loads the production icon geometry into WinUI without opening any windows or books.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ChordBookIconSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeIconsSmoke.cs') -Destination (Join-Path $fixture 'Program.cs')
$source = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Book.Maui/Platforms/Windows/FluentIconSource.cs'))
$icons = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Book.Maui/Resources/Fluent/*.svg'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework><UseWinUI>true</UseWinUI><UseMaui>true</UseMaui><WindowsPackageType>None</WindowsPackageType><RuntimeIdentifier>win-x64</RuntimeIdentifier><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AppxGeneratePriEnabled>false</AppxGeneratePriEnabled><AppxGeneratePrisForPortableLibrariesEnabled>false</AppxGeneratePrisForPortableLibrariesEnabled></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.Maui.Controls" Version="10.0.20" /><Compile Include="$source" /><EmbeddedResource Include="$icons"><LogicalName>Menees.Chords.Book.Maui.Resources.Fluent.%(Filename)%(Extension)</LogicalName></EmbeddedResource></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $fixture 'Smoke.csproj'), $project)
dotnet run --project (Join-Path $fixture 'Smoke.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Native icon build/run failed.' }
$result = Get-ChildItem -Path (Join-Path $fixture 'bin') -Filter result.txt -Recurse | Select-Object -First 1
if ($null -eq $result) { throw 'Native icon check produced no result.' }
$message = [IO.File]::ReadAllText($result.FullName)
$message
if (!$message.StartsWith('PASS:')) { throw 'Native icon check failed.' }
"Disposable fixture: $fixture"
