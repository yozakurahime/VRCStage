using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class PoseLibrarySmokeTest : MonoBehaviour
    {
        static void Check(bool condition,string message) {if(!condition) throw new Exception(message);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs(); if(!args.Contains("--poses-test")) yield break;
            var app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+60;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            if(app.driver==null) {Finish(false,"No avatar");yield break;}
            yield return new WaitForSeconds(.5f);
            var studio=app.poses; string savedPath=null,badPath=null,savedContactPath=null; string result=""; bool pass=false;
            var motor=app.motor.Root; var originalPosition=motor.position; var originalRotation=motor.rotation; var originalScale=motor.localScale;
            var animator=app.driver.Definition.animator; HumanPoseHandler reader=null;
            try
            {
                app.driver.UseManualEvaluation(); studio.Refresh(); reader=new HumanPoseHandler(animator.avatar,animator.transform);
                Check(studio.Items.Count(i=>i.category=="Poses")==12,"Built-in poses missing"); Check(studio.Items.Count(i=>i.category=="Animações")>=12,"Built-in animations missing");
                Check(studio.Items.Any(i=>i.category=="Do avatar"),"Avatar motion catalog missing");
                var wave=studio.Items.First(i=>i.id=="wave"); var tpose=studio.Items.First(i=>i.id=="tpose");
                app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); var initialArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;
                float fx=app.driver.Weight("FX"); var beforeRoot=app.currentAvatar.transform.localPosition;
                studio.Play(tpose); studio.Seek(0); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate");
                var arm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); var tRotation=arm.localRotation;
                var elbow=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);var wrist=animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Check(Vector3.Dot((elbow.position-arm.position).normalized,-animator.transform.right)>.97f,"T-Pose upper arm is not horizontal");
                Check(Vector3.Dot((wrist.position-elbow.position).normalized,-animator.transform.right)>.97f,"T-Pose forearm is not horizontal");
                studio.Play(studio.Items.First(i=>i.id=="relaxed")); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate");
                Check(Quaternion.Angle(tRotation,arm.localRotation)>15,"Preset did not change actual arm pose");
                studio.Play(wave); studio.Seek(.125f); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); var hand=animator.GetBoneTransform(HumanBodyBones.RightHand); var first=hand.localRotation;
                studio.Seek(.625f); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); Check(Quaternion.Angle(first,hand.localRotation)>5,"Wave did not animate the actual hand");
                studio.Pause(true); float held=studio.TimePosition; studio.Advance(.7f); Check(Mathf.Abs(studio.TimePosition-held)<1e-6f,"Pause advanced time");
                studio.Pause(false); studio.speed=.5f; studio.Advance(.2f); Check(Mathf.Abs(studio.TimePosition-held-.1f)<1e-5f,"Playback speed incorrect");
                studio.loop=false; studio.Seek(studio.Duration-.01f); studio.Advance(1); Check(!studio.Playing && Mathf.Abs(studio.TimePosition-studio.Duration)<1e-5f,"Non-looping end did not hold");
                studio.loop=true; studio.Pause(false); studio.Seek(studio.Duration-.01f); studio.Advance(.1f); Check(studio.TimePosition<.1f,"Loop did not wrap");
                Check(Mathf.Abs(app.driver.Weight("FX")-fx)<1e-5f,"Library changed FX layer weight"); Check(Vector3.Distance(beforeRoot,app.currentAvatar.transform.localPosition)<1e-5f,"Preview moved avatar root");
                Check(app.driver.PoseOverride,"Tracking not suspended while locked"); studio.lockTracking=false; studio.Advance(0); Check(!app.driver.PoseOverride,"Tracking not released when unlocked");studio.lockTracking=true;
                studio.Play(tpose); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); studio.BeginEdit(); int muscle=Array.IndexOf(HumanTrait.MuscleName,"Left Arm Down-Up"); studio.SetMuscle(muscle,-.5f);
                studio.SendMessage("LateUpdate"); var pose=new HumanPose(); reader.GetHumanPose(ref pose); Check(Mathf.Abs(pose.muscles[muscle]+.5f)<.05f,"Edited pose did not reach humanoid");
                var hips=animator.GetBoneTransform(HumanBodyBones.Hips); var hipLocal=hips.localPosition; var armLocal=arm.localRotation;
                motor.SetPositionAndRotation(originalPosition+new Vector3(2,0,-1),Quaternion.Euler(0,65,0)); motor.localScale=originalScale*1.5f; app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); studio.SendMessage("LateUpdate");
                Check(Vector3.Distance(hips.localPosition,hipLocal)<.005f && Quaternion.Angle(arm.localRotation,armLocal)<.5f,"Saved pose depended on world origin/rotation/scale: hips="+hipLocal+" -> "+hips.localPosition+" delta="+Vector3.Distance(hips.localPosition,hipLocal)+"; arm="+Quaternion.Angle(arm.localRotation,armLocal)+"; humanScale="+animator.humanScale+"; bodyLocal="+studio.Draft.bodyLocal);
                savedPath=studio.SaveCurrent("Temporary pose validation"); string id=Path.GetFileNameWithoutExtension(savedPath); studio.Stop(); studio.LoadSaved(); var loaded=studio.Items.Single(i=>i.id==id); Check(loaded.saved.avatarId==app.AvatarId,"Pose avatar scope lost");
                studio.Play(loaded); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); studio.SendMessage("LateUpdate"); reader.GetHumanPose(ref pose); Check(Mathf.Abs(pose.muscles[muscle]+.5f)<.05f,"Saved pose failed to reload and apply");
                studio.Play(studio.Items.First(i=>i.id=="hips"));app.driver.EvaluateManual(0);studio.SendMessage("LateUpdate");
                var waistHand=animator.GetBoneTransform(HumanBodyBones.LeftHand).position;savedContactPath=studio.SaveCurrent("Temporary contact pose validation");
                var waistId=Path.GetFileNameWithoutExtension(savedContactPath);studio.Stop();studio.LoadSaved();studio.Play(studio.Items.Single(i=>i.id==waistId));
                app.driver.EvaluateManual(0);studio.SendMessage("LateUpdate");Check(Vector3.Distance(waistHand,animator.GetBoneTransform(HumanBodyBones.LeftHand).position)<.025f,"Saved waist contact did not round trip");
                badPath=Path.Combine(studio.PoseDirectory,"invalid-validation-"+Guid.NewGuid().ToString("N")+".json"); File.WriteAllText(badPath,"{\"format\":1,\"muscles\":[999]}"); studio.LoadSaved();
                studio.Stop(); app.driver.EvaluateManual(0); studio.SendMessage("LateUpdate"); Check(!app.driver.LibraryActive && !app.driver.PoseOverride,"Stop did not restore tracking/normal animation");
                Check(Quaternion.Angle(arm.localRotation,initialArm)<2,"Stop did not restore actual arm pose: "+Quaternion.Angle(arm.localRotation,initialArm));
                studio.Play(tpose); app.motor.SetControl(true); Check(!app.driver.LibraryActive,"Entering play mode did not release preview"); app.motor.SetControl(false);
                Check(Mathf.Abs(app.driver.Weight("FX")-fx)<1e-5f,"Stop changed original FX weight");
                result="12 built-in poses, 12 built-in animations and avatar clips; actual arm/hand pose change; pause, speed, seek, loop/end hold; FX weight/root preserved; tracking lock/unlock; edit/save/reload avatar-scoped pose; origin/rotation/scale independence; malformed pose rejected; stop/play mode releases preview. Temporary files removed."; pass=true;
            }
            catch(Exception ex) {result=ex.ToString();}
            finally
            {
                studio.Stop(); reader?.Dispose(); motor.SetPositionAndRotation(originalPosition,originalRotation); motor.localScale=originalScale;
                if(savedPath!=null && File.Exists(savedPath)) File.Delete(savedPath); if(savedContactPath!=null && File.Exists(savedContactPath)) File.Delete(savedContactPath); if(badPath!=null && File.Exists(badPath)) File.Delete(badPath); studio.LoadSaved();
            }
            Finish(pass,result);
        }
        static void Finish(bool pass,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"poses-test-result.txt"),(pass?"PASS\n":"FAIL\n")+detail); Debug.Log("AVATARLAB_POSES_"+(pass?"PASS ":"FAIL ")+detail);Application.Quit(pass?0:1);
        }
    }
}
