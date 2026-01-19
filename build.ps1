# build.ps1
$projectFile = "VirtualDesktop.FaceTracking.csproj"
$outputDir = "$env:APPDATA\VRCFaceTracking\CustomLibs"

# Ensure the output directory exists
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir
}

Write-Host "Building project in Release mode..."
dotnet build $projectFile -c Release

if ($LASTEXITCODE -eq 0) {
    $dllPath = Get-ChildItem -Path "bin\Release" -Filter "VirtualDesktop.FaceTracking.dll" -Recurse | Select-Object -First 1
    if ($dllPath) {
        Write-Host "Copying DLL to $outputDir..."
        Copy-Item $dllPath.FullName -Destination $outputDir -Force
        Write-Host "Build and copy successful!"
    } else {
        Write-Error "Could not find the compiled DLL in bin\Release."
    }
} else {
    Write-Error "Build failed."
}
