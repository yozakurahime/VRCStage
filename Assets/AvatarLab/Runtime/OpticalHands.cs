using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LeapInternal;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class OpticalHand
    {
        public bool left, elbowValid;
        public float confidence = 1;
        public Vector3 elbow;
        public Vector3[] joints;
    }
    [Serializable] public sealed class OpticalFrame
    {
        public int version = 1;
        public string source;
        public OpticalHand[] hands;
    }
    // Provider coordinates: metres, Unity handedness. Joints follow MediaPipe's 21-point order.
    public sealed class OpticalInput : IDisposable
    {
        public volatile string Status = "Desligado";
        volatile bool running;
        Thread worker;
        UdpClient udp;
        OpticalFrame latest;
        readonly object gate = new object();
        public bool Running => running;
        public OpticalFrame Read() { lock (gate) { var f = latest; latest = null; return f; } }
        public static bool Valid(OpticalFrame f)
        {
            if (f == null || f.version != 1 || f.hands == null || f.hands.Length > 2 || (f.source != "leap" && f.source != "webcam")) return false;
            var seen = new HashSet<bool>();
            foreach (var h in f.hands)
            {
                if (h == null || !seen.Add(h.left) || h.joints == null || h.joints.Length != 21 || !Finite(h.confidence) || h.confidence < 0 || h.confidence > 1) return false;
                if (h.elbowValid && !ValidPoint(h.elbow)) return false;
                foreach (var p in h.joints) if (!ValidPoint(p)) return false;
            }
            return true;
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool ValidPoint(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && p.sqrMagnitude < 100;
        void Publish(OpticalFrame f) { if (Valid(f)) lock (gate) latest = f; }
        public void StartWebcam(int port)
        {
            Dispose();
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)); udp.Client.ReceiveTimeout = 250;
                running = true; Status = "Aguardando ponte webcam em 127.0.0.1:" + port;
                worker = new Thread(() =>
                {
                    while (running) try
                    {
                        var endpoint = new IPEndPoint(IPAddress.Any, 0); var data = udp.Receive(ref endpoint);
                        if (data.Length > 24000) continue;
                        var frame = JsonUtility.FromJson<OpticalFrame>(Encoding.UTF8.GetString(data));
                        if (frame != null && frame.source == "webcam") { Publish(frame); Status = "Webcam: dados locais recebidos"; }
                    }
                    catch (SocketException) { } catch (ObjectDisposedException) { break; } catch (Exception) { Status = "Quadro webcam inválido ignorado"; }
                }) { IsBackground = true }; worker.Start();
            }
            catch (Exception ex) { Dispose(); Status = ex.Message; }
        }
        static Vector3 Point(LEAP_VECTOR p) => new Vector3(p.x, p.y, -p.z) * .001f;
        static OpticalHand ConvertHand(LEAP_HAND h)
        {
            var joints = new Vector3[21]; joints[0] = Point(h.arm.next_joint);
            var fingers = new[] { h.thumb, h.index, h.middle, h.ring, h.pinky };
            for (int i = 0; i < 5; i++)
            {
                int j = 1 + i * 4;
                joints[j] = Point(i == 0 ? fingers[i].metacarpal.prev_joint : fingers[i].proximal.prev_joint);
                joints[j + 1] = Point(fingers[i].proximal.next_joint);
                joints[j + 2] = Point(fingers[i].intermediate.next_joint);
                joints[j + 3] = Point(fingers[i].distal.next_joint);
            }
            return new OpticalHand { left = h.type == eLeapHandType.eLeapHandType_Left, joints = joints, elbow = Point(h.arm.prev_joint), elbowValid = true, confidence = Mathf.Clamp01(h.confidence) };
        }
        public void StartLeap(bool mounted)
        {
            Dispose(); running = true; Status = "Conectando ao serviço Ultraleap...";
            worker = new Thread(() =>
            {
                try
                {
                    using (var native = new LeapNativeClient())
                    {
                    var mode = mounted ? eLeapTrackingMode.eLeapTrackingMode_HMD : eLeapTrackingMode.eLeapTrackingMode_Desktop;
                    Status = "Aguardando resposta do serviço Ultraleap...";
                    var elapsed = Stopwatch.StartNew(); long frames=0, lastFrame=0;
                    var message = new LEAP_CONNECTION_MESSAGE { size = (uint)Marshal.SizeOf(typeof(LEAP_CONNECTION_MESSAGE)) };
                    int stride = Marshal.SizeOf(typeof(LEAP_HAND));
                    while (running)
                    {
                        var result=native.Poll(ref message);
                        if (result != eLeapRS.eLeapRS_Success)
                        {
                            if(result!=eLeapRS.eLeapRS_Timeout) Status="Leap: "+result;
                            else if(elapsed.ElapsedMilliseconds-lastFrame>3000) Status="Sem quadros do Leap. Verifique sensor/serviço no painel Ultraleap.";
                            continue;
                        }
                        if (message.type == eLeapEventType.eLeapEventType_Connection) { native.Configure(mode); Status="Serviço conectado; aguardando sensor."; }
                        if (message.type == eLeapEventType.eLeapEventType_Device) Status="Sensor detectado; aguardando quadros.";
                        if (message.type == eLeapEventType.eLeapEventType_ConnectionLost || message.type == eLeapEventType.eLeapEventType_DeviceLost || message.type == eLeapEventType.eLeapEventType_DeviceFailure)
                        { Publish(new OpticalFrame {source="leap",hands=Array.Empty<OpticalHand>()}); Status="Leap: "+message.type; }
                        if (message.type != eLeapEventType.eLeapEventType_Tracking) continue;
                        var frame = Marshal.PtrToStructure<LEAP_TRACKING_EVENT>(message.eventStructPtr);
                        if (frame.nHands > 2 || (frame.nHands > 0 && frame.pHands == IntPtr.Zero)) continue;
                        var hands = new OpticalHand[(int)frame.nHands];
                        for (int i = 0; i < hands.Length; i++) hands[i] = ConvertHand(Marshal.PtrToStructure<LEAP_HAND>(IntPtr.Add(frame.pHands, i * stride)));
                        frames++; lastFrame=elapsed.ElapsedMilliseconds;
                        Publish(new OpticalFrame { source = "leap", hands = hands }); Status = "Leap: " + hands.Length + " mão(s) • " + frames + " quadros";
                    }
                    }
                }
                catch (Exception ex) { Status = "Leap Motion: " + ex.Message; }
                finally { running = false; }
            }) { IsBackground = true }; worker.Start();
        }
        public void Dispose()
        {
            running = false; udp?.Close();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(1500);
            worker = null; udp = null; lock (gate) latest = null;
        }
    }

    [DefaultExecutionOrder(125)]
    public sealed class OpticalHands : MonoBehaviour
    {
        public LabApp app;
        public readonly OpticalInput input = new OpticalInput();
        public bool mounted, fingersOnly, enabledTracking = true;
        public int source, cameraIndex;
        public Vector3 sensorPosition = new Vector3(0, 0, .08f), sensorEuler = new Vector3(-90, 0, 180);
        public float smoothing = 18, confidence = .5f, scale = 1, dropoutHold = .18f, dropoutFade = .25f;
        public string Status = "Conecte uma fonte, abra as mãos e calibre. Requer avatar Humanoid com dedos.";
        public bool Calibrated { get; private set; }
        public LeapDeskSetup desk;
        public readonly float[] wristError = new float[2];
        public readonly long[] appliedFrames = new long[2];
        public bool WaitingForDeskGuide { get; private set; }
        float guideDeadline, guideStable;
        public bool HasHandBinding(int side)=>offsets.ContainsKey(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
        bool deskControls=true;
        readonly OpticalHand[] hands = new OpticalHand[2];
        public readonly OpticalHandFilter[] filters={new OpticalHandFilter(),new OpticalHandFilter()};
        readonly Dictionary<HumanBodyBones,Quaternion> filteredRotations=new Dictionary<HumanBodyBones,Quaternion>();
        readonly Vector3[] elbowDirections=new Vector3[2];
        readonly Dictionary<HumanBodyBones, Quaternion> offsets = new Dictionary<HumanBodyBones, Quaternion>();
        readonly Dictionary<HumanBodyBones, Quaternion> rests = new Dictionary<HumanBodyBones, Quaternion>();
        Animator animator;
        GameObject attached;
        Vector3 translation;
        Process bridge;
        public const int WebcamPort = 39541;
        void Awake() { desk=gameObject.AddComponent<LeapDeskSetup>(); desk.optical=this; }
        static readonly HumanBodyBones[] Left = {
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal };
        static readonly HumanBodyBones[] Right = {
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal };
        public Vector3 Map(Vector3 p)
        {
            if (source == 0 && mounted) return app.tracking.head.TransformPoint(sensorPosition + Quaternion.Euler(sensorEuler) * p);
            if (source == 0) return desk.Map(p);
            return app.currentAvatar.transform.TransformPoint(p * scale + translation);
        }
        public static Quaternion Basis(Vector3[] p, bool left)
        {
            var forward = p[9] - p[0]; var side = p[5] - p[17]; var normal = Vector3.Cross(forward, side) * (left ? 1 : -1);
            return forward.sqrMagnitude < 1e-8f || normal.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.LookRotation(forward, normal);
        }
        Vector3[] Mapped(OpticalHand h) { var p = new Vector3[21]; for (int i = 0; i < 21; i++) p[i] = Map(h.joints[i]); return p; }
        public void Inject(OpticalFrame frame)
        {
            if (!OpticalInput.Valid(frame) || (source == 0 ? frame.source != "leap" : frame.source != "webcam")) return;
            for(int side=0;side<2;side++)
            {
                var hand=Array.Find(frame.hands,h=>(h.left?0:1)==side);
                filters[side].Observe(hand,Time.unscaledTime,confidence);
                if(filters[side].Fresh(Time.unscaledTime)) hands[side]=filters[side].Sample;
            }
        }
        bool Active(int side) => filters[side].Fresh(Time.unscaledTime);
        public bool TryGetHand(int side,out OpticalHand hand) { hand=filters[side].Usable?filters[side].Pose:null; return hand!=null; }
        void Update()
        {
            if (attached != app.currentAvatar)
            {
                Release(); attached = app.currentAvatar; offsets.Clear(); rests.Clear(); Calibrated = false;
                ResetFilters();
                animator = attached != null ? attached.GetComponent<AvatarDefinition>().animator : null;
                if(attached!=null) desk.Attach(app.AvatarId);
            }
            var frame = input.Read(); if (frame != null) Inject(frame);
            // Desk retarget bases come from the rig, not a live two-hand pose. Do not
            // make the user hold both hands above the sensor while clicking a mouse.
            if(source==0 && !mounted && !Calibrated && enabledTracking && animator!=null && animator.isHuman && (Active(0) || Active(1))) Calibrate();
            if(WaitingForDeskGuide)
            {
                if(source!=0 || mounted || Time.unscaledTime>guideDeadline)
                { WaitingForDeskGuide=false; Status="Pose guia não concluída. O tracking continua usando a posição atual da mesa."; }
                else
                {
                    guideStable=Active(0) && Active(1)?guideStable+Time.unscaledDeltaTime:0;
                    if(guideStable>=.6f) { WaitingForDeskGuide=false; CalibrateDeskGuide(); }
                }
            }
            if (bridge != null && bridge.HasExited)
            {
                int code = bridge.ExitCode; bridge.Dispose(); bridge = null;
                string errorFile = Path.Combine(Application.persistentDataPath, "webcam-error.txt");
                Status = code == 0 ? "Ponte webcam encerrada." : File.Exists(errorFile) ? File.ReadAllText(errorFile) : "Ponte webcam falhou; verifique o índice e o acesso à câmera.";
            }
        }
        public void Calibrate()
        {
            if (animator == null || !animator.isHuman) { Status = "Carregue um avatar Humanoid."; return; }
            bool deskMode=source==0 && !mounted;
            if (!deskMode && !Active(0) && !Active(1)) { Status = "Nenhuma mão válida. Coloque as mãos abertas no campo do sensor."; return; }
            Release(); offsets.Clear(); rests.Clear(); Calibrated = false;
            filteredRotations.Clear();
            translation = Vector3.zero;
            if (source != 0)
            {
                Vector3 raw = Vector3.zero, target = Vector3.zero; int n = 0;
                for (int i = 0; i < 2; i++) if (Active(i)) { var hand = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); if (hand != null) { raw += hands[i].joints[0] * scale; target += attached.transform.InverseTransformPoint(hand.position); n++; } }
                if (n > 0) translation = (target - raw) / n;
            }
            for (int side = 0; side < 2; side++) if (deskMode || Active(side))
            {
                var h = hands[side]; var p = deskMode?RigHand(side):Mapped(h); var palm = Basis(p, side==0);
                // Use the rig's open-hand axes as the retarget basis. Fitting tracked axes to
                // lowered wrists would bake the lowered pose into the calibration.
                Capture(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, palm);
                var bones = side == 0 ? Left : Right;
                for (int i = 0; i < bones.Length; i++)
                {
                    int j = 1 + i / 3 * 4 + i % 3; var d = p[j + 1] - p[j];
                    if (d.sqrMagnitude > 1e-8f) Capture(bones[i], Quaternion.LookRotation(d, palm * Vector3.up));
                }
            }
            Calibrated = offsets.Count > 0; Status = "Calibrado: " + offsets.Count + " ossos. " + (deskMode?"Os dois lados preparados; cada mão detectada já move o avatar. Posição da mesa preservada.":"Recalibre após mudar fonte, montagem, escala ou avatar.");
        }
        public void RequestDeskGuide()
        {
            WaitingForDeskGuide=true; guideDeadline=Time.unscaledTime+30; guideStable=0;
            Status="Devolva as DUAS mãos abertas ao sensor, cotovelos dobrados e antebraços para a frente. A pose guia será aplicada automaticamente.";
        }
        Vector3[] RigHand(int side)
        {
            var p=new Vector3[21]; var bones=side==0?Left:Right;
            p[0]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand).position;
            for(int f=0;f<5;f++)
            {
                for(int j=0;j<3;j++) { var bone=animator.GetBoneTransform(bones[f*3+j]); p[1+f*4+j]=bone!=null?bone.position:p[0]; }
                p[4+f*4]=p[3+f*4]+(p[3+f*4]-p[2+f*4]);
            }
            return p;
        }
        public void CalibrateDeskGuide()
        {
            if(animator==null || !animator.isHuman) { Status="Carregue um avatar Humanoid."; return; }
            if(!Active(0) || !Active(1)) { Status="Mostre DUAS mãos abertas, com cotovelos dobrados e antebraços para a frente, sem cruzar os braços."; return; }
            if(desk.AlignGuide(hands[0],hands[1],animator)) { fingersOnly=false; Calibrate(); }
            else Status=desk.Status;
        }
        void Capture(HumanBodyBones which, Quaternion basis)
        {
            var bone = animator.GetBoneTransform(which); if (bone == null) return;
            offsets[which] = Quaternion.Inverse(basis) * bone.rotation; rests[which] = bone.localRotation;
        }
        void Apply(HumanBodyBones which, Quaternion basis, float blend, float weight)
        {
            if (!offsets.TryGetValue(which, out var offset)) return; var bone = animator.GetBoneTransform(which);
            if (bone != null)
            {
                var target=basis*offset;
                if(filteredRotations.TryGetValue(which,out var previous)) target=Quaternion.Slerp(previous,target,blend);
                filteredRotations[which]=target;
                bone.rotation=Quaternion.Slerp(bone.rotation,target,weight);
            }
        }
        void LateUpdate()
        {
            using var timing=LabPerformance.Measure("Optical");
            if(app.driver!=null && app.driver.PoseOverride) return;
            if (!Calibrated || animator == null || !animator.isHuman) return;
            float blend = 1 - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            for (int side = 0; side < 2; side++)
            {
                var filter=filters[side]; filter.Step(Time.unscaledTime,smoothing,dropoutHold,dropoutFade);
                var bones = side == 0 ? Left : Right;
                if (!enabledTracking || !filter.Usable)
                {
                    // Let imported Gesture/Base animations take over when optical data expires.
                    if(app.driver==null || !app.driver.BodyAnimation)
                        foreach (var which in bones) if (rests.TryGetValue(which, out var rest)) { var bone = animator.GetBoneTransform(which); if (bone != null) bone.localRotation = Quaternion.Slerp(bone.localRotation, rest, blend); }
                    continue;
                }
                var h = filter.Pose; var p = Mapped(h); var palm = Basis(p, h.left); float weight=filter.Weight;
                var handBone = side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                if (!offsets.ContainsKey(handBone)) continue;
                if (!fingersOnly && (app.driver==null || app.driver.AllowsTracking(side==0?"LeftHand":"RightHand")))
                {
                    var a = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                    var b = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                    var c = animator.GetBoneTransform(handBone);
                    if(a!=null && b!=null && c!=null)
                    {
                        var beforeA=a.rotation; var beforeB=b.rotation;
                        var direction=(p[0]-a.position).normalized;
                        var hint=h.elbowValid?Map(h.elbow):b.position;
                        var bend=Vector3.ProjectOnPlane(hint-a.position,direction);
                        var previous=Vector3.ProjectOnPlane(elbowDirections[side],direction);
                        if(bend.magnitude<.025f && previous.sqrMagnitude>1e-8f) bend=previous;
                        if(previous.sqrMagnitude>1e-8f && bend.sqrMagnitude>1e-8f) bend=Vector3.Slerp(previous.normalized,bend.normalized,blend*.6f);
                        if(bend.sqrMagnitude>1e-8f) { elbowDirections[side]=bend.normalized; hint=a.position+bend.normalized*.2f; }
                        Arm(a,b,c,p[0],hint);
                        var solvedA=a.rotation; var solvedB=b.rotation;
                        a.rotation=Quaternion.Slerp(beforeA,solvedA,weight); b.rotation=Quaternion.Slerp(beforeB,solvedB,weight);
                    }
                    wristError[side]=c!=null?Vector3.Distance(c.position,p[0]):0;
                    Apply(handBone, palm, blend,weight);
                }
                for (int i = 0; i < bones.Length && (app.driver==null || app.driver.AllowsTracking(side==0?"LeftFingers":"RightFingers")); i++)
                {
                    int j = 1 + i / 3 * 4 + i % 3; var direction = p[j + 1] - p[j];
                    if (direction.sqrMagnitude > 1e-8f) Apply(bones[i], Quaternion.LookRotation(direction, palm * Vector3.up), blend,weight);
                }
                appliedFrames[side]++;
            }
        }
        public static void Arm(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            if (a == null || b == null || c == null) return;
            float l1 = Vector3.Distance(a.position, b.position), l2 = Vector3.Distance(b.position, c.position);
            var direction = target - a.position; if (l1 < .0001f || l2 < .0001f || direction.sqrMagnitude < 1e-8f) return;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(l1 - l2) + .0001f, l1 + l2 - .0001f); direction.Normalize();
            var bend = Vector3.ProjectOnPlane(hint - a.position, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.up, direction);
            float along = (l1*l1 - l2*l2 + distance*distance) / (2*distance);
            var elbow = a.position + direction*along + bend.normalized*Mathf.Sqrt(Mathf.Max(0, l1*l1 - along*along));
            a.rotation = Quaternion.FromToRotation(b.position-a.position, elbow-a.position)*a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position-b.position, a.position+direction*distance-b.position)*b.rotation;
        }
        void Release()
        {
            if (animator != null && animator.isHuman) foreach (var pair in rests) { var b = animator.GetBoneTransform(pair.Key); if (b != null) b.localRotation = pair.Value; }
        }
        public void Stop()
        {
            input.Dispose(); Release(); Calibrated = false;
            ResetFilters();
            WaitingForDeskGuide=false;
            Status = "Tracking óptico parado. Conecte a fonte e calibre novamente.";
            if (bridge != null) { try { if (!bridge.HasExited) bridge.Kill(); } catch { } bridge.Dispose(); bridge = null; }
        }
        void ResetFilters()
        {
            foreach(var f in filters) f.Reset(); filteredRotations.Clear();
            for(int i=0;i<2;i++) { hands[i]=null; elbowDirections[i]=Vector3.zero; }
        }
        public void DrawUI()
        {
            GUILayout.Label("Mãos, dedos e antebraços • Leap Motion / webcam");
            int chosen = GUILayout.SelectionGrid(source, new[] { "Leap Motion", "Webcam" }, 2);
            if (chosen != source) { Stop(); source = chosen; }
            enabledTracking = GUILayout.Toggle(enabledTracking, "Aplicar tracking óptico");
            fingersOnly = GUILayout.Toggle(fingersOnly, "Somente dedos; mãos/braços continuam no tracking VR");
            if (source == 0)
            {
                bool mount = GUILayout.Toggle(mounted, "Leap montado no headset (desmarcado: mesa)");
                if (mount != mounted) { Stop(); mounted = mount; }
                if (mounted) { sensorPosition = StudioGui.Vector("Offset do sensor no headset (m)", sensorPosition); sensorEuler = StudioGui.Vector("Rotação do sensor (graus)", sensorEuler); }
                if (GUILayout.Button("Conectar Leap Motion")) { Stop(); input.StartLeap(mounted); Status="Fonte iniciada. Abra as mãos no campo do sensor e clique CALIBRAR MÃOS ABERTAS."; }
                GUILayout.Label("Requer driver/serviço Ultraleap e sensor compatível. Orientação da montagem deve ser ajustada. Cotovelo é a estimativa do modelo da Ultraleap.");
            }
            else
            {
                cameraIndex = Mathf.Clamp(Mathf.RoundToInt(StudioGui.Number("Índice da webcam", cameraIndex)), 0, 20);
                if (GUILayout.Button("Iniciar webcam / ponte local"))
                {
                    Stop(); input.StartWebcam(WebcamPort);
                    string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "HandTracking", "AvatarLab Webcam.exe");
                    if (File.Exists(file)) { bridge = Process.Start(new ProcessStartInfo(file, "--camera " + cameraIndex + " --port " + WebcamPort) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(file) }); Status = "Ponte iniciada; aguardando mãos e braços visíveis."; }
                    else Status = "Ponte não encontrada: " + file;
                }
                GUILayout.Label("Webcam monocular: mãos/dedos e cotovelos estimados. Mantenha tronco, braços e mãos visíveis. Profundidade, oclusão e torção do antebraço são aproximadas; não substitui trackers 6DoF. Os frames ficam neste PC.");
            }
            GUILayout.Label(input.Status); GUILayout.Label(Status);
            GUILayout.Label("Esquerda: " + filters[0].State(Time.unscaledTime) + " / Direita: " + filters[1].State(Time.unscaledTime));
            GUILayout.Label(!enabledTracking?"APLICAÇÃO DESATIVADA":app.driver!=null && app.driver.PoseOverride?"APLICAÇÃO SUSPENSA: desmarque ‘Fixar corpo e mãos’ em Poses / clipes, ou desative Sitting / TPose / IKPose em Animações.":!Calibrated?(source==0 && !mounted?"Aguardando uma mão para iniciar o tracking de mesa.":"Calibre as mãos para aplicar o tracking."):(!filters[0].Usable && !filters[1].Usable?"APLICAÇÃO PRONTA • sem mãos visíveis":"APLICANDO AO AVATAR • estabilizado")+" • E: "+appliedFrames[0]+" / D: "+appliedFrames[1]+" quadros");
            if(source!=0) scale = StudioGui.Slider("Escala da fonte desktop", scale, .2f, 3);
            smoothing = StudioGui.Slider("Resposta (menor = mais suave)", smoothing, 5, 35);
            dropoutHold=StudioGui.Slider("Manter pose em perdas curtas (s)",dropoutHold,.05f,.4f);
            dropoutFade=StudioGui.Slider("Retorno suave após perda (s)",dropoutFade,.1f,.5f);
            GUILayout.Label("Falhas curtas E/D: "+filters[0].Dropouts+" / "+filters[1].Dropouts+" • saltos rejeitados: "+filters[0].RejectedSpikes+" / "+filters[1].RejectedSpikes);
            confidence = StudioGui.Slider("Confiança mínima", confidence, .1f, .95f);
            if(source==0 && !mounted)
            {
                GUILayout.Label("O tracking já funciona com cada mão detectada. A pose guia é opcional para alinhar sua postura.");
                if(GUILayout.Button("CALIBRAR POSIÇÃO • AGUARDAR 2 MÃOS")) RequestDeskGuide();
                if(WaitingForDeskGuide)
                {
                    GUILayout.Label("Aguardando duas mãos estáveis • "+Mathf.CeilToInt(Mathf.Max(0,guideDeadline-Time.unscaledTime))+" s. Você pode soltar o mouse.");
                    if(GUILayout.Button("Cancelar pose guia")) WaitingForDeskGuide=false;
                }
                GUILayout.Label("2. Ajuste o sensor na cena ou nos controles abaixo para coincidir com sua mesa.");
                if(GUILayout.Button("CALIBRAR DEDOS • MANTER POSIÇÃO DA MESA")) Calibrate();
                if(fingersOnly) GUILayout.Label("Somente dedos está ativo: desligue essa opção para mover braços e mãos.");
                if(Calibrated && !fingersOnly && ((Active(0) && wristError[0]>.06f) || (Active(1) && wristError[1]>.06f))) GUILayout.Label("Mão fora do alcance do braço. Aproxime/suba a mesa ou reduza a escala dos movimentos.");
                deskControls=GUILayout.Toggle(deskControls,"Controles de posição e rotação da mesa");
                if(deskControls && attached!=null) desk.DrawUI();
            }
            else if (GUILayout.Button("CALIBRAR MÃOS ABERTAS")) Calibrate();
            if (GUILayout.Button("Parar tracking óptico")) Stop();
            GUILayout.Label("Modo completo aplica IK de braço após o corpo VR. Para manter posições dos controles/full body, escolha Somente dedos. Braços e dedos exigem rig Humanoid; o robô de demonstração não possui esse rig.");
        }
        void OnDestroy() { Stop(); }
    }
}
