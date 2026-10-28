# Runs production MAUI metadata/comparison controls with disposable books and no personal preferences.
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
dotnet build (Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj') -c Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'App build/viewer asset restore failed.' }
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ChordBookMetadataSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeMetadataSmoke.cs') -Destination (Join-Path $fixtureDirectory 'Program.cs')
foreach ($fixtureSource in @('NativeComparisonSession.cs', 'NativeComparisonSmoke.cs', 'NativeSavedTabSmoke.cs', 'SyntheticPdf.cs'))
{
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $fixtureSource) -Destination (Join-Path $fixtureDirectory $fixtureSource)
}
$productionSources = @('SongVersionComparisonPage.cs', 'ScreenLayout.cs', 'ScreenHeader.cs', 'FluentIconButton.cs',
    'Platforms/Windows/WindowsSavedTabDialog.cs', 'Platforms/Windows/WindowsDocumentViewer.cs', 'Platforms/Windows/WindowsWebViewProfile.cs', 'Platforms/Windows/FluentIconButtonHandler.cs', 'Platforms/Windows/FluentIconSource.cs')
$compiledSources = ($productionSources | ForEach-Object {
    '<Compile Include="' + [Security.SecurityElement]::Escape((Join-Path $repoDirectory ('src/Menees.Chords.Book.Maui/' + $_))) + '" />'
}) -join ''
$iconSources = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/Resources/Fluent/*.svg'))
$viewerSources = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/bin/Release/net10.0-windows10.0.19041.0/win-x64/Viewer/**/*'))
$applicationProject = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Application/Menees.Chords.Book.Application.csproj'))
$pickerSource = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/MetadataValuePicker.cs'))
$scalarSource = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/ScalarMetadataEditor.cs'))
$mauiResources = [IO.File]::ReadAllText((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/App.xaml'))
$mauiResources = $mauiResources.Replace('Menees.Chords.Book.Maui.App', 'MetadataSmoke.SmokeMauiApp')
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'SmokeMauiApp.xaml'), $mauiResources, [Text.UTF8Encoding]::new($false))
$applicationXaml = '<maui:MauiWinUIApplication x:Class="MetadataSmoke.SmokeWindowsApp" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:maui="using:Microsoft.Maui" />'
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'SmokeApp.xaml'), $applicationXaml, [Text.UTF8Encoding]::new($false))
[xml]$packages = Get-Content -LiteralPath (Join-Path $repoDirectory 'Directory.Packages.props')
$mauiVersion = ($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Maui.Controls').Version
Copy-Item -LiteralPath (Join-Path $repoDirectory 'global.json') -Destination (Join-Path $fixtureDirectory 'global.json')
$metadataXamlSource = [Security.SecurityElement]::Escape((Join-Path $repoDirectory 'src/Menees.Chords.Book.Maui/SongEditorPage.xaml'))
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework><UseWinUI>true</UseWinUI><UseMaui>true</UseMaui><WindowsPackageType>None</WindowsPackageType><RuntimeIdentifier>win-x64</RuntimeIdentifier><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><DefineConstants>DISABLE_XAML_GENERATED_MAIN</DefineConstants></PropertyGroup>
<ItemGroup><Page Remove="SmokeApp.xaml;SmokeMauiApp.xaml" /><MauiXaml Remove="SmokeApp.xaml" /><ApplicationDefinition Include="SmokeApp.xaml" /><PackageReference Include="Microsoft.Maui.Controls" Version="$mauiVersion" /><PackageReference Include="Microsoft.Web.WebView2" Version="1.0.3179.45" /><ProjectReference Include="$applicationProject" /><Compile Include="$pickerSource" /><Compile Include="$scalarSource" />$compiledSources<EmbeddedResource Include="$iconSources"><LogicalName>Menees.Chords.Book.Maui.Resources.Fluent.%(Filename)%(Extension)</LogicalName></EmbeddedResource><Content Include="$metadataXamlSource" Link="SongEditorPage.xaml" CopyToOutputDirectory="PreserveNewest" /><Content Include="$viewerSources" Link="Viewer/%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'Smoke.csproj'), ($projectText -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
dotnet run --project (Join-Path $fixtureDirectory 'Smoke.csproj')
$runExitCode = $LASTEXITCODE
$resultFile = Get-ChildItem -Path (Join-Path $fixtureDirectory 'bin') -Filter result.txt -Recurse | Select-Object -First 1
if ($null -eq $resultFile) { throw "Native metadata check produced no result (exit $runExitCode)." }
$message = [IO.File]::ReadAllText($resultFile.FullName)
$message
if ($runExitCode -ne 0 -or !$message.StartsWith('PASS:')) { throw 'Native metadata check failed.' }
"Capture and disposable fixture: $fixtureDirectory"
