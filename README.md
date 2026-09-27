# VirtualDesktop.VRCFaceTracking
VRCFaceTracking module for Virtual Desktop

This is my fork of [guygodin/VirtualDesktop.VRCFaceTracking](https://github.com/guygodin/VirtualDesktop.VRCFaceTracking). It keeps the eyelids moving when the headset loses gaze tracking. Upstream only reads eye openness while the eye-following blendshapes are valid, so a blink that lost the pupils left the lids frozen until gaze came back. Openness now comes from the face model whenever the face is tracked, and a debug log line records each gaze dropout.

Builds are published on the Releases page. MIT licensed, same as upstream.
