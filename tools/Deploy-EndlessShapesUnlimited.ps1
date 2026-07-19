[CmdletBinding()]
param(
    [string]$SourceRoot = '',
    [string]$LicensesRoot = '',
    [string]$ModsRoot = (Join-Path $env:USERPROFILE 'Documents\From The Depths\Mods'),
    [string]$ModName = 'EndlessShapesUnlimited',
    [string[]]$AdditionalModsRoots = @(),
    [switch]$OnlyModsRoot,
    [switch]$OnlyConfiguredRoots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$usingRepositoryDefaultSource = [string]::IsNullOrWhiteSpace($SourceRoot)
if ($usingRepositoryDefaultSource)
{
    $SourceRoot = Join-Path $PSScriptRoot '..\EndlessShapesUnlimited'
}

if (!(Test-Path -LiteralPath $SourceRoot -PathType Container))
{
    throw "Runtime source folder does not exist: $SourceRoot"
}

$sourceFull = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourceRoot).Path)
if ([string]::IsNullOrWhiteSpace($LicensesRoot))
{
    if ($usingRepositoryDefaultSource)
    {
        $LicensesRoot = Join-Path $PSScriptRoot '..\LICENSES'
    }
    else
    {
        $LicensesRoot = Join-Path $sourceFull 'LICENSES'
    }
}

if (!(Test-Path -LiteralPath $LicensesRoot -PathType Container))
{
    throw "Third-party licence folder does not exist: $LicensesRoot. " +
          'A caller-supplied SourceRoot must contain LICENSES or be paired with -LicensesRoot.'
}

$licensesSourceFull = [System.IO.Path]::GetFullPath(
    (Resolve-Path -LiteralPath $LicensesRoot).Path)

if ([string]::IsNullOrWhiteSpace($ModName) -or
    [System.IO.Path]::GetFileName($ModName) -ne $ModName)
{
    throw "ModName must be one folder name: '$ModName'"
}

$runtimeFiles = @(
    'EndlessShapesUnlimited.dll',
    '0Harmony.dll',
    'plugin.json',
    'header.header',
    'header.jpg',
    'LICENSE',
    'README.md',
    'releases',
    'THIRD_PARTY_NOTICES.md'
)
$runtimeDirectories = @(
    'Assets',
    'Character Items',
    'Items',
    'Meshes'
)
$licenseFiles = @(
    'EndlessShapes2-MIT.txt',
    'Harmony-MIT.txt'
)

function Resolve-FullPathText {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        return $null
    }

    return [System.IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($Path))
}

function Get-RelativePathText {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootPrefix = $Root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (!$fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Path is outside the expected root '$Root': $fullPath"
    }

    return $fullPath.Substring($rootPrefix.Length).Replace(
        [System.IO.Path]::AltDirectorySeparatorChar,
        [System.IO.Path]::DirectorySeparatorChar)
}

function Get-PackageIdentity {
    param(
        [string]$PackageRoot,
        [string]$ExpectedVersion = '',
        [string]$ExpectedDllHash = ''
    )

    $pluginPath = Join-Path $PackageRoot 'plugin.json'
    $dllPath = Join-Path $PackageRoot 'EndlessShapesUnlimited.dll'
    if (!(Test-Path -LiteralPath $pluginPath -PathType Leaf))
    {
        throw "Runtime package is missing plugin.json: $pluginPath"
    }
    if (!(Test-Path -LiteralPath $dllPath -PathType Leaf))
    {
        throw "Runtime package is missing EndlessShapesUnlimited.dll: $dllPath"
    }

    $plugin = Get-Content -LiteralPath $pluginPath -Raw | ConvertFrom-Json
    $version = [string]$plugin.version
    if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)$')
    {
        throw "plugin.json version '$version' is not a three-part semantic version."
    }

    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version
    if ($assemblyVersion.Major -ne [int]$Matches[1] -or
        $assemblyVersion.Minor -ne [int]$Matches[2] -or
        $assemblyVersion.Build -ne [int]$Matches[3] -or
        $assemblyVersion.Revision -gt 0)
    {
        throw "Assembly version $assemblyVersion does not match plugin.json version $version."
    }

    $dllItem = Get-Item -LiteralPath $dllPath
    $dllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dllPath).Hash
    if (![string]::IsNullOrWhiteSpace($ExpectedVersion) -and
        $version -ne $ExpectedVersion)
    {
        throw "Runtime plugin.json version $version does not match expected version $ExpectedVersion."
    }
    if (![string]::IsNullOrWhiteSpace($ExpectedDllHash) -and
        $dllHash -ne $ExpectedDllHash)
    {
        throw "Runtime DLL hash $dllHash does not match expected hash $ExpectedDllHash."
    }

    return [pscustomobject]@{
        Version = $version
        AssemblyVersion = $assemblyVersion.ToString()
        Dll = $dllPath
        DllHash = $dllHash
        DllLength = $dllItem.Length
        DllLastWriteTime = $dllItem.LastWriteTime
    }
}

