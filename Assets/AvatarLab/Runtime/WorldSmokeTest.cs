using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AvatarLab
{
    public sealed class WorldSmokeTest : MonoBehaviour
    {
        [Serializable] public sealed class Report {public bool pass;public string error;public int renderers,colliders,lightmaps;public string[] unsupportedShaders;public Vector3 avatar;public float averageFrameMs;}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--world-test")) yield break;
            var app=FindObjectOfType<LabApp>();int at=Array.IndexOf(args,"--world-test-dir");string directory=at>=0 && at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"WorldValidation");Directory.CreateDirectory(directory);
            int studioAt=Array.IndexOf(args,"--studio");var report=new Report();float end=Time.realtimeSinceStartup+120;
            while((app.driver==null || app.stage.Busy || app.stage.ObjectCount==0) && Time.realtimeSinceStartup<end) yield return null;
            yield return new WaitForSeconds(.5f);
            try
            {
                if(studioAt<0 || app.stage.Selected==null || app.stage.Busy) throw new Exception("World did not load: "+app.stage.Status);
                var session=JsonUtility.FromJson<StudioSession>(File.ReadAllText(args[studioAt+1]));report.avatar=app.motor.Root.position;
                if(Vector3.Distance(report.avatar,session.avatarPosition)>.1f) throw new Exception("Avatar did not reach the saved world spawn");
                var world=app.stage.Selected;var meshes=world.GetComponentsInChildren<Renderer>(true);report.renderers=meshes.Length;report.colliders=world.GetComponentsInChildren<Collider>(true).Length;report.lightmaps=LightmapSettings.lightmaps.Length;
                if(report.renderers<1 || report.colliders<1 || app.stage.showLab) throw new Exception("World geometry/colliders or room visibility incorrect");
                report.unsupportedShaders=meshes.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.shader).Where(s=>s==null || !s.isSupported || s.name=="Hidden/InternalErrorShader").Select(s=>s!=null?s.name:"null").Distinct().ToArray();
                if(report.unsupportedShaders.Length>0) throw new Exception("Unsupported world shaders: "+string.Join(", ",report.unsupportedShaders));
                var floors=Physics.RaycastAll(report.avatar+Vector3.up*1.5f,Vector3.down,3).Where(h=>h.collider.transform.IsChildOf(world) && h.point.y<=report.avatar.y+.2f).ToArray();
                if(floors.Length==0) throw new Exception("No world floor collider below avatar spawn");
                app.cinema.transition=0;app.cinema.Evaluate(0);app.photo.Capture(Path.Combine(directory,"World-loaded.png"));
            }
            catch(Exception ex) {report.error=ex.ToString();}
            end=Time.realtimeSinceStartup+30;while(app.photo.Saving && Time.realtimeSinceStartup<end) yield return null;
            double time=0;int count=0;while(count<45) {time+=Time.unscaledDeltaTime;count++;yield return null;}report.averageFrameMs=(float)(time/count*1000);
            if(report.error==null && !File.Exists(Path.Combine(directory,"World-loaded.png"))) report.error="World photo failed: "+app.photo.Status;
            report.pass=report.error==null;File.WriteAllText(Path.Combine(directory,"world-test-result.json"),JsonUtility.ToJson(report,true));Debug.Log("AVATARLAB_WORLD "+JsonUtility.ToJson(report));Application.Quit(report.pass?0:1);
        }
    }
}
