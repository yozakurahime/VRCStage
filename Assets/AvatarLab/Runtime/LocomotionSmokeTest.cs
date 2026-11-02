using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class LocomotionSmokeTest : MonoBehaviour
    {
        LabApp app; Transform leg,head; bool failed; string details="";
        void Check(bool condition,string message) { if(!condition) { failed=true; details+=message+"\n"; } }
        IEnumerator Phase(Vector2 input,bool run,bool crouch,int frames,Action<int> inspect=null,bool jump=false)
        {
            for(int i=0;i<frames;i++)
            {
                app.motor.StepInput(input,run,jump && i==0,crouch,1f/60); app.driver.EvaluateManual(1f/60); inspect?.Invoke(i); yield return null;
            }
            details+=string.Join(",",app.driver.ActiveClipNames("Base"))+"\n";
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs(); if(!args.Contains("--locomotion-test")) yield break;
            app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+60;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            if(app.driver==null) { Finish(false,"No avatar"); yield break; }
            yield return new WaitForSeconds(.5f);
            if(args.Contains("--default-locomotion"))
            {
                var definition=app.driver.Definition; app.driver.Dispose(); definition.baseController=null; definition.animationLayers.RemoveAll(l=>l.type=="Base");
                app.driver=new AvatarDriver(definition,app.motionLibrary);
            }
            app.enabled=false; app.motor.enabled=false; app.physicsStudio.simulate=false; app.driver.UseManualEvaluation(); Time.captureDeltaTime=1f/60;
            var animator=app.driver.Definition.animator; leg=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); head=animator.GetBoneTransform(HumanBodyBones.Head);
            app.driver.Set("TrackingType",3); app.driver.Set("VRMode",0); app.driver.Set("IsLocal",1); app.driver.PreviewPose("");
            yield return Phase(Vector2.zero,false,false,60);
            Check(app.motor.Grounded,"Motor did not settle on floor"); float standing=head.position.y-app.motor.Root.position.y;
            var start=app.motor.Root.position; Quaternion reference=leg.localRotation; float walking=0;
            yield return Phase(Vector2.up,false,false,90,i=>{if(i==30) reference=leg.localRotation; if(i>30) walking=Mathf.Max(walking,Quaternion.Angle(reference,leg.localRotation));});
            Check(Vector3.Distance(start,app.motor.Root.position)>1,"Walk did not move actor"); Check(walking>5,"Leg did not animate when walking: "+walking);
            Check(app.driver.GetValue("VelocityMagnitude")>1,"Missing movement speed");
            app.motor.Teleport(Vector3.zero,Quaternion.identity); yield return Phase(Vector2.zero,false,false,15);
            reference=leg.localRotation; float running=0;
            yield return Phase(Vector2.up,true,false,60,i=>running=Mathf.Max(running,Quaternion.Angle(reference,leg.localRotation)));
            Check(running>5,"Leg did not animate when running"); Check(app.driver.GetValue("VelocityMagnitude")>3,"Run speed not published");
            app.motor.Teleport(Vector3.zero,Quaternion.identity); yield return Phase(Vector2.zero,false,false,15);
            yield return Phase(Vector2.zero,false,true,45);
            float crouched=head.position.y-app.motor.Root.position.y;
            Check(app.motor.Crouched && app.driver.GetValue("Upright")<.7f,"Crouch input not published"); Check(crouched<standing-.1f,"Crouch did not lower head: "+standing+" -> "+crouched);
            yield return Phase(Vector2.up,false,true,60); Check(app.driver.GetValue("VelocityMagnitude")<1.2f,"Crouch walk not slowed");
            yield return Phase(Vector2.zero,false,false,45); Check(!app.motor.Crouched && app.driver.GetValue("Upright")>.9f,"Standing did not recover");
            float floor=app.motor.Root.position.y,peak=floor; bool rising=false,falling=false,air=false,unityAir=false; Quaternion groundedLeg=leg.localRotation; float airPose=0;
            yield return Phase(Vector2.zero,false,false,120,i=>{peak=Mathf.Max(peak,app.motor.Root.position.y); air|=app.driver.GetValue("Grounded")<.5f; rising|=app.driver.GetValue("VelocityY")>.5f; falling|=app.driver.GetValue("VelocityY")<-.5f; unityAir|=app.driver.ActiveClipNames("Base").Any(n=>n=="StandingJump_Frame02" || n=="Falling");if(!app.motor.Grounded) airPose=Mathf.Max(airPose,Quaternion.Angle(groundedLeg,leg.localRotation));},true);
            Check(unityAir,"Official Unity air clips were not used");
            Check(airPose>5,"Airborne leg pose did not change"); Check(peak-floor>.3f && air && rising && falling,"Jump/fall parameters or movement failed"); Check(app.motor.Grounded && Mathf.Abs(app.motor.Root.position.y-floor)<.08f,"Did not land on floor");
            yield return Phase(Vector2.zero,false,false,30); Check(app.driver.GetValue("VelocityMagnitude")<.05f,"Idle speed did not clear");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); var direction=Vector3.ProjectOnPlane(app.room.view.transform.forward,Vector3.up).normalized;
            wall.transform.SetPositionAndRotation(app.motor.Root.position+direction*.6f+Vector3.up,Quaternion.LookRotation(direction)); wall.transform.localScale=new Vector3(3,2,.1f); Physics.SyncTransforms();
            yield return Phase(Vector2.up,false,false,60); Check(Vector3.ProjectOnPlane(app.motor.Velocity,Vector3.up).magnitude<.05f,"Blocked actor still reports walking velocity"); Destroy(wall);
            details+="Resolved proxies="+app.driver.ResolvedMotionProxies+"; walk leg motion="+walking+"deg; run="+running+"deg; head standing/crouched="+standing+"/"+crouched+"; jump rise="+(peak-floor)+"m; airborne leg motion="+airPose+"deg.\n";
            if(app.driver.Definition.baseController!=null && app.driver.Definition.baseController.animationClips.Any(c=>c!=null && c.name.StartsWith("proxy_walk"))) Check(app.driver.ResolvedMotionProxies>0,"Locomotion proxies not resolved");
            app.motor.SetControl(false);Check(!app.driver.ActiveClipNames("Base").Any(n=>n=="WalkForwards" || n=="RunForwards"),"Game locomotion did not release the original Base");
            Finish(!failed,details);
        }
        static void Finish(bool pass,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"locomotion-test-result.txt"),(pass?"PASS\n":"FAIL\n")+detail); Debug.Log("AVATARLAB_LOCOMOTION_"+(pass?"PASS ":"FAIL ")+detail); Application.Quit(pass?0:1);
        }
    }
}
