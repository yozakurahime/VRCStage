using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace AvatarLab
{
    public sealed class PostProcessSmokeTest : MonoBehaviour
    {
        static Texture2D Read(RenderTexture rt) {var previous=RenderTexture.active;try {RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();return t;}finally {RenderTexture.active=previous;}}
        static float Difference(Color32[] a,Color32[] b) {double result=0;for(int i=0;i<a.Length;i++) result+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return (float)(result/(a.Length*3*255));}
        static void Check(bool pass,string message) {if(!pass) throw new Exception(message);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--post-test")) yield break;
            var app=FindObjectOfType<LabApp>();float end=Time.realtimeSinceStartup+90;while(app.driver==null && Time.realtimeSinceStartup<end) yield return null;yield return null;
            int at=Array.IndexOf(args,"--post-test-dir");string dir=at>=0 && at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"PostValidation");Directory.CreateDirectory(dir);
            string error=null,detail="";bool pass=true;void Guard(Action action) {if(!pass) return;try {action();}catch(Exception ex) {pass=false;error=ex.ToString();}}
            var camera=app.cinema.Camera;RenderTexture source=null,target=null;Texture2D input=null,pixels=null;GameObject cube=null;Material cubeMaterial=null;Klak.Spout.SpoutReceiver receiver=null;
            var effects=new CameraPostEffects();
            Guard(()=>
            {
                Check(app.driver!=null && effects.Supported,"Post shader missing or unsupported");
                source=new RenderTexture(128,128,0,RenderTextureFormat.ARGB32);source.Create();target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);target.Create();
                input=new Texture2D(128,128,TextureFormat.RGBA32,false);var colors=Enumerable.Repeat(new Color(0,0,0,.25f),128*128).ToArray();for(int y=48;y<80;y++) for(int x=48;x<80;x++) colors[y*128+x]=new Color(1,.7f,.3f,.75f);input.SetPixels(colors);input.Apply();Graphics.Blit(input,source);
                effects.Render(source,target,new CameraPostSettings(),true,false);pixels=Read(target);var neutral=pixels.GetPixels32();Destroy(pixels);
                var settings=new CameraPostSettings{enabled=true,bloom=true,bloomIntensity=1,bloomThreshold=.4f,bloomRadius=3};effects.Render(source,target,settings,true,false);pixels=Read(target);var bloom=pixels.GetPixels32();Check(Difference(neutral,bloom)>.001f && pixels.GetPixel(43,64).r>.01f,"Bloom did not spread beyond bright source");Check(neutral.Zip(bloom,(a,b)=>Math.Abs(a.a-b.a)).Max()<=1,"Bloom unexpectedly changed alpha");Destroy(pixels);
                settings.bloomAlpha=true;effects.Render(source,target,settings,true,false);pixels=Read(target);Check(pixels.GetPixel(46,64).a>=.25f,"Bloom alpha invalid");Destroy(pixels);
                settings=new CameraPostSettings{enabled=true,chromatic=1,grain=.1f};effects.Render(source,target,settings,true,false);pixels=Read(target);Check(Difference(neutral,pixels.GetPixels32())>.002f,"Chromatic/grain post did not affect GPU pixels");Destroy(pixels);
                detail="GPU bloom halo, alpha preservation, chromatic/grain pixels; ";
                app.photo.preview=app.photo.mainPreview=false;app.enabled=false;app.motor.enabled=false;app.cinema.enabled=false;app.physicsStudio.simulate=false;
                camera.targetTexture=target;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.transform.SetPositionAndRotation(new Vector3(0,1,-3),Quaternion.identity);camera.fieldOfView=60;
                cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.layer=29;cube.transform.position=new Vector3(-.4f,1,0);cube.transform.localScale=Vector3.one*.7f;cube.GetComponent<Renderer>().motionVectorGenerationMode=MotionVectorGenerationMode.Object;
                cubeMaterial=new Material(Shader.Find("Standard"));cubeMaterial.color=Color.white;cubeMaterial.EnableKeyword("_EMISSION");cubeMaterial.SetColor("_EmissionColor",Color.white);cube.GetComponent<Renderer>().sharedMaterial=cubeMaterial;
                app.photo.settings.post=new CameraPostSettings{enabled=true,motionBlur=true,shutter=1,maxMotionPixels=48,quality=1};camera.Render();
            });
            yield return null;
            Guard(()=>
            {
                cube.transform.position+=Vector3.right*.6f;camera.Render();pixels=Read(target);var blurred=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(dir,"Motion-blur.png"),pixels.EncodeToPNG());Destroy(pixels);
                app.photo.Post.enabled=false;camera.Render();pixels=Read(target);var sharp=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(dir,"Motion-sharp.png"),pixels.EncodeToPNG());Destroy(pixels);
                float motion=Difference(blurred,sharp);Check(motion>.0005f,"Actual camera/object motion vectors did not blur moving object: "+motion);detail+="real moving cube motion vectors difference="+motion+"; ";
                app.photo.settings.post=new CameraPostSettings{enabled=true,depthOfField=true,focusDistance=.2f,focusRange=.3f,focusBlur=12};camera.Render();pixels=Read(target);Check(Difference(sharp,pixels.GetPixels32())>.0005f,"Camera depth did not produce defocus");Destroy(pixels);
                DestroyImmediate(cube);cube=null;camera.targetTexture=null;app.photo.Frame(1);app.cinema.transition=0;app.cinema.Evaluate(0);app.photo.settings.post=new CameraPostSettings{enabled=true,bloom=true,bloomIntensity=.5f,motionBlur=true,shutter=.35f};
                var session=Path.Combine(dir,"post-session.json");app.stage.Save(session);var saved=JsonUtility.FromJson<StudioSession>(File.ReadAllText(session));Check(saved.photo.post.enabled && saved.photo.post.bloom && saved.photo.post.motionBlur,"Studio did not save post settings");
                app.video.width=1280;app.video.cpuReadback=false;app.video.useSpout=true;app.video.transparent=true;app.video.avatarOnly=true;app.video.StartOutput(false);Check(app.video.Running,app.video.Status);
                receiver=gameObject.AddComponent<Klak.Spout.SpoutReceiver>();receiver.SetResources(app.spoutResources);receiver.sourceName=app.video.SpoutName;
            });
            int frames=app.video.RenderedFrames;yield return new WaitForSeconds(.5f);end=Time.realtimeSinceStartup+5;while(pass && receiver!=null && receiver.receivedTexture==null && Time.realtimeSinceStartup<end) yield return null;
            Guard(()=>
            {
                Check(app.video.RenderedFrames>frames && receiver!=null && receiver.receivedTexture!=null,"Processed video did not reach native Spout receiver");pixels=Read(app.video.Texture);Check(pixels.GetPixels32().Any(p=>p.a==0) && pixels.GetPixels32().Any(p=>p.a>240),"Processed video lost avatar/alpha");File.WriteAllBytes(Path.Combine(dir,"Bloom-stream.png"),pixels.EncodeToPNG());Destroy(pixels);
                app.photo.settings.resolution=0;app.photo.settings.avatarOnly=true;app.photo.settings.background=2;app.photo.Capture(Path.Combine(dir,"Post-photo.png"));Check(camera.targetTexture==app.video.Texture,"Post photo stole video target");detail+="camera depth defocus, saved settings, processed native Spout video with alpha and still photo during output.";
            });
            end=Time.realtimeSinceStartup+20;while(app.photo.Saving && Time.realtimeSinceStartup<end) yield return null;
            Guard(()=>Check(File.Exists(Path.Combine(dir,"Post-photo.png")) && app.video.Running,"Post photo did not complete during video"));
            app.video.StopOutput();if(receiver!=null) Destroy(receiver);camera.targetTexture=null;RenderTexture.active=null;effects.Dispose();if(source!=null) {source.Release();Destroy(source);}if(target!=null) {target.Release();Destroy(target);}if(input!=null) Destroy(input);if(cube!=null) Destroy(cube);if(cubeMaterial!=null) Destroy(cubeMaterial);
            string result=(pass?"PASS\n"+detail:"FAIL\n"+error);File.WriteAllText(Path.Combine(dir,"post-test-result.txt"),result);Debug.Log("AVATARLAB_POST "+result);Application.Quit(pass?0:1);
        }
    }
}
