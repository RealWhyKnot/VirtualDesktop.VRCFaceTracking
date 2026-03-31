# VirtualDesktop.VRCFaceTracking

A [VRCFaceTracking](https://store.steampowered.com/app/3329480/VRCFaceTracking/) module that streams face and eye tracking data from [Virtual Desktop](https://www.vrdesktop.net/) directly into VRChat's Unified Expression system. Includes built-in continuous calibration that adapts to your face automatically — no manual calibration sessions required.

## Requirements

| Requirement | Notes |
|---|---|
| Meta Quest Pro, Quest 3, or Quest 3S | Must have face/eye tracking enabled in headset settings |
| [Virtual Desktop](https://www.vrdesktop.net/) (paid) | Streamer v1.30 or later on your PC |
| [VRCFaceTracking](https://store.steampowered.com/app/3329480/) | Free on Steam |
| A VRChat avatar with Unified Expressions | Most modern face-tracking avatars support this |

---

## Installation

### Step 1 — Download

Go to the [Releases](https://github.com/RealWhyKnot/VirtualDesktop.VRCFaceTracking/releases/latest) page and download `VirtualDesktop.VRCFaceTracking.zip` from the latest release.

### Step 2 — Copy the DLL

1. Extract the ZIP file
2. Open your VRCFaceTracking CustomLibs folder. The easiest way is to press **Win + R** and paste:
   ```
   %APPDATA%\VRCFaceTracking\CustomLibs
   ```
3. Copy `VirtualDesktop.FaceTracking.dll` into that folder
4. Create the `CustomLibs` folder if it does not exist

### Step 3 — Configure Virtual Desktop

1. Make sure **Virtual Desktop Streamer v1.30 or later** is installed on your PC
2. Open the Streamer app and go to the **Streaming** tab
3. Enable **Forward tracking data**

### Step 4 — Launch

1. Start **VRCFaceTracking**
2. The *Virtual Desktop* module will appear and activate automatically once you connect your headset through Virtual Desktop
3. Launch VRChat — your avatar will now respond to your face

---

## Calibration

This module runs continuous automatic calibration. You do not need to hold a neutral face at startup or press any buttons.

**What it does:**
- Learns each expression's resting baseline within the first few seconds and removes it, so your neutral face produces zero output
- Applies a gentle gain curve that amplifies subtle expressions without making strong ones look exaggerated
- Dead zones around the resting baseline prevent sensor noise from causing micro-movements on your avatar

**What to expect at startup:**
- Tracking quality improves over the first 10–30 seconds as the calibration settles
- If your mouth appears very slightly open right after loading, it will close on its own as the baseline is established

---

## Troubleshooting

**Module does not appear in VRCFaceTracking**
- Confirm the DLL is directly inside `%APPDATA%\VRCFaceTracking\CustomLibs\`, not in a subfolder
- Restart VRCFaceTracking after copying the file

**"Failed to open MemoryMappedFile" error**
- The Virtual Desktop Streamer (PC app) must be running — not just the Quest app
- Confirm *Forward tracking data* is enabled in the Streamer's Streaming tab
- Make sure you are actively connected to your PC via Virtual Desktop

**Tracking is not active / avatar face is frozen**
- Verify your headset supports face/eye tracking and that it is enabled in the headset's settings menu
- You must be inside a VR game or have SteamVR running — Virtual Desktop only forwards tracking data when in an active VR session

**Mouth is slightly open at rest**
- Wait 30–60 seconds for the calibration floor to settle to your resting baseline
- Making a few exaggerated expressions and returning to neutral speeds up calibration

---

## Building from Source

### Prerequisites

- [.NET 7.0 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) or later
- VRCFaceTracking installed (provides `VRCFaceTracking.Core.dll` via the `3rdParty/` folder)
- Git

### First-time setup

Clone the repo, then run the build script once to set up git hooks:

```powershell
git clone https://github.com/RealWhyKnot/VirtualDesktop.VRCFaceTracking.git
cd VirtualDesktop.VRCFaceTracking
.\build.ps1
```

Running `build.ps1` automatically:
- Sets `core.hooksPath` so the commit hook activates
- Stamps the next `YYYY.MM.DD.N` version into `.csproj` and `module.json`
- Stops VRCFaceTracking, builds, deploys the DLL, and restarts via Steam

Use `-NoDeploy` to build without touching VRCFaceTracking:

```powershell
.\build.ps1 -NoDeploy
```

### Commit hook

After running `build.ps1` once, every commit automatically gets the current version appended:

```
Fix eye wide sync (v2026.03.30.0)
```

### Publishing a release

Push a version tag and GitHub Actions will build and publish a release automatically:

```bash
git tag v2026.03.30.0
git push origin v2026.03.30.0
```

The release will appear on the [Releases](https://github.com/RealWhyKnot/VirtualDesktop.VRCFaceTracking/releases) page within a few minutes with an auto-generated changelog.

---

## Credits

Expression mapping based on work by [regzo2/VRCFaceTracking-QuestProOpenXR](https://github.com/regzo2/VRCFaceTracking-QuestProOpenXR).
Original module by [Virtual Desktop, Inc.](https://www.vrdesktop.net/)
