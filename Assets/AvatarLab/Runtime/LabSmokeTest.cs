using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace AvatarLab
{
    public sealed class LabSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            bool test = Array.IndexOf(args, "--smoke-test") >= 0;
            bool visual = Array.IndexOf(args, "--visual-test") >= 0;
            if (!test && !visual) yield break;
            var app = FindObjectOfType<LabApp>();
            float end = Time.realtimeSinceStartup + 40;
            while (app.currentAvatar == null && Time.realtimeSinceStartup < end) yield return null;
            if (app.currentAvatar == null) { Finish(false, "Import did not complete: " + app.Status); yield break; }
            yield return new WaitForSeconds(.5f);
            if (test)
            {
                try
                {
                    if (app.currentAvatar.transform.Find("Hat").gameObject.activeSelf) throw new Exception("Hat default must be off.");
                    if (!app.osc.Running) throw new Exception("OSC bind failed: " + app.osc.Error);
                    using (var sender = new UdpClient())
                    {
                        var packet = Packet("/avatar/parameters/HatVisible", "T", null); sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, 9000));
                        packet = Packet("/avatar/parameters/JawOpen", "f", .75f); sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, 9000));
                    }
                }
                catch (Exception ex) { Finish(false, ex.ToString()); yield break; }
                yield return new WaitForSeconds(.5f);
                try
                {
                    if (!app.currentAvatar.transform.Find("Hat").gameObject.activeSelf) throw new Exception("OSC bool did not drive animation.");
                    if (Mathf.Abs(app.currentAvatar.transform.Find("Jaw").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0) - 75) > 1) throw new Exception("OSC float did not drive face binding.");
                    if (Directory.GetFiles(app.LibraryPath, "*.alavatar").Length == 0) throw new Exception("Library persistence missing.");
                }
                catch (Exception ex) { Finish(false, ex.ToString()); yield break; }
                var previous = app.currentAvatar;
                var avatarIndex = Array.IndexOf(args, "--avatar");
                app.Import(args[avatarIndex + 1]);
                yield return new WaitForSeconds(.2f);
                if (app.currentAvatar != previous) { Finish(false, "Duplicate import replaced the active avatar."); yield break; }
                app.Import(Path.Combine(Path.GetDirectoryName(Application.dataPath), "does-not-exist.alavatar"));
                yield return new WaitForSeconds(.2f);
                if (app.currentAvatar != previous || app.driver == null) { Finish(false, "Failed import removed active avatar."); yield break; }
                Finish(true, "Exported AssetBundle loaded; default animation applied; UDP OSC bool toggled hat; UDP OSC float drove jaw to 75%; library persisted; duplicate import and failed import preserved the active avatar.");
            }
            else
            {
                app.driver.Set("HatVisible", 1); app.driver.Set("JawOpen", .5f);
                while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
                yield return new WaitForSeconds(2);
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "visual-test.png");
                ScreenCapture.CaptureScreenshot(path); yield return new WaitForSeconds(1);
                Debug.Log("AVATARLAB_VISUAL_TEST " + path); Application.Quit(0);
            }
        }
        public static byte[] Packet(string address, string tag, float? value)
        {
            using (var memory = new MemoryStream())
            {
                WriteString(memory, address); WriteString(memory, "," + tag);
                if (value.HasValue) { var bytes = BitConverter.GetBytes(value.Value); if (BitConverter.IsLittleEndian) Array.Reverse(bytes); memory.Write(bytes, 0, 4); }
                return memory.ToArray();
            }
        }
        static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value); stream.Write(bytes, 0, bytes.Length); stream.WriteByte(0);
            while (stream.Position % 4 != 0) stream.WriteByte(0);
        }
        static void Finish(bool success, string detail)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "smoke-test-result.txt"), (success ? "PASS\n" : "FAIL\n") + detail);
            Debug.Log("AVATARLAB_SMOKE_" + (success ? "PASS " : "FAIL ") + detail); Application.Quit(success ? 0 : 1);
        }
    }
}
