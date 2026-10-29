# VirtualDesktop.VRCFaceTracking
VRCFaceTracking module for Virtual Desktop

This is my fork of [guygodin/VirtualDesktop.VRCFaceTracking](https://github.com/guygodin/VirtualDesktop.VRCFaceTracking). It keeps the eyelids moving when the headset loses gaze tracking and fixes eye gaze angles that came out short.

Upstream reads eye openness only while the eye-following blendshapes are valid. When a blink lost the pupils, the lids froze until gaze came back. In this fork openness comes from the face model whenever the face is tracked, and each gaze dropout writes a debug log line.

Upstream also sent gaze angles in radians, and VRCFaceTracking reads gaze as the tangent of the angle. The eyes fell short the further you looked: 30° to the side showed as about 27.6°, and 40° as about 35°.

Builds are on the Releases page. MIT licensed, like upstream.
