# Requires Windows, the selected .NET SDK and installed WebView2. No Node.js.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("ChordBookNativeViewer-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeViewerSmoke.cs') -Destination (Join-Path $fixture 'Program.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SyntheticPdf.cs') -Destination (Join-Path $fixture 'SyntheticPdf.cs')
$links = @(
 'src/Menees.Chords.Book.Application/TextViewerBridge.cs',
 'src/Menees.Chords.Book.Application/DocumentViewerPosition.cs',
 'src/Menees.Chords.Db/PerformanceCommand.cs',
 'src/Menees.Chords.Db/PerformanceKeyGesture.cs',
 'src/Menees.Chords.Db/PerformanceKeyBinding.cs'
) | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape((Join-Path $repo $_)) + '" />' }
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.Web.WebView2" Version="1.0.3179.45" />$($links -join '')</ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $fixture 'Smoke.csproj'), $project)
$env:CHORDBOOK_TEST_REPO = $repo
$env:CHORDBOOK_TEST_ASSETS = Join-Path $repo 'src/Menees.Chords.Book.Maui/bin/Release/net10.0-windows10.0.19041.0/win-x64/Viewer'
$env:CHORDBOOK_TEST_OUTPUT = $fixture
dotnet build (Join-Path $repo 'src/Menees.Chords.Book.Maui/Menees.Chords.Book.Maui.csproj') -c Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'App build and client library restore failed.' }
dotnet run --project (Join-Path $fixture 'Smoke.csproj')
$result = Join-Path $fixture 'bin/Debug/net10.0-windows/smoke-result.txt'
if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $result) -or !([IO.File]::ReadAllText($result).StartsWith('PASS:'))) { throw 'Native viewer check failed.' }
"Capture and disposable fixture: $fixture"
