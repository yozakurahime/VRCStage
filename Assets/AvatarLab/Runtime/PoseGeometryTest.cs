using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class PoseGeometryTest : MonoBehaviour
    {
        static readonly HumanBodyBones[] Parts={HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes};
        [Serializable] public class Frame {public string id;public float time;public Vector3[] joints;}
        [Serializable] public class Report {public List<Frame> frames=new List<Frame>();public List<string> failures=new List<string>();}
        readonly Report report=new Report();LabApp app;Animator animator;
        Vector3 V(Frame f,HumanBodyBones bone)=>f.joints[Array.IndexOf(Parts,bone)];
        void Check(bool value,string message) {if(!value) report.failures.Add(message);}
        Frame Capture(string id,float time)=>new Frame{id=id,time=time,joints=Parts.Select(b=>animator.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray()};
        IEnumerator Start()
        {
            if(!Environment.GetCommandLineArgs().Contains("--pose-geometry-test")) yield break;
            app=FindObjectOfType<LabApp>();float deadline=Time.realtimeSinceStartup+60;while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            if(app.driver==null) {Application.Quit(1);yield break;}yield return new WaitForSeconds(.5f);
            animator=app.driver.Definition.animator;app.enabled=false;app.motor.enabled=false;app.poses.enabled=false;app.driver.UseManualEvaluation();
            foreach(var item in app.poses.Items.Where(i=>i.category=="Poses" || i.category=="Animações").ToArray())
            {
                app.poses.Play(item);app.poses.Pause(true);int samples=item.pose?1:8;
                for(int i=0;i<samples;i++)
                {
                    float time=item.clip.length*i/samples;app.poses.Seek(time);app.driver.EvaluateManual(0);app.poses.SendMessage("LateUpdate");yield return null;
                    var frame=Capture(item.id,time);report.frames.Add(frame);
                    Check(frame.joints.All(v=>!float.IsNaN(v.x+v.y+v.z) && !float.IsInfinity(v.x+v.y+v.z)),item.id+" nonfinite joint");
                }
            }
            var t=report.frames.First(f=>f.id=="tpose");float height=V(t,HumanBodyBones.Head).y-Mathf.Min(V(t,HumanBodyBones.LeftFoot).y,V(t,HumanBodyBones.RightFoot).y);
            foreach(var f in report.frames)
            {
                bool standing=f.id!="sit" && f.id!="kneel" && f.id!="crouch" && f.id!="crouchwalk" && f.id!="land";
                foreach(bool left in new[]{true,false})
                {
                    var shoulder=left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;var elbow=left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;var hand=left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
                    var knee=left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg;var foot=left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot;float sign=left?-1:1;
                    Check(Mathf.Abs(Vector3.Distance(V(f,shoulder),V(f,elbow))-Vector3.Distance(V(t,shoulder),V(t,elbow)))<height*.025f,f.id+" changed upper arm length");
                    if(f.id=="hips") Check(Mathf.Abs(V(f,hand).x-V(f,HumanBodyBones.Hips).x)>height*.08f,"Waist hand inside torso");
                    if(f.id=="tpose") Check(Vector3.Dot((V(f,hand)-V(f,shoulder)).normalized,Vector3.right*sign)>.97f,"T-Pose arm not horizontal");
                    if(f.id=="relaxed") Check(V(f,hand).y<V(f,shoulder).y-height*.25f,"Relaxed hand not down");
                    if(f.id=="hips") Check(Mathf.Abs(V(f,hand).y-V(f,HumanBodyBones.Hips).y)<height*.16f && Mathf.Abs(V(f,hand).z-V(f,HumanBodyBones.Hips).z)<height*.16f,"Hand not near waist");
                    if(f.id=="crossed") Check(V(f,hand).x*sign<height*.025f && V(f,hand).z>V(f,HumanBodyBones.Hips).z+height*.04f,"Hand did not cross in front of chest");
                    if(f.id=="victory") Check(V(f,hand).y>V(f,HumanBodyBones.Head).y+height*.08f,"Raised hand not above head");
                    if(f.id=="sit") Check(V(f,knee).z>V(f,HumanBodyBones.Hips).z+height*.14f && Mathf.Abs(V(f,knee).y-V(f,HumanBodyBones.Hips).y)<height*.14f,"Sitting knee not in front of hips");
                    if(f.id=="kneel") Check(Mathf.Abs(V(f,knee).y-V(t,foot).y)<height*.15f,"Kneeling knee not near floor");
                    if(standing && f.id!="walk" && f.id!="run") Check(Mathf.Abs(V(f,foot).y-V(t,foot).y)<height*.055f,f.id+" lifted planted foot");
                }
                if(f.id=="point") Check(Vector3.Dot((V(f,HumanBodyBones.RightHand)-V(f,HumanBodyBones.RightUpperArm)).normalized,Vector3.forward)>.8f,"Pointing arm not forward");
                if(f.id=="wave" || f.id=="wavepose") Check(V(f,HumanBodyBones.RightHand).y>V(f,HumanBodyBones.Head).y-height*.06f,"Waving hand below head");
                if(f.id=="sit" || f.id=="kneel" || f.id=="crouch") Check(V(f,HumanBodyBones.Hips).y<V(t,HumanBodyBones.Hips).y-height*.15f,f.id+" did not lower hips");
            }
            var clap=report.frames.Where(f=>f.id=="clap").Select(f=>Vector3.Distance(V(f,HumanBodyBones.LeftHand),V(f,HumanBodyBones.RightHand))).ToArray();
            Check(clap.Min()<height*.14f && clap.Min()>height*.025f && clap.Max()>height*.18f,"Clapping hands did not close and open: "+clap.Min()+" / "+clap.Max());
            foreach(string id in new[]{"nod","shake","breathe","wave"})
            {
                var frames=report.frames.Where(f=>f.id==id).ToArray();foreach(var f in frames) foreach(var foot in new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}) Check(Vector3.Distance(V(f,foot),V(frames[0],foot))<height*.02f,id+" moved unrelated planted foot");
            }
            app.poses.Play(app.poses.Items.First(i=>i.id=="tpose"));app.driver.EvaluateManual(0);app.poses.SendMessage("LateUpdate");yield return null;
            var repeated=Capture("tpose_after_other_clips",0);Check(repeated.joints.Zip(t.joints,(a,b)=>Vector3.Distance(a,b)).Max()<.003f,"Clip switch leaked previous pose");
            app.poses.Stop();string directory=Path.GetDirectoryName(Application.dataPath);File.WriteAllText(Path.Combine(directory,"pose-geometry-report.json"),JsonUtility.ToJson(report,true));
            string result=report.failures.Count==0?"PASS\n":"FAIL\n";result+=report.frames.Count+" full-frame pose samples; arm direction, wrist placement, crossed hands, sitting/kneeling/crouch, foot planting, clap closure and unrelated-limb stability.\n"+string.Join("\n",report.failures);
            File.WriteAllText(Path.Combine(directory,"pose-geometry-test-result.txt"),result);Debug.Log("AVATARLAB_GEOMETRY "+result);Application.Quit(report.failures.Count==0?0:1);
        }
    }
}
