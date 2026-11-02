using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace AvatarLab
{
    public sealed class WorldLightingSmokeTest : MonoBehaviour
    {
        [Serializable] public sealed class Report {public bool pass;public string error;public int probes,avatarProbeRenderers;public float imageDifference,probeVariation,spatialImageDifference;public string ambientMode;}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--world-lighting-test")) yield break;
            var app=FindObjectOfType<LabApp>();float end=Time.realtimeSinceStartup+120;
            while((app.driver==null || app.stage.Busy || app.stage.ObjectCount==0) && Time.realtimeSinceStartup<end) yield return null;
            yield return new WaitForSeconds(1);
            var report=new Report();int at=Array.IndexOf(args,"--world-test-dir");string directory=at>=0 && at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"WorldLightingValidation");Directory.CreateDirectory(directory);
            var probes=LightmapSettings.lightProbes;RenderTexture rt=null;Texture2D read=null;
            try
            {
                if(app.driver==null || probes==null || probes.count==0) throw new Exception("World baked probes not loaded");
                app.enabled=false;app.motor.enabled=false;app.physicsStudio.simulate=false;app.driver.UseManualEvaluation();app.driver.EvaluateManual(0);
                report.probes=probes.count;report.ambientMode=RenderSettings.ambientMode.ToString();
                report.avatarProbeRenderers=app.currentAvatar.GetComponentsInChildren<Renderer>().Count(r=>r.lightProbeUsage!=LightProbeUsage.Off);
                if(report.avatarProbeRenderers==0) throw new Exception("Avatar renderers do not use probes");
                var baked=probes.bakedProbes;float first=baked[0][0,0];
                foreach(var sh in baked) for(int c=0;c<3;c++) for(int k=0;k<9;k++) if(float.IsNaN(sh[c,k]) || float.IsInfinity(sh[c,k])) throw new Exception("Invalid world probe coefficients");
                report.probeVariation=baked.Max(p=>Mathf.Abs(p[0,0]-first));if(report.probeVariation<.0001f) throw new Exception("World lighting probes have no spatial variation");
                app.cinema.transition=0;app.photo.Frame(0);app.cinema.Evaluate(0);
                rt=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);rt.Create();read=new Texture2D(512,512,TextureFormat.RGB24,false);
                var ambientMode=RenderSettings.ambientMode;var ambient=RenderSettings.ambientLight;
                LightmapSettings.lightProbes=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=app.stage.Selected.GetComponent<StageDefinition>().ambient;
                app.photo.RenderTo(rt,true,1,Color.black);Read(rt,read);var before=read.GetPixels32();File.WriteAllBytes(Path.Combine(directory,"Avatar-before-probes.png"),read.EncodeToPNG());
                LightmapSettings.lightProbes=probes;RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;
                app.photo.RenderTo(rt,true,1,Color.black);Read(rt,read);var after=read.GetPixels32();File.WriteAllBytes(Path.Combine(directory,"Avatar-with-world-light.png"),read.EncodeToPNG());
                double difference=0;int changed=0;for(int i=0;i<before.Length;i++) {difference+=Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b);if(before[i].r!=after[i].r || before[i].g!=after[i].g || before[i].b!=after[i].b) changed++;}
                report.imageDifference=(float)(difference/(before.Length*3*255));if(changed<100 || report.imageDifference<.0001f) throw new Exception("Avatar pixels did not respond to world probe lighting: "+report.imageDifference);
                var renderers=app.currentAvatar.GetComponentsInChildren<Renderer>();var anchors=renderers.Select(r=>r.probeAnchor).ToArray();var anchor=new GameObject("Lighting test anchor");
                try
                {
                    var indices=Enumerable.Range(0,baked.Length).OrderBy(i=>baked[i][0,0]+baked[i][1,0]+baked[i][2,0]).ToArray();
                    foreach(var r in renderers) r.probeAnchor=anchor.transform;
                    anchor.transform.position=probes.positions[indices.First()];app.photo.RenderTo(rt,true,1,Color.black);Read(rt,read);var dark=read.GetPixels32();
                    anchor.transform.position=probes.positions[indices.Last()];app.photo.RenderTo(rt,true,1,Color.black);Read(rt,read);var bright=read.GetPixels32();
                    difference=0;for(int i=0;i<dark.Length;i++) difference+=Math.Abs(dark[i].r-bright[i].r)+Math.Abs(dark[i].g-bright[i].g)+Math.Abs(dark[i].b-bright[i].b);
                    report.spatialImageDifference=(float)(difference/(dark.Length*3*255));if(report.spatialImageDifference<.0001f) throw new Exception("Avatar did not respond to different world probe positions");
                }
                finally {for(int i=0;i<renderers.Length;i++) renderers[i].probeAnchor=anchors[i];Destroy(anchor);}
                app.photo.lighting.Preset(0);app.photo.lighting.settings.enabled=false;app.photo.lighting.Apply();
                if(RenderSettings.ambientMode!=ambientMode || LightmapSettings.lightProbes!=probes) throw new Exception("Studio lighting did not restore world lighting");
                app.stage.SetLab(true);if(LightmapSettings.lightProbes!=null) throw new Exception("Lab kept world probes");
                app.stage.SetLab(false);if(LightmapSettings.lightProbes==null || LightmapSettings.lightProbes.count!=report.probes) throw new Exception("World probes did not restore after lab toggle");
                report.pass=true;
            }
            catch(Exception ex) {report.error=ex.ToString();}
            finally {LightmapSettings.lightProbes=probes;if(rt!=null) {rt.Release();Destroy(rt);}if(read!=null) Destroy(read);}
            File.WriteAllText(Path.Combine(directory,"world-lighting-result.json"),JsonUtility.ToJson(report,true));Debug.Log("AVATARLAB_WORLD_LIGHTING "+JsonUtility.ToJson(report));Application.Quit(report.pass?0:1);
        }
        static void Read(RenderTexture rt,Texture2D read) {var previous=RenderTexture.active;try {RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();}finally {RenderTexture.active=previous;}}
    }
}
