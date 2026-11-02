using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace AvatarLab
{
    public sealed class StudioSmokeTest : MonoBehaviour
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); bool graphics = args.Contains("--studio-video-test");
            if (!args.Contains("--studio-smoke-test") && !graphics) yield break;
            var app = FindObjectOfType<LabApp>(); float deadline = Time.realtimeSinceStartup + 45;
            while (app.currentAvatar == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (app.currentAvatar == null) { Finish(false, "Avatar did not load: " + app.Status); yield break; }
            yield return new WaitForSeconds(.3f);
            try
            {
                app.stage.AddPrimitive(PrimitiveType.Cube); app.stage.Selected.position = new Vector3(1, 1, 0); app.stage.Selected.localScale = new Vector3(.2f, 2, 2); Physics.SyncTransforms();
                var flags = app.motor.Move(Vector3.right * 2);
                Check(app.motor.Root.position.x < .8f && (flags & CollisionFlags.Sides) != 0, "CharacterController crossed a wall.");
                app.motor.Teleport(Vector3.zero, Quaternion.identity);
                var shot = new CameraShot { name = "Validation path", mode = 3, lookAt = false, loop = false, duration = 4 };
                shot.keys.Add(new CameraKeyframe { position = new Vector3(0, 1, 3), euler = new Vector3(0, 180, 0), fov = 40 });
                shot.keys.Add(new CameraKeyframe { position = new Vector3(3, 2, 0), euler = new Vector3(0, 270, 0), fov = 70 });
                app.cinema.shots.Add(shot); app.cinema.selected = app.cinema.shots.Count - 1; app.cinema.Evaluate(4);
                Check(Vector3.Distance(app.cinema.Camera.transform.position, shot.keys[1].position) < .001f && Mathf.Abs(app.cinema.Camera.fieldOfView - 70) < .01f, "Cinematic path endpoint/lens mismatch.");
                app.cinema.Evaluate(2); Check(Vector3.Distance(app.cinema.Camera.transform.position, new Vector3(1.5f, 1.5f, 1.5f)) < .001f, "Cinematic path interpolation mismatch.");
                app.stage.Save(Path.Combine(Application.persistentDataPath, "Studios", "Validation-studio.json"));
            }
            catch (Exception ex) { Finish(false, ex.ToString()); yield break; }
            app.stage.Clear(); yield return app.stage.Load(Path.Combine(Application.persistentDataPath, "Studios", "Validation-studio.json"));
            if (app.stage.ObjectCount != 1 || app.cinema.shots.Last().keys.Count != 2) { Finish(false, "Studio persistence mismatch."); yield break; }
            app.faces.profile.enabled = true;
            var facial = IfacialCodec.Decode("jawOpen&65|eyeBlink_L&0|eyeBlink_R&0|"); app.faces.Inject(facial);
            yield return new WaitForSeconds(.35f);
            try
            {
                var jaw = app.currentAvatar.transform.Find("Jaw").GetComponent<SkinnedMeshRenderer>();
                Check(Mathf.Abs(jaw.GetBlendShapeWeight(0) - 65) < 4, "iFacial-to-blendshape retargeting failed: " + jaw.GetBlendShapeWeight(0));
                app.faces.SaveProfile(); app.faces.LoadProfile(); Check(app.faces.profile.routes.Count > 0, "Face profile persistence failed.");
            }
            catch (Exception ex) { Finish(false, ex.ToString()); yield break; }
            var portProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); int port = ((IPEndPoint)portProbe.Client.LocalEndPoint).Port; portProbe.Close();
            app.faces.ifacial.Start("", port);
            using (var sender = new UdpClient()) { byte[] bytes = Encoding.UTF8.GetBytes("jawOpen&80|eyeBlink_L&0|"); sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port)); }
            yield return new WaitForSeconds(.35f);
            if (app.faces.ifacial.Received < 1) { Finish(false, "iFacial UDP receiver failed."); yield break; }
            app.faces.ifacial.Dispose();
            if (!graphics) { Finish(true, "Avatar load; physical capsule/wall collision; cinematic path/lens interpolation; studio scene save/load; facial mapping and profile persistence; actual iFacial UDP receive."); yield break; }
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            app.stage.Clear(); app.cinema.selected = 0; app.cinema.playhead = 0; app.video.StartOutput(false);
            var receiver = gameObject.AddComponent<Klak.Spout.SpoutReceiver>(); receiver.SetResources(app.spoutResources); receiver.sourceName = app.video.SpoutName;
            yield return new WaitForSeconds(3);
            try
            {
                Check(app.video.Running && app.video.Frames > 5, "Clean video frame publication failed: " + app.video.Status);
                Check(Klak.Spout.SpoutManager.GetSourceNames().Contains(app.video.SpoutName), "Spout sender was not advertised.");
                Check(receiver.receivedTexture != null && receiver.receivedTexture.width == app.video.width, "Spout receiver did not receive the texture.");
                var prior = RenderTexture.active; var capture = new Texture2D(app.video.width, app.video.height, TextureFormat.RGB24, false);
                try { RenderTexture.active = app.video.Texture; capture.ReadPixels(new Rect(0, 0, app.video.width, app.video.height), 0, 0); capture.Apply(); File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), "studio-program-test.png"), capture.EncodeToPNG()); }
                finally { RenderTexture.active = prior; Destroy(capture); }
            }
            catch (Exception ex) { Finish(false, ex.ToString()); yield break; }
            receiver.enabled = false; Destroy(receiver); app.video.StopOutput();
            Finish(true, "Studio functional tests plus GPU-rendered clean video, shared-memory publication, native Spout advertisement and texture received by a Spout receiver.");
        }
        static void Finish(bool pass, string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "studio-smoke-result.txt"), (pass ? "PASS\n" : "FAIL\n") + detail);
            Debug.Log("AVATARLAB_STUDIO_" + (pass ? "PASS " : "FAIL ") + detail); Application.Quit(pass ? 0 : 1);
        }
    }
}
