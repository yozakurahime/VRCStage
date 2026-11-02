using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class GoGoPoseSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--gogo-test")) yield break;
            var app=FindObjectOfType<LabApp>();float deadline=Time.realtimeSinceStartup+90;
            while(app.driver==null && Time.realtimeSinceStartup<deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            string error=null;int count=0;var directory=Path.Combine(Application.persistentDataPath,"GoGoValidation");Directory.CreateDirectory(directory);
            if(app.driver==null) error="Avatar not loaded";
            else
            {
                app.enabled=false;app.motor.enabled=false;app.driver.UseManualEvaluation();
                var actor=app.motor.Root;var pos=actor.position;var rotation=actor.rotation;float fx=app.driver.Weight("FX");
                var animator=app.driver.Definition.animator;var items=app.poses.Items.Where(i=>i.category=="GoGo Loco").ToArray();
                try
                {
                    if(items.Length<60 || app.poses.Items.Any(i=>i.category=="Poses")) throw new Exception("GoGo catalog did not replace generated poses");
                    foreach(var item in items)
                    {
                        app.poses.Play(item);app.poses.Pause(true);app.driver.EvaluateManual(0);app.poses.SendMessage("LateUpdate");
                        var first=animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                        foreach(var bone in new[]{HumanBodyBones.Head,HumanBodyBones.Hips,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot})
                        {
                            var point=animator.GetBoneTransform(bone).position;
                            if(float.IsNaN(point.sqrMagnitude) || float.IsInfinity(point.sqrMagnitude) || Vector3.Distance(point,pos)>5) throw new Exception("Invalid pose coordinates: "+item.id);
                        }
                        if(Vector3.Distance(actor.position,pos)>.001f || Quaternion.Angle(actor.rotation,rotation)>.1f || Mathf.Abs(app.driver.Weight("FX")-fx)>.001f) throw new Exception("Pose changed actor root or FX");
                        app.poses.Stop();app.driver.EvaluateManual(0);app.poses.Play(item);app.driver.EvaluateManual(0);app.poses.SendMessage("LateUpdate");
                        if(Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,first)>.003f) throw new Exception("Pose switch inherited earlier pose: "+item.id);
                        count++;
                    }
                }
                catch(Exception ex) {error=ex.ToString();}
                foreach(var id in new[]{"go_seiza","go_sit_crossed_leg","go_laydown_side"})
                {
                    var item=items.FirstOrDefault(i=>i.id=="gogo:"+id);if(item==null) continue;
                    app.poses.Play(item);app.driver.EvaluateManual(0);app.poses.SendMessage("LateUpdate");app.photo.Frame(0);
                    app.photo.Capture(Path.Combine(directory,id+".png"));while(app.photo.Saving) yield return null;
                }
                app.poses.Stop();app.driver.EvaluateManual(0);
                if(app.driver.LibraryActive) error="Stop did not release body";
            }
            string result=(error==null?"PASS":"FAIL")+"\nGoGo poses checked="+count+"\n"+error;
            File.WriteAllText(Path.Combine(directory,"result.txt"),result);Debug.Log("AVATARLAB_GOGO "+result);Application.Quit(error==null?0:1);
        }
    }
}
