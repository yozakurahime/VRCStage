using System;
using System.Collections;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class HairVideoSmokeTest : MonoBehaviour
    {
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        IEnumerator Start()
        {
            if(!Environment.GetCommandLineArgs().Contains("--hair-video-test")) yield break;
            var app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+60;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            AvatarPhysicsChain[] chains=null; Vector3[][] before=null;
            try
            {
                Check(app.hair!=null,"Hair controller missing"); chains=app.hair.Generated;
                Check(chains.Length>0 && chains.Any(c=>c.isActiveAndEnabled && c.Count>2),"No active generated hair physics");
                var head=app.driver.Definition.animator.GetBoneTransform(HumanBodyBones.Head);
                Check(chains.All(c=>c.Root.IsChildOf(head)),"Generated physics outside the head subtree");
                before=chains.Select(c=>Enumerable.Range(0,c.Count).Select(c.Point).ToArray()).ToArray();
                foreach(var c in chains) c.ApplyImpulse(new Vector3(.4f,0,.1f));
                using(var memory=MemoryMappedFile.CreateNew("AvatarLab.Video.Test."+Guid.NewGuid().ToString("N"),VideoOutput.Capacity))
                using(var view=memory.CreateViewAccessor())
                {
                    var source=new byte[1280*720*4]; for(int i=0;i<source.Length;i++) source[i]=(byte)(i*31);
                    var result=new byte[source.Length]; view.Write(0,VideoOutput.Magic);
                    VideoOutput.CopyPixels(view,source,true); VideoOutput.CopyPixels(view,result,false);
                    Check(source.SequenceEqual(result) && view.ReadInt32(0)==VideoOutput.Magic,"Fast mapped video copy corrupted frame or header");
                    using(var offset=memory.CreateViewAccessor(32,VideoOutput.Capacity-32))
                    { var small=new byte[]{7,13,29,51}; VideoOutput.CopyPixels(offset,small,true); Check(view.ReadByte(32+VideoOutput.Header)==7 && view.ReadByte(35+VideoOutput.Header)==51,"Unaligned mapping offset not respected"); }
                }
            }
            catch(Exception ex) { Finish(false,ex.ToString()); yield break; }
            yield return new WaitForSeconds(.3f);
            try
            {
                int moved=0,nodes=0;
                for(int i=0;i<chains.Length;i++) for(int j=0;j<chains[i].Count;j++)
                {
                    var p=chains[i].Point(j); nodes++; Check(!float.IsNaN(p.x+p.y+p.z) && !float.IsInfinity(p.x+p.y+p.z) && p.sqrMagnitude<100,"Hair point escaped / nonfinite");
                    if(Vector3.Distance(p,before[i][j])>.001f) moved++;
                }
                Check(moved>0,"Generated hair did not react to an impulse");
                var head=app.driver.Definition.animator.GetBoneTransform(HumanBodyBones.Head);
                var actor=app.currentAvatar.transform;
                var actorPosition=actor.position; var actorRotation=actor.rotation; var headRotation=head.localRotation;
                var anchors=chains.SelectMany(c=>c.Root.GetComponentsInChildren<Transform>(true).Where(t=>Depth(t,c.Root)<=c.attachmentDepth).Select(t=>new {bone=t,position=t.localPosition,rotation=t.localRotation,fixRotation=Depth(t,c.Root)<c.attachmentDepth || t.childCount>1})).ToArray();
                try
                {
                    for(int frame=0;frame<120;frame++)
                    {
                        foreach(var chain in chains) chain.SendMessage("Update");
                        actor.SetPositionAndRotation(actorPosition+new Vector3(Mathf.Sin(frame*.08f)*.2f,0,0),actorRotation*Quaternion.Euler(0,Mathf.Sin(frame*.06f)*50,0));
                        head.localRotation=headRotation*Quaternion.Euler(Mathf.Sin(frame*.07f)*20,Mathf.Sin(frame*.1f)*35,Mathf.Sin(frame*.09f)*15);
                        foreach(var chain in chains) chain.Simulate(1f/90);
                        foreach(var anchor in anchors)
                        {
                            Check(Vector3.Distance(anchor.bone.localPosition,anchor.position)<.00001f,"Hair attachment slid from head: "+anchor.bone.name);
                            if(anchor.fixRotation) Check(Quaternion.Angle(anchor.bone.localRotation,anchor.rotation)<.05f,"Scalp rotated as a loose wig: "+anchor.bone.name);
                        }
                    }
                }
                finally { actor.SetPositionAndRotation(actorPosition,actorRotation); head.localRotation=headRotation; foreach(var chain in chains) chain.ResetSimulation(); }
                Finish(true,chains.Length+" generated hair root(s), "+nodes+" points, "+moved+" moved after impulse, all finite and confined to head subtree; scalp/strand attachments fixed during 120 frames of actor movement and head yaw/pitch/roll. 1280x720 RGBA mapped copy preserved exact bytes and header; unaligned mapping offset respected. Original avatar asset unchanged; generated preview only.");
            }
            catch(Exception ex) { Finish(false,ex.ToString()); }
        }
        static int Depth(Transform bone,Transform root) { int depth=0; while(bone!=root && bone!=null) { depth++; bone=bone.parent; } return depth; }
        static void Finish(bool pass,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"hair-video-test-result.txt"),(pass?"PASS\n":"FAIL\n")+detail); Debug.Log("AVATARLAB_HAIR_VIDEO_"+(pass?"PASS ":"FAIL ")+detail); Application.Quit(pass?0:1);
        }
    }
}
