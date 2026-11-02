using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace AvatarLab
{
    public sealed class VmcInputState
    {
        public struct Bone {public Vector3 position;public Quaternion rotation;public float time;}
        public readonly Dictionary<HumanBodyBones,Bone> bones=new Dictionary<HumanBodyBones,Bone>();
        public readonly Dictionary<string,float> pendingFace=new Dictionary<string,float>(StringComparer.Ordinal);
        public Bone root=new Bone{rotation=Quaternion.identity,time=-100};public bool available=true;
        public int invalid,frames;public float lastBone=-100;
        static bool Finite(float n)=>!float.IsNaN(n) && !float.IsInfinity(n);
        public static bool Number(object[] values,int i,out float value) {value=0;if(i>=values.Length || !(values[i] is float || values[i] is int)) return false;value=Convert.ToSingle(values[i]);return Finite(value);}
        public static bool Pose(object[] values,float now,out Bone bone)
        {
            bone=default;if(values.Length<8 || !(values[0] is string name) || name.Length>256) return false;
            if(!Number(values,1,out float x) || !Number(values,2,out float y) || !Number(values,3,out float z) || !Number(values,4,out float qx) || !Number(values,5,out float qy) || !Number(values,6,out float qz) || !Number(values,7,out float qw)) return false;
            var q=new Quaternion(qx,qy,qz,qw);float length=Quaternion.Dot(q,q);
            if(length<.5f || length>1.5f) return false;
            bone=new Bone{position=new Vector3(x,y,z),rotation=q.normalized,time=now};return bone.position.sqrMagnitude<1e8f;
        }
        public bool Read(OscMessage message,float now)
        {
            var v=message.values;
            switch(message.address)
            {
                case "/VMC/Ext/Root/Pos":if(Pose(v,now,out var rp)) {root=rp;return true;}break;
                case "/VMC/Ext/Bone/Pos":
                    if(v.Length>=8 && v[0] is string name && Enum.TryParse(name,false,out HumanBodyBones id) && Enum.IsDefined(typeof(HumanBodyBones),id) && id!=HumanBodyBones.LastBone && Pose(v,now,out var bp)) {bones[id]=bp;lastBone=now;return true;}break;
                case "/VMC/Ext/OK":if(v.Length>0 && v[0] is int ok) {available=ok!=0;return true;}break;
                case "/VMC/Ext/T":if(Number(v,0,out _)) {frames++;return true;}break;
                case "/VMC/Ext/Blend/Val":if(v.Length>=2 && v[0] is string key && key.Length>0 && key.Length<=128 && Number(v,1,out float value) && (pendingFace.Count<256 || pendingFace.ContainsKey(key))) {pendingFace[key]=Mathf.Clamp01(value);return true;}break;
                case "/VMC/Ext/Blend/Apply":return true;
                default:return false;
            }
            invalid++;return false;
        }
        public void Clear() {bones.Clear();pendingFace.Clear();root=new Bone{rotation=Quaternion.identity,time=-100};available=true;lastBone=-100;frames=invalid=0;}
        public static HumanBodyBones Parent(HumanBodyBones b,HashSet<HumanBodyBones> present)
        {
            HumanBodyBones Pick(params HumanBodyBones[] candidates)=>candidates.FirstOrDefault(p=>present.Contains(p));
            string name=b.ToString();bool left=name.StartsWith("Left",StringComparison.Ordinal);string side=left?"Left":"Right";
            if(name.EndsWith("Proximal",StringComparison.Ordinal)) return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"Hand");
            if(name.EndsWith("Intermediate",StringComparison.Ordinal)) return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),name.Replace("Intermediate","Proximal"));
            if(name.EndsWith("Distal",StringComparison.Ordinal)) return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),name.Replace("Distal","Intermediate"));
            switch(b)
            {
                case HumanBodyBones.Hips:return HumanBodyBones.LastBone;
                case HumanBodyBones.Spine:return HumanBodyBones.Hips;
                case HumanBodyBones.Chest:return HumanBodyBones.Spine;
                case HumanBodyBones.UpperChest:return Pick(HumanBodyBones.Chest,HumanBodyBones.Spine);
                case HumanBodyBones.Neck:return Pick(HumanBodyBones.UpperChest,HumanBodyBones.Chest,HumanBodyBones.Spine);
                case HumanBodyBones.Head:return Pick(HumanBodyBones.Neck,HumanBodyBones.UpperChest,HumanBodyBones.Chest,HumanBodyBones.Spine);
                case HumanBodyBones.LeftShoulder:case HumanBodyBones.RightShoulder:return Pick(HumanBodyBones.UpperChest,HumanBodyBones.Chest,HumanBodyBones.Spine);
                case HumanBodyBones.LeftUpperArm:case HumanBodyBones.RightUpperArm:return Pick((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"Shoulder"),HumanBodyBones.UpperChest,HumanBodyBones.Chest,HumanBodyBones.Spine);
                case HumanBodyBones.LeftLowerArm:case HumanBodyBones.RightLowerArm:return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"UpperArm");
                case HumanBodyBones.LeftHand:case HumanBodyBones.RightHand:return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"LowerArm");
                case HumanBodyBones.LeftUpperLeg:case HumanBodyBones.RightUpperLeg:return HumanBodyBones.Hips;
                case HumanBodyBones.LeftLowerLeg:case HumanBodyBones.RightLowerLeg:return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"UpperLeg");
                case HumanBodyBones.LeftFoot:case HumanBodyBones.RightFoot:return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"LowerLeg");
                case HumanBodyBones.LeftToes:case HumanBodyBones.RightToes:return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"Foot");
                default:return HumanBodyBones.LastBone;
            }
        }
    }

    [DefaultExecutionOrder(90)]
    public sealed class VmcStudio : MonoBehaviour
    {
        public LabApp app;public readonly OscReceiver receiver=new OscReceiver();public readonly VmcInputState input=new VmcInputState();
        public bool receiveBody=true,receiveHands=true,receiveFace=true,receiveParameters,receiveEvents=true,receiveCamera,receiveLight;
        public bool sendReference,sendFace=true;public int port=39539,sendPort=39540;public string sendHost="127.0.0.1";
        public float smoothing=18;public string Status="VMC local: receba movimento, calibre em T-Pose e grave em .almotion.";
        public bool Calibrated=>source!=null;public bool Sending=>sender!=null;public float Weight {get;private set;}
        public bool BodyActive=>Weight>.001f && source!=null && app.poses.Selected==null && (receiveBody || receiveHands);
        UdpClient sender;float nextSend;string portText="39539",sendPortText="39540";
        AvatarDriver bound;Animator target;HumanPoseHandler targetHandler;HumanPose working;
        RgbMotionSampler source;RgbMotion.Header calibration,sendHeader;Transform[] sendBones;Quaternion[] worldR,filteredR;Vector3[] worldP;
        HumanBodyBones[] mapping;bool[] muscleMask;Vector3 origin,basisOffset,bodyOffset,filteredHip;Quaternion basis=Quaternion.identity;bool hipsRoot;
        static readonly HumanBodyBones[] Required={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
        void Attach()
        {
            if(bound!=null) bound.ExternalBodyOverride=false;targetHandler?.Dispose();targetHandler=null;source?.Dispose();source=null;Weight=0;
            bound=app.driver;target=bound?.Definition.animator;sendHeader=null;sendBones=null;
            if(target!=null && target.isHuman) {targetHandler=new HumanPoseHandler(target.avatar,target.transform);working=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};}
        }
        public void StartReceiver()
        {
            StopReceiver();input.Clear();receiver.Start(port,false);
            if(receiver.Running && receiveFace) {app.ClearManualFace();app.faces.profile.enabled=true;if(app.faces.profile.routes.Count==0) app.faces.Suggest();}
            Status=receiver.Running?"Recebendo em 127.0.0.1:"+port+". Faça T-Pose na origem e calibre.":receiver.Error;
        }
        public void StopReceiver()
        {
            receiver.Dispose();source?.Dispose();source=null;Weight=0;if(bound!=null) bound.ExternalBodyOverride=false;
            input.Clear();Status="VMC parado; tracking local liberado.";
        }
        void Update()
        {
            if(bound!=app.driver) Attach();int limit=2048;
            while(limit-->0 && receiver.TryDequeue(out var message)) Read(message);
            bool fresh=input.available && Time.unscaledTime-input.lastBone<.2f && Time.unscaledTime-input.root.time<.5f && Required.All(b=>b==HumanBodyBones.Hips && hipsRoot || input.bones.TryGetValue(b,out var value) && Time.unscaledTime-value.time<.5f);
            Weight=Mathf.MoveTowards(Weight,fresh && source!=null?1:0,Time.unscaledDeltaTime*5);
            if(bound!=null) bound.ExternalBodyOverride=BodyActive;
        }
        static string FaceName(string name)=>name=="blinkLeft"?"eyeBlinkLeft":name=="blinkRight"?"eyeBlinkRight":FaceChannels.Canonical(name);
        public void Read(OscMessage message)
        {
            float now=Time.unscaledTime;
            if(input.Read(message,now))
            {
                if(message.address=="/VMC/Ext/Blend/Apply")
                {
                    if(receiveFace) foreach(var p in input.pendingFace) app.faces.ReceiveOsc(FaceName(p.Key),p.Value);
                    input.pendingFace.Clear();
                }
                return;
            }
            var v=message.values;
            if(message.address=="/NyaVMC/Trigger" && receiveEvents && v.Length>=1 && v[0] is string trigger && trigger.Length<=128)
                app.actions.Fire(trigger,VmcInputState.Number(v,1,out var amount)?amount:0);
            else if(receiveParameters && message.address.StartsWith("/NyaVMC/Ext/Anim/",StringComparison.Ordinal) && v.Length>=2 && v[0] is string path && (path.Length==0 || path=="." || path==app.currentAvatar?.name) && v[1] is int hash && bound!=null)
            {
                string name=bound.Types.Keys.FirstOrDefault(n=>Animator.StringToHash(n)==hash);
                if(name!=null && message.address.EndsWith("TriggerParam",StringComparison.Ordinal)) bound.SetValue(name,1);
                else if(name!=null && v.Length>=3 && (VmcInputState.Number(v,2,out float value) || v[2] is bool)) bound.SetValue(name,v[2] is bool flag?flag?1:0:Convert.ToSingle(v[2]));
            }
            else if(message.address=="/VMC/Ext/Cam" && receiveCamera && v.Length>=9 && VmcInputState.Pose(v,now,out var camera) && VmcInputState.Number(v,8,out float fov))
            {var shot=app.cinema.shots[app.cinema.selected];shot.mode=0;shot.lookAt=false;shot.position=camera.position;shot.euler=camera.rotation.eulerAngles;shot.fov=Mathf.Clamp(fov,10,120);}
            else if(message.address=="/VMC/Ext/Light" && receiveLight && v.Length>=12 && VmcInputState.Pose(v,now,out var light) && VmcInputState.Number(v,8,out float r) && VmcInputState.Number(v,9,out float g) && VmcInputState.Number(v,10,out float b))
            {app.room.key.transform.SetPositionAndRotation(light.position,light.rotation);app.room.key.color=new Color(Mathf.Clamp01(r),Mathf.Clamp01(g),Mathf.Clamp01(b));}
        }
        void Worlds()
        {
            for(int i=0;i<mapping.Length;i++)
            {
                var local=input.bones.TryGetValue(mapping[i],out var bone)?bone:new VmcInputState.Bone{rotation=Quaternion.identity};
                int p=calibration.parents[i];Quaternion parent=p<0?input.root.rotation:worldR[p];Vector3 position=p<0?input.root.position:worldP[p];
                worldR[i]=parent*local.rotation;worldP[i]=position+parent*local.position;
            }
        }
        public bool Calibrate()
        {
            try
            {
                if(bound!=app.driver) Attach();if(targetHandler==null) throw new InvalidOperationException("Carregue um Humanoid.");
                hipsRoot=!input.bones.ContainsKey(HumanBodyBones.Hips);
                foreach(var b in Required) if(!(b==HumanBodyBones.Hips && hipsRoot && Time.unscaledTime-input.root.time<.5f) && (!input.bones.TryGetValue(b,out var bone) || Time.unscaledTime-bone.time>.5f)) throw new InvalidOperationException("Aguardando osso VMC: "+b);
                var present=new HashSet<HumanBodyBones>(input.bones.Keys.Where(b=>b!=HumanBodyBones.Jaw && b!=HumanBodyBones.LeftEye && b!=HumanBodyBones.RightEye));present.Add(HumanBodyBones.Hips);
                int Depth(HumanBodyBones b) {int n=0;for(;b!=HumanBodyBones.LastBone && n<60;b=VmcInputState.Parent(b,present)) n++;return n;}
                mapping=present.OrderBy(Depth).ToArray();calibration=new RgbMotion.Header{format=1,name="VMC",source="VMC",fps=30,bones=mapping.Select(b=>b.ToString()).ToArray(),parents=mapping.Select(b=>Array.IndexOf(mapping,VmcInputState.Parent(b,present))).ToArray(),restPositions=new Vector3[mapping.Length],restRotations=new Quaternion[mapping.Length]};
                worldR=new Quaternion[mapping.Length];worldP=new Vector3[mapping.Length];Worlds();origin=worldP[0];
                Vector3 P(HumanBodyBones b)=>worldP[Array.IndexOf(mapping,b)];
                var up=(P(HumanBodyBones.Head)-origin).normalized;var right=(P(HumanBodyBones.RightUpperArm)-P(HumanBodyBones.LeftUpperArm)).normalized;
                basis=Quaternion.LookRotation(Vector3.Cross(right,up).normalized,up);var inverse=Quaternion.Inverse(basis);
                foreach(var side in new[]{"Left","Right"})
                {
                    var shoulder=(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"UpperArm");var elbow=(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"LowerArm");var wrist=(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+"Hand");
                    if(Mathf.Abs(Vector3.Dot((P(elbow)-P(shoulder)).normalized,right))<.9f || Mathf.Abs(Vector3.Dot((P(wrist)-P(elbow)).normalized,right))<.9f) throw new InvalidOperationException("A origem precisa estar em T-Pose, com braços e cotovelos esticados.");
                }
                float floor=Mathf.Min((inverse*(P(HumanBodyBones.LeftFoot)-origin)).y,(inverse*(P(HumanBodyBones.RightFoot)-origin)).y);
                foreach(var toe in new[]{HumanBodyBones.LeftToes,HumanBodyBones.RightToes}) if(present.Contains(toe)) floor=Mathf.Min(floor,(inverse*(P(toe)-origin)).y);
                basisOffset=Vector3.up*-floor;
                for(int i=0;i<mapping.Length;i++) {calibration.restPositions[i]=inverse*(worldP[i]-origin)+basisOffset;calibration.restRotations[i]=inverse*worldR[i];}
                var candidate=new RgbMotionSampler(calibration);source?.Dispose();source=candidate;
                filteredR=(Quaternion[])calibration.restRotations.Clone();filteredHip=calibration.restPositions[0];
                targetHandler.GetHumanPose(ref working);var localBody=target.transform.InverseTransformPoint(working.bodyPosition*target.humanScale)/target.humanScale;
                float targetFloor=Required.Where(b=>b==HumanBodyBones.LeftFoot || b==HumanBodyBones.RightFoot).Min(b=>target.transform.InverseTransformPoint(target.GetBoneTransform(b).position).y);
                foreach(var toe in new[]{HumanBodyBones.LeftToes,HumanBodyBones.RightToes}) {var t=target.GetBoneTransform(toe);if(t!=null) targetFloor=Mathf.Min(targetFloor,target.transform.InverseTransformPoint(t.position).y);}
                bodyOffset=new Vector3(localBody.x-source.NeutralBody.x,targetFloor/target.humanScale-source.NeutralFoot,localBody.z-source.NeutralBody.z);
                muscleMask=new bool[HumanTrait.MuscleCount];for(int i=0;i<muscleMask.Length;i++) {int b=HumanTrait.BoneFromMuscle(i);muscleMask[i]=b>=0 && present.Contains((HumanBodyBones)b);}
                Status="VMC calibrado: "+mapping.Length+" ossos"+(hipsRoot?" • raiz/quadril Jayo":" • raiz VMC padrão");return true;
            }
            catch(Exception ex) {source?.Dispose();source=null;Weight=0;if(bound!=null) bound.ExternalBodyOverride=false;Status=ex.Message;return false;}
        }
        void LateUpdate()
        {
            if(BodyActive && targetHandler!=null)
            {
                Worlds();var inverse=Quaternion.Inverse(basis);float blend=smoothing<=0?1:1-Mathf.Exp(-smoothing*Time.unscaledDeltaTime);
                for(int i=0;i<worldR.Length;i++) filteredR[i]=Quaternion.Slerp(filteredR[i],inverse*worldR[i],blend);
                filteredHip=Vector3.Lerp(filteredHip,inverse*(worldP[0]-origin)+basisOffset,blend);var pose=source.SampleLive(filteredHip,filteredR);
                targetHandler.GetHumanPose(ref working);var localBody=target.transform.InverseTransformPoint(working.bodyPosition*target.humanScale)/target.humanScale;var localRotation=Quaternion.Inverse(target.transform.rotation)*working.bodyRotation;
                for(int i=0;i<working.muscles.Length;i++)
                {
                    int b=HumanTrait.BoneFromMuscle(i);bool hand=b>=0 && ((HumanBodyBones)b==HumanBodyBones.LeftHand || (HumanBodyBones)b==HumanBodyBones.RightHand || b>=(int)HumanBodyBones.LeftThumbProximal && b<=(int)HumanBodyBones.RightLittleDistal);
                    if(muscleMask[i] && (hand?receiveHands:receiveBody)) working.muscles[i]=Mathf.Lerp(working.muscles[i],pose.muscles[i],Weight);
                }
                working.bodyPosition=receiveBody?Vector3.Lerp(localBody,pose.bodyPosition+bodyOffset,Weight):localBody;working.bodyRotation=receiveBody?Quaternion.Slerp(localRotation,pose.bodyRotation,Weight):localRotation;targetHandler.SetHumanPose(ref working);
            }
            if(sender!=null && target!=null && Time.unscaledTime>=nextSend)
            {
                nextSend=Time.unscaledTime+1f/30;
                try {foreach(var packet in Outgoing()) sender.Send(packet,packet.Length);}catch(Exception ex) {Status=ex.Message;StopSender();}
            }
        }
        public IEnumerable<byte[]> Outgoing()
        {
            if(target==null) yield break;
            if(sendHeader==null) sendHeader=MotionCaptureStudio.Describe(target,out sendBones);
            var packets=new List<byte[]>();var root=target.transform;var rp=sendReference?Vector3.zero:root.position;var rq=sendReference?Quaternion.identity:root.rotation;
            packets.Add(OscCodec.Encode("/VMC/Ext/Root/Pos","root",rp.x,rp.y,rp.z,rq.x,rq.y,rq.z,rq.w));
            for(int i=0;i<sendBones.Length;i++)
            {
                int parent=sendHeader.parents[i];Vector3 p;Quaternion q;
                if(sendReference) {p=parent<0?sendHeader.restPositions[i]:Quaternion.Inverse(sendHeader.restRotations[parent])*(sendHeader.restPositions[i]-sendHeader.restPositions[parent]);p*=root.lossyScale.x;q=parent<0?sendHeader.restRotations[i]:Quaternion.Inverse(sendHeader.restRotations[parent])*sendHeader.restRotations[i];}
                else {var t=sendBones[i];var pt=parent<0?root:sendBones[parent];p=Quaternion.Inverse(pt.rotation)*(t.position-pt.position);q=Quaternion.Inverse(pt.rotation)*t.rotation;}
                packets.Add(OscCodec.Encode("/VMC/Ext/Bone/Pos",sendHeader.bones[i],p.x,p.y,p.z,q.x,q.y,q.z,q.w));
            }
            if(sendFace) foreach(var pair in app.faces.LiveValues.Select(p=>new KeyValuePair<string,float>(FaceChannels.Canonical(p.Key),p.Value)).GroupBy(p=>p.Key).Select(g=>g.First()).Take(128)) packets.Add(OscCodec.Encode("/VMC/Ext/Blend/Val",pair.Key,Mathf.Clamp01(app.faces.ReadChannel(pair.Key))));
            packets.Add(OscCodec.Encode("/VMC/Ext/Blend/Apply"));packets.Add(OscCodec.Encode("/VMC/Ext/OK",1));packets.Add(OscCodec.Encode("/VMC/Ext/T",Time.unscaledTime));
            yield return OscCodec.Bundle(packets);
        }
        public void StartSender()
        {
            StopSender();try {if(!IPAddress.TryParse(sendHost,out var ip) || !IPAddress.IsLoopback(ip)) throw new InvalidOperationException("O envio desta versão é local: use 127.0.0.1 ou ::1.");if(sendPort==port && receiver.Running) throw new InvalidOperationException("Use portas diferentes para evitar retorno do próprio movimento.");sender=new UdpClient(ip.AddressFamily);sender.Connect(ip,sendPort);Status="Enviando VMC para "+sendHost+":"+sendPort;}catch(Exception ex) {StopSender();Status=ex.Message;}
        }
        public void StopSender() {sender?.Dispose();sender=null;}
        public void DrawUI()
        {
            GUILayout.Label("Ponte VNyan / VMC");GUILayout.Label(Status);GUILayout.Label("Mensagens "+receiver.Received+" • quadros "+input.frames+" • inválidas "+input.invalid+" • aplicação "+Weight.ToString("P0"));
            portText=GUILayout.TextField(portText,5);if(int.TryParse(portText,out int p) && p>1024 && p<65536) port=p;
            if(GUILayout.Button(receiver.Running?"PARAR RECEPÇÃO VMC":"RECEBER VMC • localhost")) {if(receiver.Running) StopReceiver();else StartReceiver();}
            receiveBody=GUILayout.Toggle(receiveBody,"Aplicar corpo e braços");receiveHands=GUILayout.Toggle(receiveHands,"Aplicar mãos e dedos");receiveFace=GUILayout.Toggle(receiveFace,"Receber canais faciais (mapear em Face / voz)");
            if(GUILayout.Button("CALIBRAR • ORIGEM EM T-POSE")) Calibrate();smoothing=StudioGui.Slider("Suavização VMC",smoothing,0,35);
            GUILayout.Label("Use uma origem Humanoid com hierarquia compatível. A calibração evita copiar eixos de ossos entre rigs diferentes. Prévia de poses tem prioridade; perda de dados libera o tracking local.");
            receiveEvents=GUILayout.Toggle(receiveEvents,"Receber gatilhos NyaVMC");receiveParameters=GUILayout.Toggle(receiveParameters,"Sincronizar parâmetros NyaVMC do avatar raiz");receiveCamera=GUILayout.Toggle(receiveCamera,"Receber câmera VMC");receiveLight=GUILayout.Toggle(receiveLight,"Receber luz VMC");
            GUILayout.Label("Enviar movimento do AvatarLab (local)");sendHost=GUILayout.TextField(sendHost);sendPortText=GUILayout.TextField(sendPortText,5);if(int.TryParse(sendPortText,out int sp) && sp>1024 && sp<65536) sendPort=sp;
            sendReference=GUILayout.Toggle(sendReference,"Transmitir T-Pose de referência para calibração");sendFace=GUILayout.Toggle(sendFace,"Transmitir canais faciais");
            if(GUILayout.Button(Sending?"PARAR ENVIO VMC":"ENVIAR VMC")) {if(Sending) StopSender();else StartSender();}
        }
        void OnDestroy() {StopReceiver();StopSender();targetHandler?.Dispose();}
    }
}
