[CmdletBinding()]
param([switch]$Map, [switch]$Native, [switch]$Persistence)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo 'artifacts/qa'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $PSScriptRoot 'fetch-geoip.ps1')
$arguments = @('run', '--project', (Join-Path $repo 'tests/EShield.Tests/EShield.Tests.csproj'), '-c', 'Release', '--', $output)
if ($Map) { $arguments += '--map' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
if ($Native -or $Persistence) {
    $admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw 'Native and persistence integration tests require an elevated PowerShell session.' }
}
if ($Native) {
    & cmd /c (Join-Path $repo 'tests/build-native-tests.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'Native test build failed.' }
    & (Join-Path $repo 'src/EShield.Native/bin/NativeFilter.Tests.exe') (Join-Path $output 'native-test.log')
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $output 'native-test.log'); throw 'Native checks failed.' }
    Get-Content (Join-Path $output 'native-test.log')
}
if ($Persistence) {
    & (Join-Path $repo 'tests/EShield.Tests/bin/Release/net9.0-windows/EShield.Tests.exe') $output '--persistence'
    if ($LASTEXITCODE -ne 0) { throw 'Persistence checks failed.' }
    Get-Content (Join-Path $output 'persistence-test.log')
}
