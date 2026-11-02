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
        string photoDirectory;
        void CaptureVisual(string name)
        {
            if(photoDirectory==null || app.photo.Saving) return;
            Directory.CreateDirectory(photoDirectory);
            var actor=app.currentAvatar.transform;
            app.room.view.transform.position=actor.position+(actor.forward+actor.right)*2+Vector3.up;
            app.photo.Frame(0);app.photo.settings.resolution=0;
            app.photo.Capture(Path.Combine(photoDirectory,name+".png"));
        }
        IEnumerator Visual(string name)
        {
            if(photoDirectory==null) yield break;
            while(app.photo.Saving) yield return null;
            CaptureVisual(name);
            while(app.photo.Saving) yield return null;
        }
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
            int photos=Array.IndexOf(args,"--locomotion-photos");if(photos>=0 && photos+1<args.Length) photoDirectory=args[photos+1];
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
            var leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
            float footReference=Mathf.Min(leftFoot.position.y,rightFoot.position.y)-app.motor.Root.position.y;
            float firstSpeed=0;
            yield return Phase(Vector2.up,false,false,1,i=>firstSpeed=Vector3.ProjectOnPlane(app.motor.Velocity,Vector3.up).magnitude);
            Check(firstSpeed>.01f && firstSpeed<.6f,"Start snapped to full walking speed: "+firstSpeed);
            var start=app.motor.Root.position; Quaternion reference=leg.localRotation; float walking=0;
            yield return Phase(Vector2.up,false,false,90,i=>{if(i==30) reference=leg.localRotation; if(i>30) walking=Mathf.Max(walking,Quaternion.Angle(reference,leg.localRotation));});
            Check(Vector3.Distance(start,app.motor.Root.position)>1,"Walk did not move actor"); Check(walking>5,"Leg did not animate when walking: "+walking);
            Check(app.driver.GetValue("VelocityMagnitude")>1,"Missing movement speed");
            yield return Visual("Walk");
            var brakeStart=app.motor.Root.position;
            yield return Phase(Vector2.zero,false,false,20);
            float brakeDistance=Vector3.Distance(brakeStart,app.motor.Root.position);
            Check(brakeDistance>.01f && brakeDistance<.2f,"Stopping did not brake within a short distance: "+brakeDistance);
            Check(app.driver.GetValue("VRCStageGait")<.01f,"Walking animation did not settle after braking");
            app.motor.speed=.9f;
            yield return Phase(Vector2.up,false,false,45);
            Check(Mathf.Abs(app.driver.GetValue("VRCStageGait")-1.8f)<.08f && app.driver.ActiveClipNames("Base").Contains("WalkForwards"),"Custom walking speed blended idle instead of rescaling cadence");
            app.motor.speed=1.8f;
            app.motor.Teleport(Vector3.zero,Quaternion.identity); yield return Phase(Vector2.zero,false,false,15);
            reference=leg.localRotation; float running=0;
            yield return Phase(Vector2.up,true,false,60,i=>running=Mathf.Max(running,Quaternion.Angle(reference,leg.localRotation)));
            Check(running>5,"Leg did not animate when running"); Check(app.driver.GetValue("VelocityMagnitude")>3,"Run speed not published");
            yield return Visual("Run");
            float maxTurn=0;var previousRotation=app.motor.Root.rotation;
            yield return Phase(Vector2.down,true,false,35,i=>{maxTurn=Mathf.Max(maxTurn,Quaternion.Angle(previousRotation,app.motor.Root.rotation));previousRotation=app.motor.Root.rotation;});
            Check(maxTurn<40,"Direction reversal snapped the character: "+maxTurn);
            app.motor.Teleport(Vector3.zero,Quaternion.identity); yield return Phase(Vector2.zero,false,false,15);
            yield return Phase(Vector2.zero,false,true,45);
            float crouched=head.position.y-app.motor.Root.position.y;
            float crouchFeet=Mathf.Min(leftFoot.position.y,rightFoot.position.y)-app.motor.Root.position.y;
            Check(Mathf.Abs(crouchFeet-footReference)<.12f,"Crouch pulled feet off the ground: "+footReference+" -> "+crouchFeet);
            Check(app.motor.Crouched && app.driver.GetValue("Upright")<.7f,"Crouch input not published"); Check(crouched<standing-.1f,"Crouch did not lower head: "+standing+" -> "+crouched);
            yield return Visual("Crouch");
            yield return Phase(Vector2.up,false,true,60); Check(app.driver.GetValue("VelocityMagnitude")<1.2f,"Crouch walk not slowed");
            Check(Mathf.Abs(Mathf.Min(leftFoot.position.y,rightFoot.position.y)-app.motor.Root.position.y-footReference)<.12f,"Crouch walk lost ground contact");
            yield return Visual("CrouchWalk");
            yield return Phase(Vector2.zero,false,false,45); Check(!app.motor.Crouched && app.driver.GetValue("Upright")>.9f,"Standing did not recover");
            float floor=app.motor.Root.position.y,peak=floor; bool rising=false,falling=false,air=false,unityAir=false; Quaternion groundedLeg=leg.localRotation; float airPose=0;
            yield return Phase(Vector2.zero,false,false,120,i=>{peak=Mathf.Max(peak,app.motor.Root.position.y); air|=app.driver.GetValue("Grounded")<.5f; rising|=app.driver.GetValue("VelocityY")>.5f; falling|=app.driver.GetValue("VelocityY")<-.5f; unityAir|=app.driver.ActiveClipNames("Base").Any(n=>n=="StandingJump_Frame02" || n=="Falling");if(!app.motor.Grounded) airPose=Mathf.Max(airPose,Quaternion.Angle(groundedLeg,leg.localRotation));},true);
            Check(unityAir,"Official Unity air clips were not used");
            Check(airPose>5,"Airborne leg pose did not change"); Check(peak-floor>.3f && air && rising && falling,"Jump/fall parameters or movement failed"); Check(app.motor.Grounded && Mathf.Abs(app.motor.Root.position.y-floor)<.08f,"Did not land on floor");
            yield return Phase(Vector2.zero,false,false,30); Check(app.driver.GetValue("VelocityMagnitude")<.05f,"Idle speed did not clear");
            bool runningJump=false;int sinceLand=0;bool landed=false;bool resumed=false;
            var viewForward=Vector3.ProjectOnPlane(app.room.view.transform.forward,Vector3.up).normalized;
            app.motor.Teleport(-viewForward*2,Quaternion.identity);yield return Phase(Vector2.zero,false,false,20);
            yield return Phase(Vector2.up,true,false,35);
            yield return Phase(Vector2.up,true,false,80,i=>
            {
                var clips=app.driver.ActiveClipNames("Base");runningJump|=clips.Contains("RunForwardsJump_Frame02");
                if(i==18) CaptureVisual("RunningJump");
                if(i>10 && app.motor.Grounded) landed=true;
                if(landed && ++sinceLand>=12) resumed|=clips.Contains("RunForwards") && !clips.Contains("FallLand");
            },true);
            Check(runningJump,"Moving jump did not use the official running jump pose");
            Check(resumed,"Landing held a stationary animation while the character kept running");
            yield return Visual("AfterLanding");
            var actor=app.currentAvatar.transform;var originalRotation=actor.localRotation;
            actor.localRotation=Quaternion.Euler(0,90,0)*originalRotation;
            app.motor.Teleport(Vector3.zero,Quaternion.identity);yield return Phase(Vector2.up,false,false,50);
            Check(Vector3.Dot(Vector3.ProjectOnPlane(actor.forward,Vector3.up).normalized,Vector3.ProjectOnPlane(app.motor.Velocity,Vector3.up).normalized)>.98f,"Imported avatar yaw offset faced away from movement");
            actor.localRotation=originalRotation;
            app.motor.Teleport(Vector3.zero,Quaternion.identity);yield return Phase(Vector2.zero,false,false,30);
            var platform=GameObject.CreatePrimitive(PrimitiveType.Cube);platform.transform.position=new Vector3(0,1.9f,0);platform.transform.localScale=new Vector3(2,.2f,2);Physics.SyncTransforms();
            app.motor.Teleport(new Vector3(0,2.04f,0),Quaternion.identity);yield return Phase(Vector2.zero,false,false,30);
            Check(app.motor.Grounded,"Could not settle on coyote-test platform");
            bool leftEdge=false;
            for(int i=0;i<150;i++)
            {
                app.motor.StepInput(Vector2.up,false,false,false,1f/60);app.driver.EvaluateManual(1f/60);yield return null;
                if(!app.motor.Grounded) {leftEdge=true;break;}
            }
            yield return Phase(Vector2.up,false,false,1,null,true);
            Check(leftEdge && app.motor.Velocity.y>1,"Jump immediately after leaving a ledge was lost");
            Destroy(platform);app.motor.Teleport(Vector3.zero,Quaternion.identity);yield return Phase(Vector2.zero,false,false,30);
            floor=app.motor.Root.position.y;yield return Phase(Vector2.zero,false,false,1,null,true);
            bool queued=false;
            for(int i=0;i<100;i++)
            {
                bool queue=!queued && app.motor.Velocity.y< -1 && app.motor.Root.position.y-floor<.14f && !app.motor.Grounded;
                app.motor.StepInput(Vector2.zero,false,queue,false,1f/60);app.driver.EvaluateManual(1f/60);yield return null;
                if(queue) {queued=true;break;}
            }
            bool bufferedRise=false;yield return Phase(Vector2.zero,false,false,15,i=>bufferedRise|=app.motor.Velocity.y>1);
            Check(queued && bufferedRise,"Jump pressed just before landing was not buffered");
            yield return Phase(Vector2.zero,false,false,90);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); var direction=Vector3.ProjectOnPlane(app.room.view.transform.forward,Vector3.up).normalized;
            wall.transform.SetPositionAndRotation(app.motor.Root.position+direction*.6f+Vector3.up,Quaternion.LookRotation(direction)); wall.transform.localScale=new Vector3(3,2,.1f); Physics.SyncTransforms();
            yield return Phase(Vector2.up,false,false,60); Check(Vector3.ProjectOnPlane(app.motor.Velocity,Vector3.up).magnitude<.05f,"Blocked actor still reports walking velocity"); Destroy(wall);
            details+="Resolved proxies="+app.driver.ResolvedMotionProxies+"; walk leg motion="+walking+"deg; run="+running+"deg; head standing/crouched="+standing+"/"+crouched+"; airborne leg motion="+airPose+"deg; first-step speed="+firstSpeed+"; brake distance="+brakeDistance+"; max turn/frame="+maxTurn+"deg; running jump/landing, coyote time and buffered jump checked.\n";
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
