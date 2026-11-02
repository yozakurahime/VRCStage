# Third-party notices

The root MIT license covers original VRCStage code and documentation only. The following components retain their own terms and copyright notices. License/source files listed below are shipped in source and copied into runtime releases.

| Component | Source and provenance | License / local notice |
| --- | --- | --- |
| Unity Standard Assets Characters motion clips | [Unity-Technologies/Standard-Assets-Characters](https://github.com/Unity-Technologies/Standard-Assets-Characters), revision `8f185aa3c782d824976653dd6a6476312daad241` | Unity Companion License; `Assets/ThirdParty/UnityCharacterMotion/LICENSE.md`, `Licencas/Unity-Companion-License.txt`, `SOURCE.txt` |
| Ultraleap LeapC binding and Windows library | [ultraleap/UnityPlugin](https://github.com/ultraleap/UnityPlugin), 7.3.0 revision `08ea531c78d70364ae13d3a02467bbcb6404d582` | Apache-2.0; `Assets/ThirdParty/Ultraleap/LICENSE.txt`, `SOURCE.txt` |
| OpenVR binding/native library | [ValveSoftware/openvr](https://github.com/ValveSoftware/openvr), revision `0924064316de3effbcd1acf1e309182a2deb1c05` | BSD-3-Clause; `Assets/ThirdParty/OpenVR/LICENSE.txt`, `SOURCE.txt` |
| KlakSpout | [keijiro/KlakSpout](https://github.com/keijiro/KlakSpout), 2.0.6 revision `849e7bca3c167839ed697796153e1749acf0c53f` | MIT; `Packages/jp.keijiro.klak.spout/LICENSE` |
| Face Motion v3 Python receiver | [emoto-yasushi/iFacialMocap_Facemotion3d_udp-tcp_v3](https://github.com/emoto-yasushi/iFacialMocap_Facemotion3d_udp-tcp_v3), revision `82e50f9d0f9f19402d8d2772c9c517f65a0d7a36` | MIT; `Tools/HandTracking/IFACIAL-V3-LICENSE.txt`, `IFACIAL-V3-SOURCE.txt` |
| VMC interoperability work | Jayo VMC plugin attribution | MIT; `Licencas/JayoVMCPlugin-LICENSE.txt` |
| MediaPipe task models | Google MediaPipe hand float16 v1 and pose lite float16 v1 | Apache-2.0; `Tools/HandTracking/MEDIAPIPE-LICENSE.txt`, `MODEL-SOURCE.txt` includes download URLs and hashes |

Unity packages resolved by the package manager are subject to their accompanying terms. Using Unity requires a valid Unity engine license. The Unity Companion content is restricted to Unity-dependent use and is not being offered under MIT or as engine-independent motion content.

Optional Python bridge builds collect notices for their installed distributions, including MediaPipe, OpenCV, NumPy, Python and PyInstaller. Their terms are separate; consult each `Licencas` directory in a binary release. PyInstaller's licensing exception for generated applications does not relicense its sources.

RGBMoCap is an optional external application, not vendored here. Its upstream code and the different AI weights/body models have separate terms, including possible noncommercial restrictions. No model access or redistribution rights are granted by VRCStage.

GoGo Loco poses, commercial avatars, clothing, shaders and worlds are not included in the public repository or release. Any local export must respect its creator's terms. Runtime use of an avatar does not transfer rights to publish its meshes, textures, animations or bundles.

VRChat, Unity, OBS, Ultraleap, iFacialMocap and other names identify interoperability targets; their names do not imply endorsement or affiliation.
