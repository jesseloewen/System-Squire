param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$solutionPath = Join-Path $projectRoot "SystemSquire.sln"
$buildOutputPath = Join-Path $projectRoot "SystemSquire\bin\$Configuration\net8.0-windows"
$distPath = Join-Path $projectRoot "dist"
$exeName = "System Squire.exe"
$distExePath = Join-Path $distPath $exeName

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

if (-not (Test-Path $solutionPath)) {
    throw "Solution not found at $solutionPath"
}

try {
    Write-Step "Pre-build checks"
    Ensure-AppNotRunning

    Write-Step "Restore"
    Invoke-Dotnet -Command "restore" -Arguments @($solutionPath)

    Write-Step "Build ($Configuration)"
    Invoke-Dotnet -Command "build" -Arguments @($solutionPath, "--configuration", $Configuration, "--no-restore")

    Write-Step "Package"
    if (Test-Path $distPath) {
        Remove-Item $distPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $distPath -Force | Out-Null

    if (-not (Test-Path $buildOutputPath)) {
        throw "Build output path not found: $buildOutputPath"
    }

    Copy-Item (Join-Path $buildOutputPath "*") $distPath -Recurse -Force

    Write-Host "Build complete. Output folder: $distPath" -ForegroundColor Green

    if (-not $NoRun) {
        if (-not (Test-Path $distExePath)) {
            throw "Built executable not found at $distExePath"
        }

        Write-Step "Run"
        Start-Process -FilePath $distExePath -WorkingDirectory $distPath
        Write-Host "Launched $exeName" -ForegroundColor Green
    }
}
catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