function Add-SourceManifestFile {
    param(
        [hashtable]$Manifest,
        [string]$RelativePath,
        [string]$SourcePath
    )

    $normalized = $RelativePath.Replace(
        [System.IO.Path]::AltDirectorySeparatorChar,
        [System.IO.Path]::DirectorySeparatorChar)
    if ($Manifest.ContainsKey($normalized))
    {
        throw "Duplicate runtime destination in source manifest: $normalized"
    }

    $Manifest[$normalized] = [pscustomobject]@{
        Source = $SourcePath
        Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $SourcePath).Hash
    }
}

function Get-ValidatedSourcePackage {
    $manifest = @{}

    foreach ($relative in $runtimeFiles)
    {
        $path = Join-Path $sourceFull $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Missing runtime file: $path"
        }
        Add-SourceManifestFile -Manifest $manifest -RelativePath $relative -SourcePath $path
    }

    foreach ($relative in $runtimeDirectories)
    {
        $directoryPath = Join-Path $sourceFull $relative
        if (!(Test-Path -LiteralPath $directoryPath -PathType Container))
        {
            throw "Missing runtime directory: $directoryPath"
        }

        foreach ($file in Get-ChildItem -LiteralPath $directoryPath -Recurse -Force -File)
        {
            $withinDirectory = Get-RelativePathText -Root $directoryPath -Path $file.FullName
            $destinationRelative = Join-Path $relative $withinDirectory
            Add-SourceManifestFile -Manifest $manifest `
                -RelativePath $destinationRelative -SourcePath $file.FullName
        }
    }

    foreach ($relative in $licenseFiles)
    {
        $path = Join-Path $licensesSourceFull $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Missing required third-party licence: $path"
        }
        Add-SourceManifestFile -Manifest $manifest `
            -RelativePath (Join-Path 'LICENSES' $relative) -SourcePath $path
    }

    $sourceDlls = @($manifest.Keys | Where-Object {
            [System.IO.Path]::GetExtension($_) -eq '.dll'
        })
    if ($sourceDlls.Count -ne 2 -or
        $sourceDlls -notcontains 'EndlessShapesUnlimited.dll' -or
        $sourceDlls -notcontains '0Harmony.dll')
    {
        throw 'Runtime source would deploy unexpected DLLs: ' + ($sourceDlls -join ', ')
    }

    $identity = Get-PackageIdentity -PackageRoot $sourceFull
    return [pscustomobject]@{
        Files = $manifest
        Identity = $identity
    }
}

