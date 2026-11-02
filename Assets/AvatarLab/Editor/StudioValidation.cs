using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace AvatarLab.Editor
{
    public static class StudioValidation
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory("TestOutput");
                Check(FaceChannels.Canonical("eyeBlink_L") == "eyeBlinkLeft", "iFacial side alias");
                Check(FaceChannels.Canonical("v2/EyeClosedLeft") == "eyeBlinkLeft", "Unified blink alias");
                Check(FaceChannels.Canonical("Eye - Blink L") == "eyeBlinkLeft", "Avatar shape alias");
                Check(FaceChannels.Canonical("vrc.v_aa") == "vowelA", "VRChat vowel alias");
                var v2 = IfacialCodec.Decode("jawOpen&65|eyeBlink_L&100|eyeBlink_R&0|=head#-10,20,-30,0,0,0|");
                Check(Mathf.Abs(v2.values["jawOpen"] - .65f) < .001f && v2.values["eyeBlinkLeft"] == 1 && v2.hasHead && v2.head.y == 20, "iFacial v2 packet");
                var v1 = IfacialCodec.Decode("jawOpen-20|eyeBlink_R-80|");
                Check(Mathf.Abs(v1.values["jawOpen"] - .2f) < .001f && Mathf.Abs(v1.values["eyeBlinkRight"] - .8f) < .001f, "iFacial v1 packet");
                bool rejected = false; try { IfacialCodec.Decode("not a facial frame"); } catch (FormatException) { rejected = true; } Check(rejected, "Invalid iFacial packet accepted");
                var route = new FaceRoute { minimum = -1, maximum = 1, invert = true, gain = .8f };
                Check(Mathf.Abs(FaceStudio.EvaluateRoute(route, -.5f) - .6f) < .0001f, "Signed calibration route");
                var silence = VoiceFeatures.Extract(new float[1024], 16000, out float rms); Check(rms == 0 && silence.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), "Silent voice frame");
                var voice = new VowelProfile();
                for (int i = 0; i < 5; i++) voice.templates.Add(new VowelTemplate { vowel = "AEIOU"[i].ToString(), features = VoiceFeatures.Extract(SyntheticVowel(i, 120), 16000, out _) });
                var recognized = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    var features = VoiceFeatures.Extract(SyntheticVowel(i, 130), 16000, out _);
                    int index = VoiceFeatures.Classify(features, voice); Check(index == i, "Vowel holdout mismatch " + i + " -> " + index); recognized.Add(voice.templates[index].vowel);
                }
                using (var query = new OscQueryBridge())
                {
                    query.UpdateAvatar(Guid.NewGuid().ToString("N"), "Avi çã 漢字", new Dictionary<string, string> { ["EyeClosedLeft"] = "f", ["v2/JawOpen"] = "f", ["Test"] = "T" });
                    Check(query.Document("/avatar/parameters/v2/JawOpen").Contains("\"TYPE\":\"f\""), "Nested OSCQuery route");
                    query.Start(19003); Check(query.Running, "OSCQuery startup: " + query.Status);
                    var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + query.HttpPort + "/?HOST_INFO"); request.Timeout = 2000;
                    using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) Check(reader.ReadToEnd().Contains("19003"), "OSCQuery host input port");
                    request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + query.HttpPort + "/avatar"); request.Timeout = 2000;
                    using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string json = reader.ReadToEnd(); Check(json.Contains("avatarfullfacetracking") && json.Contains("EyeClosedLeft") && json.Contains("avtr_"), "OSCQuery avatar tree");
                    }
                }
                File.WriteAllText("TestOutput/quad.obj", "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf -4 -3 -2 -1\n");
                var obj = ObjLoader.Load("TestOutput/quad.obj", null); var mesh = obj.GetComponent<MeshFilter>().sharedMesh;
                Check(mesh.vertexCount == 4 && mesh.triangles.Length == 6 && obj.GetComponent<MeshCollider>() != null, "OBJ triangulation/negative indices");
                UnityEngine.Object.DestroyImmediate(obj);
                LabBuild.Prepare(); var stage = LabBuild.CreateStageExample();
                string stageFile = Path.GetFullPath("TestOutput/Studio.alworld"); StageExporter.Export(stage, false, true, stageFile);
                var manifest = StageArchive.Read(stageFile, out byte[] data); var bundle = AssetBundle.LoadFromMemory(data);
                Check(bundle != null, "Stage bundle invalid"); var prefab = bundle.LoadAsset<GameObject>(manifest.prefab); var definition = prefab.GetComponent<StageDefinition>();
                Check(definition != null && definition.kind == "world" && prefab.GetComponentsInChildren<Collider>(true).Length >= 3 && prefab.GetComponentsInChildren<Light>(true).Length > 0, "Stage geometry/lighting/colliders");
                Check(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(b => b is StageDefinition), "Unexpected scripts in stage");
                Check(stage != null && stage.GetComponentsInChildren<Collider>().Length >= 3, "Stage source changed");
                bundle.Unload(true); UnityEngine.Object.DestroyImmediate(stage);
                File.WriteAllText("TestOutput/studio-editor-result.txt", "PASS\nARKit/Unified/rig aliases; iFacial v1/v2 transport fixtures and malformed input; signed face calibration; silent audio; MFCC vowel holdouts with different fundamental frequency: " + string.Join(",", recognized) + "; nested OSCQuery HTTP service; OBJ polygon triangulation and negative indices; stage export/import with lighting and colliders and no external scripts.\nPhysical microphones/phones/trackers are not validated by these tests.\n");
                Debug.Log("AVATARLAB_STUDIO_EDITOR_PASS"); EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory("TestOutput"); File.WriteAllText("TestOutput/studio-editor-result.txt", "FAIL\n" + ex); Debug.LogException(ex); EditorApplication.Exit(1);
            }
        }
        public static float[] SyntheticVowel(int vowel, float fundamental)
        {
            float[,] formants = { { 800, 1200, 2500 }, { 450, 2200, 2900 }, { 300, 2500, 3100 }, { 500, 900, 2600 }, { 300, 700, 2300 } };
            var output = new float[1024];
            for (int harmonic = 1; harmonic * fundamental < 7500; harmonic++)
            {
                float frequency = fundamental * harmonic, amplitude = .003f / harmonic;
                for (int f = 0; f < 3; f++) amplitude += .1f * Mathf.Exp(-Mathf.Pow((frequency - formants[vowel, f]) / (f == 0 ? 100 : 150), 2));
                for (int i = 0; i < output.Length; i++) output[i] += amplitude * Mathf.Sin(2 * Mathf.PI * frequency * i / 16000);
            }
            return output;
        }
    }
}
