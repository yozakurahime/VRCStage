using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class FurySmokeTest : MonoBehaviour
    {
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        static Dictionary<string,float> Snapshot(GameObject root)
        {
            var values=new Dictionary<string,float>(); int i=0;
            foreach(var t in root.GetComponentsInChildren<Transform>(true)) values["object"+i++]=t.gameObject.activeSelf?1:0;
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))
            {
                values["renderer"+i++]=r.enabled?1:0;
                foreach(var m in r.sharedMaterials) values["material"+i++]=m!=null?m.GetInstanceID():0;
                if(r is SkinnedMeshRenderer s && s.sharedMesh!=null) for(int j=0;j<s.sharedMesh.blendShapeCount;j++) values["shape"+i++]=s.GetBlendShapeWeight(j);
            }
            return values;
        }
        static int Difference(Dictionary<string,float> a,Dictionary<string,float> b)=>a.Count(p=>b.TryGetValue(p.Key,out var other) && Mathf.Abs(p.Value-other)>.05f);
        IEnumerator Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--fury-test")<0) yield break;
            var app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+90;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            yield return new WaitForSeconds(.8f);
            AvatarConstraintRunner runner=null; List<ExpressionControl> toggles=null; int evaluated=0,sourceCount=0,changed=0,checkedToggles=0;
            try
            {
                Check(app.driver!=null,"Avatar did not import");
                var definition=app.driver.Definition;
                Check(definition.compatibilityNotes.Any(n=>n.StartsWith("Preparação local concluída")),"Avatar package did not run local VRCFury preprocessing");
                runner=app.currentAvatar.GetComponent<AvatarConstraintRunner>(); Check(runner!=null && runner.constraints.Length>0,"Converted constraints missing");
                foreach(var c in runner.constraints)
                {
                    if(c.Evaluations>0) evaluated++;
                    sourceCount+=Enumerable.Range(0,c.Sources.Count).Count(i=>c.Sources.Get(i).SourceTransform!=null);
                    var p=c.Target.position; Check(!float.IsNaN(p.x+p.y+p.z) && !float.IsInfinity(p.x+p.y+p.z) && p.sqrMagnitude<10000,"Constraint target escaped / nonfinite: "+c.Target.name);
                }
                Check(evaluated>0 && sourceCount>0,"Constraints did not evaluate or lost their source references");
                Check(app.currentAvatar.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c==null || !(c.GetType().Namespace??"").StartsWith("VRC.") && !(c.GetType().Namespace??"").StartsWith("VF.")),"SDK or VRCFury runtime component leaked");
                toggles=definition.controls.Where(c=>c.type==1 && !string.IsNullOrEmpty(c.parameter) && c.parameter.StartsWith("VF",StringComparison.Ordinal) && app.driver.Types.ContainsKey(c.parameter)).GroupBy(c=>c.parameter).Select(g=>g.First()).Take(24).ToList();
                Check(toggles.Count>0,"No generated VRCFury toggle parameters were imported");
            }
            catch(Exception ex) { Finish(false,ex.ToString()); yield break; }
            foreach(var toggle in toggles)
            {
                float old=app.driver.GetValue(toggle.parameter); var before=Snapshot(app.currentAvatar);
                yield return new WaitForSeconds(.15f); var idle1=Snapshot(app.currentAvatar);
                yield return new WaitForSeconds(.15f); var idle2=Snapshot(app.currentAvatar);
                var stable=before.Where(p=>idle1.TryGetValue(p.Key,out var b) && idle2.TryGetValue(p.Key,out var c) && Mathf.Abs(p.Value-b)<.01f && Mathf.Abs(p.Value-c)<.01f).ToDictionary(p=>p.Key,p=>p.Value);
                app.driver.Set(toggle.parameter,Mathf.Approximately(old,toggle.value)?0:toggle.value);
                yield return new WaitForSeconds(.4f);
                var after=Snapshot(app.currentAvatar); checkedToggles++;
                var affected=stable.Where(p=>after.TryGetValue(p.Key,out var value) && Mathf.Abs(p.Value-value)>.05f).ToArray();
                app.driver.Set(toggle.parameter,old); yield return new WaitForSeconds(.35f); var restored=Snapshot(app.currentAvatar);
                if(affected.Any(p=>restored.TryGetValue(p.Key,out var value) && Mathf.Abs(p.Value-value)<.05f)) changed++;
                if(changed>=3) break;
            }
            try
            {
                Check(changed>0,"Generated toggles did not change any object, renderer, material or blendshape");
                Finish(true,runner.constraints.Length+" converted constraints; "+evaluated+" evaluated, "+sourceCount+" source references, "+runner.CyclicDependencies+" cyclic dependencies; "+changed+" / "+checkedToggles+" generated VRCFury toggles changed stable runtime object/renderer/material/blendshape fields AND restored them after reverting the parameter (idle changes excluded); "+app.driver.Definition.controls.Count+" expression controls; no SDK/VRCFury runtime components.");
            }
            catch(Exception ex) { Finish(false,ex.ToString()); }
        }
        static void Finish(bool pass,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"fury-test-result.txt"),(pass?"PASS\n":"FAIL\n")+detail); Debug.Log("AVATARLAB_FURY_"+(pass?"PASS ":"FAIL ")+detail); Application.Quit(pass?0:1);
        }
    }
}
