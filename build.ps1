[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'EndlessShapesUnlimited\Source\EndlessShapesUnlimited.csproj'
$verification = Join-Path $root 'tools\EndlessShapesUnlimited.Verification\EndlessShapesUnlimited.Verification.csproj'
$packageSource = Join-Path $root 'EndlessShapesUnlimited'
$manifestPath = Join-Path $packageSource 'plugin.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "plugin.json version '$version' is not a three-part release version."
}

$artifacts = Join-Path $root 'artifacts'
$stagingRoot = Join-Path $artifacts 'staging'
$stagedPackage = Join-Path $stagingRoot 'EndlessShapesUnlimited'
$zip = Join-Path $artifacts "EndlessShapesUnlimited-$version.zip"
$buildOutput = Join-Path $packageSource 'Source\bin\Release\netstandard2.1'
$buildDll = Join-Path $buildOutput 'EndlessShapesUnlimited.dll'
$packageDll = Join-Path $packageSource 'EndlessShapesUnlimited.dll'

if ([string]::IsNullOrWhiteSpace($env:FTD_DIR) -or -not (Test-Path -LiteralPath $env:FTD_DIR)) {
    throw 'FTD_DIR must point to the From The Depths installation.'
}

dotnet build $project -c Release --nologo -p:CopyReleaseAssemblyToModFolder=true
if ($LASTEXITCODE -ne 0) { throw 'Mod Release build failed.' }

dotnet format $project --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Mod formatting verification failed.' }

dotnet format $verification --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Verifier formatting verification failed.' }

dotnet run --project $verification -c Release -p:NoWarn=MSB3277
if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }

if (-not (Test-Path -LiteralPath $buildDll) -or -not (Test-Path -LiteralPath $packageDll)) {
    throw 'The Release assembly or packaged assembly is missing.'
}
$buildDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $buildDll).Hash
$packageDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packageDll).Hash
if ($buildDllHash -ne $packageDllHash) {
    throw 'The packaged EndlessShapesUnlimited.dll is stale relative to the Release build.'
}
if (Get-ChildItem -LiteralPath $buildOutput -Recurse -File -Filter '*.pdb') {
    throw 'Release output contains a PDB.'
}

$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($packageDll).Version
if ($assemblyVersion.Major -ne [int]($version.Split('.')[0]) -or
    $assemblyVersion.Minor -ne [int]($version.Split('.')[1]) -or
    $assemblyVersion.Build -ne [int]($version.Split('.')[2])) {
    throw "Assembly version $assemblyVersion does not match plugin.json version $version."
}
$harmonyVersion = [Reflection.AssemblyName]::GetAssemblyName(
    (Join-Path $packageSource '0Harmony.dll')).Version
if ($harmonyVersion.Major -ne 2 -or $harmonyVersion.Minor -ne 3 -or $harmonyVersion.Build -ne 5) {
    throw "Expected Harmony 2.3.5, found $harmonyVersion."
}

if (Test-Path -LiteralPath $stagingRoot) {
    $resolved = (Resolve-Path -LiteralPath $stagingRoot).Path
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifacts).Path
    if (-not $resolved.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected staging path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Path $stagedPackage -Force | Out-Null

$runtimeFiles = @(
    '0Harmony.dll',
    'EndlessShapesUnlimited.dll',
    'header.jpg',
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

$runtimeDirectories = @('Assets', 'Character Items', 'Items', 'Meshes')
foreach ($directory in $runtimeDirectories) {
    Copy-Item -LiteralPath (Join-Path $packageSource $directory) -Destination $stagedPackage -Recurse
}
$licensesDestination = Join-Path $stagedPackage 'LICENSES'
New-Item -ItemType Directory -Path $licensesDestination -Force | Out-Null
$licenseFiles = @('EndlessShapes2-MIT.txt', 'Harmony-MIT.txt')
foreach ($licenseFile in $licenseFiles) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $root 'LICENSES') $licenseFile) -Destination $licensesDestination
}

$allowedTopLevel = $runtimeFiles + $runtimeDirectories + @('LICENSES')
$unexpectedTopLevel = Get-ChildItem -LiteralPath $stagedPackage -Force | Where-Object {
    $_.Name -notin $allowedTopLevel
}
if ($unexpectedTopLevel) {
    throw 'Unexpected top-level package entries: ' +
          (($unexpectedTopLevel | Select-Object -ExpandProperty Name) -join ', ')
}

