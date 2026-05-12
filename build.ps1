param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$BuildInstaller,
    [string]$InstallerVersion,
    [switch]$NoVersionInInstallerName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$solutionPath = Join-Path $projectRoot "SystemSquire.sln"
$projectPath = Join-Path $projectRoot "SystemSquire\SystemSquire.csproj"
$dummyProjectPath = Join-Path $projectRoot "SystemSquireDummyWindow\SystemSquireDummyWindow.csproj"
$installerScriptPath = Join-Path $projectRoot "installer\SystemSquire.iss"
$installerOutputPath = Join-Path $projectRoot "installer\output"
$selfContained = -not $FrameworkDependent

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Dotnet {
    param(
        [string]$Command,
        [string[]]$Arguments
    )

    & dotnet $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $Command failed with exit code $LASTEXITCODE"
    }
}

function Get-InnoSetupCompilerPath {
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER) -and (Test-Path $env:INNO_SETUP_COMPILER)) {
        return $env:INNO_SETUP_COMPILER
    }

    $isccFromPath = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($isccFromPath -and -not [string]::IsNullOrWhiteSpace($isccFromPath.Source)) {
        return $isccFromPath.Source
    }

    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Get-ResolvedInstallerVersion {
    param([string]$ProjectFilePath)

    if (-not [string]::IsNullOrWhiteSpace($InstallerVersion)) {
        return $InstallerVersion
    }

    try {
        [xml]$projectXml = Get-Content -Path $ProjectFilePath -Raw
        $candidateNodes = @(
            $projectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='Version']"),
            $projectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='AssemblyVersion']"),
            $projectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='FileVersion']")
        )

        foreach ($node in $candidateNodes) {
            if ($null -eq $node) {
                continue
            }

            $version = $node.InnerText.Trim()
            if (-not [string]::IsNullOrWhiteSpace($version)) {
                return $version
            }
        }
    }
    catch {
        Write-Host "Could not parse project version metadata. Using fallback version 1.1.0." -ForegroundColor Yellow
    }

    return "1.1.0"
}

function Get-InstallerOutputBaseName {
    param(
        [string]$Version,
        [bool]$UseUnversionedName
    )

    $defaultBaseName = "SystemSquireSetup"
    if ($UseUnversionedName) {
        return $defaultBaseName
    }

    $safeVersion = [string]$Version
    $safeVersion = $safeVersion.Trim()
    if ([string]::IsNullOrWhiteSpace($safeVersion)) {
        return $defaultBaseName
    }

    $safeVersion = $safeVersion -replace "[^0-9A-Za-z._-]", "-"
    $safeVersion = $safeVersion.Trim("-")
    if ([string]::IsNullOrWhiteSpace($safeVersion)) {
        return $defaultBaseName
    }

    return "$defaultBaseName-$safeVersion"
}

function Get-ResolvedTargetFramework {
    param([string]$ProjectFilePath)

    try {
        [xml]$projectXml = Get-Content -Path $ProjectFilePath -Raw
        $targetFrameworkNode = $projectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='TargetFramework']")
        if ($null -ne $targetFrameworkNode) {
            $targetFramework = $targetFrameworkNode.InnerText.Trim()
            if (-not [string]::IsNullOrWhiteSpace($targetFramework)) {
                return $targetFramework
            }
        }
    }
    catch {
        Write-Host "Could not parse target framework metadata. Using fallback net8.0-windows." -ForegroundColor Yellow
    }

    return "net8.0-windows"
}

if (-not (Test-Path $solutionPath)) {
    throw "Solution not found at $solutionPath"
}

if (-not (Test-Path $projectPath)) {
    throw "Project not found at $projectPath"
}

if (-not (Test-Path $dummyProjectPath)) {
    throw "Dummy window project not found at $dummyProjectPath"
}

