param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$BuildInstaller,
    [string]$InstallerVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$solutionPath = Join-Path $projectRoot "SystemSquire.sln"
$projectPath = Join-Path $projectRoot "SystemSquire\SystemSquire.csproj"
$installerScriptPath = Join-Path $projectRoot "installer\SystemSquire.iss"
$installerOutputPath = Join-Path $projectRoot "installer\output"
$selfContained = -not $FrameworkDependent

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-SystemSquireProcesses {
    $byName = Get-Process -Name "System Squire", "SystemSquire" -ErrorAction SilentlyContinue
    return $byName | Sort-Object Id -Unique
}

function Ensure-AppNotRunning {
    $runningProcesses = @(Get-SystemSquireProcesses)
    if ($runningProcesses.Count -eq 0) {
        return
    }

    Write-Host "System Squire is currently running:" -ForegroundColor Yellow
    foreach ($proc in $runningProcesses) {
        Write-Host (" - {0} (PID {1})" -f $proc.ProcessName, $proc.Id) -ForegroundColor Yellow
    }

    $answer = Read-Host "Stop running instance(s) and continue build? [Y/n]"
    if ($answer -match "^(n|no)$") {
        throw "Build cancelled because the app is running."
    }

    $runningProcesses | Stop-Process -Force
    Wait-Process -Id ($runningProcesses | ForEach-Object { $_.Id }) -ErrorAction SilentlyContinue
    Write-Host "Stopped running instance(s)." -ForegroundColor Green
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

if ($selfContained -and [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    throw "RuntimeIdentifier is required for self-contained publish."
}

try {
    Write-Step "Pre-build checks"
    Ensure-AppNotRunning

    $projectDirectory = Split-Path -Path $projectPath -Parent
    $targetFramework = Get-ResolvedTargetFramework -ProjectFilePath $projectPath
    $publishPath = Join-Path (Join-Path (Join-Path $projectDirectory "bin") $Configuration) $targetFramework
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishPath = Join-Path $publishPath $RuntimeIdentifier
    }

    $publishPath = Join-Path $publishPath "publish"

    $restoreArgs = @($solutionPath)
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $restoreArgs += @("--runtime", $RuntimeIdentifier)
    }

    Write-Step "Restore"
    Invoke-Dotnet -Command "restore" -Arguments $restoreArgs

    Write-Step "Prepare publish output"
    if (Test-Path $publishPath) {
        Remove-Item $publishPath -Recurse -Force
    }

    $publishArgs = @(
        $projectPath,
        "--configuration", $Configuration,
        "--no-restore"
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishArgs += @("--runtime", $RuntimeIdentifier)
    }

    if ($selfContained) {
        $publishArgs += @("--self-contained", "true")
    }
    else {
        $publishArgs += @("--self-contained", "false")
    }

    $publishMode = if ($selfContained) { "self-contained" } else { "framework-dependent" }
    Write-Step "Publish ($Configuration, $RuntimeIdentifier, $publishMode)"
    Invoke-Dotnet -Command "publish" -Arguments $publishArgs

    Write-Host "Publish complete. Output folder: $publishPath" -ForegroundColor Green

    if ($BuildInstaller) {
        if (-not (Test-Path $installerScriptPath)) {
            throw "Installer script not found at $installerScriptPath"
        }

        $isccPath = Get-InnoSetupCompilerPath
        if ([string]::IsNullOrWhiteSpace($isccPath)) {
            throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER to the full ISCC.exe path."
        }

        $resolvedVersion = Get-ResolvedInstallerVersion -ProjectFilePath $projectPath

        Write-Step "Build installer (Inno Setup, version $resolvedVersion)"
        New-Item -ItemType Directory -Path $installerOutputPath -Force | Out-Null

        & $isccPath "/DAppVersion=$resolvedVersion" "/DSourceDir=$publishPath" "/DOutputDir=$installerOutputPath" $installerScriptPath
        if ($LASTEXITCODE -ne 0) {
            throw "Installer build failed with exit code $LASTEXITCODE"
        }

        $installerExeName = "SystemSquireSetup.exe"
        $installerExePath = Join-Path $installerOutputPath $installerExeName
        Write-Host "Installer complete. Output file: $installerExePath" -ForegroundColor Green
    }

}
catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
