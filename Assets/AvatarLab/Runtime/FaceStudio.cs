using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Net;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace AvatarLab
{
    [Serializable] public sealed class FaceRoute
    {
        public string source = "jawOpen", rendererPath = "", target = "";
        public bool parameter, invert;
        public float minimum = 0, maximum = 1, gain = 1, offset, response = 1;
    }
    [Serializable] public sealed class FaceProfile
    {
        public int version = 1;
        public bool enabled, headTracking, audioMouth;
        public int rotationVersion;
        public bool eyeTracking=true;
        public float neckShare=.35f,eyeLimit=35;
        public Vector3 leftEyeNeutral,rightEyeNeutral,eyeScale=Vector3.one;
        public float smoothing = 15, micThreshold = .008f, micGain = 12;
        public Vector3 headNeutral, headScale = Vector3.one;
        public List<FaceRoute> routes = new List<FaceRoute>();
        public VowelProfile voice = new VowelProfile();
    }
    [DefaultExecutionOrder(300)]
    public sealed class FaceStudio : MonoBehaviour
    {
        public LabApp app;
        public FaceProfile profile = new FaceProfile();
        public readonly AudioVisemes audio = new AudioVisemes();
        public readonly IfacialReceiver ifacial = new IfacialReceiver();
        public readonly Dictionary<string, float> LiveValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public string Status = "Escolha uma fonte facial e confira o mapeamento sugerido.";
        public string PhoneIp = "";
        public int ifacialPort = 49983;
        public int ifacialProtocol;
        public bool fixed52 = true;
        Process ifacialBridge;
        string bridgeStatus = "", localAddresses = "";
        float testUntil = -100, nextBridgeStatus;
        public int AppliedTargets { get; private set; }
        public int BoundTargets => bindings.Count(b => b.target != null);
        public OscQueryBridge query = new OscQueryBridge();
        sealed class Sample { public float value, time; }
        sealed class Target { public SkinnedMeshRenderer renderer; public int index; public string path, name, label; public float baseline, weight; }
        sealed class Bound { public FaceRoute route; public Target target; }
        readonly Dictionary<string, Sample> incoming = new Dictionary<string, Sample>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,float> playbackFace=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase);
        public void SetPlaybackChannel(string name,float value) {playbackFace[FaceChannels.Canonical(name)]=value;}
        public void ClearPlaybackFace() {if(playbackFace.Count>0) {playbackFace.Clear();Release();}}
        public float ReadChannel(string name)=>Value(name,out float value)?value:0;
        readonly List<Target> targets = new List<Target>();
        readonly List<Bound> bindings = new List<Bound>();
        readonly HashSet<Target> driven = new HashSet<Target>();
        readonly Dictionary<Target, float> frameWeights = new Dictionary<Target, float>();
        string avatarId = "none", targetSearch = "", sourceSearch = "", undo;
        int selectedRoute = -1, targetPage, routePage, micIndex;
        float lastFace = -100, nextQuery;
        Vector3 headAngles;
        Quaternion headRest;
        Transform head;
        FaceBoneTracking boneTracking;
        Vector3 leftEyeAngles,rightEyeAngles,smoothedHead,smoothedLeftEye,smoothedRightEye;
        float lastHead=-100,lastLeftEye=-100,lastRightEye=-100;
        public bool HasLeftEyeRotation=>Time.unscaledTime-lastLeftEye<.5f;
        public bool HasRightEyeRotation=>Time.unscaledTime-lastRightEye<.5f;
        public bool HasHeadRotation=>Time.unscaledTime-lastHead<.5f;
        bool previouslyEnabled;
        string ProfilePath => Path.Combine(Application.persistentDataPath, "Profiles", avatarId + ".face.json");
        void Awake()
        {
            PhoneIp = PlayerPrefs.GetString("ifacial.phone", "");
            try { localAddresses = string.Join(", ", Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString())); } catch { }
        }
        public void StopIfacial()
        {
            ifacial.Dispose();
            if (ifacialBridge != null) { try { if (!ifacialBridge.HasExited) ifacialBridge.Kill(); } catch { } ifacialBridge.Dispose(); ifacialBridge = null; }
            bridgeStatus = "";
        }
        public void ConnectIfacial()
        {
            StopIfacial();
            if (!string.IsNullOrWhiteSpace(PhoneIp) && !IPAddress.TryParse(PhoneIp.Trim(), out _)) { Status = "Informe um IP válido para o iPhone."; return; }
            PhoneIp = PhoneIp.Trim(); PlayerPrefs.SetString("ifacial.phone", PhoneIp); PlayerPrefs.Save();
            profile.enabled = true; int cleared = app.ClearManualFace();
            if (profile.routes.Count == 0) Suggest();
            Status = "Recepção iniciada; confirme o contador de frames." + (cleared > 0 ? " Edições manuais liberadas: " + cleared : "");
            if (ifacialProtocol == 0) { ifacial.Start(PhoneIp, ifacialPort); return; }
            try
            {
                string helper = Path.Combine(Path.GetDirectoryName(Application.dataPath), "HandTracking", "AvatarLab Webcam.exe");
                if (!File.Exists(helper)) throw new FileNotFoundException("Ponte facial não encontrada. Extraia o pacote completo com a pasta HandTracking.");
                ifacial.Start("", 49985, true); if (!ifacial.Running) throw new Exception(ifacial.Error);
                string statusFile = Path.Combine(Application.persistentDataPath, "ifacial-v3-status.txt");
                File.WriteAllText(statusFile, "Iniciando Face Motion v3...");
                string args = "--ifacial-v3 --transport " + (ifacialProtocol == 2 ? "tcp" : "udp") + " --output-port 49985" + (fixed52 ? " --fixed52" : "") + (PhoneIp.Length > 0 ? " --phone " + PhoneIp : "");
                ifacialBridge = Process.Start(new ProcessStartInfo(helper,args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper) });
            }
            catch (Exception ex) { StopIfacial(); Status = ex.Message; }
        }
        static string Relative(Transform root, Transform child)
        {
            if (root == child) return "";
            var parts = new List<string>(); while (child != null && child != root) { parts.Add(child.name); child = child.parent; } parts.Reverse(); return string.Join("/", parts);
        }
        public void Attach(AvatarDefinition definition, string id)
        {
            Release(); targets.Clear(); bindings.Clear(); incoming.Clear(); LiveValues.Clear(); avatarId = id;
            foreach (var r in definition.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null)
                    for (int i = 0; i < r.sharedMesh.blendShapeCount; i++) targets.Add(new Target { renderer = r, index = i, path = Relative(definition.transform, r.transform), name = r.sharedMesh.GetBlendShapeName(i), label = r.name + " / " + r.sharedMesh.GetBlendShapeName(i) });
            head = definition.animator != null && definition.animator.isHuman ? definition.animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null) headRest = head.localRotation;
            boneTracking=definition.animator!=null && definition.animator.isHuman?new FaceBoneTracking(definition.animator):null;
            lastHead=lastLeftEye=lastRightEye=-100;smoothedHead=smoothedLeftEye=smoothedRightEye=Vector3.zero;
            if (File.Exists(ProfilePath)) LoadProfile();
            else { profile = new FaceProfile(); Suggest(); }
            MigrateRotations();
            audio.profile = profile.voice ?? new VowelProfile(); audio.threshold = profile.micThreshold; audio.gain = profile.micGain;
            Rebind(); selectedRoute = -1;
            RefreshQuery();
        }
        public void Suggest()
        {
            undo = JsonUtility.ToJson(profile); profile.routes.Clear();
            foreach (var target in targets)
                if (FaceChannels.Aliases.TryGetValue(FaceChannels.Normalize(target.name), out var canonical))
                    profile.routes.Add(new FaceRoute { source = canonical, rendererPath = target.path, target = target.name });
            Rebind(); Status = profile.routes.Count + " associações sugeridas por nome. Confira cada expressão; não são criadas novas blendshapes.";
        }
        public void Rebind()
        {
            Release(); bindings.Clear();
            foreach (var route in profile.routes)
            {
                var target = route.parameter ? null : targets.FirstOrDefault(t => t.path == route.rendererPath && t.name == route.target);
                bindings.Add(new Bound { route = route, target = target });
            }
        }
        public void ReceiveOsc(string name, float value)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value)) Set(name, value);
        }
        void Set(string name, float value)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 256 || (incoming.Count >= 2048 && !incoming.ContainsKey(FaceChannels.Canonical(name)))) return;
            string canonical = FaceChannels.Canonical(name);
            if (!incoming.TryGetValue(canonical, out var sample)) { sample = new Sample(); incoming[canonical] = sample; }
            sample.value = value; sample.time = Time.unscaledTime; LiveValues[canonical] = value;
            if (canonical != name) LiveValues[name] = value;
            lastFace = Time.unscaledTime;
        }
        public void Inject(FacialFrame frame)
        {
            foreach (var pair in frame.values) Set(pair.Key, pair.Value);
            if (frame.hasHead) { headAngles = frame.head; lastHead=lastFace = Time.unscaledTime; }
            if(frame.hasLeftEye) {leftEyeAngles=frame.leftEye;lastLeftEye=lastFace=Time.unscaledTime;}
            if(frame.hasRightEye) {rightEyeAngles=frame.rightEye;lastRightEye=lastFace=Time.unscaledTime;}
        }
        void Update()
        {
            int limit = 64; while (limit-- > 0 && ifacial.TryRead(out var frame)) Inject(frame);
            if (ifacialBridge != null && Time.unscaledTime > nextBridgeStatus)
            {
                nextBridgeStatus = Time.unscaledTime + 1;
                try { string file = Path.Combine(Application.persistentDataPath, "ifacial-v3-status.txt"); if (File.Exists(file)) bridgeStatus = File.ReadAllText(file); if (ifacialBridge.HasExited) { int exit = ifacialBridge.ExitCode; ifacialBridge.Dispose(); ifacialBridge = null; ifacial.Dispose(); Status = "Ponte v3 encerrada (" + exit + "). " + bridgeStatus; } } catch (IOException) { }
            }
            if (Time.unscaledTime < testUntil)
            {
                Set("jawOpen", .65f); Set("eyeBlinkLeft", .75f); Set("eyeBlinkRight", .75f);
            }
            audio.Pump();
            if (query.Running && Time.unscaledTime > nextQuery) { nextQuery = Time.unscaledTime + 3; RefreshQuery(); }
        }
        bool Value(string source, out float value)
        {
            source = FaceChannels.Canonical(source);
            if(playbackFace.TryGetValue(source,out value)) return true;
            if (audio.Running && (profile.audioMouth || Time.unscaledTime - lastFace > .5f) && FaceChannels.IsMouth(source))
            {
                if (source == "jawOpen") { value = audio.openness; return true; }
                int vowel = Array.IndexOf(FaceChannels.Vowels, source); if (vowel >= 0) { value = audio.vowels[vowel]; return true; }
            }
            if (source == "blinkBoth")
            {
                bool l = Value("eyeBlinkLeft", out var left), r = Value("eyeBlinkRight", out var right); value = (left + right) * .5f; return l || r;
            }
            if (incoming.TryGetValue(source, out var sample)) { value = Time.unscaledTime - sample.time < .5f ? sample.value : 0; return true; }
            value = 0; return false;
        }
        public static float EvaluateRoute(FaceRoute route, float input)
        {
            float range = route.maximum - route.minimum;
            float value = Mathf.Clamp01(Mathf.Abs(range) > .00001f ? (input - route.minimum) / range : 0);
            if (route.invert) value = 1 - value;
            return Mathf.Clamp01(Mathf.Pow(value, Mathf.Max(.05f, route.response)) * route.gain + route.offset);
        }
        void LateUpdate()
        {
            using var timing=LabPerformance.Measure("Face");
            boneTracking?.Release();
            AppliedTargets = 0;
            if ((!profile.enabled && playbackFace.Count==0) || app.driver == null || (playbackFace.Count==0 && !audio.Running && Time.unscaledTime - lastFace > 1.5f))
            {
                if (previouslyEnabled) Release(); previouslyEnabled = false; app.ApplyManualOverrides(); return;
            }
            previouslyEnabled = true; frameWeights.Clear();
            foreach (var binding in bindings)
            {
                if(profile.eyeTracking && boneTracking!=null && ((boneTracking.HasLeftEye && binding.route.source.StartsWith("eyeLook") && binding.route.source.EndsWith("Left")) || (boneTracking.HasRightEye && binding.route.source.StartsWith("eyeLook") && binding.route.source.EndsWith("Right")))) continue;
                if (!Value(binding.route.source, out var input)) continue;
                float weight = EvaluateRoute(binding.route, input);
                if (binding.route.parameter) app.driver.Set(binding.route.target, weight);
                else if (binding.target != null)
                {
                    if(binding.target.renderer==null || !binding.target.renderer.enabled || !binding.target.renderer.gameObject.activeInHierarchy) continue;
                    frameWeights.TryGetValue(binding.target, out float old); frameWeights[binding.target] = Mathf.Max(old, weight * 100);
                }
            }
            foreach(var target in driven.Where(t=>!frameWeights.ContainsKey(t)).ToArray()) {if(target.renderer!=null) target.renderer.SetBlendShapeWeight(target.index,target.baseline);driven.Remove(target);}
            foreach (var pair in frameWeights)
            {
                var target = pair.Key; if (target.renderer == null) continue;
                if (driven.Add(target)) { target.baseline = target.renderer.GetBlendShapeWeight(target.index); target.weight = target.baseline; }
                target.weight = Mathf.Lerp(target.weight, pair.Value, 1 - Mathf.Exp(-profile.smoothing * Time.unscaledDeltaTime));
                target.renderer.SetBlendShapeWeight(target.index, target.weight);
                if (target.renderer.enabled && target.renderer.gameObject.activeInHierarchy) AppliedTargets++;
            }
            float blend=1-Mathf.Exp(-profile.smoothing*Time.unscaledDeltaTime);
            if (profile.headTracking && head != null && !app.driver.ExternalBodyOverride && !app.tracking.HeadTracked && HasHeadRotation)
            {
                var angle = headAngles - profile.headNeutral;
                var target=new Vector3(Mathf.Clamp(Mathf.DeltaAngle(0, angle.x) * profile.headScale.x, -60, 60), Mathf.Clamp(Mathf.DeltaAngle(0, angle.y) * profile.headScale.y, -75, 75), Mathf.Clamp(Mathf.DeltaAngle(0, angle.z) * profile.headScale.z, -45, 45));
                smoothedHead=Vector3.Lerp(smoothedHead,target,blend);boneTracking?.ApplyHead(smoothedHead,profile.neckShare);
            }
            else smoothedHead=Vector3.zero;
            ApplyEyeRotation(true,ref smoothedLeftEye,blend);ApplyEyeRotation(false,ref smoothedRightEye,blend);
            app.ApplyManualOverrides();
        }
        void ApplyEyeRotation(bool left,ref Vector3 smoothed,float blend)
        {
            if(!profile.eyeTracking || boneTracking==null) {smoothed=Vector3.zero;return;}
            bool fresh=left?HasLeftEyeRotation:HasRightEyeRotation;Vector3 raw=left?leftEyeAngles-profile.leftEyeNeutral:rightEyeAngles-profile.rightEyeNeutral;
            if(!fresh)
            {
                string side=left?"Left":"Right";
                bool up=Value("eyeLookUp"+side,out float u),down=Value("eyeLookDown"+side,out float d),inside=Value("eyeLookIn"+side,out float i),outside=Value("eyeLookOut"+side,out float o);
                fresh=up || down || inside || outside;raw=new Vector3((d-u)*profile.eyeLimit,(left?o-i:i-o)*profile.eyeLimit,0);
            }
            if(!fresh) {smoothed=Vector3.zero;return;}
            var target=new Vector3(Mathf.Clamp(Mathf.DeltaAngle(0,raw.x)*profile.eyeScale.x,-profile.eyeLimit,profile.eyeLimit),Mathf.Clamp(Mathf.DeltaAngle(0,raw.y)*profile.eyeScale.y,-profile.eyeLimit,profile.eyeLimit),0);
            smoothed=Vector3.Lerp(smoothed,target,blend);boneTracking.ApplyEye(left,smoothed);
        }
        void MigrateRotations() {if(profile.rotationVersion<1) {profile.headTracking=profile.eyeTracking=true;profile.neckShare=.35f;profile.eyeLimit=35;profile.eyeScale=Vector3.one;profile.rotationVersion=1;}}
        void Release()
        {
            boneTracking?.Release();
            foreach (var target in driven) if (target.renderer != null) target.renderer.SetBlendShapeWeight(target.index, target.baseline); driven.Clear();
        }
        public void SaveProfile()
        {
            try
            {
                profile.voice = audio.profile; profile.micThreshold = audio.threshold; profile.micGain = audio.gain;
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)); string temp = ProfilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(profile, true)); AvatarArchive.CommitFile(temp, ProfilePath); Status = "Perfil facial salvo para este avatar.";
            }
            catch (Exception ex) { Status = ex.Message; }
        }
        public void LoadProfile()
        {
            try
            {
                if (new FileInfo(ProfilePath).Length > 2 * 1024 * 1024) throw new InvalidDataException("Perfil muito grande.");
                var loaded = JsonUtility.FromJson<FaceProfile>(File.ReadAllText(ProfilePath));
                if (loaded == null || loaded.version != 1 || loaded.routes == null || loaded.routes.Count > 4096) throw new InvalidDataException("Perfil inválido.");
                profile = loaded;MigrateRotations(); audio.profile = profile.voice ?? new VowelProfile(); audio.threshold = profile.micThreshold; audio.gain = profile.micGain; Rebind(); Status = "Perfil facial carregado.";
            }
            catch (Exception ex) { Status = ex.Message; profile = new FaceProfile(); }
        }
        public void RefreshQuery()
        {
            if (app.driver == null) return;
            var names = new Dictionary<string, string>();
            foreach (var pair in FaceChannels.LegacyUnified) if (!pair.Value.StartsWith("vowel") && pair.Value != "blinkBoth") names[pair.Key] = "f";
            foreach (var pair in app.driver.Types) names[pair.Key] = pair.Value == AnimatorControllerParameterType.Bool ? "T" : pair.Value == AnimatorControllerParameterType.Int ? "i" : "f";
            query.UpdateAvatar(avatarId, app.driver.Definition.displayName, names);
        }
        public void DrawUI()
        {
            GUILayout.Label("Rosto / tradução ARKit ↔ Unified"); GUILayout.Label(Status);
            profile.enabled = GUILayout.Toggle(profile.enabled, "Aplicar mapeamento facial");
            GUILayout.Label("iFacialMocap: IP local do iPhone"); PhoneIp = GUILayout.TextField(PhoneIp);
            int protocol = GUILayout.SelectionGrid(ifacialProtocol, new[] { "Texto v1/v2", "v3 UDP", "v3 TCP" }, 3);
            if (protocol != ifacialProtocol) { StopIfacial(); ifacialProtocol = protocol; }
            if (ifacialProtocol != 0) fixed52 = GUILayout.Toggle(fixed52, "v3: modo ARKit 52 (desmarcado: nomes variáveis)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Conectar iFacialMocap")) ConnectIfacial();
            if (GUILayout.Button("Parar iFacial")) StopIfacial(); GUILayout.EndHorizontal();
            GUILayout.Label(ifacial.Running ? ifacial.Age < 1 ? "RECEBENDO tracking • " + ifacial.Received + " frames válidos" : "AGUARDANDO DADOS • " + ifacial.Received + " frames válidos" : "iFacial parado. " + ifacial.Error);
            GUILayout.Label("Pacotes: " + ifacial.Datagrams + " / inválidos: " + ifacial.Invalid + " / rotas válidas: " + BoundTargets + " / alvos ativos aplicados: " + AppliedTargets);
            if (!string.IsNullOrEmpty(ifacial.DecodeError)) GUILayout.Label(ifacial.DecodeError);
            if (!string.IsNullOrEmpty(bridgeStatus)) GUILayout.Label("Face Motion v3: " + bridgeStatus);
            if (ifacial.Running && ifacial.Received == 0) GUILayout.Label("Abrir a porta não confirma conexão. No iPhone, mantenha a tela de tracking aberta e confira formato/destino. IP(s) deste PC: " + localAddresses);
            foreach (string channel in new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight", "mouthSmileLeft" }) if (LiveValues.TryGetValue(channel,out float live)) GUILayout.Label(channel + " = " + live.ToString("0.00"));
            if (GUILayout.Button("TESTE LOCAL: boca e piscada (3 s)")) { app.ClearManualFace(); profile.enabled = true; testUntil = Time.unscaledTime + 3; Status = "TESTE LOCAL: dados simulados, independentes do iPhone."; }
            if (Time.unscaledTime < testUntil) GUILayout.Label("TESTE LOCAL ATIVO — os valores abaixo são simulados.");
            if (GUILayout.Button(query.Running ? "Desligar descoberta VRCFaceTracking" : "Ativar descoberta VRCFaceTracking"))
            {
                if (query.Running) query.Dispose(); else { RefreshQuery(); query.Start(app.OscPort); profile.enabled = true; }
            }
            GUILayout.Label(query.Status);
            GUILayout.Label("ARKit e Unified são associados por nome. Para nomes diferentes ou expressões combinadas, ajuste as rotas. A tradução não gera geometria ausente.");
            profile.smoothing = StudioGui.Slider("Suavização", profile.smoothing, 1, 40);
            profile.headTracking = GUILayout.Toggle(profile.headTracking, "Rotação de cabeça do iFacial (desktop)");
            if (profile.headTracking) { if (GUILayout.Button("Calibrar cabeça neutra")) profile.headNeutral = headAngles; profile.headScale = StudioGui.Vector("Escala / inversão dos eixos", profile.headScale); }
            profile.neckShare=StudioGui.Slider("Participação do pescoço",profile.neckShare,0,.7f);
            profile.eyeTracking=GUILayout.Toggle(profile.eyeTracking,"Rotação dos olhos (ossos / ARKit)");
            GUILayout.Label("Rotação recebida: cabeça "+(HasHeadRotation?"OK":"sem sinal")+" / olho E "+(HasLeftEyeRotation?"OK":"sem sinal direto")+" / olho D "+(HasRightEyeRotation?"OK":"sem sinal direto"));
            if(profile.eyeTracking) {if(GUILayout.Button("Calibrar olhar neutro")) {profile.leftEyeNeutral=leftEyeAngles;profile.rightEyeNeutral=rightEyeAngles;}profile.eyeLimit=StudioGui.Slider("Limite do olhar (graus)",profile.eyeLimit,5,45);profile.eyeScale=StudioGui.Vector("Escala / inversão dos olhos",profile.eyeScale);}
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Salvar perfil")) SaveProfile(); if (GUILayout.Button("Carregar perfil") && File.Exists(ProfilePath)) LoadProfile(); GUILayout.EndHorizontal();
            if (GUILayout.Button("Sugerir mapeamento por nomes")) Suggest();
            if (!string.IsNullOrEmpty(undo) && GUILayout.Button("Desfazer sugestão")) { profile = JsonUtility.FromJson<FaceProfile>(undo); undo = null; Rebind(); }
            GUILayout.Space(12); GUILayout.Label("Microfone / A E I O U");
            string[] devices = Microphone.devices;
            if (devices.Length > 0)
            {
                micIndex = Mathf.Clamp(micIndex, 0, devices.Length - 1);
                if (GUILayout.Button("Entrada: " + devices[micIndex])) micIndex = (micIndex + 1) % devices.Length;
                if (GUILayout.Button(audio.Running ? "Parar microfone" : "Iniciar microfone")) { if (audio.Running) audio.Dispose(); else { audio.Start(devices[micIndex]); profile.enabled = true; } }
            }
            else GUILayout.Label("Nenhuma entrada de áudio disponível.");
            GUILayout.Label(audio.Status); audio.threshold = StudioGui.Slider("Limiar de silêncio", audio.threshold, .001f, .08f); audio.gain = StudioGui.Slider("Sensibilidade", audio.gain, 1, 60);
            profile.audioMouth = GUILayout.Toggle(profile.audioMouth, "Priorizar áudio para a boca (olhos continuam no tracking)");
            GUILayout.BeginHorizontal(); foreach (string vowel in new[] { "A", "E", "I", "O", "U" }) if (GUILayout.Button("Cal. " + vowel)) audio.Calibrate(vowel); GUILayout.EndHorizontal();
            GUILayout.Label("Calibradas: " + string.Join(", ", audio.profile.templates.Select(t => t.vowel)) + " / abertura " + audio.openness.ToString("0.00"));
            GUILayout.Label("A/E/I/O/U são estimados por características espectrais calibradas na sua voz. Antes da calibração, há somente abertura de mandíbula; não é reconhecimento preciso de fala.");
            GUILayout.Space(12); GUILayout.Label("Rotas faciais: " + profile.routes.Count);
            if (GUILayout.Button("Adicionar rota")) { profile.routes.Add(new FaceRoute()); selectedRoute = profile.routes.Count - 1; Rebind(); }
            int pages = Mathf.Max(1, Mathf.CeilToInt(profile.routes.Count / 12f)); routePage = Mathf.Clamp(routePage, 0, pages - 1);
            GUILayout.BeginHorizontal(); if (GUILayout.Button("‹")) routePage = Mathf.Max(0, routePage - 1); GUILayout.Label((routePage + 1) + "/" + pages); if (GUILayout.Button("›")) routePage = Mathf.Min(pages - 1, routePage + 1); GUILayout.EndHorizontal();
            for (int i = routePage * 12; i < Mathf.Min(profile.routes.Count, (routePage + 1) * 12); i++)
                if (GUILayout.Button(profile.routes[i].source + " → " + profile.routes[i].target)) selectedRoute = i;
            if (selectedRoute >= 0 && selectedRoute < profile.routes.Count)
            {
                var route = profile.routes[selectedRoute]; route.source = GUILayout.TextField(route.source); route.parameter = GUILayout.Toggle(route.parameter, "Destino é parâmetro do Animator");
                if (route.parameter) route.target = GUILayout.TextField(route.target);
                else
                {
                    GUILayout.Label("Destino: " + route.rendererPath + " / " + route.target); targetSearch = GUILayout.TextField(targetSearch);
                    var filtered = targets.Where(t => string.IsNullOrEmpty(targetSearch) || t.label.IndexOf(targetSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    int maxPage = Mathf.Max(1, Mathf.CeilToInt(filtered.Length / 10f)); targetPage = Mathf.Clamp(targetPage, 0, maxPage - 1);
                    GUILayout.BeginHorizontal(); if (GUILayout.Button("‹ alvos")) targetPage = Mathf.Max(0, targetPage - 1); GUILayout.Label(filtered.Length + " · " + (targetPage + 1) + "/" + maxPage); if (GUILayout.Button("alvos ›")) targetPage = Mathf.Min(maxPage - 1, targetPage + 1); GUILayout.EndHorizontal();
                    foreach (var target in filtered.Skip(targetPage * 10).Take(10)) if (GUILayout.Button(target.label)) { route.rendererPath = target.path; route.target = target.name; Rebind(); }
                }
                route.minimum = StudioGui.Number("Entrada mín.", route.minimum, -10, 10); route.maximum = StudioGui.Number("Entrada máx.", route.maximum, -10, 10);
                route.invert = GUILayout.Toggle(route.invert, "Inverter"); route.gain = StudioGui.Slider("Ganho", route.gain, 0, 2); route.offset = StudioGui.Slider("Deslocamento", route.offset, -1, 1); route.response = StudioGui.Slider("Resposta", route.response, .1f, 3);
                if (GUILayout.Button("Aplicar edição da rota")) Rebind();
                if (GUILayout.Button("Remover rota")) { profile.routes.RemoveAt(selectedRoute); selectedRoute = -1; Rebind(); }
            }
            GUILayout.Space(8); GUILayout.Label("Canais recebidos"); sourceSearch = GUILayout.TextField(sourceSearch);
            foreach (var pair in LiveValues.Where(p => string.IsNullOrEmpty(sourceSearch) || p.Key.IndexOf(sourceSearch, StringComparison.OrdinalIgnoreCase) >= 0).Take(20)) GUILayout.Label(pair.Key + " = " + pair.Value.ToString("0.000"));
        }
        void OnDestroy() { Release(); StopIfacial(); audio.Dispose(); query.Dispose(); }
    }
}
