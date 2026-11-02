using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using LeapInternal;
using UnityEngine;

namespace AvatarLab
{
    public sealed class HandSmokeTest : MonoBehaviour
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--hand-test") < 0) yield break;
            var app = FindObjectOfType<LabApp>(); float deadline = Time.realtimeSinceStartup+50;
            while (app.currentAvatar == null && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            OpticalFrame frame = null; Quaternion rest = Quaternion.identity; Transform finger = null;
            try
            {
                Check(app.driver != null && app.driver.Definition.animator.isHuman, "Test requires a Humanoid avatar.");
                Check(Marshal.SizeOf(typeof(LEAP_HAND)) == 1084, "LeapC hand ABI mismatch.");
                var result = LeapC.CreateConnection(out var native);
                Check(result == eLeapRS.eLeapRS_Success && native != IntPtr.Zero, "Native LeapC library failed to create a connection: " + result);
                LeapC.DestroyConnection(native);
                var a = new GameObject("IK test upper").transform;
                var b = new GameObject("IK test lower").transform; b.SetParent(a); b.localPosition = Vector3.right;
                var c = new GameObject("IK test hand").transform; c.SetParent(b); c.localPosition = Vector3.right;
                var target = new Vector3(1,1,0); OpticalHands.Arm(a,b,c,target,Vector3.up);
                Check(Vector3.Distance(c.position,target) < .001f && Mathf.Abs(Vector3.Distance(a.position,b.position)-1) < .001f && Mathf.Abs(Vector3.Distance(b.position,c.position)-1) < .001f, "Two-bone optical arm IK failed."); Destroy(a.gameObject);
                var animator = app.driver.Definition.animator;
                app.optical.desk.pose.position=Vector3.zero; app.optical.desk.pose.euler=Vector3.zero; app.optical.desk.pose.scale=1;
                frame = new OpticalFrame { source = "leap", hands = new[] { BuildHand(animator, app.currentAvatar.transform, true), BuildHand(animator, app.currentAvatar.transform, false) } };
                Check(OpticalInput.Valid(frame), "Valid skeleton rejected.");
                frame.hands[0].joints[0].x = float.NaN; Check(!OpticalInput.Valid(frame), "NaN accepted."); frame.hands[0] = BuildHand(animator,app.currentAvatar.transform,true);
                app.optical.source = 0; app.optical.Inject(frame); app.optical.Calibrate(); Check(app.optical.Calibrated, "Optical calibration failed.");
                finger = animator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate); Check(finger != null, "Test rig needs index fingers."); rest = finger.localRotation;
                var h = frame.hands[0]; var basis = OpticalHands.Basis(h.joints,true); var rotation = Quaternion.AngleAxis(65,basis*Vector3.right);
                h.joints[7] = h.joints[6]+rotation*(h.joints[7]-h.joints[6]); h.joints[8] = h.joints[7]+rotation*(h.joints[8]-h.joints[7]);
                app.optical.Inject(frame);
            }
            catch (Exception ex) { Finish(false,ex.ToString()); yield break; }
            float until = Time.unscaledTime+.25f;
            while (Time.unscaledTime < until) { app.optical.Inject(frame); yield return null; }
            if (Quaternion.Angle(rest,finger.localRotation) < 10) { Finish(false,"Finger retargeting did not bend the real Humanoid index."); yield break; }
            yield return new WaitForSeconds(.7f);
            if (Quaternion.Angle(rest,finger.localRotation) > 5) { Finish(false,"Finger did not return after stale tracking."); yield break; }
            app.optical.Stop();
            using (var input = new OpticalInput())
            {
                var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback,0)); int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port; probe.Close();
                input.StartWebcam(port); frame.source = "webcam";
                using (var sender = new UdpClient()) { var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(frame)); sender.Send(bytes,bytes.Length,new IPEndPoint(IPAddress.Loopback,port)); }
                yield return new WaitForSeconds(.2f);
                if (input.Read() == null) { Finish(false,"Webcam JSON UDP receiver failed."); yield break; }
            }
            Finish(true,"Official native LeapC loaded, hand ABI; optical arm IK and preserved lengths; malformed landmarks rejected; real Humanoid finger retargeting/calibration, stale-signal rest; webcam JSON over actual loopback UDP. Physical optical sensors/camera inference on humans remain untested.");
        }
        static OpticalHand BuildHand(Animator a,Transform root,bool left)
        {
            string prefix = left ? "Left" : "Right"; var joints = new Vector3[21];
            joints[0] = root.InverseTransformPoint(a.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),prefix+"Hand")).position);
            string[] names = {"Thumb","Index","Middle","Ring","Little"};
            for (int i=0;i<5;i++)
            {
                for (int k=0;k<3;k++) { string suffix = k==0 ? "Proximal" : k==1 ? "Intermediate" : "Distal"; var bone = a.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),prefix+names[i]+suffix)); if (bone == null) throw new Exception("Missing test finger bone"); joints[1+i*4+k] = root.InverseTransformPoint(bone.position); }
                joints[4+i*4] = joints[3+i*4]+(joints[3+i*4]-joints[2+i*4]);
            }
            return new OpticalHand {left=left,joints=joints,elbowValid=true,elbow=root.InverseTransformPoint(a.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm).position)};
        }
        static void Finish(bool pass,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"hand-test-result.txt"),(pass?"PASS\n":"FAIL\n")+detail);
            Debug.Log("AVATARLAB_HAND_"+(pass?"PASS ":"FAIL ")+detail); Application.Quit(pass?0:1);
        }
    }
}
