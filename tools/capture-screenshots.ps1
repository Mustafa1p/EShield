[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo 'docs/images'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet run --project (Join-Path $repo 'tests/EShield.Tests/EShield.Tests.csproj') -c Release -- $output --screenshots
if ($LASTEXITCODE -ne 0) { throw 'Synthetic screenshot capture failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'src/EShield.App/Resources/shield.svg') -Destination (Join-Path $output 'shield.svg')
Write-Host "Example-only screenshots saved to $output"
