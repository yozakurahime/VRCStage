using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AvatarLab
{
    public sealed class PhotoSmokeTest : MonoBehaviour
    {
        static void Check(bool value,string why) {if(!value) throw new Exception(why);}
        static Texture2D Pixels(RenderTexture rt)
        {
            var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);t.Apply();RenderTexture.active=previous;return t;
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--photo-test")) yield break;
            var app=FindObjectOfType<LabApp>();float end=Time.realtimeSinceStartup+60;while(app.driver==null && Time.realtimeSinceStartup<end) yield return null;yield return null;
            string error=null;bool pass=true;void Guard(Action action) {if(!pass) return;try {action();}catch(Exception ex) {pass=false;error=ex.ToString();}}
            int at=Array.IndexOf(args,"--photo-test-dir");string directory=at>=0 && at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"PhotoValidation");Directory.CreateDirectory(directory);
            string session=Path.Combine(directory,"photo-validation-studio.json");Camera camera=app.cinema.Camera;var initialTarget=camera.targetTexture;var initialAspect=camera.aspect;int initialMask=camera.cullingMask;
            Guard(()=>
            {
                Check(Resources.Load<Shader>("AvatarLabGrade")?.isSupported==true,"Color shader missing/unsupported in Windows player");app.cinema.transition=0;app.photo.Frame(0);
                var a=app.driver.Definition.animator;foreach(var bone in new[]{HumanBodyBones.Head,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}) {var p=camera.WorldToViewportPoint(a.GetBoneTransform(bone).position);Check(p.z>0 && p.y>.025f && p.y<.975f,"Full body frame clips "+bone+": "+p);}
                app.photo.lighting.Preset(0);Check(app.room.key.transform.parent!=app.room.transform,"Studio lamps were not independent from inspection room");
                app.stage.SetLab(false);app.photo.lighting.Apply();Check(app.room.key.isActiveAndEnabled,"Hiding room also hid studio lighting");app.stage.SetLab(true);app.photo.Frame(2);app.photo.LookPreset(1);app.photo.settings.resolution=2;app.photo.settings.avatarOnly=true;app.photo.settings.background=2;
                app.photo.Capture(Path.Combine(directory,"Beauty-portrait.png"));Check(app.photo.Saving,"Photo did not start writing: "+app.photo.Status);
            });
            end=Time.realtimeSinceStartup+20;while(app.photo.Saving && Time.realtimeSinceStartup<end) yield return null;
            Guard(()=>
            {
                string path=Path.Combine(directory,"Beauty-portrait.png");Check(File.Exists(path),app.photo.Status);var t=new Texture2D(2,2);Check(t.LoadImage(File.ReadAllBytes(path)),"PNG cannot be decoded");Check(t.width==2160 && t.height==2700,"Portrait size incorrect");var pixels=t.GetPixels32();Check(pixels.Count(p=>p.a==0)>pixels.Length/5 && pixels.Count(p=>p.a>240)>pixels.Length/20,"PNG alpha/avatar missing");Destroy(t);
                Check(camera.targetTexture==initialTarget && camera.cullingMask==initialMask && Mathf.Abs(camera.aspect-initialAspect)<.001f,"Photo changed video camera state");
                app.photo.settings.background=0;app.photo.settings.avatarOnly=false;app.photo.settings.resolution=0;app.photo.lighting.Preset(3);app.photo.LookPreset(3);app.photo.Frame(1);app.cinema.shots[app.cinema.selected].roll=12;app.cinema.Evaluate(0);
                app.photo.Capture(Path.Combine(directory,"Neon-cinema.png"));
            });
            end=Time.realtimeSinceStartup+20;while(app.photo.Saving && Time.realtimeSinceStartup<end) yield return null;
            RenderTexture rt=null;Texture2D p0=null,p1=null;Klak.Spout.SpoutReceiver receiver=null;
            Guard(()=>
            {
                Check(File.Exists(Path.Combine(directory,"Neon-cinema.png")),app.photo.Status);
                app.photo.settings.look=new PhotoLook();rt=new RenderTexture(64,64,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();camera.targetTexture=rt;camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.08f,.12f,.25f);camera.Render();p0=Pixels(rt);
                app.photo.settings.look.exposure=1;camera.Render();p1=Pixels(rt);var a=p0.GetPixel(32,32);var b=p1.GetPixel(32,32);Check(b.r>a.r+.03f && b.g>a.g+.03f && Mathf.Abs(a.a-b.a)<.01f,"GPU exposure or alpha preservation failed");
                app.photo.LookPreset(4);camera.Render();Destroy(p1);p1=Pixels(rt);var mono=p1.GetPixel(32,32);Check(Mathf.Abs(mono.r-mono.g)<.01f && Mathf.Abs(mono.g-mono.b)<.01f,"GPU monochrome did not apply");
                camera.targetTexture=initialTarget;camera.cullingMask=initialMask;camera.aspect=initialAspect;app.photo.lighting.Preset(1);app.photo.settings.resolution=3;app.photo.LookPreset(2);app.cinema.shots[app.cinema.selected].roll=17;app.cinema.transition=.8f;app.stage.Save(session);
            });
            if(p0!=null) Destroy(p0);if(p1!=null) Destroy(p1);if(rt!=null) {rt.Release();Destroy(rt);}
            if(pass) yield return app.stage.Load(session);
            Guard(()=>
            {
                Check(app.photo.lighting.settings.enabled && Mathf.Abs(app.photo.lighting.settings.lamps[0].azimuth+55)<.01f,"Studio lighting persistence failed");Check(app.photo.settings.resolution==3 && app.photo.settings.look.film && Mathf.Abs(app.cinema.shots[app.cinema.selected].roll-17)<.01f && Mathf.Abs(app.cinema.transition-.8f)<.01f,"Look / roll / output persistence failed");
                app.photo.settings.background=2;app.video.avatarOnly=true;app.video.useSpout=true;app.video.cpuReadback=true;app.video.width=1280;app.video.StartOutput(false);Check(app.video.Running,"Video did not start");
                receiver=gameObject.AddComponent<Klak.Spout.SpoutReceiver>();receiver.SetResources(app.spoutResources);receiver.sourceName=app.video.SpoutName;
            });
            int frames=app.video.Frames;yield return new WaitForSeconds(.35f);
            end=Time.realtimeSinceStartup+5;while(receiver!=null && receiver.receivedTexture==null && Time.realtimeSinceStartup<end) yield return null;
            Guard(()=>Check(app.video.Frames>frames,"Video did not continue after photo / session load"));
            Guard(()=>
            {
                var spout=app.GetComponent<Klak.Spout.SpoutSender>();Check(spout!=null && spout.keepAlpha && spout.sourceTexture==app.video.Texture,"Spout did not inherit transparent studio background");
                Check(receiver!=null && receiver.receivedTexture!=null && receiver.receivedTexture.width==app.video.width,"Native Spout receiver did not receive the output texture");
                var frame=Pixels(app.video.Texture);var values=frame.GetPixels32();Check(values.Any(v=>v.a==0) && values.Any(v=>v.a>240),"Live video alpha/avatar missing");Destroy(frame);
                app.photo.Capture(Path.Combine(directory,"During-broadcast.png"));Check(app.cinema.Camera.targetTexture==app.video.Texture,"Capturing stole the live camera render target");
            });
            end=Time.realtimeSinceStartup+20;while(app.photo.Saving && Time.realtimeSinceStartup<end) yield return null;
            Guard(()=>Check(File.Exists(Path.Combine(directory,"During-broadcast.png")) && app.video.Running && app.video.Frames>frames,"Photo while broadcasting did not complete"));
            app.video.StopOutput();if(receiver!=null) Destroy(receiver);camera.targetTexture=initialTarget;camera.cullingMask=initialMask;camera.aspect=initialAspect;if(File.Exists(session)) File.Delete(session);
            string result=pass?"PASS\nGPU shader, full-body framing, lamps independent from room, 2160x2700 transparent avatar PNG, styled neon photo, alpha preservation, exposure/monochrome pixels, camera state restoration, lighting/look/camera persistence, native Spout sender/receiver, transparent live video and photo during broadcast. Private sample photos retained outside delivery.":"FAIL\n"+error;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"photo-test-result.txt"),result);Debug.Log("AVATARLAB_PHOTO "+result);Application.Quit(pass?0:1);
        }
    }
}
