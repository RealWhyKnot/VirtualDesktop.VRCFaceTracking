# VirtualDesktop.VRCFaceTracking
VRCFaceTracking module for Virtual Desktop

This is my fork of [guygodin/VirtualDesktop.VRCFaceTracking](https://github.com/guygodin/VirtualDesktop.VRCFaceTracking). It keeps the eyelids moving when the headset loses gaze tracking.

Upstream reads eye openness only while the eye-following blendshapes are valid. When a blink lost the pupils, the lids froze until gaze came back. In this fork openness comes from the face model whenever the face is tracked, and each gaze dropout writes a debug log line.

Builds are on the Releases page. MIT licensed, like upstream.
