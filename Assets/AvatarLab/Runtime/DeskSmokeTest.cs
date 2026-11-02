using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class DeskSmokeTest : MonoBehaviour
    {
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        static OpticalHand Hand(bool left)
        {
            var wrist=new Vector3(left?-.15f:.15f,.22f,0); var points=new Vector3[21]; points[0]=wrist;
            for(int f=0;f<5;f++) for(int j=0;j<4;j++) points[1+f*4+j]=wrist+new Vector3((f-2)*.018f,0,-.04f-j*.025f);
            return new OpticalHand {left=left,joints=points,confidence=1,elbowValid=true,elbow=wrist+new Vector3(left?-.12f:.12f,-.08f,.2f)};
        }
        IEnumerator Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--desk-test")<0) yield break;
            var app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+60;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            yield return null;
            OpticalHands optical=null; OpticalFrame frame=null; Transform left=null,right=null; string temporary=null;
            try
            {
                Check(app.driver!=null && app.driver.Definition.animator.isHuman,"Humanoid avatar required");
                optical=app.optical; optical.Stop(); optical.source=0; optical.mounted=false; optical.enabledTracking=true; optical.fingersOnly=false;
                string id="desk-validation-"+Guid.NewGuid().ToString("N"); optical.desk.Attach(id);
                temporary=Path.Combine(Application.persistentDataPath,"LeapDesk",id+".json");
                var a=app.driver.Definition.animator; left=a.GetBoneTransform(HumanBodyBones.LeftHand); right=a.GetBoneTransform(HumanBodyBones.RightHand);
                var lowered=(left.position+right.position)*.5f;
                optical.Inject(new OpticalFrame {source="leap",hands=new[]{Hand(true)}}); optical.SendMessage("Update");
                Check(optical.Calibrated && optical.HasHandBinding(0) && optical.HasHandBinding(1),"One detected hand did not automatically prepare BOTH rig bindings");
                frame=new OpticalFrame {source="leap",hands=new[]{Hand(true),Hand(false)}}; optical.Inject(frame); optical.CalibrateDeskGuide();
                Check(optical.Calibrated,"Two-hand guide calibration failed");
                var target=(a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position+a.GetBoneTransform(HumanBodyBones.RightUpperArm).position)*.5f+app.currentAvatar.transform.rotation*new Vector3(0,-.22f,.30f);
                Check(Vector3.Distance(optical.Map((frame.hands[0].joints[0]+frame.hands[1].joints[0])*.5f),target)<.001f,"Guide extrinsics target mismatch");
                Check(Vector3.Distance(target,lowered)>.12f,"Calibration still anchored to lowered avatar wrists");
                var mapped=optical.Map(frame.hands[0].joints[0]); var oldScale=app.currentAvatar.transform.localScale; app.currentAvatar.transform.localScale=oldScale*2;
                Check(Vector3.Distance(mapped,optical.Map(frame.hands[0].joints[0]))<.0001f,"Rig scale distorted physical metres"); app.currentAvatar.transform.localScale=oldScale;
                var original=optical.desk.pose.position; optical.Calibrate(); Check(Vector3.Distance(original,optical.desk.pose.position)<.0001f,"Finger calibration changed sensor extrinsics");
                optical.desk.Save(); var reload=app.gameObject.AddComponent<LeapDeskSetup>(); reload.optical=optical; reload.Attach(id);
                Check(Vector3.Distance(reload.pose.position,original)<.0001f && Quaternion.Angle(Quaternion.Euler(reload.pose.euler),Quaternion.Euler(optical.desk.pose.euler))<.001f,"Desk pose did not survive save/reload"); Destroy(reload);
            }
            catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
            float until=Time.unscaledTime+.35f;
            while(Time.unscaledTime<until) { optical.Inject(frame); yield return null; }
            try
            {
                Check(Vector3.Distance(left.position,optical.Map(frame.hands[0].joints[0]))<.035f,"Left arm failed to reach raised desk target");
                Check(Vector3.Distance(right.position,optical.Map(frame.hands[1].joints[0]))<.035f,"Right arm failed to reach raised desk target");
                optical.desk.pose.position+=new Vector3(.02f,.06f,.01f);
            }
            catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
            until=Time.unscaledTime+.2f;
            while(Time.unscaledTime<until) { optical.Inject(new OpticalFrame {source="leap",hands=new[]{frame.hands[0]}}); yield return null; }
            until=Time.unscaledTime+.3f;
            while(Time.unscaledTime<until) { optical.Inject(frame); yield return null; }
            try
            {
                Check(Vector3.Distance(left.position,optical.Map(frame.hands[0].joints[0]))<.035f,"Left arm ignored sensor offset");
                Check(Vector3.Distance(right.position,optical.Map(frame.hands[1].joints[0]))<.035f,"Right hand did not resume after occlusion / sensor offset");
                Check(!LeapDeskSetup.Valid(new LeapDeskPose {scale=float.NaN}),"Nonfinite pose accepted");
                optical.RequestDeskGuide(); optical.Inject(new OpticalFrame {source="leap",hands=new[]{frame.hands[0]}}); optical.SendMessage("Update");
                Check(optical.WaitingForDeskGuide,"Guide request did not wait for the user's second hand");
            }
            catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
            until=Time.unscaledTime+.85f;
            while(Time.unscaledTime<until) { optical.Inject(frame); yield return null; }
            try
            {
                Check(!optical.WaitingForDeskGuide && optical.Calibrated,"Queued calibration did not execute automatically after both hands returned");
                Check(optical.appliedFrames[0]>0 && optical.appliedFrames[1]>0,"Live application counters did not advance for both hands");
            }
            catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
            for(int cycle=0;cycle<12;cycle++)
            {
                var sample=cycle%2==0?new OpticalFrame {source="leap",hands=Array.Empty<OpticalHand>()}:frame;
                until=Time.unscaledTime+.04f;
                while(Time.unscaledTime<until) { optical.Inject(sample); yield return null; }
                try
                {
                    Check(optical.filters[0].Weight>.95f && optical.filters[1].Weight>.95f,"Brief repeated dropouts released hand application");
                    Check(Vector3.Distance(left.position,optical.Map(frame.hands[0].joints[0]))<.035f && Vector3.Distance(right.position,optical.Map(frame.hands[1].joints[0]))<.035f,"Humanoid wrists snapped to animation during intermittent tracking");
                }
                catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
            }
            var hips=app.driver.Definition.animator.GetBoneTransform(HumanBodyBones.Hips);
            float relative=optical.desk.Position.y-hips.position.y;
            var savedPosition=optical.desk.pose.position;
            string poseDetails="";
            foreach(string poseId in new[]{"gogo:go_seiza","gogo:go_sit_crossed_leg"})
            {
                try
                {
                    var item=app.poses.Items.FirstOrDefault(p=>p.id==poseId);
                    Check(item!=null,"Missing real pose "+poseId); app.poses.lockTracking=false; Check(app.poses.Play(item),"Pose failed to start");
                }
                catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
                until=Time.unscaledTime+.8f;
                while(Time.unscaledTime<until) { optical.Inject(frame); yield return new WaitForEndOfFrame(); }
                try
                {
                    Check(Mathf.Abs(optical.desk.Position.y-hips.position.y-relative)<.002f,"Desk stayed above seated actor: "+poseId);
                    Check(optical.desk.HeightOffset<-.2f,"Pose did not lower body for regression: "+poseId);
                    Check(!app.driver.PoseOverride,"Pose unexpectedly blocked tracking");
                    float le=Vector3.Distance(left.position,optical.Map(frame.hands[0].joints[0])),re=Vector3.Distance(right.position,optical.Map(frame.hands[1].joints[0]));
                    Check(le<.04f && re<.04f,"Seated hands missed targets "+poseId+": "+le+" / "+re);
                    var mapped=optical.Map(frame.hands[0].joints[0]); optical.desk.Save();
                    var reload=app.gameObject.AddComponent<LeapDeskSetup>(); reload.optical=optical; reload.Attach(Path.GetFileNameWithoutExtension(temporary));
                    Check(Vector3.Distance(reload.Map(frame.hands[0].joints[0]),mapped)<.001f,"Reload in seated pose rebased height"); Destroy(reload);
                    Check(Vector3.Distance(optical.desk.LocalFromWorld(optical.desk.Position),savedPosition)<.001f,"Inverse calibration includes pose offset");
                    poseDetails+=poseId+": offset="+optical.desk.HeightOffset+", wrist errors="+le+"/"+re+"m; ";
                }
                catch(Exception ex) { Finish(false,ex.ToString(),temporary); yield break; }
            }
            app.poses.Stop(); until=Time.unscaledTime+.5f;
            while(Time.unscaledTime<until) {optical.Inject(frame);yield return new WaitForEndOfFrame();}
            try {Check(Mathf.Abs(optical.desk.HeightOffset)<.025f,"Standing did not recover calibration");Check(optical.desk.pose.position==savedPosition,"Pose altered saved desk calibration");}
            catch(Exception ex) {Finish(false,ex.ToString(),temporary);yield break;}
            Finish(true,"Both arms and dropout stability passed; rig scale independence and saved calibration passed. Actual GoGo seated poses follow hips, both wrists within 4cm, reload while seated and return standing passed. "+poseDetails+" Synthetic tracking landmarks.",temporary);
        }
        static void Finish(bool pass,string detail,string temporary)
        {
            if(temporary!=null && File.Exists(temporary)) File.Delete(temporary);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"desk-test-result.txt"),(pass?"PASS\n":"FAIL\n")+detail);
            Debug.Log("AVATARLAB_DESK_"+(pass?"PASS ":"FAIL ")+detail); Application.Quit(pass?0:1);
        }
    }
}