function Assert-StagedRuntime {
    param(
        [string]$PackageRoot,
        [object]$ValidatedSource
    )

    foreach ($relative in $runtimeFiles)
    {
        $path = Join-Path $PackageRoot $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Staged runtime is missing file: $relative"
        }
    }
    foreach ($relative in $runtimeDirectories)
    {
        $path = Join-Path $PackageRoot $relative
        if (!(Test-Path -LiteralPath $path -PathType Container))
        {
            throw "Staged runtime is missing directory: $relative"
        }
    }
    foreach ($relative in $licenseFiles)
    {
        $path = Join-Path (Join-Path $PackageRoot 'LICENSES') $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Staged runtime is missing third-party licence: $relative"
        }
    }

    $allowedTopLevel = @($runtimeFiles + $runtimeDirectories + @('LICENSES'))
    $unexpectedTopLevel = @(Get-ChildItem -LiteralPath $PackageRoot -Force | Where-Object {
            $allowedTopLevel -notcontains $_.Name
        })
    if ($unexpectedTopLevel.Count -ne 0)
    {
        throw 'Unexpected top-level runtime entries: ' +
              (($unexpectedTopLevel | Select-Object -ExpandProperty Name) -join ', ')
    }

    if (Test-Path -LiteralPath (Join-Path $PackageRoot 'Source'))
    {
        throw 'Clean runtime deploy must not contain the development Source folder.'
    }

    $dlls = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse -Force -File -Filter '*.dll')
    if ($dlls.Count -ne 2 -or
        $dlls.Name -notcontains 'EndlessShapesUnlimited.dll' -or
        $dlls.Name -notcontains '0Harmony.dll')
    {
        throw 'Runtime package must contain only Harmony and EndlessShapesUnlimited DLLs. Found: ' +
              ($dlls.FullName -join ', ')
    }

    $actualFiles = @{}
    foreach ($file in Get-ChildItem -LiteralPath $PackageRoot -Recurse -Force -File)
    {
        $relative = Get-RelativePathText -Root $PackageRoot -Path $file.FullName
        $actualFiles[$relative] = $file.FullName
    }

    $missingFiles = @($ValidatedSource.Files.Keys | Where-Object {
            !$actualFiles.ContainsKey($_)
        })
    $unexpectedFiles = @($actualFiles.Keys | Where-Object {
            !$ValidatedSource.Files.ContainsKey($_)
        })
    if ($missingFiles.Count -ne 0 -or $unexpectedFiles.Count -ne 0)
    {
        throw "Staged runtime structure differs from source. Missing: $($missingFiles -join ', '); " +
              "unexpected: $($unexpectedFiles -join ', ')."
    }

    foreach ($relative in $ValidatedSource.Files.Keys)
    {
        $actualHash = (Get-FileHash -Algorithm SHA256 `
            -LiteralPath $actualFiles[$relative]).Hash
        $expectedHash = $ValidatedSource.Files[$relative].Hash
        if ($actualHash -ne $expectedHash)
        {
            throw "Staged runtime hash mismatch for $relative."
        }
    }

    return Get-PackageIdentity -PackageRoot $PackageRoot `
        -ExpectedVersion $ValidatedSource.Identity.Version `
        -ExpectedDllHash $ValidatedSource.Identity.DllHash
}

function Copy-SourceToStage {
    param([string]$StageRoot)

    New-Item -ItemType Directory -Path $StageRoot | Out-Null
    foreach ($relative in $runtimeFiles)
    {
        Copy-Item -LiteralPath (Join-Path $sourceFull $relative) `
            -Destination $StageRoot -Force
    }
    foreach ($relative in $runtimeDirectories)
    {
        Copy-Item -LiteralPath (Join-Path $sourceFull $relative) `
            -Destination $StageRoot -Recurse -Force
    }

    $licensesDestination = Join-Path $StageRoot 'LICENSES'
    New-Item -ItemType Directory -Path $licensesDestination | Out-Null
    foreach ($relative in $licenseFiles)
    {
        Copy-Item -LiteralPath (Join-Path $licensesSourceFull $relative) `
            -Destination $licensesDestination -Force
    }
}

