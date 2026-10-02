# Tests and builds the plugin. Writes FreeMusicFinder/bin/Release/FreeMusicFinder-<version>.zip
# and copies it to dist/FreeMusicFinder.zip, the file the README links to.
# Needs the .NET 10 SDK and git.
#
# The plugin is built against the Noctis release named by "minAppVersion" in plugin.json (the
# oldest Noctis it runs in; Plugin.props says why), cloned into extern/Noctis-<that version>.
# Noctis.Plugins.Abstractions is not on NuGet yet. Pass -NoctisRepo to use your own checkout.
param(
    [string]$NoctisRepo = '',
    [string]$Dotnet = 'dotnet',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'

$manifest = Get-Content (Join-Path $PSScriptRoot 'FreeMusicFinder\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $manifest.version -or -not $manifest.minAppVersion) { throw 'plugin.json needs a "version" and a "minAppVersion".' }

if (-not $NoctisRepo) { $NoctisRepo = Join-Path $PSScriptRoot "extern\Noctis-$($manifest.minAppVersion)" }
if (-not (Test-Path (Join-Path $NoctisRepo 'src\Noctis.Plugins.Abstractions'))) {
    git -c advice.detachedHead=false clone --depth 1 --branch "v$($manifest.minAppVersion)" https://github.com/heartached/Noctis.git $NoctisRepo
    if ($LASTEXITCODE -ne 0) { throw "Could not clone Noctis $($manifest.minAppVersion)." }
}

if (-not $SkipTests) {
    & $Dotnet run --project (Join-Path $PSScriptRoot 'tests\FreeMusicFinder.Tests.csproj') "-p:NoctisRepo=$NoctisRepo"
    if ($LASTEXITCODE -ne 0) { throw 'The tests did not pass; nothing was built.' }
}

& $Dotnet build (Join-Path $PSScriptRoot 'FreeMusicFinder\FreeMusicFinder.csproj') -c Release "-p:NoctisRepo=$NoctisRepo"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$zip = Join-Path $PSScriptRoot "FreeMusicFinder\bin\Release\FreeMusicFinder-$($manifest.version).zip"
if (-not (Test-Path $zip)) { throw "The build did not write $zip." }
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
Copy-Item $zip (Join-Path $PSScriptRoot 'dist\FreeMusicFinder.zip') -Force
"Free Music Finder $($manifest.version), built against Noctis $($manifest.minAppVersion): dist\FreeMusicFinder.zip"
