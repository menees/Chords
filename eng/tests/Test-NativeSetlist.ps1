# Runs the production setlist editor against a disposable book, without opening personal books.
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ChordBookSetlistSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeSetlistSmoke.cs') -Destination (Join-Path $fixtureDirectory 'Program.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeImportReviewSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeImportReviewSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeSongBrowserSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeSongBrowserSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeSongMetadataSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeSongMetadataSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeBookStartupSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeBookStartupSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeFileOpeningSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeFileOpeningSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeMetadataImportSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeMetadataImportSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeViewSortSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeViewSortSmoke.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeOutputEditorSmoke.cs') -Destination (Join-Path $fixtureDirectory 'NativeOutputEditorSmoke.cs')
Copy-Item -LiteralPath (Join-Path $repoDirectory 'global.json') -Destination (Join-Path $fixtureDirectory 'global.json')
$resources = [IO.File]::ReadAllText((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/App.xaml')).Replace('Menees.Chords.Book.Maui.App', 'SetlistSmoke.SmokeMauiApp')
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'SmokeMauiApp.xaml'), $resources, [Text.UTF8Encoding]::new($false))
$applicationXaml = '<maui:MauiWinUIApplication x:Class="SetlistSmoke.SmokeWindowsApp" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:maui="using:Microsoft.Maui" />'
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'SmokeApp.xaml'), $applicationXaml, [Text.UTF8Encoding]::new($false))
[xml]$packages = Get-Content -LiteralPath (Join-Path $repoDirectory 'Directory.Packages.props')
$mauiVersion = ($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Maui.Controls').Version
$project = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj'))
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework><UseWinUI>true</UseWinUI><UseMaui>true</UseMaui><WindowsPackageType>None</WindowsPackageType><RuntimeIdentifier>win-x64</RuntimeIdentifier><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><DefineConstants>DISABLE_XAML_GENERATED_MAIN</DefineConstants></PropertyGroup>
<ItemGroup><Page Remove="SmokeApp.xaml;SmokeMauiApp.xaml" /><MauiXaml Remove="SmokeApp.xaml" /><ApplicationDefinition Include="SmokeApp.xaml" /><PackageReference Include="Microsoft.Maui.Controls" Version="$mauiVersion" /><ProjectReference Include="$project" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'Smoke.csproj'), ($projectText -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
$startupFile = Join-Path $fixtureDirectory 'Startup Ω sheet.cho'
[IO.File]::WriteAllText($startupFile, "{title: Startup activation}`r`n[C]Synthetic startup source", [Text.UTF8Encoding]::new($false))
dotnet run --project (Join-Path $fixtureDirectory 'Smoke.csproj') -- --open $startupFile
$runExitCode = $LASTEXITCODE
$resultFile = Get-ChildItem -LiteralPath (Join-Path $fixtureDirectory 'bin') -Filter result.txt -Recurse | Select-Object -First 1
if ($null -eq $resultFile) { throw "Native setlist check produced no result (exit $runExitCode). Fixture: $fixtureDirectory" }
$message = [IO.File]::ReadAllText($resultFile.FullName)
$message
"Capture and disposable fixture: $fixtureDirectory"
if ($runExitCode -ne 0 -or !$message.StartsWith('PASS:')) { throw 'Native setlist check failed.' }
