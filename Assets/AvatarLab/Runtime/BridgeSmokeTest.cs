using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarLab
{
    public sealed class BridgeSmokeTest : MonoBehaviour
    {
        static void Check(bool value,string error) {if(!value) throw new Exception(error);}
        static int Port(bool tcp)
        {
            if(tcp) {var p=new TcpListener(IPAddress.Loopback,0);p.Start();int n=((IPEndPoint)p.LocalEndpoint).Port;p.Stop();return n;}
            using(var p=new UdpClient(new IPEndPoint(IPAddress.Loopback,0))) return ((IPEndPoint)p.Client.LocalEndPoint).Port;
        }
        static byte[] Frame(RgbMotion data,int frame,bool jayo=false)
        {
            var h=data.Info;var positions=new Vector3[h.bones.Length];var rotations=new Quaternion[h.bones.Length];
            Quaternion global=Quaternion.Euler(0,37,0);Vector3 offset=new Vector3(2,0,-4);
            for(int i=0;i<positions.Length;i++)
            {
                int p=h.parents[i];rotations[i]=data.Rotations[frame*positions.Length+i];
                positions[i]=p<0?data.Roots[frame]:positions[p]+rotations[p]*Quaternion.Inverse(h.restRotations[p])*(h.restPositions[i]-h.restPositions[p]);
            }
            var messages=new List<byte[]>();Vector3 root=jayo?offset+global*positions[0]:offset;Quaternion rootR=jayo?global*rotations[0]:global;
            messages.Add(OscCodec.Encode("/VMC/Ext/Root/Pos","root",root.x,root.y,root.z,rootR.x,rootR.y,rootR.z,rootR.w));
            for(int i=0;i<positions.Length;i++)
            {
                if(jayo && i==0) continue;int p=h.parents[i];var pos=p<0?positions[i]:Quaternion.Inverse(rotations[p])*(positions[i]-positions[p]);var rot=p<0?rotations[i]:Quaternion.Inverse(rotations[p])*rotations[i];
                messages.Add(OscCodec.Encode("/VMC/Ext/Bone/Pos",h.bones[i],pos.x,pos.y,pos.z,rot.x,rot.y,rot.z,rot.w));
            }
            messages.Add(OscCodec.Encode("/VMC/Ext/Blend/Val","jawOpen",.65f));messages.Add(OscCodec.Encode("/VMC/Ext/Blend/Val","blinkLeft",.75f));messages.Add(OscCodec.Encode("/VMC/Ext/Blend/Apply"));messages.Add(OscCodec.Encode("/VMC/Ext/OK",1));messages.Add(OscCodec.Encode("/VMC/Ext/T",frame/60f));return OscCodec.Bundle(messages);
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--bridges-test")) yield break;
            var app=FindObjectOfType<LabApp>();float deadline=Time.realtimeSinceStartup+60;
            while((app.driver==null || app.poses.Items.Count==0) && Time.realtimeSinceStartup<deadline) yield return null;
            yield return new WaitForSeconds(.25f);
            string failure=null,saved=null,graphFile=null;bool pass=true;RgbMotion fixture=null;byte[] neutral=null,raised=null;Vector3 capturedHand=Vector3.zero;
            var graphs=app.actions.Graphs.ToArray();var originalFace=JsonUtility.ToJson(app.faces.profile);var originalProps=JsonUtility.ToJson(app.props.settings);var actor=app.motor.Root;var actorPosition=actor.position;var actorRotation=actor.rotation;var actorScale=actor.localScale;
            ClientWebSocket client=null;UdpClient sender=null;int vmcPort=Port(false),wsPort=Port(true);Task network=null;
            void Guard(Action action) {if(!pass) return;try {action();}catch(Exception ex) {pass=false;failure=ex.ToString();}}
            Guard(()=>
            {
                int at=Array.IndexOf(args,"--vmc-fixture");Check(at>=0 && at+1<args.Length,"Missing --vmc-fixture");fixture=RgbMotion.Read(args[at+1]);neutral=Frame(fixture,0);raised=Frame(fixture,60);
                app.driver.UseManualEvaluation();app.poses.Stop();app.vmc.port=vmcPort;app.vmc.smoothing=0;app.vmc.StartReceiver();Check(app.vmc.receiver.Running,"VMC bind failed");sender=new UdpClient();sender.Connect(IPAddress.Loopback,vmcPort);sender.Send(neutral,neutral.Length);
            });
            yield return new WaitForSeconds(.12f);
            Guard(()=>{Check(app.vmc.input.bones.Count>=50,"UDP bundle did not decode the body");Check(!app.vmc.Calibrated && !app.driver.ExternalBodyOverride,"Uncalibrated input moved the body");Check(app.vmc.Calibrate(),"VMC calibration failed: "+app.vmc.Status);});
            for(int i=0;i<20 && pass;i++) {sender.Send(raised,raised.Length);app.driver.EvaluateManual(0);yield return new WaitForSeconds(.025f);}
            Guard(()=>
            {
                var a=app.driver.Definition.animator;var hand=a.GetBoneTransform(HumanBodyBones.RightHand);var left=a.GetBoneTransform(HumanBodyBones.LeftHand);var head=a.GetBoneTransform(HumanBodyBones.Head);
                Check(app.driver.ExternalBodyOverride && hand.position.y>head.position.y+.03f && left.position.y<head.position.y,"Calibrated VMC body did not reach test avatar / mirrored: "+hand.position+" / "+head.position);
                Check(Mathf.Abs(app.faces.LiveValues["jawOpen"]-.65f)<.01f && Mathf.Abs(app.faces.LiveValues["eyeBlinkLeft"]-.75f)<.01f,"VMC face Apply did not reach canonical channels");
                int visible=0;foreach(var r in app.currentAvatar.GetComponentsInChildren<SkinnedMeshRenderer>()) if(r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh!=null) for(int b=0;b<r.sharedMesh.blendShapeCount;b++) if(FaceChannels.Canonical(r.sharedMesh.GetBlendShapeName(b))=="jawOpen" && r.GetBlendShapeWeight(b)>55) visible++;
                Check(visible>0,"VMC did not deform a visible mouth target");
                var before=hand.position;app.vmc.Read(new OscMessage{address="/VMC/Ext/Bone/Pos",values=new object[]{"RightHand",0f,0f,0f,0f,0f,0f,float.NaN}});Check(app.vmc.input.invalid>0 && (before-hand.position).sqrMagnitude<1e-8f,"Non-finite quaternion accepted");
                app.poses.Play(app.poses.Items.First(p=>p.id=="relaxed"));app.poses.SendMessage("LateUpdate");Check(!app.vmc.BodyActive,"VMC overrode library preview");app.poses.Stop();
            });
            for(int i=0;i<4 && pass;i++) {sender.Send(raised,raised.Length);app.driver.EvaluateManual(0);yield return new WaitForSeconds(.025f);}
            Guard(()=>
            {
                var a=app.driver.Definition.animator;capturedHand=a.GetBoneTransform(HumanBodyBones.RightHand).position-a.GetBoneTransform(HumanBodyBones.Hips).position;
                app.capture.takeName="Bridge temporary validation";Check(app.capture.StartRecording(),app.capture.Status);app.capture.CaptureFrame();app.capture.CaptureFrame();app.capture.StopRecording();
            });
            deadline=Time.realtimeSinceStartup+20;while(app.capture.Saving && Time.realtimeSinceStartup<deadline) yield return null;
            Guard(()=>
            {
                saved=app.capture.LastPath;Check(!app.capture.Saving && File.Exists(saved),"Recording did not save: "+app.capture.Status);var clip=RgbMotion.Read(saved);
                Check(clip.Info.format==2 && clip.Info.faceNames.Contains("jawOpen") && clip.Faces.Any(v=>v>.6f),"Native recording lost face channels");
                app.vmc.StopReceiver();app.mocap.RequestPlay(app.poses.Items.Single(i=>i.motionPath==saved));
            });
            // Play through the public asynchronous motion-library path.
            deadline=Time.realtimeSinceStartup+15;while(app.mocap.Busy && Time.realtimeSinceStartup<deadline) yield return null;
            Guard(()=>{Check(app.poses.Selected?.motionPath!=null,"Recorded clip did not play");app.poses.Pause(true);app.poses.SendMessage("LateUpdate");app.faces.SendMessage("LateUpdate");Check(Math.Abs(app.faces.ReadChannel("jawOpen")-.65f)<.02f,"Recorded face did not play back");
                var a=app.driver.Definition.animator;var replayHand=a.GetBoneTransform(HumanBodyBones.RightHand).position;
                var relativeHand=replayHand-a.GetBoneTransform(HumanBodyBones.Hips).position;
                Check(replayHand.y>a.GetBoneTransform(HumanBodyBones.Head).position.y && Vector3.Distance(relativeHand,capturedHand)<.08f,"Recorded body changed posture: "+capturedHand+" -> "+relativeHand);
                app.poses.Stop();
                app.vmc.StartReceiver();var n=Frame(fixture,0,true);sender.Send(n,n.Length);
            });
            yield return new WaitForSeconds(.12f);
            Guard(()=>Check(app.vmc.Calibrate(),"Jayo root/hips calibration failed: "+app.vmc.Status));
            for(int i=0;i<18 && pass;i++) {var packet=Frame(fixture,60,true);sender.Send(packet,packet.Length);app.driver.EvaluateManual(0);yield return new WaitForSeconds(.025f);}
            Guard(()=>{var a=app.driver.Definition.animator;Check(a.GetBoneTransform(HumanBodyBones.RightHand).position.y>a.GetBoneTransform(HumanBodyBones.Head).position.y,"Jayo root/hips mode failed");});
            yield return new WaitForSeconds(.9f);
            Guard(()=>
            {
                Check(!app.driver.ExternalBodyOverride,"Tracking did not release after stream loss");
                app.actions.Graphs.Clear();var graph=new StudioActionGraph{name="Bridge temporary validation"};
                graph.nodes.Add(new StudioActionNode{id=0,kind=StudioActionKind.Event,text="network_test",next=new[]{1}});graph.nodes.Add(new StudioActionNode{id=1,kind=StudioActionKind.Drop,value=3,next=new[]{2}});graph.nodes.Add(new StudioActionNode{id=2,kind=StudioActionKind.Camera,value=0});
                app.actions.Graphs.Add(graph);graphFile=app.actions.Save(graph);ActionStudio.Validate(graph);app.actions.socket.Start(wsPort);Check(app.actions.socket.Running,"WebSocket could not bind without admin: "+app.actions.socket.Error);
                client=new ClientWebSocket();network=client.ConnectAsync(new Uri("ws://127.0.0.1:"+wsPort+"/vnyan"),CancellationToken.None);
            });
            deadline=Time.realtimeSinceStartup+10;while(network!=null && !network.IsCompleted && Time.realtimeSinceStartup<deadline) yield return null;
            Guard(()=>{Check(network.IsCompleted,"WebSocket handshake timed out");network.GetAwaiter().GetResult();var msg=Encoding.UTF8.GetBytes("{\"event\":\"network_test\",\"value\":7}");network=client.SendAsync(new ArraySegment<byte>(msg),WebSocketMessageType.Text,true,CancellationToken.None);});
            yield return new WaitForSeconds(.2f);
            Guard(()=>
            {
                network.GetAwaiter().GetResult();Check(app.actions.socket.Received>0 && app.props.Count==3,"WebSocket event -> graph -> physics props failed: "+app.actions.socket.Error+" / "+app.actions.Status);
                int props=app.props.Count;app.actions.Command("unknown_event");app.actions.Step(Time.unscaledTime);Check(app.props.Count==props,"Unknown event executed an action");
                app.actions.Cancel();var graph=app.actions.Graphs[0];graph.nodes.Add(new StudioActionNode{id=3,kind=StudioActionKind.Delay,value=.5f,next=new[]{4}});graph.nodes.Add(new StudioActionNode{id=4,kind=StudioActionKind.Drop,value=1});graph.nodes[0].next=new[]{3};
                app.actions.Fire("network_test");app.actions.Step(Time.unscaledTime);Check(app.props.Count==props,"Delay was ignored");app.actions.Step(Time.unscaledTime+1);Check(app.props.Count==props+1,"Delayed branch did not execute");
                graph.nodes[4].next=new[]{4};bool refused=false;try {ActionStudio.Validate(graph);}catch(InvalidDataException) {refused=true;}Check(refused,"Untimed graph cycle accepted");graph.nodes[4].next=Array.Empty<int>();
                app.vmc.receiveParameters=true;var p=app.driver.Types.FirstOrDefault(v=>v.Value!=AnimatorControllerParameterType.Trigger);Check(p.Key!=null,"No avatar parameter to test");float original=app.driver.GetValue(p.Key);float expected=p.Value==AnimatorControllerParameterType.Bool?original>.5f?0:1:p.Value==AnimatorControllerParameterType.Int?original+1:original+.35f;app.vmc.Read(new OscMessage{address="/NyaVMC/Ext/Anim/FloatParam",values=new object[]{"",Animator.StringToHash(p.Key),expected}});Check(Math.Abs(app.driver.GetValue(p.Key)-expected)<.01f,"NyaVMC root parameter failed");app.driver.SetValue(p.Key,original);
                app.actions.Cancel();for(int i=0;i<1000;i++) app.actions.Fire("network_test");Check(app.actions.Pending<=256 && app.actions.Dropped>0,"Event queue is unbounded");app.actions.Cancel();
                app.vmc.sendReference=true;foreach(var packet in app.vmc.Outgoing()) Check(OscCodec.Decode(packet).Count>40,"VMC sender did not emit a complete body bundle");
            });
            Guard(()=>
            {
                // Native interaction path: every network endpoint stopped, no companion app.
                app.vmc.StopReceiver();app.vmc.StopSender();app.actions.socket.Dispose();client?.Dispose();sender?.Dispose();app.actions.Cancel();app.props.Clear();
                Check(!app.actions.socket.Running && !app.vmc.receiver.Running && !app.vmc.Sending,"Native test still has an external endpoint enabled");
            });
            yield return null;
            Guard(()=>
            {
                app.props.settings.size=.24f;app.props.settings.mass=.2f;app.props.settings.gravity=false;app.props.settings.lifetime=1;app.props.settings.color=Color.red;
                var graph=app.actions.Graphs[0];graph.nodes.Clear();graph.nodes.Add(new StudioActionNode{id=0,kind=StudioActionKind.Event,text="native_test",next=new[]{1}});graph.nodes.Add(new StudioActionNode{id=1,kind=StudioActionKind.Drop,value=2});
                for(int shape=0;shape<3;shape++) {app.props.settings.shape=shape;app.actions.Fire("native_test");app.actions.Step(Time.unscaledTime);}
                Check(app.props.Count==6,"Native button/event -> physical objects requires a network connection");
                var bodies=FindObjectsOfType<Rigidbody>().Where(b=>b.name=="AvatarLab interactive prop").ToArray();
                Check(bodies.Length==6 && bodies.All(b=>!b.useGravity && Mathf.Abs(b.mass-.2f)<.001f && Mathf.Abs(b.transform.localScale.x-.24f)<.001f),"Native prop physics configuration lost");
                Check(bodies.Any(b=>b.GetComponent<SphereCollider>()!=null) && bodies.Any(b=>b.GetComponent<BoxCollider>()!=null) && bodies.Any(b=>b.GetComponent<CapsuleCollider>()!=null),"Native object shapes were not created");
                foreach(var body in bodies) {var tint=new MaterialPropertyBlock();body.GetComponent<Renderer>().GetPropertyBlock(tint);Check(tint.GetColor("_Color")==Color.red,"Native object tint lost");}
            });
            yield return new WaitForSeconds(1.15f);
            Guard(()=>Check(app.props.Count==0,"Native objects did not expire"));
            // Restore all user settings; delete only artifacts created by this validation.
            app.vmc.StopReceiver();app.vmc.StopSender();app.actions.Cancel();app.actions.socket.Dispose();client?.Dispose();sender?.Dispose();app.poses.Stop();app.props.Clear();
            app.faces.profile=JsonUtility.FromJson<FaceProfile>(originalFace);app.faces.Rebind();app.props.settings=JsonUtility.FromJson<InteractivePropSettings>(originalProps);app.actions.Graphs.Clear();app.actions.Graphs.AddRange(graphs);
            actor.SetPositionAndRotation(actorPosition,actorRotation);actor.localScale=actorScale;
            if(saved!=null && File.Exists(saved)) File.Delete(saved);if(graphFile!=null && File.Exists(graphFile)) File.Delete(graphFile);app.mocap.RefreshItems();
            string result=pass?"PASS\nUDP OSC bundles -> calibrated VMC Humanoid -> test avatar; standard rotated root and Jayo root/hips mode; actual visible face shape; preview precedence, malformed pose rejection, stream-loss release; native body/face ALMOTION v2 capture/playback; real WebSocket client/server -> bounded action graph -> Rigidbody props; delays, cycles, unknown events, NyaVMC root parameters, VMC sender bundle. Synthetic sender; no live VNyan plugin test. User settings restored; temporary files removed.":"FAIL\n"+failure;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"bridges-test-result.txt"),result);Debug.Log("AVATARLAB_BRIDGES "+result);Application.Quit(pass?0:1);
        }
    }
}
