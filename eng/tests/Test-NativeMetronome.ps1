# Runs a disposable console host against the production Windows audio adapter. Output is muted.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ChordBookNativeMetronome-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeMetronomeSmoke.cs') -Destination (Join-Path $fixture 'Program.cs')
$engine = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Book.Maui/Platforms/Windows/WindowsMetronomeEngine.cs'))
$contract = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Book.Maui/Services/IMetronomeEngine.cs'))
$application = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Menees.Chords.Book.Application/Menees.Chords.Book.Application.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><ProjectReference Include="$application" /><Compile Include="$engine" /><Compile Include="$contract" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $fixture 'Smoke.csproj'), $project)
dotnet run --project (Join-Path $fixture 'Smoke.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Native metronome check failed.' }
"Disposable fixture: $fixture"