function Get-KnownModsRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    $roots.Add($ModsRoot)
    if (!$OnlyModsRoot)
    {
        if (!$OnlyConfiguredRoots)
        {
            $documents = [Environment]::GetFolderPath('MyDocuments')
            if (![string]::IsNullOrWhiteSpace($documents))
            {
                $roots.Add((Join-Path $documents 'From The Depths\Mods'))
            }

            if (![string]::IsNullOrWhiteSpace($env:USERPROFILE))
            {
                $roots.Add((Join-Path $env:USERPROFILE 'Documents\From The Depths\Mods'))
                $roots.Add((Join-Path $env:USERPROFILE 'OneDrive\Dokumenter\From The Depths\Mods'))
            }
        }

        foreach ($root in $AdditionalModsRoots)
        {
            $roots.Add($root)
        }
    }

    $seen = New-Object System.Collections.Generic.HashSet[string] `
        ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $roots)
    {
        $full = Resolve-FullPathText $root
        if ($null -ne $full -and $seen.Add($full))
        {
            $full
        }
    }
}

function Assert-DeploymentChildPath {
    param(
        [string]$ModsRootFull,
        [string]$Path,
        [string]$ExpectedLeaf
    )

    $modsRootPrefix = $ModsRootFull.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (!$fullPath.StartsWith($modsRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $fullPath) -ne $ExpectedLeaf)
    {
        throw "Refusing to modify a path outside the configured Mods folder: $fullPath"
    }
}

function New-DeploymentTransaction {
    param(
        [string]$ModsRootFull,
        [bool]$CreateRoot,
        [object]$ValidatedSource
    )

    $rootCreated = $false
    if ($CreateRoot -and !(Test-Path -LiteralPath $ModsRootFull))
    {
        $modsRootItem = New-Item -ItemType Directory -Force -Path $ModsRootFull
        $modsRootFull = [System.IO.Path]::GetFullPath($modsRootItem.FullName)
        $rootCreated = $true
    }
    elseif (!(Test-Path -LiteralPath $ModsRootFull -PathType Container))
    {
        Write-Host "Skipping missing Mods root $ModsRootFull"
        return $null
    }
    else
    {
        $modsRootFull = [System.IO.Path]::GetFullPath(
            (Resolve-Path -LiteralPath $ModsRootFull).Path)
    }

    $destinationFull = [System.IO.Path]::GetFullPath((Join-Path $modsRootFull $ModName))
    Assert-DeploymentChildPath -ModsRootFull $modsRootFull `
        -Path $destinationFull -ExpectedLeaf $ModName
    if (!$CreateRoot -and !(Test-Path -LiteralPath $destinationFull -PathType Container))
    {
        Write-Host "Skipping Mods root without existing $ModName runtime $ModsRootFull"
        return $null
    }
    if ((Test-Path -LiteralPath $destinationFull) -and
        !(Test-Path -LiteralPath $destinationFull -PathType Container))
    {
        throw "Destination exists but is not a directory: $destinationFull"
    }

    $transactionId = [System.Guid]::NewGuid().ToString('N')
    $modsRootLeaf = Split-Path -Leaf $modsRootFull
    $transactionParent = Split-Path -Parent $modsRootFull
    if ([string]::IsNullOrWhiteSpace($transactionParent) -or
        $transactionParent -eq $modsRootFull)
    {
        throw "Mods root must have a parent folder for safe rollback storage: $modsRootFull"
    }
    $stageLeaf = ".$modsRootLeaf-$ModName.deploy-$transactionId"
    $backupLeaf = ".$modsRootLeaf-$ModName.backup-$transactionId"
    $failedLeaf = ".$modsRootLeaf-$ModName.failed-$transactionId"
    # Every transaction folder stays outside the scanned Mods root. If
    # best-effort cleanup later fails, FtD cannot discover a staged, retired, or
    # failed plugin as a duplicate mod.
    $stageFull = Join-Path $transactionParent $stageLeaf
    $backupFull = Join-Path $transactionParent $backupLeaf
    $failedFull = Join-Path $transactionParent $failedLeaf
    Assert-DeploymentChildPath -ModsRootFull $transactionParent `
        -Path $stageFull -ExpectedLeaf $stageLeaf
    Assert-DeploymentChildPath -ModsRootFull $transactionParent `
        -Path $backupFull -ExpectedLeaf $backupLeaf
    Assert-DeploymentChildPath -ModsRootFull $transactionParent `
        -Path $failedFull -ExpectedLeaf $failedLeaf

    try
    {
        Copy-SourceToStage -StageRoot $stageFull
        $null = Assert-StagedRuntime -PackageRoot $stageFull -ValidatedSource $ValidatedSource
    }
    catch
    {
        if (Test-Path -LiteralPath $stageFull)
        {
            try
            {
                Remove-Item -LiteralPath $stageFull -Recurse -Force
            }
            catch
            {
                Write-Warning "Could not remove failed deployment stage $stageFull`: $_"
            }
        }
        if ($rootCreated -and (Test-Path -LiteralPath $modsRootFull -PathType Container))
        {
            try
            {
                if (@(Get-ChildItem -LiteralPath $modsRootFull -Force).Count -eq 0)
                {
                    Remove-Item -LiteralPath $modsRootFull -Force
                }
            }
            catch
            {
                Write-Warning "Could not remove newly-created empty Mods root $modsRootFull`: $_"
            }
        }
        throw
    }

    return [pscustomobject]@{
        ModsRoot = $modsRootFull
        Destination = $destinationFull
        Stage = $stageFull
        Backup = $backupFull
        Failed = $failedFull
        RootCreated = $rootCreated
        BackupCreated = $false
        StagePromoted = $false
        Identity = $null
    }
}