if ($selfContained -and [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    throw "RuntimeIdentifier is required for self-contained publish."
}

try {
    Write-Step "Pre-build checks"

    $resolvedBuildVersion = Get-ResolvedInstallerVersion -ProjectFilePath $projectPath

    $projectDirectory = Split-Path -Path $projectPath -Parent
    $targetFramework = Get-ResolvedTargetFramework -ProjectFilePath $projectPath
    $publishRootPath = Join-Path (Join-Path (Join-Path $projectDirectory "bin") $Configuration) $targetFramework
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishRootPath = Join-Path $publishRootPath $RuntimeIdentifier
    }

    $publishRootPath = Join-Path $publishRootPath "publish"
    $publishPath = $publishRootPath

    $restoreArgs = @($solutionPath)
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $restoreArgs += @("--runtime", $RuntimeIdentifier)
    }

    Write-Step "Restore"
    Invoke-Dotnet -Command "restore" -Arguments $restoreArgs

    Write-Step "Prepare publish output"
    if (Test-Path $publishRootPath) {
        Remove-Item $publishRootPath -Recurse -Force
    }

    $publishArgs = @(
        $projectPath,
        "--configuration", $Configuration,
        "--no-restore",
        "/p:Version=$resolvedBuildVersion"
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishArgs += @("--runtime", $RuntimeIdentifier)
    }

    $publishArgs += @("--output", $publishPath)

    if ($selfContained) {
        $publishArgs += @("--self-contained", "true")
    }
    else {
        $publishArgs += @("--self-contained", "false")
    }

    $publishMode = if ($selfContained) { "self-contained" } else { "framework-dependent" }
    Write-Step "Publish ($Configuration, $RuntimeIdentifier, $publishMode, version $resolvedBuildVersion)"
    Invoke-Dotnet -Command "publish" -Arguments $publishArgs

    $dummyPublishPath = Join-Path $publishRootPath "_dummy-window"
    if (Test-Path $dummyPublishPath) {
        Remove-Item $dummyPublishPath -Recurse -Force
    }

    $dummyPublishArgs = @(
        $dummyProjectPath,
        "--configuration", $Configuration,
        "--no-restore",
        "/p:Version=$resolvedBuildVersion",
        "--runtime", $RuntimeIdentifier,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:PublishTrimmed=false",
        "--output", $dummyPublishPath
    )

    Write-Step "Publish dummy blackout window executable"
    Invoke-Dotnet -Command "publish" -Arguments $dummyPublishArgs

    $dummySourcePath = Join-Path $dummyPublishPath "SystemSquireDummyWindow.exe"
    if (-not (Test-Path $dummySourcePath)) {
        throw "Dummy window executable was not produced at $dummySourcePath"
    }

    $toolsOutputPath = Join-Path $publishPath "Tools"
    New-Item -ItemType Directory -Path $toolsOutputPath -Force | Out-Null
    $dummyDestinationPath = Join-Path $toolsOutputPath "SystemSquireDummyWindow.exe"
    Copy-Item -Path $dummySourcePath -Destination $dummyDestinationPath -Force

    Remove-Item $dummyPublishPath -Recurse -Force

    Write-Host "Publish complete. Output folder: $publishPath" -ForegroundColor Green

    if ($BuildInstaller) {
        if (-not (Test-Path $installerScriptPath)) {
            throw "Installer script not found at $installerScriptPath"
        }

        $isccPath = Get-InnoSetupCompilerPath
        if ([string]::IsNullOrWhiteSpace($isccPath)) {
            throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER to the full ISCC.exe path."
        }

        $installerOutputBaseName = Get-InstallerOutputBaseName -Version $resolvedBuildVersion -UseUnversionedName $NoVersionInInstallerName.IsPresent

        Write-Step "Build installer (Inno Setup, version $resolvedBuildVersion, file $installerOutputBaseName.exe)"
        New-Item -ItemType Directory -Path $installerOutputPath -Force | Out-Null

        & $isccPath "/DAppVersion=$resolvedBuildVersion" "/DSourceDir=$publishPath" "/DOutputDir=$installerOutputPath" "/DOutputBaseFilename=$installerOutputBaseName" $installerScriptPath
        if ($LASTEXITCODE -ne 0) {
            throw "Installer build failed with exit code $LASTEXITCODE"
        }

        $installerExeName = "$installerOutputBaseName.exe"
        $installerExePath = Join-Path $installerOutputPath $installerExeName
        Write-Host "Installer complete. Output file: $installerExePath" -ForegroundColor Green
    }

}
catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
