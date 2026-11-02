using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
namespace AvatarLab
{
    public sealed class IfacialSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("--ifacial-test")) yield break;
            var app = FindObjectOfType<LabApp>(); float end = Time.realtimeSinceStartup+50;
            while(app.currentAvatar == null && Time.realtimeSinceStartup < end) yield return null;
            if(app.currentAvatar == null) { Finish(false,"Avatar did not load"); yield break; }
            var f=app.faces; f.profile.enabled=true;
            app.enabled=false;app.motor.enabled=false;app.driver.UseManualEvaluation();app.driver.EvaluateManual(0);
            f.profile.headTracking=f.profile.eyeTracking=true;f.profile.headNeutral=f.profile.leftEyeNeutral=f.profile.rightEyeNeutral=Vector3.zero;f.profile.headScale=f.profile.eyeScale=Vector3.one;f.profile.neckShare=.35f;f.profile.smoothing=40;
            var animator=app.driver.Definition.animator;var parts=new[]{HumanBodyBones.Head,HumanBodyBones.Neck,HumanBodyBones.LeftEye,HumanBodyBones.RightEye};
            var bones=parts.Select(p=>animator.GetBoneTransform(p)).ToArray();var rest=bones.Select(b=>b!=null?b.localRotation:Quaternion.identity).ToArray();
            var probe=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)); int port=((IPEndPoint)probe.Client.LocalEndPoint).Port; probe.Close();
            f.ifacial.Start("",port,true);
            using(var sender=new UdpClient())
            {
                var data=Encoding.UTF8.GetBytes("jawOpen&65|eyeBlink_L&75|eyeBlink_R&75|mouthSmile_L&40|mouthSmile_R&40|=head#15,25,10,0,0,0|rightEye#-8,12,0|leftEye#10,15,0|");
                for(int i=0;i<30;i++) { sender.Send(data,data.Length,new IPEndPoint(IPAddress.Loopback,port)); yield return new WaitForSeconds(.03f); }
            }
            yield return null;
            string detail=null,error=null;
            try
            {
                if(f.ifacial.Received<20 || f.ifacial.Invalid!=0 || f.AppliedTargets==0) throw new Exception("Frame decode/routing failed: "+f.ifacial.Received+" / "+f.AppliedTargets);
                int jaw=0, blink=0, deforms=0;
                foreach(var r in app.currentAvatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(!r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh==null) continue;
                    for(int i=0;i<r.sharedMesh.blendShapeCount;i++)
                    {
                        string canonical=FaceChannels.Canonical(r.sharedMesh.GetBlendShapeName(i));
                        if(canonical!="jawOpen" && canonical!="eyeBlinkLeft") continue;
                        float expected=canonical=="jawOpen"?65:75;
                        if(Mathf.Abs(r.GetBlendShapeWeight(i)-expected)>4) throw new Exception("Actual avatar shape did not receive tracking: "+r.name+" / "+r.sharedMesh.GetBlendShapeName(i)+" = "+r.GetBlendShapeWeight(i));
                        if(canonical=="jawOpen") jaw++; else blink++;
                        var delta=new Vector3[r.sharedMesh.vertexCount]; r.sharedMesh.GetBlendShapeFrameVertices(i,r.sharedMesh.GetBlendShapeFrameCount(i)-1,delta,null,null);
                        if(delta.Any(v=>v.sqrMagnitude>1e-10f)) deforms++;
                    }
                }
                if(jaw==0 || blink==0 || deforms==0) throw new Exception("No visible jaw/blink/deforming shapes were validated.");
                for(int i=0;i<bones.Length;i++) if(bones[i]==null || Quaternion.Angle(rest[i],bones[i].localRotation)<3) throw new Exception("Rotation not applied to "+parts[i]);
                var onlyEyes=IfacialCodec.Decode("rightEye#1#2#3|leftEye#4,5,6|");if(!onlyEyes.hasLeftEye || !onlyEyes.hasRightEye || onlyEyes.hasHead) throw new Exception("Eye-only protocol failed");
                var malformed=IfacialCodec.Decode("jawOpen&20|leftEye#NaN,1,0|");if(malformed.hasLeftEye) throw new Exception("Invalid eye angles accepted");
                detail="Actual UDP v2 -> canonical channels -> "+f.AppliedTargets+" visible targets; jaw "+jaw+" and blink "+blink+" at requested weights; "+deforms+" shapes have nonzero geometry deltas. Actual Head, Neck, LeftEye and RightEye rotations changed; eye-only/hash-separated packets accepted; nonfinite eye rotation rejected.";
            }
            catch(Exception ex) {error=ex.ToString();}
            yield return new WaitForSeconds(.8f);
            if(error==null) for(int i=0;i<bones.Length;i++) if(Quaternion.Angle(rest[i],bones[i].localRotation)>.1f) error="Lost tracking did not release "+parts[i];
            Finish(error==null,error??detail+" Stale rotations release without accumulating.");
        }
        static void Finish(bool ok,string detail)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"ifacial-test-result.txt"),(ok?"PASS\n":"FAIL\n")+detail);
            Debug.Log("AVATARLAB_IFACIAL_"+(ok?"PASS ":"FAIL ")+detail); Application.Quit(ok?0:1);
        }
    }
}
