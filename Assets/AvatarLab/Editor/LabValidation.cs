using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarLab.Editor
{
    public static class LabValidation
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static void MustReject(Action action, string message)
        {
            bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, message);
        }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory("TestOutput");
                File.WriteAllText("TestOutput/commit.tmp", "new"); File.WriteAllText("TestOutput/commit.txt", "old");
                AvatarArchive.CommitFile("TestOutput/commit.tmp", "TestOutput/commit.txt");
                Assert(File.ReadAllText("TestOutput/commit.txt") == "new" && !File.Exists("TestOutput/commit.tmp"), "File update failed");
                File.WriteAllText("TestOutput/commit.tmp", "next");
                using (var locked = new FileStream("TestOutput/commit.txt", FileMode.Open, FileAccess.Read, FileShare.None))
                    MustReject(() => AvatarArchive.CommitFile("TestOutput/commit.tmp", "TestOutput/commit.txt"), "Locked destination accepted");
                Assert(File.ReadAllText("TestOutput/commit.txt") == "new" && File.ReadAllText("TestOutput/commit.tmp") == "next", "Failed update did not preserve files");
                var packet = LabSmokeTest.Packet("/avatar/parameters/JawOpen", "f", -.25f);
                var message = OscCodec.Decode(packet).Single(); Assert(message.address.EndsWith("JawOpen") && Math.Abs(message.Number() + .25f) < .00001f, "OSC float endian");
                Assert(OscCodec.Decode(LabSmokeTest.Packet("/bool", "T", null))[0].Number() == 1, "OSC true");
                Assert(OscCodec.Decode(LabSmokeTest.Packet("/bool", "F", null))[0].Number() == 0, "OSC false");
                for (int n = 0; n < packet.Length; n++) { int len = n; MustReject(() => OscCodec.Decode(packet.Take(len).ToArray()), "Truncated OSC accepted at " + n); }
                using (var memory = new MemoryStream())
                {
                    memory.Write(new byte[] { 35, 98, 117, 110, 100, 108, 101, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, 0, 16);
                    var length = BitConverter.GetBytes(packet.Length); if (BitConverter.IsLittleEndian) Array.Reverse(length);
                    memory.Write(length, 0, 4); memory.Write(packet, 0, packet.Length);
                    Assert(OscCodec.Decode(memory.ToArray()).Single().Number() == -.25f, "OSC bundle");
                }
                var manifest = new AvatarManifest { id = Guid.NewGuid().ToString("N"), name = "Validation", unityVersion = Application.unityVersion };
                var bytes = Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();
                string archive = "TestOutput/valid.alavatar";
                if (File.Exists(archive)) File.Delete(archive);
                AvatarArchive.Write(archive, manifest, bytes);
                var roundtrip = AvatarArchive.Read(archive, out var loaded);
                Assert(roundtrip.id == manifest.id && bytes.SequenceEqual(loaded), "Archive roundtrip");
                string corrupt = "TestOutput/corrupt.alavatar"; File.Copy(archive, corrupt, true);
                using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update))
                {
                    zip.GetEntry("avatar.bundle").Delete();
                    using (var stream = zip.CreateEntry("avatar.bundle").Open()) { bytes[0] = 99; stream.Write(bytes, 0, bytes.Length); }
                }
                MustReject(() => AvatarArchive.Read(corrupt, out _), "Corrupted bundle accepted");
                string traversal = "TestOutput/traversal.alavatar"; File.Copy(archive, traversal, true);
                using (var zip = ZipFile.Open(traversal, ZipArchiveMode.Update)) zip.CreateEntry("../outside.txt");
                MustReject(() => AvatarArchive.Read(traversal, out _), "Unexpected archive entries accepted");
                manifest.unityVersion = "invalid"; string incompatible = "TestOutput/incompatible.alavatar";
                if (File.Exists(incompatible)) File.Delete(incompatible); AvatarArchive.Write(incompatible, manifest, bytes);
                MustReject(() => AvatarArchive.Read(incompatible, out _), "Incompatible Unity accepted");
                // Verify exported fixture and preservation of its source controller/scene state.
                LabBuild.Prepare(); var source = LabBuild.CreateSample();
                var controller = source.GetComponent<AvatarDefinition>().fxController as AnimatorController;
                string controllerPath = AssetDatabase.GetAssetPath(controller);
                string before = File.ReadAllText(controllerPath);
                string exported = Path.GetFullPath("TestOutput/robot.alavatar");
                var result = AvatarExporter.Export(source, exported);
                Assert(source.transform.Find("Hat").gameObject.activeSelf, "Exporter changed source GameObject");
                Assert(File.ReadAllText(controllerPath) == before, "Exporter changed source controller");
                Assert(!AssetDatabase.IsValidFolder("Assets/AvatarLabExport"), "Export temporary folder leaked");
                Assert(result.notes.Any(x => x.Contains("Humanoid")), "Missing compatibility report");
                AvatarArchive.Read(exported, out var bundleBytes);
                var bundle = AssetBundle.LoadFromMemory(bundleBytes);
                Assert(bundle != null, "Exported bundle invalid");
                var prefab = bundle.LoadAsset<GameObject>(result.prefab);
                var definition = prefab.GetComponent<AvatarDefinition>();
                Assert(definition != null && definition.fxController != null && definition.controls.Count == 2, "Avatar settings did not survive export");
                Assert(definition.faceBindings.Count == 1, "Face binding did not survive export");
                Assert(!prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(x => x != null && x.GetType().Assembly.GetName().Name != "AvatarLab.Core"), "Unexpected runtime script in bundle");
                bundle.Unload(true); UnityEngine.Object.DestroyImmediate(source);
                File.WriteAllText("TestOutput/editor-test-result.txt", "PASS\nOSC float/bool/bundle decoding; all truncated packet lengths rejected; archive roundtrip; hash corruption rejected; unexpected entries rejected; Unity mismatch rejected; source avatar and controller unchanged; temporary folder cleaned; exported settings, controller, menu and face binding preserved; no external runtime MonoBehaviours.\n");
                Debug.Log("AVATARLAB_EDITOR_TESTS_PASS"); EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); File.WriteAllText("TestOutput/editor-test-result.txt", "FAIL\n" + ex); EditorApplication.Exit(1); }
        }
    }
}
