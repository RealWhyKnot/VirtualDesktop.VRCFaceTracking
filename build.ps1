# build.ps1 - Build, version, and deploy VirtualDesktop.VRCFaceTracking
param(
    [switch]$NoDeploy  # Build only, skip stopping/copying/restarting VRCFaceTracking
)

$projectFile  = "VirtualDesktop.FaceTracking.csproj"
$outputDir    = "$env:APPDATA\VRCFaceTracking\CustomLibs"
$vrcftSteamId = "3329480"
$processNames = @("VRCFaceTracking", "VRCFaceTracking.ModuleProcess")

# ── Git hooks ────────────────────────────────────────────────────────────────
git config core.hooksPath .githooks 2>$null

# ── Auto-version (YYYY.MM.DD.N) ──────────────────────────────────────────────
$today = Get-Date -Format "yyyy.MM.dd"

# Pull latest tags so we don't collide with remote builds
git fetch --tags --quiet 2>$null

# Find the highest patch number already used today, then add 1
$existingTags = git tag --list 2>$null | Where-Object { $_ -like "v$today.*" }
$patches = $existingTags | ForEach-Object {
    $parts = $_ -split '\.'
    if ($parts.Length -eq 4) { [int]($parts[3]) }
} | Where-Object { $_ -ne $null }

$nextPatch = if ($patches) { ($patches | Measure-Object -Maximum).Maximum + 1 } else { 0 }
$version   = "$today.$nextPatch"

Write-Host "Version: $version"

# ── Stamp version into project files ─────────────────────────────────────────
$csproj = Get-Content $projectFile -Raw
$csproj = $csproj -replace '<Version>[^<]*</Version>',         "<Version>$version</Version>"
$csproj = $csproj -replace '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$version</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
[System.IO.File]::WriteAllText((Resolve-Path $projectFile), $csproj)

$moduleJson = Get-Content "module.json" -Raw
$moduleJson = $moduleJson -replace '"Version":\s*"[^"]*"', """Version"": ""$version"""
[System.IO.File]::WriteAllText((Resolve-Path "module.json"), $moduleJson)

Write-Host "Stamped v$version into $projectFile and module.json"

# ── Stop VRCFaceTracking (unless -NoDeploy) ───────────────────────────────────
if (-not $NoDeploy) {
    $stopped = $false
    foreach ($proc in $processNames) {
        if (Get-Process -Name $proc -ErrorAction SilentlyContinue) {
            Write-Host "Stopping $proc..."
            Stop-Process -Name $proc -Force
            $stopped = $true
        }
    }
    if ($stopped) {
        Write-Host "Waiting for processes to exit..."
        Start-Sleep -Seconds 2
    }
}

# ── Build ─────────────────────────────────────────────────────────────────────
Write-Host "Building in Release mode..."
dotnet build $projectFile -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

# ── Deploy (unless -NoDeploy) ─────────────────────────────────────────────────
if (-not $NoDeploy) {
    if (!(Test-Path $outputDir)) {
        New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    }

    $dll = Get-ChildItem -Path "bin\Release" -Filter "VirtualDesktop.FaceTracking.dll" -Recurse |
           Select-Object -First 1
    if (-not $dll) {
        Write-Error "Could not find compiled DLL in bin\Release."
        exit 1
    }

    Write-Host "Deploying to $outputDir..."
    Copy-Item $dll.FullName -Destination $outputDir -Force

    Write-Host "Starting VRCFaceTracking via Steam..."
    Start-Process "steam://rungameid/$vrcftSteamId"
}

Write-Host ""
Write-Host "Done. v$version"
Write-Host "To publish a release:  git tag v$version && git push origin v$version"