$forbidden = Get-ChildItem -LiteralPath $stagedPackage -Recurse -Force | Where-Object {
    $_.Name -in @('Source', '.vs', 'bin', 'obj', 'ModAssemblySelector.dll', 'AssemblyFilePath.txt') -or
    $_.Extension -in @('.pdb', '.user', '.suo')
}
if ($forbidden) {
    throw 'Forbidden files entered the runtime package: ' +
          (($forbidden | Select-Object -ExpandProperty FullName) -join ', ')
}

$dlls = @(Get-ChildItem -LiteralPath $stagedPackage -Recurse -File -Filter '*.dll')
if ($dlls.Count -ne 2 -or
    $dlls.Name -notcontains '0Harmony.dll' -or
    $dlls.Name -notcontains 'EndlessShapesUnlimited.dll') {
    throw 'Runtime package must contain only Harmony and EndlessShapesUnlimited DLLs.'
}

function Assert-NoSensitiveContent {
    param([string[]]$Paths)

    $patterns = @(
        @{ Name = 'Windows user-profile path'; Regex = '(?i)[a-z]:[\\/]+users[\\/]+[^\s"''<>]+'; AllowHarmonyVendorPath = $true },
        @{ Name = 'Unix user-profile path'; Regex = '(?i)/(users|home)/[^/\s"''<>]+'; AllowHarmonyVendorPath = $true },
        @{ Name = 'local machine name'; Regex = '(?i)desktop-[a-z0-9]+' },
        @{ Name = 'GitHub token'; Regex = '(?i)(gh[pousr]_[a-z0-9]{20,}|github' + '_pat_[a-z0-9_]{20,})' },
        @{ Name = 'private key'; Regex = '-----BEGIN [A-Z ]*PRIVATE' + ' KEY-----' },
        @{ Name = 'embedded credential'; Regex = '(?i)(password|secret|token)\s*[:=]\s*["''][^"'']{8,}["'']' }
    )
    $localIdentities = @($env:USERNAME, $env:COMPUTERNAME) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.Length -ge 3 } |
        Select-Object -Unique
    foreach ($identity in $localIdentities) {
        $patterns += @{
            Name = 'local identity'
            Regex = '(?i)' + [Regex]::Escape($identity)
        }
    }

    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $bytes = [IO.File]::ReadAllBytes($path)
        $content = [Text.Encoding]::UTF8.GetString($bytes) + "`n" +
                   [Text.Encoding]::Unicode.GetString($bytes)
        foreach ($pattern in $patterns) {
            if ($pattern.AllowHarmonyVendorPath -and
                [IO.Path]::GetFileName($path) -eq '0Harmony.dll') {
                continue
            }
            if ([Regex]::IsMatch($content, $pattern.Regex)) {
                throw "$($pattern.Name) found in $path"
            }
        }
    }
}

$trackedPaths = @(& git -C $root ls-files | ForEach-Object { Join-Path $root $_ })
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked files for privacy scanning.' }
$stagedPaths = @(Get-ChildItem -LiteralPath $stagedPackage -Recurse -File |
    Select-Object -ExpandProperty FullName)
Assert-NoSensitiveContent -Paths ($trackedPaths + $stagedPaths)

if (-not (Test-Path -LiteralPath $artifacts)) {
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Add-Type -AssemblyName System.IO.Compression
$zipStream = $null
$archive = $null
try {
    $zipStream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $archive = New-Object IO.Compression.ZipArchive(
        $zipStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)
    $fixedTimestamp = New-Object DateTimeOffset(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $stagedRootPath = (Resolve-Path -LiteralPath $stagedPackage).Path.TrimEnd('\') + '\'
    $files = Get-ChildItem -LiteralPath $stagedPackage -Recurse -File | Sort-Object {
        $_.FullName.Substring($stagedRootPath.Length).Replace('\', '/')
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stagedRootPath.Length).Replace('\', '/')
        $entry = $archive.CreateEntry(
            'EndlessShapesUnlimited/' + $relative,
            [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTimestamp
        $input = $null
        $output = $null
        try {
            $input = [IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            $input.CopyTo($output)
        }
        finally {
            if ($output) { $output.Dispose() }
            if ($input) { $input.Dispose() }
        }
    }
}
catch {
    if ($archive) { $archive.Dispose(); $archive = $null }
    if ($zipStream) { $zipStream.Dispose(); $zipStream = $null }
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    throw
}
finally {
    if ($archive) { $archive.Dispose() }
    if ($zipStream) { $zipStream.Dispose() }
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zip
Write-Host "Created $zip"
Write-Host "SHA256 $($hash.Hash)"