function Commit-DeploymentTransaction {
    param(
        [object]$Transaction,
        [object]$ValidatedSource
    )

    if (Test-Path -LiteralPath $Transaction.Destination -PathType Container)
    {
        [System.IO.Directory]::Move($Transaction.Destination, $Transaction.Backup)
        $Transaction.BackupCreated = $true
    }

    [System.IO.Directory]::Move($Transaction.Stage, $Transaction.Destination)
    $Transaction.StagePromoted = $true
    $Transaction.Identity = Assert-StagedRuntime `
        -PackageRoot $Transaction.Destination -ValidatedSource $ValidatedSource
}

function Undo-DeploymentTransaction {
    param([object]$Transaction)

    if ($null -eq $Transaction)
    {
        return
    }

    if ($Transaction.StagePromoted -and
        (Test-Path -LiteralPath $Transaction.Destination))
    {
        [System.IO.Directory]::Move($Transaction.Destination, $Transaction.Failed)
        $Transaction.StagePromoted = $false
    }
    if ($Transaction.BackupCreated)
    {
        if (Test-Path -LiteralPath $Transaction.Destination)
        {
            throw "Cannot restore the retired runtime because the destination is occupied: $($Transaction.Destination)"
        }
        [System.IO.Directory]::Move($Transaction.Backup, $Transaction.Destination)
        $Transaction.BackupCreated = $false
    }

    foreach ($cleanupPath in @($Transaction.Stage, $Transaction.Failed))
    {
        if (Test-Path -LiteralPath $cleanupPath)
        {
            try
            {
                Remove-Item -LiteralPath $cleanupPath -Recurse -Force
            }
            catch
            {
                Write-Warning "Could not remove deployment transaction folder $cleanupPath`: $_"
            }
        }
    }

    if ($Transaction.BackupCreated -or (Test-Path -LiteralPath $Transaction.Backup))
    {
        throw "The retired runtime could not be restored from $($Transaction.Backup)."
    }

    if ($Transaction.RootCreated -and
        (Test-Path -LiteralPath $Transaction.ModsRoot -PathType Container))
    {
        try
        {
            if (@(Get-ChildItem -LiteralPath $Transaction.ModsRoot -Force).Count -eq 0)
            {
                Remove-Item -LiteralPath $Transaction.ModsRoot -Force
            }
        }
        catch
        {
            Write-Warning "Could not remove newly-created empty Mods root $($Transaction.ModsRoot): $_"
        }
    }
}

