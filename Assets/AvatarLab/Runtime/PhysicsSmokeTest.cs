using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class PhysicsSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            if(!Environment.GetCommandLineArgs().Contains("--physics-test")) yield break;
            var app=FindObjectOfType<LabApp>(); float deadline=Time.realtimeSinceStartup+60;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<deadline) yield return null;
            if(app.currentAvatar==null) { Finish(false,"Avatar did not load"); yield break; }
            yield return new WaitForSeconds(.3f);
            var chains=app.currentAvatar.GetComponentsInChildren<AvatarPhysicsChain>(true);
            var colliders=app.currentAvatar.GetComponentsInChildren<AvatarPhysicsCollider>(true);
            if(chains.Length==0 || colliders.Length==0) { Finish(false,"No converted chains/colliders"); yield break; }
            var active=chains.Where(c=>c.isActiveAndEnabled && c.Count>1).ToArray();
            if(active.Length==0) { Finish(false,"No active nonempty physics chain"); yield break; }
            foreach(var c in active) c.ApplyImpulse(new Vector3(.3f,.05f,.1f));
            float end=Time.unscaledTime+2;
            int checkedPoints=0;
            while(Time.unscaledTime<end)
            {
                foreach(var c in active) for(int i=0;i<c.Count;i++)
                {
                    var p=c.Point(i);
                    if(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z) || p.sqrMagnitude>10000) { Finish(false,"Nonfinite / escaped simulation point in "+c.name); yield break; }
                    checkedPoints++;
                }
                yield return null;
            }
            var grab=active.FirstOrDefault(c=>c.allowGrabbing);
            if(grab!=null)
            {
                for(int i=1;i<grab.Count && !grab.IsGrabbed;i++) grab.Grab(i,grab.Point(i));
                if(!grab.IsGrabbed) { Finish(false,"Grabbable component has no movable points"); yield break; }
                yield return new WaitForSeconds(.1f); grab.Release();
                if(grab.IsGrabbed) { Finish(false,"Grab was not released"); yield break; }
            }
            foreach(var c in chains) c.ResetSimulation();
            if(chains.Any(c=>c.IsPosed || c.IsGrabbed)) { Finish(false,"Pose/grab survived reset"); yield break; }
            if(app.currentAvatar.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c!=null && c.GetType().FullName.StartsWith("VRC."))) { Finish(false,"SDK runtime script present"); yield break; }
            Finish(true,chains.Length+" chains, "+colliders.Length+" colliders, "+active.Sum(c=>c.Count)+" active points; "+checkedPoints+" finite-state checks during impulse simulation; grab/release and pose reset; no SDK runtime components.");
        }
        static void Finish(bool pass,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"physics-player-result.txt"),(pass?"PASS\n":"FAIL\n")+detail);
            Debug.Log("AVATARLAB_PHYSICS_"+(pass?"PASS ":"FAIL ")+detail); Application.Quit(pass?0:1);
        }
    }
}
