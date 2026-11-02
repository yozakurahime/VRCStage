using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class PerformanceProbe : MonoBehaviour
    {
        [Serializable] public class Cost { public string name; public double msPerFrame,callsPerFrame; }
        [Serializable] public class Phase { public string name; public float fps,frameMs; public Cost[] costs; }
        [Serializable] public class Report { public string device,avatar; public int width,height; public List<Phase> phases=new List<Phase>(); public string[] physics,hairMeshes; }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs(); if(!args.Contains("--performance-probe")) yield break;
            var app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+90;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            if(app.driver==null) { Application.Quit(1); yield break; }
            yield return new WaitForSeconds(1);
            var report=new Report {device=SystemInfo.graphicsDeviceName,avatar=app.currentAvatar.name,width=Screen.width,height=Screen.height};
            var chains=app.currentAvatar.GetComponentsInChildren<AvatarPhysicsChain>(true);
            report.physics=chains.Select(c=>c.name+" | root="+PathTo(c.Root)+" | enabled="+c.enabled+" | active="+c.gameObject.activeInHierarchy+" | nodes="+c.Count+" | multi="+c.multiChildType+" | endpoint="+c.endpointPosition+" | ignored="+c.ignoreTransforms.Length+" | bones="+string.Join(",",Enumerable.Range(0,c.Count).Select(c.PointName))).ToArray();
            report.hairMeshes=app.currentAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.IndexOf("hair",StringComparison.OrdinalIgnoreCase)>=0).Select(r=>r.name+" | active="+r.gameObject.activeInHierarchy+" enabled="+r.enabled+" | bones="+string.Join(",",r.bones.Where(b=>b!=null).Select(b=>b.name))).ToArray();
            string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"performance-probe.json"); File.WriteAllText(output,JsonUtility.ToJson(report,true));
            app.driver.UseManualEvaluation();
            if(args.Contains("--probe-live-sensors")) { app.optical.input.StartLeap(false); app.faces.ConnectIfacial(); }
            var constraints=app.currentAvatar.GetComponent<AvatarConstraintRunner>();
            var renderers=app.currentAvatar.GetComponentsInChildren<Renderer>(true); var states=renderers.Select(r=>r.enabled).ToArray();
            var phases=new[]{"all_mirror","no_mirror","no_physics","no_constraints","no_animation","no_avatar_render","video_cpu","video_spout"};
            foreach(string phase in phases)
            {
                app.room.SetMirror(phase=="all_mirror");
                app.physicsStudio.simulate=phase!="no_physics" && phase!="no_constraints" && phase!="no_animation" && phase!="no_avatar_render";
                if(constraints!=null) constraints.enabled=phase!="no_constraints" && phase!="no_animation" && phase!="no_avatar_render";
                app.driver.AdvanceAnimation=phase!="no_animation" && phase!="no_avatar_render";
                for(int i=0;i<renderers.Length;i++) renderers[i].enabled=phase!="no_avatar_render" && states[i];
                if(phase.StartsWith("video_")) { app.video.useSpout=phase=="video_spout"; app.video.cpuReadback=phase=="video_cpu"; app.video.StartOutput(false); }
                else app.video.StopOutput();
                app.Status="Medição de desempenho: "+phase;
                yield return new WaitForSeconds(.7f);
                LabPerformance.Counters.Clear(); LabPerformance.Enabled=true;
                int frames=0; float start=Time.realtimeSinceStartup,end=start+4;
                while(Time.realtimeSinceStartup<end) { frames++; yield return null; }
                float elapsed=Time.realtimeSinceStartup-start; LabPerformance.Enabled=false;
                report.phases.Add(new Phase {name=phase,fps=frames/elapsed,frameMs=elapsed*1000/Mathf.Max(1,frames),costs=LabPerformance.Counters.Select(p=>new Cost{name=p.Key,msPerFrame=p.Value.milliseconds/Math.Max(1,frames),callsPerFrame=p.Value.calls/(double)Math.Max(1,frames)}).ToArray()});
                File.WriteAllText(output,JsonUtility.ToJson(report,true));
            }
            app.video.StopOutput(); app.optical.Stop(); app.faces.StopIfacial(); Application.Quit(0);
        }
        static string PathTo(Transform t)=>t.parent==null?t.name:PathTo(t.parent)+"/"+t.name;
    }
}
