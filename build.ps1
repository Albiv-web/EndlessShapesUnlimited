[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj'
$verification = Join-Path $root 'tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj'
$packageSource = Join-Path $root 'EndlessShapesUnlimited'
$artifacts = Join-Path $root 'artifacts'
$stagingRoot = Join-Path $artifacts 'staging'
$stagedPackage = Join-Path $stagingRoot 'EndlessShapesUnlimited'
$zip = Join-Path $artifacts 'EndlessShapesUnlimited-1.0.0.zip'

if ([string]::IsNullOrWhiteSpace($env:FTD_DIR) -or -not (Test-Path -LiteralPath $env:FTD_DIR)) {
    throw 'FTD_DIR must point to the From The Depths installation.'
}

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }

dotnet run --project $verification -c Release -p:NoWarn=MSB3277
if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }

if (Test-Path -LiteralPath $stagingRoot) {
    $resolved = (Resolve-Path -LiteralPath $stagingRoot).Path
    $resolvedArtifacts = if (Test-Path -LiteralPath $artifacts) {
        (Resolve-Path -LiteralPath $artifacts).Path
    } else {
        $artifacts
    }
    if (-not $resolved.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected staging path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Path $stagedPackage -Force | Out-Null

$runtimeFiles = @(
    '0Harmony.dll',
    'EndlessShapesUnlimited.dll',
    'header.header',
    'plugin.json',
    'releases',
    'README.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md'
)
foreach ($relative in $runtimeFiles) {
    Copy-Item -LiteralPath (Join-Path $packageSource $relative) -Destination $stagedPackage
}

foreach ($directory in @('Assets', 'Character Items', 'Items', 'Meshes')) {
    Copy-Item -LiteralPath (Join-Path $packageSource $directory) -Destination $stagedPackage -Recurse
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSES') -Destination $stagedPackage -Recurse

$forbidden = Get-ChildItem -LiteralPath $stagedPackage -Recurse -Force | Where-Object {
    $_.Name -in @('Source', '.vs', 'bin', 'obj', 'ModAssemblySelector.dll', 'AssemblyFilePath.txt') -or
    $_.Extension -in @('.pdb', '.user', '.suo')
}
if ($forbidden) {
    throw 'Forbidden files entered the runtime package: ' +
          (($forbidden | Select-Object -ExpandProperty FullName) -join ', ')
}

$dlls = Get-ChildItem -LiteralPath $stagedPackage -Recurse -File -Filter '*.dll'
if ($dlls.Count -ne 2 -or
    $dlls.Name -notcontains '0Harmony.dll' -or
    $dlls.Name -notcontains 'EndlessShapesUnlimited.dll') {
    throw 'Runtime package must contain only Harmony and EndlessShapesUnlimited DLLs.'
}

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path $stagedPackage -DestinationPath $zip -CompressionLevel Optimal

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zip
Write-Host "Created $zip"
Write-Host "SHA256 $($hash.Hash)"
