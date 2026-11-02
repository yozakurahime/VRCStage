# First stream

## Export your avatar

Use a copy or version-controlled checkout of your existing avatar's Unity project. Install `VRCStage-Exporter.unitypackage` from the Windows release. Select the avatar's root GameObject and use **Tools → VRCStage → Exportar avatar local**. Review the compatibility report, then save the `.alavatar` package. Optional Base/FX overrides are for advanced users; the exporter normally reads the avatar descriptor automatically.

Exporting requires Unity and the dependencies already used by that avatar, including its shaders and editor build tools. The standalone player does not import `.fbx`, raw avatar folders or `.unitypackage` files as fully configured avatars. No VRChat upload is performed.

## Set up VRCStage

Open `VRCStage.exe` and **Começar**. Import the exported package and configure the inputs you own:

- **Face / voz:** choose microphone/lip sync or iFacialMocap. For the phone, use the PC's current LAN IP and keep the phone in tracking mode. A running receiver is not evidence of tracking: confirm that valid frame counters increase. Recalibrate the neutral head/eyes as needed.
- **Mãos / webcam:** select Leap/Ultraleap or the optional webcam bridge. Tabletop Leap needs the correct desk position and orientation. Use the guide pose and refine the sensor position; the arm solver cannot measure your torso from a hand sensor alone.
- **VR / corpo:** use your headset's active OpenXR runtime, then discover and calibrate supported SteamVR trackers. Start with headset/controllers before adding full body.

For iPhone input, UDP 49983 must reach the program. If you need a Windows Firewall rule, `Tools/AllowIfacial.ps1` supports a single phone IPv4 or the local subnet, restricted to that executable and UDP port. The script requires an administrator terminal and does not run automatically. Local subnet access tolerates a changing phone IP; use it only on a network you trust.

## Frame and output

Use **Enquadrar meio corpo**, then **Luz / foto** or **Cinema** to adjust the camera. **F3** switches the main view between the editing camera and output camera. **Transmissão** controls resolution, frame rate, background, alpha and Spout. Begin with 1280×720 at 30 FPS and enable additional effects as needed.

For OBS window/game capture, select **AvatarLab VIDEO - OBS**, the compatibility name of the clean output window. For a Spout receiver, select **AvatarLab Studio**. Hide the editing application from the final scene. Transparency depends on the selected output/capture method. The optional OBS virtual camera shares the OBS result; VRCStage does not install a camera driver itself. A local output is not an internet broadcast.

Save the scene in **Cenário → Salvar estúdio**. Face settings and Leap desk calibration have their own per-avatar save controls. Settings remain under `%USERPROFILE%\AppData\LocalLow\AvatarLab\AvatarLab Offline` after the rename.

## When something does not move

Confirm input counters first, then check whether application of tracking is enabled and whether another source owns the same bones. In **Poses / clipes**, **Fixar corpo e mãos durante a prévia** blocks live body/hand tracking deliberately. Face mapping depends on actual target blendshapes, usable animation parameters and signal availability; some Unified Expressions channels have no independent ARKit source.

For physics, controllers, constraints and shaders, read the export report. Differences from VRChat are expected. Open an issue with steps, versions and sanitized diagnostics; do not attach commercial avatar/world packages.