function Complete-DeploymentTransaction {
    param([object]$Transaction)

    if ($Transaction.BackupCreated -and
        (Test-Path -LiteralPath $Transaction.Backup))
    {
        try
        {
            Remove-Item -LiteralPath $Transaction.Backup -Recurse -Force
            $Transaction.BackupCreated = $false
        }
        catch
        {
            Write-Warning ("Deployment succeeded but its retired backup outside the Mods root " +
                           "could not be removed: $($Transaction.Backup)")
        }
    }

    $identity = $Transaction.Identity
    Write-Host "Deployed clean EndlessShapesUnlimited runtime to $($Transaction.Destination)"
    Write-Host ("DLL: $($identity.DllLength) bytes, $($identity.DllLastWriteTime), " +
                "SHA256 $($identity.DllHash), plugin.json version $($identity.Version), " +
                "assembly version $($identity.AssemblyVersion)")

    return [pscustomobject]@{
        ModsRoot = $Transaction.ModsRoot
        Destination = $Transaction.Destination
        Dll = $identity.Dll
        Hash = $identity.DllHash
        Version = $identity.Version
        AssemblyVersion = $identity.AssemblyVersion
    }
}

# Complete source, licence, semantic-version, assembly-version, and source-hash
# validation before creating or replacing anything under a Mods root.
$validatedSource = Get-ValidatedSourcePackage

$knownRoots = @(Get-KnownModsRoots)
$transactions = New-Object System.Collections.Generic.List[object]
$deployments = New-Object System.Collections.Generic.List[object]
try
{
    # Stage and hash-validate every selected root before replacing the first
    # installed runtime. Backups are then retained until the whole set commits.
    for ($index = 0; $index -lt $knownRoots.Count; $index++)
    {
        $transaction = New-DeploymentTransaction -ModsRootFull $knownRoots[$index] `
            -CreateRoot:($index -eq 0) -ValidatedSource $validatedSource
        if ($null -ne $transaction)
        {
            $transactions.Add($transaction)
        }
    }

    if ($transactions.Count -lt 1)
    {
        throw 'No EndlessShapesUnlimited runtime folders were selected for deployment.'
    }

    foreach ($transaction in $transactions)
    {
        Commit-DeploymentTransaction -Transaction $transaction `
            -ValidatedSource $validatedSource
    }

    $identities = @($transactions | Select-Object -ExpandProperty Identity)
    $hashes = @($identities | Select-Object -ExpandProperty DllHash -Unique)
    $versions = @($identities | Select-Object -ExpandProperty Version -Unique)
    $assemblyVersions = @($identities | Select-Object -ExpandProperty AssemblyVersion -Unique)
    if ($hashes.Count -ne 1)
    {
        throw "Deployed DLL hashes differ: $($hashes -join ', ')"
    }
    if ($versions.Count -ne 1)
    {
        throw "Deployed plugin.json versions differ: $($versions -join ', ')"
    }
    if ($assemblyVersions.Count -ne 1)
    {
        throw "Deployed assembly versions differ: $($assemblyVersions -join ', ')"
    }

    foreach ($transaction in $transactions)
    {
        $deployments.Add((Complete-DeploymentTransaction -Transaction $transaction))
    }
}
catch
{
    $deploymentError = $_
    $rollbackErrors = New-Object System.Collections.Generic.List[string]
    for ($index = $transactions.Count - 1; $index -ge 0; $index--)
    {
        try
        {
            Undo-DeploymentTransaction -Transaction $transactions[$index]
        }
        catch
        {
            $rollbackErrors.Add([string]$_)
        }
    }

    if ($rollbackErrors.Count -ne 0)
    {
        throw "Deployment failed and deployment-set rollback was incomplete. Deployment: $deploymentError; rollback: $($rollbackErrors -join '; ')"
    }
    throw "Deployment failed; all selected runtimes were restored: $deploymentError"
}

Write-Host 'All deployed EndlessShapesUnlimited runtime folders match.'
Write-Host "version=$($versions[0])"
Write-Host "assembly_version=$($assemblyVersions[0])"
Write-Host "dll_sha256=$($hashes[0])"
