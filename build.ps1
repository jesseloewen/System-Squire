# Build and Package Script for System Squire
# Builds both projects and copies them to a dist folder for distribution

Write-Host "Building System Squire..." -ForegroundColor Cyan

# Clean previous build
if (Test-Path "dist") {
    Remove-Item -Recurse -Force "dist"
}

# Build solution in Release mode
dotnet build SystemSquire.sln --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Create distribution folder
New-Item -ItemType Directory -Path "dist" -Force | Out-Null

# Copy main application
Write-Host "Packaging System Squire..." -ForegroundColor Cyan
Copy-Item "SystemSquire\bin\Release\net8.0-windows\*" "dist\" -Recurse -Force

# Copy Dummy.exe to dist
Write-Host "Packaging Dummy..." -ForegroundColor Cyan
Copy-Item "DummyWindow\bin\Release\net8.0-windows\Dummy.exe" "dist\" -Force
Copy-Item "DummyWindow\bin\Release\net8.0-windows\Dummy.dll" "dist\" -Force
Copy-Item "DummyWindow\bin\Release\net8.0-windows\Dummy.runtimeconfig.json" "dist\" -Force

Write-Host "`nBuild complete! Files are in the 'dist' folder." -ForegroundColor Green
Write-Host "Run: .\dist\System Squire.exe" -ForegroundColor Yellow
