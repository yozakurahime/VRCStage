# VRCStage

**VTuber studio for VRChat avatars — no VRM conversion.**

[Português brasileiro](docs/README.pt-BR.md) · [Quick start](docs/QUICKSTART.md) · [Download Windows alpha](https://github.com/yozakurahime/VRCStage/releases) · [Roadmap](ROADMAP.md) · [Contribute](CONTRIBUTING.md)

VRCStage is a standalone Windows studio for creators who want to stream with their existing VRChat avatar. Export your avatar once from its Unity project, open it locally in VRCStage, and send a clean camera to OBS. You do not need to convert the avatar to VRM or keep Unity and VRChat running during a stream.

**Status: 0.10.0 alpha.** Previously developed as AvatarLab Offline. This is an independent project, not affiliated with VRChat Inc. It implements its own runtime; compatibility with the VRChat client is partial.

```text
Your avatar's Unity project
  → VRCStage local exporter → .alavatar
  → VRCStage: tracking + avatar controls + scene + camera
  → OBS: clean window / Spout → livestream or virtual camera
```

## Start here

1. Download and extract the Windows ZIP from **Releases**. Run `VRCStage.exe`.
2. Import `VRCStage-Exporter.unitypackage` into the Unity project that already contains your avatar. The tested editor is **Unity 2022.3.22f1**.
3. Select the avatar root and choose **Tools → VRCStage → Exportar avatar local**. Export a `.alavatar` file or send it to the local Inbox.
4. In VRCStage, use **Começar** to import the avatar, configure face/voice tracking, frame the camera, and open the output controls.
5. In OBS, capture the clean window or use a Spout receiver. The OBS virtual camera can share this result with other applications.

The exporter reads the existing avatar configuration automatically. Some features are approximated or unsupported; read its compatibility report. The source ZIP is for developers; it is not the Windows application.

## What is available

| Area | Current implementation |
| --- | --- |
| Avatars | Local `.alavatar` packages; expression menus/toggles; imported animation controllers/layers; Unity and exported VRChat constraints |
| Face and voice | iFacialMocap input; head/neck/eye rotations; ARKit/Unified Expressions mapping; microphone-driven mouth shapes and calibration |
| Hands and body | Ultraleap/Leap tabletop or headset-mounted; webcam bridge; OpenXR headset/controllers; SteamVR trackers and full-body calibration; optional VMC/OSC integrations |
| Studio | Local world/object packages; lighting, baked probe data, cinematic cameras, PNG photos; bloom, motion blur and other post effects |
| Streaming | Clean video window, native Spout, transparency, integration with an installed OBS virtual camera |
| Movement | Third-person desktop camera, mouse orbit/zoom, walk/run/jump/crouch, poses and local motion library |
| Physics | Custom secondary bone physics and colliders, including exported settings and optional test hair physics |

Hardware support depends on device drivers, the selected tracking runtime, avatar rig and calibration. An iPhone's ARKit input cannot supply every independent Unified Expressions signal; mapping does not create measurements the source does not provide. Webcam forearm motion and untracked joints are estimates.

## Current limits

- A one-time **Unity export is still required**. VRCStage does not load a raw Unity project or `.unitypackage` directly at runtime, download avatars from VRChat, or read the VRChat cache.
- Physics and some constraints use this project's implementations. They are not a redistribution of VRChat's PhysBone runtime and do not promise identical results.
- VRCFury/Modular Avatar output depends on the avatar build/export pipeline. Inspect the export report and test the result; arbitrary editor plugins do not run inside the player.
- Udon, VRChat networking, VRChat mirrors and world scripts are not a supported runtime.
- RGB video reconstruction uses an **optional external RGBMoCap installation**. Its models and terms are separate. GoGo Loco poses can be exported locally by their owner; no GoGo pack is bundled.
- The current UI is primarily Brazilian Portuguese. English UI/localization and a simpler setup wizard are on the roadmap.

## Build and contribute

Install Unity **2022.3.22f1** with Windows build support, Git, PowerShell and Python 3.10+ for optional bridges. Open this folder as a Unity project, or run:

```powershell
./Tools/Build.ps1
# Optional webcam / RGB adapters:
./Tools/BuildWebcam.ps1
./Tools/BuildRGBMotion.ps1
./Tools/Package.ps1
```

`LabBuild.Prepare` regenerates the laboratory scene, sample avatars and motion assets. Unity caches and generated assets are intentionally ignored. See [BUILD.md](docs/BUILD.md) for bridge models, validation and packaging.

Original VRCStage code is **MIT licensed**. Bundled third-party code, models and Unity animation assets retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Unity-dependent assets are not relicensed as MIT. This repository does not grant rights to avatars, outfits, commercial worlds or motion packs.

Issues and contributions are welcome. Please include reproduction steps and sanitized diagnostics, never an avatar or private tracking profile unless you own the rights and intentionally want it public.
