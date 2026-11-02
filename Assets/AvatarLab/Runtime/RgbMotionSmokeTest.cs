using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AvatarLab
{
    public sealed class RgbMotionSmokeTest : MonoBehaviour
    {
        static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--rgb-motion-test")) yield break;
            var app=FindObjectOfType<LabApp>();float deadline=Time.realtimeSinceStartup+60;
            while((app.driver==null || app.mocap.Busy || !app.poses.Items.Any(i=>i.motionPath!=null)) && Time.realtimeSinceStartup<deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            bool pass=false;string result="";PoseStudio.Item tested=null;var actor=app.motor.Root;Vector3 scale=actor.localScale;var position=actor.position;var rotation=actor.rotation;
            try
            {
                Check(app.driver!=null && !app.mocap.Busy,"Motion import did not complete: "+app.mocap.Status);
                var item=app.poses.Items.First(i=>i.label=="RGB bridge validation");tested=item;var path=item.motionPath;
                app.driver.UseManualEvaluation();var animator=app.driver.Definition.animator;
                var rh=animator.GetBoneTransform(HumanBodyBones.RightHand);var lh=animator.GetBoneTransform(HumanBodyBones.LeftHand);var hips=animator.GetBoneTransform(HumanBodyBones.Hips);var head=animator.GetBoneTransform(HumanBodyBones.Head);
                item.motion=RgbMotion.Read(path);Check(item.motion.Info.frameCount==121 && Math.Abs(item.motion.Duration-2)<.001f,"Timing changed");
                Check(app.poses.Play(item),"Motion did not start");app.poses.Pause(true);app.mocap.travel=false;
                Action<float> sample=t=>{app.poses.Seek(t);app.driver.EvaluateManual(0);app.poses.SendMessage("LateUpdate");};
                sample(0);var left=animator.transform.InverseTransformPoint(lh.position);var hip0=animator.transform.InverseTransformPoint(hips.position);var right0=animator.transform.InverseTransformPoint(rh.position);
                var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);var finger0=index.localRotation;
                var leftIndex=animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);var leftFinger0=leftIndex.localRotation;
                sample(1);var raised=animator.transform.InverseTransformPoint(rh.position);var h=animator.transform.InverseTransformPoint(head.position);var hip1=animator.transform.InverseTransformPoint(hips.position);
                Check(raised.y>h.y+.03f && raised.y>right0.y+.2f,"Captured RIGHT arm did not lift above head: "+raised+" head="+h);
                Check(Vector3.Distance(left,animator.transform.InverseTransformPoint(lh.position))<.06f,"Unrelated LEFT hand moved / capture mirrored");
                Check(Quaternion.Angle(finger0,index.localRotation)>15,"Captured right index finger did not bend");
                Check(Quaternion.Angle(leftFinger0,leftIndex.localRotation)<1,"Finger capture changed the opposite hand");
                Check(new Vector2(hip1.x-hip0.x,hip1.z-hip0.z).magnitude<.08f,"In-place capture travelled");
                app.mocap.travel=true;sample(1);var travelled=animator.transform.InverseTransformPoint(hips.position);
                Check(travelled.x<hip1.x-.15f && travelled.z>hip1.z+.05f,"Root displacement lost / wrong coordinate handedness");
                app.mocap.travel=false;sample(.5f);Check(animator.transform.InverseTransformPoint(hips.position).y>hip0.y+.1f,"Vertical jump was clamped to floor");
                var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);sample(.75f);var arm=upper.localRotation;
                actor.SetPositionAndRotation(position+new Vector3(3,0,-2),Quaternion.Euler(0,70,0));actor.localScale=scale*1.5f;sample(.75f);
                Check(Quaternion.Angle(arm,upper.localRotation)<.5f,"Retarget changed after moving/scaling actor");
                var fx=app.driver.Weight("FX");sample(.3f);var saved=app.poses.Capture("RGB frame validation");Check(saved.bones.Length>20,"Could not extract a pose from capture");
                app.poses.Pause(false);app.poses.speed=.5f;float before=app.poses.TimePosition;app.poses.Advance(.2f);Check(Math.Abs(app.poses.TimePosition-before-.1f)<.001f,"Playback speed failed");
                app.poses.loop=false;app.poses.Seek(1.99f);app.poses.Advance(.5f);Check(!app.poses.Playing && Math.Abs(app.poses.TimePosition-2)<.001f,"End hold failed");
                Check(Math.Abs(app.driver.Weight("FX")-fx)<.001f && actor.position==position+new Vector3(3,0,-2),"Playback changed FX or actor root");
                app.poses.Stop();Check(!app.driver.PoseOverride && !app.driver.LibraryActive,"Tracking stayed locked after stop");
                var bytes=File.ReadAllBytes(path);string invalid=Path.Combine(Application.temporaryCachePath,"rgb-invalid-"+Guid.NewGuid().ToString("N")+".almotion");
                try {File.WriteAllBytes(invalid,bytes.Take(bytes.Length-4).ToArray());bool rejected=false;try {RgbMotion.Read(invalid);}catch(InvalidDataException) {rejected=true;}Check(rejected,"Truncated movement accepted");} finally {if(File.Exists(invalid)) File.Delete(invalid);}
                result="52-bone upstream rig -> native ALMOTION -> runtime Humanoid -> test avatar. Correct right/left arm and individual finger motion, unrelated limb/finger stability, timing, in-place/travel axes, vertical jump, origin/rotation/scale independence, pose extraction, playback speed/end, FX/root preserved, stop releases tracking, truncated file rejected. Synthetic capture; no video inference test.";pass=true;
            }
            catch(Exception ex) {result=ex.ToString();}
            finally {app.poses.Stop();actor.SetPositionAndRotation(position,rotation);actor.localScale=scale;}
            if(pass)
            {
                tested.motion=RgbMotion.Read(tested.motionPath);app.poses.Play(tested);app.poses.Pause(true);app.poses.Seek(1);app.driver.EvaluateManual(0);
                yield return null;yield return null;
                var a=app.driver.Definition.animator;var right=a.GetBoneTransform(HumanBodyBones.RightHand);var left=a.GetBoneTransform(HumanBodyBones.LeftHand);var head=a.GetBoneTransform(HumanBodyBones.Head);
                if(right.position.y<head.position.y+.03f || left.position.y>head.position.y) {pass=false;result="Full-frame constraints/physics changed the retargeted body pose.";}
                else result+=" Full-frame constraint/physics evaluation preserved right-hand raise and side mapping.";
                app.poses.Stop();
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"rgb-motion-test-result.txt"),(pass?"PASS\n":"FAIL\n")+result);Debug.Log("AVATARLAB_RGB_MOTION "+(pass?"PASS ":"FAIL ")+result);Application.Quit(pass?0:1);
        }
    }
}
