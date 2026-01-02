# Virtual Desktop VRCFaceTracking Module

This is a [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) module that enables face, eye, and tongue tracking for Quest Pro and other compatible headsets when using [Virtual Desktop](https://www.vrdesktop.net).

## Features

- **Eye Tracking**: Full gaze tracking and eye openness.
    - **Enhanced Logic**: Includes wink detection to keep eyes in sync during blinks and gaze synchronization to prevent jitter/cross-eyed looking.
- **Face Tracking**: Support for a wide range of facial expressions using Unified Expressions.
    - **Tuned Sensitivity**: All expression weights are multiplied by 1.5x to make them easier to trigger naturally.
- **Tongue Tracking**: Smart tongue movement support.
    - **Hybrid Control**: Vertical tongue movement is controlled by eyebrow height (ideal for Quest Pro).
    - **Anti-Clipping**: Jaw automatically opens when the tongue is extended.
- **High Performance**: Uses Memory Mapped Files for low-latency data transfer.
- **Custom Logging**: Detailed logs are saved to `Documents\VRCFaceTracking\CustomLibs\VirtualDesktop\VD_Tracking.log` for easy troubleshooting.

## Requirements

- **Virtual Desktop Streamer**: Version 1.30 or greater.
- **VRCFaceTracking**: The latest version of the VRCFaceTracking application.
- **Hardware**: A headset supporting facial tracking (e.g., Quest Pro) connected via Virtual Desktop.

## Setup

1. Ensure **Virtual Desktop Streamer (v1.30 or later)** is installed and running on your PC.
2. In the Virtual Desktop **Streaming** tab, ensure **Forward tracking data** is enabled.
3. Install this module through the VRCFaceTracking module registry or manually by placing the DLL in the modules folder.
4. Launch a VR game or SteamVR.

## Technical Details

The module communicates with the Virtual Desktop Streamer using a Memory Mapped File named `VirtualDesktop.BodyState` and synchronizes updates via an Event Wait Handle named `VirtualDesktop.BodyStateEvent`. It maps the proprietary Virtual Desktop expression weights to the standard Unified Expressions used by VRCFT.