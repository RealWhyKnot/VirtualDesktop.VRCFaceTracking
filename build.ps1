# Build script for VirtualDesktop.VRCFaceTracking
# This script builds the project in Release mode and copies the output to the VRCFaceTracking custom modules directory.

$ErrorActionPreference = "Stop"

$vrcftPath = "$env:AppData\VRCFaceTracking\CustomLibs"
if (-not (Test-Path $vrcftPath)) {
    Write-Host "VRCFT CustomLibs directory not found at $vrcftPath. Creating it..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $vrcftPath -Force
}

Write-Host "Building project in Release mode..." -ForegroundColor Cyan
dotnet build -c Release

$targetDir = "bin\Release\net7.0-windows"
$dllName = "VirtualDesktop.FaceTracking.dll"
$jsonName = "module.json"

if (Test-Path "$targetDir\$dllName") {
    Write-Host "Copying $dllName to $vrcftPath..." -ForegroundColor Cyan
    Copy-Item "$targetDir\$dllName" "$vrcftPath" -Force
}

if (Test-Path $jsonName) {
    Write-Host "Copying $jsonName to $vrcftPath..." -ForegroundColor Cyan
    Copy-Item $jsonName "$vrcftPath" -Force
}

Write-Host "Build and deployment complete!" -ForegroundColor Green

