using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace AvatarLab
{
    public sealed class LabApp : MonoBehaviour
    {
        public Material roomMaterial, mirrorMaterial;
        public LabMotionLibrary motionLibrary;
        public Klak.Spout.SpoutResources spoutResources;
        public AvatarLocomotion motor;
        public StageManager stage;
        public CinematicDirector cinema;
        public VideoOutput video;
        public FaceStudio faces;
        public OpticalHands optical;
        public PhysicsStudio physicsStudio;
        public HairPhysicsPreview hair;
        public PoseStudio poses;
        public RgbMocapStudio mocap;
        public VmcStudio vmc;public ActionStudio actions;public MotionCaptureStudio capture;public InteractiveProps props;public PhotoStudio photo;
        int bridgeSection;
        public int OscPort { get; private set; } = 9000;
        public string AvatarId { get; private set; }
        bool viewerMode, keyboardFocus;
        string startupStudio;bool startupStudioLoaded;
        public string LibraryPath => Path.Combine(Application.persistentDataPath, "Avatars");
        public string InboxPath => Path.Combine(Application.persistentDataPath, "Inbox");
        public string Status = "Pronto. Importe um .alavatar para começar.";
        public AvatarDriver driver;
        public GameObject currentAvatar;
        public BodySolver solver;
        public TrackingHub tracking;
        public LabRoom room;
        public OscReceiver osc = new OscReceiver();
        AssetBundle loadedBundle;
        string currentHash;
        readonly List<string> library = new List<string>();
        readonly Dictionary<string, string> libraryNames = new Dictionary<string, string>();
        sealed class BlendShapeControl { public SkinnedMeshRenderer renderer; public int index; public string label; public float originalWeight, manualWeight; }
        readonly List<BlendShapeControl> blendShapes = new List<BlendShapeControl>();
        readonly HashSet<BlendShapeControl> manualBlendShapes = new HashSet<BlendShapeControl>();
        int parameterPage, blendShapePage;
        bool showBlendShapes;
        const int InspectorPageSize = 48;
        readonly Dictionary<string, DateTime> seenInbox = new Dictionary<string, DateTime>();
        string inputPath = "", search = "", menu = "", portText = "9000";
        string[] tabs = { "Biblioteca", "Expressões", "Luz / foto", "VR / corpo", "Face / voz", "Compatibilidade", "Movimento", "Cenário", "Cinema", "Transmissão", "Mãos / webcam", "Física / colisões", "Animações", "Constraints", "Poses / clipes", "Interações", "Começar" };
        int tab=16;
        Vector2 scroll;
        bool importing, lan, showUi = true, showMirror = true, applyOsc = true;
        public bool PanelVisible=>showUi;
        public bool StudioInputAllowed=>!keyboardFocus && !motor.ControlActive;
        float nextScan, fps;
        GUIStyle titleStyle, headingStyle, smallStyle, bodyStyle;
        Texture2D panelTexture, buttonTexture, activeTexture;
        readonly HashSet<string> heldButtons = new HashSet<string>();
        readonly HashSet<string> submenuParameters = new HashSet<string>();
        public event Action<bool> ImportFinished;
        void Awake()
        {
            var startup = Environment.GetCommandLineArgs();
            if(startup.Contains("--poses") || startup.Contains("--motion")) tab=14;
            if(startup.Contains("--bridges")) {tab=15;bridgeSection=3;}
            if(startup.Contains("--interactions")) tab=15;
            if(startup.Contains("--photo")) tab=2;
            int studioAt=Array.IndexOf(startup,"--studio");if(studioAt>=0 && studioAt+1<startup.Length) {startupStudio=startup[studioAt+1];if(!startup.Contains("--photo")) tab=7;}
            if (startup.Contains("--video-viewer"))
            {
                viewerMode = true; int index = Array.IndexOf(startup, "--video-channel");
                if (index >= 0 && index + 1 < startup.Length) gameObject.AddComponent<CleanVideoViewer>().Configure(startup[index + 1]);
                enabled = false; return;
            }
            Application.runInBackground = true; Application.targetFrameRate = 90;
            Directory.CreateDirectory(LibraryPath); Directory.CreateDirectory(InboxPath);
            // An explicitly requested avatar takes priority over packages already waiting in Inbox.
            if (Environment.GetCommandLineArgs().Contains("--avatar"))
                foreach (var file in Directory.GetFiles(InboxPath, "*.alavatar")) seenInbox[file] = File.GetLastWriteTimeUtc(file);
            var origin = new GameObject("XR Origin").transform;
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetParent(origin); camera.nearClipPlane = .03f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .04f, .065f);
            camera.gameObject.AddComponent<AudioListener>();
            var head = camera.transform;
            tracking = gameObject.AddComponent<TrackingHub>(); tracking.origin = origin; tracking.head = head;
            tracking.leftHand = new GameObject("Left Controller").transform; tracking.leftHand.SetParent(origin);
            tracking.rightHand = new GameObject("Right Controller").transform; tracking.rightHand.SetParent(origin);
            room = new GameObject("Laboratório").AddComponent<LabRoom>(); room.view = camera; room.orbit = origin;
            room.litTemplate = roomMaterial; room.mirrorTemplate = mirrorMaterial; room.Create();
            motor = gameObject.AddComponent<AvatarLocomotion>(); motor.app = this;
            poses=gameObject.AddComponent<PoseStudio>(); poses.app=this;
            mocap=gameObject.AddComponent<RgbMocapStudio>();mocap.app=this;
            stage = gameObject.AddComponent<StageManager>(); stage.app = this;
            cinema = gameObject.AddComponent<CinematicDirector>(); cinema.app = this;
            video = gameObject.AddComponent<VideoOutput>(); video.app = this;
            photo=gameObject.AddComponent<PhotoStudio>();photo.app=this;photo.Initialize();photo.mainPreview=startup.Contains("--photo");
            faces = gameObject.AddComponent<FaceStudio>(); faces.app = this;
            optical = gameObject.AddComponent<OpticalHands>(); optical.app = this;
            physicsStudio = gameObject.AddComponent<PhysicsStudio>(); physicsStudio.app = this;
            hair=gameObject.AddComponent<HairPhysicsPreview>(); hair.app=this;
            props=gameObject.AddComponent<InteractiveProps>();props.app=this;
            capture=gameObject.AddComponent<MotionCaptureStudio>();capture.app=this;
            actions=gameObject.AddComponent<ActionStudio>();actions.app=this;
            vmc=gameObject.AddComponent<VmcStudio>();vmc.app=this;
            osc.Start(9000, false); RefreshLibrary();
        }
        void Start()
        {
            StartCoroutine(StageBrand.SetTitle());
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--vr")) { StartCoroutine(tracking.StartVr()); tracking.StartTrackers(); }
            int at = Array.IndexOf(args, "--avatar");
            if (at >= 0 && at + 1 < args.Length) Import(args[at + 1]);
        }
        public void Import(string path)
        {
            if (!importing) StartCoroutine(ImportRoutine(path));
        }
        IEnumerator ImportRoutine(string path)
        {
            importing = true; Status = "Validando pacote local...";
            yield return null;
            AvatarManifest manifest = null; byte[] bytes = null; Exception failure = null;
            try { manifest = AvatarArchive.Read(path, out bytes); }
            catch (Exception ex) { failure = ex; }
            if (failure != null) { FailImport(failure.Message); yield break; }
            if (currentAvatar != null && currentHash == manifest.bundleSha256)
            {
                Status = manifest.name + " já está carregado."; importing = false; ImportFinished?.Invoke(true); yield break;
            }
            Status = "Carregando " + manifest.name + "...";
            // All exports use unique internal bundle names, allowing transactional replacement.
            var request = AssetBundle.LoadFromMemoryAsync(bytes); yield return request;
            var bundle = request.assetBundle;
            if (bundle == null) { FailImport("Bundle inválido, já carregado ou incompatível com Windows."); yield break; }
            var asset = bundle.LoadAssetAsync<GameObject>(manifest.prefab); yield return asset;
            var prefab = asset.asset as GameObject;
            if (prefab == null || prefab.GetComponent<AvatarDefinition>() == null)
            { bundle.Unload(true); FailImport("O pacote não contém uma definição de avatar válida."); yield break; }
            GameObject candidate = null; AvatarDriver next = null;
            try
            {
                candidate = Instantiate(prefab); candidate.name = manifest.name; candidate.transform.position = Vector3.zero;
                next = new AvatarDriver(candidate.GetComponent<AvatarDefinition>(),motionLibrary);
                var destination = Path.Combine(LibraryPath, manifest.id + ".alavatar");
                if (Path.GetFullPath(path) != Path.GetFullPath(destination))
                {
                    var temporary = destination + ".tmp";
                    File.Copy(path, temporary, true);
                    AvatarArchive.CommitFile(temporary, destination);
                }
                motor.Detach();
                if (currentAvatar != null) { driver?.Dispose(); currentAvatar.SetActive(false); DestroyImmediate(currentAvatar); }
                loadedBundle?.Unload(true); loadedBundle = bundle; currentAvatar = candidate; driver = next; currentHash = manifest.bundleSha256;
                AvatarId = manifest.id;
                foreach (var t in candidate.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
                solver = candidate.AddComponent<BodySolver>(); solver.animator = next.Definition.animator; solver.tracking = tracking;
                var constraintRunner=candidate.GetComponent<AvatarConstraintRunner>(); if(constraintRunner!=null) constraintRunner.debugCamera=room.view;
                motor.Attach(candidate); faces.Attach(next.Definition, manifest.id);
                blendShapes.Clear(); manualBlendShapes.Clear(); parameterPage = blendShapePage = 0;
                foreach (var r in candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.sharedMesh != null)
                        for (int i = 0; i < r.sharedMesh.blendShapeCount; i++)
                            blendShapes.Add(new BlendShapeControl { renderer = r, index = i, label = r.name + " / " + r.sharedMesh.GetBlendShapeName(i) });
                room.Frame(candidate); menu = ""; heldButtons.Clear(); submenuParameters.Clear();
                Status = manifest.name + " carregado. Nenhum upload para a internet.";
                RefreshLibrary();
            }
            catch (Exception ex)
            {
                next?.Dispose(); if (candidate != null) DestroyImmediate(candidate); bundle.Unload(true);
                FailImport(ex.Message); yield break;
            }
            importing = false; ImportFinished?.Invoke(true);
        }
        void FailImport(string error) { Status = "Falha na importação: " + error; importing = false; ImportFinished?.Invoke(false); Debug.LogWarning(Status); }
        void RefreshLibrary()
        {
            library.Clear(); libraryNames.Clear(); library.AddRange(Directory.GetFiles(LibraryPath, "*.alavatar").OrderBy(File.GetLastWriteTimeUtc).Reverse());
            foreach (var file in library)
            {
                string label = Path.GetFileNameWithoutExtension(file);
                try
                {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(file))
                    {
                        var entry = zip.GetEntry("manifest.json");
                        if (entry == null || entry.Length > 256 * 1024) throw new InvalidDataException();
                        using (var read = new StreamReader(entry.Open())) label = JsonUtility.FromJson<AvatarManifest>(read.ReadToEnd()).name;
                    }
                }
                catch { label += " (inválido)"; }
                libraryNames[file] = label;
            }
        }
        void Update()
        {
            NativeFilePicker.Pump();
            if(!startupStudioLoaded && !string.IsNullOrWhiteSpace(startupStudio) && currentAvatar!=null && !importing && stage!=null && !stage.Busy) {startupStudioLoaded=true;StartCoroutine(stage.Load(startupStudio));}
            fps = Mathf.Lerp(fps, 1 / Mathf.Max(.001f, Time.unscaledDeltaTime), .03f);
            if (Input.GetKeyDown(KeyCode.F1)) showUi = !showUi;
            if(!keyboardFocus && !motor.ControlActive && Input.GetKeyDown(KeyCode.F3)) photo.mainPreview=!photo.mainPreview;
            if (!keyboardFocus && Input.GetKeyDown(KeyCode.F)) { if (currentAvatar != null) room.Frame(currentAvatar); }
            if (!keyboardFocus && Input.GetKeyDown(KeyCode.C)) solver?.Calibrate();
            if (Input.GetKeyDown(KeyCode.F12))
            {
                string capture = Path.Combine(Application.persistentDataPath, "Captura-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                if(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) {ScreenCapture.CaptureScreenshot(capture);Status="Captura da interface: "+capture;}else photo.RequestCapture();
            }
            if(!photo.mainPreview && !motor.ControlActive) room.Orbit(tracking.VrActive);
            if (driver != null)
            {
                driver.UpdateLayers();
                driver.Set("IsLocal", 1); driver.Set("VRMode", tracking.VrActive ? 1 : 0);
                driver.Set("TrackingType", tracking.VrActive ? (solver != null && solver.Calibrated ? 6 : 3) : (driver.Definition.animator!=null && driver.Definition.animator.isHuman?3:1));
                if (tracking.LeftTracked) { driver.Set("GestureLeft", tracking.LeftGrip > .6f ? 1 : 0); driver.Set("GestureLeftWeight", tracking.LeftGrip); }
                if (tracking.RightTracked) { driver.Set("GestureRight", tracking.RightGrip > .6f ? 1 : 0); driver.Set("GestureRightWeight", tracking.RightGrip); }
            }
            int count = 0;
            while (count++ < 512 && osc.TryDequeue(out var message))
            {
                if (!applyOsc) continue;
                try { ApplyOsc(message); } catch (Exception) { }
            }
            if (Time.unscaledTime >= nextScan && !importing)
            {
                nextScan = Time.unscaledTime + 2;
                foreach (var file in Directory.GetFiles(InboxPath, "*.alavatar"))
                {
                    var time = File.GetLastWriteTimeUtc(file);
                    if (seenInbox.TryGetValue(file, out var old) && old == time) continue;
                    seenInbox[file] = time; Import(file); break;
                }
            }
        }
        public void ApplyOsc(OscMessage message)
        {
            const string prefix = "/avatar/parameters/";
            if (message.address.StartsWith(prefix, StringComparison.Ordinal) && message.values.Length == 1)
            { string name = message.address.Substring(prefix.Length); float value = message.Number(); driver?.Set(name, value); faces?.ReceiveOsc(name, value); return; }
            var parts = message.address.Split('/');
            if (parts.Length != 5 || parts[1] != "tracking" || parts[2] != "trackers" || message.values.Length != 3) return;
            string id = "osc:" + parts[3];
            var v = new Vector3(message.Number(0), message.Number(1), message.Number(2));
            if (!Finite(v)) return;
            if (!tracking.points.TryGetValue(id, out var point))
            { tracking.SetPoint(id, "OSC " + parts[3], Vector3.zero, Quaternion.identity); point = tracking.points[id]; }
            if (parts[4] == "position") tracking.SetPoint(id, point.label, v, Quaternion.Inverse(tracking.origin.rotation) * point.rotation);
            else if (parts[4] == "rotation") tracking.SetPoint(id, point.label, tracking.origin.InverseTransformPoint(point.position), Quaternion.Euler(v));
        }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        void LateUpdate()
        {
            driver?.EvaluateManual(Time.deltaTime);
            driver?.ApplyNeutralRoot();
            driver?.ApplyFaceBindings();
        }
        public void ApplyManualOverrides()
        {
            foreach (var shape in manualBlendShapes)
                if (shape.renderer != null) shape.renderer.SetBlendShapeWeight(shape.index, shape.manualWeight);
        }
        public int ClearManualFace()
        {
            int count = manualBlendShapes.Count;
            foreach (var changed in manualBlendShapes) if (changed.renderer != null) changed.renderer.SetBlendShapeWeight(changed.index, changed.originalWeight);
            manualBlendShapes.Clear(); return count;
        }
        void Styles()
        {
            if (titleStyle != null) return;
            panelTexture = Solid(new Color(.045f, .065f, .095f, .97f)); buttonTexture = Solid(new Color(.1f, .16f, .21f)); activeTexture = Solid(new Color(.05f, .45f, .48f));
            GUI.skin.box.normal.background = panelTexture;
            GUI.skin.button.normal.background = buttonTexture; GUI.skin.button.hover.background = activeTexture;
            GUI.skin.button.active.background = activeTexture; GUI.skin.button.fontSize = 13;
            GUI.skin.button.padding = new RectOffset(10, 10, 8, 8);
            GUI.skin.label.normal.textColor = new Color(.85f, .91f, .96f); GUI.skin.label.fontSize = 13; GUI.skin.label.wordWrap = true;
            GUI.skin.button.wordWrap = true;
            GUI.skin.textField.fontSize = 13; GUI.skin.toggle.fontSize = 13;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold };
            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
            bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 11 }; smallStyle.normal.textColor = new Color(.53f, .68f, .76f);
        }
        static Texture2D Solid(Color color) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, color); t.Apply(); return t; }
        void OnGUI()
        {
            using var timing=LabPerformance.Measure("UI");
            if (viewerMode) return;
            Styles(); float scale = Mathf.Clamp(Screen.height / 900f, .7f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float height = Screen.height / scale;
            if (Event.current.type == EventType.MouseDown && Input.mousePosition.x > 440 * scale) { GUI.FocusControl(null); keyboardFocus = false; }
            if (!showUi) return;
            GUI.enabled = !motor.ControlActive;
            GUILayout.BeginArea(new Rect(16, 16, 420, height - 32), GUI.skin.box);
            GUILayout.Space(10); GUILayout.Label(StageBrand.Name.ToUpperInvariant(), titleStyle); GUILayout.Label("VTUBER STUDIO  /  ALPHA "+StageBrand.Version, smallStyle);
            GUILayout.Space(8); GUILayout.Label(Status, bodyStyle); GUILayout.Space(8);
            int chosen = GUILayout.SelectionGrid(tab, tabs, 3);
            if (chosen != tab)
            {
                foreach (var p in heldButtons) driver?.Set(p, 0); heldButtons.Clear();
                foreach (var p in submenuParameters) driver?.Set(p, 0); submenuParameters.Clear(); menu = "";
                tab = chosen; scroll = Vector2.zero;
            }
            GUILayout.Space(10); scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: LibraryUi(); break;
                case 1: ExpressionsUi(); break;
                case 2: LightUi(); break;
                case 3: TrackingUi(); break;
                case 4: OscUi(); break;
                case 5: CompatibilityUi(); break;
                case 6: motor.DrawUI(); break;
                case 7: stage.DrawUI(); break;
                case 8: cinema.DrawUI(); break;
                case 9: video.DrawUI(); break;
                case 10: optical.DrawUI(); break;
                case 11: physicsStudio.DrawUI(); break;
                case 12: if(driver!=null) driver.DrawAnimationUI(); else GUILayout.Label("Carregue um avatar."); break;
                case 13: ConstraintsUi(); break;
                case 14: poses.DrawUI(); break;
                case 15:
                    bridgeSection=GUILayout.SelectionGrid(bridgeSection,new[]{"Ações / nós","Objetos físicos","Gravação","Conexões opcionais"},2);
                    if(bridgeSection==0) actions.DrawUI();else if(bridgeSection==1) props.DrawUI();else if(bridgeSection==2) capture.DrawUI();else vmc.DrawUI();break;
                case 16: QuickStartUi(); break;
            }
            GUILayout.EndScrollView(); GUILayout.Space(8);
            GUILayout.Label((tracking.VrActive ? "VR" : "DESKTOP") + "  •  " + Mathf.RoundToInt(fps) + " FPS  •  OSC " + osc.Received, smallStyle);
            GUILayout.Label("F1 painel · F2 jogar · Esc editar · F enquadrar · F12 captura", smallStyle);
            GUILayout.EndArea();
            keyboardFocus = GUIUtility.keyboardControl != 0; GUI.enabled = true;
        }
        void QuickStartUi()
        {
            GUILayout.Label("Seu avatar do VRChat, pronto para a live",headingStyle);
            GUILayout.Label("Exporte uma vez pelo Unity. Depois use o avatar aqui, sem converter para VRM e sem manter o VRChat aberto.",bodyStyle);
            GUILayout.Space(8);GUILayout.Label("1 • Escolha seu avatar",headingStyle);
            GUILayout.Label(currentAvatar!=null?"Carregado: "+driver.Definition.displayName:"Importe um arquivo .alavatar para começar.",bodyStyle);
            GUI.enabled=!importing;
            if(GUILayout.Button("Importar avatar…")) NativeFilePicker.PickAsync(Import);
            GUI.enabled=true;
            if(GUILayout.Button("Ver biblioteca e meus avatares")) tab=0;
            GUILayout.Label("Primeiro uso: importe o pacote do exportador no projeto Unity do avatar. Selecione a raiz e use Tools > VRCStage > Exportar avatar local. A exportação é local.",smallStyle);
            GUILayout.Space(8);GUILayout.Label("2 • Dê vida ao personagem",headingStyle);
            GUILayout.Label("Escolha apenas os dispositivos que você usa. Comece pelo rosto ou microfone; mãos, headset e trackers são opcionais.",bodyStyle);
            if(GUILayout.Button("Configurar rosto e voz")) tab=4;
            if(GUILayout.Button("Configurar mãos / Leap / webcam")) tab=10;
            if(GUILayout.Button("Configurar VR e corpo")) tab=3;
            GUILayout.Space(8);GUILayout.Label("3 • Prepare a imagem",headingStyle);
            GUI.enabled=currentAvatar!=null;
            if(GUILayout.Button("Enquadrar meio corpo")) {photo.Frame(1);photo.mainPreview=true;}
            GUI.enabled=true;
            if(GUILayout.Button("Ajustar luz, câmera e efeitos")) tab=2;
            GUILayout.Space(8);GUILayout.Label("4 • Leve para o OBS",headingStyle);
            GUILayout.Label(video.Running?"Saída de vídeo ativa. Selecione a janela limpa ou o receptor Spout no OBS.":"Abra Transmissão para escolher fundo, transparência e saída. Iniciar a saída local não inicia uma live na internet.",bodyStyle);
            if(GUILayout.Button("Configurar transmissão")) tab=9;
            GUILayout.Space(8);
            if(GUILayout.Button("Guia, código e comunidade no GitHub")) Application.OpenURL(StageBrand.Repository);
            GUILayout.Label("Projeto independente em desenvolvimento. Física, shaders e recursos do VRChat podem apresentar diferenças. Confira Compatibilidade para seu avatar.",smallStyle);
        }
        void LibraryUi()
        {
            GUILayout.Label("Seu avatar, no seu computador", headingStyle);
            GUILayout.Label("No projeto Unity do avatar: Tools > VRCStage > Exportar avatar local. Não precisa converter para VRM.", bodyStyle);
            GUI.enabled = !importing;
            if (GUILayout.Button("IMPORTAR AVATAR…")) NativeFilePicker.PickAsync(Import);
            inputPath = GUILayout.TextField(inputPath);
            if (GUILayout.Button("Importar pelo caminho acima")) Import(inputPath.Trim().Trim('"'));
            GUI.enabled = true;
            if (GUILayout.Button("Abrir pasta de envio automático")) Application.OpenURL(InboxPath);
            GUILayout.Label("O exportador pode enviar direto para Inbox. Pacotes novos são detectados automaticamente.", smallStyle);
            GUILayout.Space(12); GUILayout.Label("Biblioteca local", headingStyle);
            foreach (var file in library.ToArray())
            {
                string label = libraryNames[file];
                GUI.enabled = !importing;
                if (GUILayout.Button("Carregar  /  " + label))
                {
                    Import(file);
                }
                GUI.enabled = true;
            }
            if (library.Count == 0) GUILayout.Label("Nenhum avatar importado ainda.", smallStyle);
        }
        void ExpressionsUi()
        {
            if (driver == null) { GUILayout.Label("Importe um avatar primeiro."); return; }
            GUILayout.Label(driver.Definition.displayName, headingStyle);
            if (!string.IsNullOrEmpty(menu) && GUILayout.Button("← Menu principal"))
            {
                foreach (var parameter in submenuParameters) driver.Set(parameter, 0);
                submenuParameters.Clear(); menu = "";
            }
            foreach (var c in driver.Definition.controls)
            {
                if (c.parent != menu) continue;
                if (c.type == 2)
                {
                    if (GUILayout.Button(c.label + "  ›"))
                    {
                        menu = c.parameter;
                        foreach (var parameter in c.axes)
                            if (!string.IsNullOrEmpty(parameter)) { driver.Set(parameter, c.value); submenuParameters.Add(parameter); }
                    }
                    continue;
                }
                if (c.type >= 3)
                {
                    GUILayout.Label(c.label);
                    foreach (var axis in c.axes) ParameterUi(axis, c.type == 5 ? 0 : -1, 1);
                }
                else if (c.type == 0)
                {
                    bool held = GUILayout.RepeatButton(c.label);
                    if (held) { driver.Set(c.parameter, c.value); heldButtons.Add(c.parameter); }
                    else if (heldButtons.Remove(c.parameter)) driver.Set(c.parameter, 0);
                }
                else
                {
                    bool old = driver.Values.TryGetValue(c.parameter, out var v) && Mathf.Abs(v - c.value) < .001f;
                    bool next = GUILayout.Toggle(old, c.label);
                    if (next != old) driver.Set(c.parameter, next ? c.value : 0);
                }
            }
            GUILayout.Space(12); GUILayout.Label("Parâmetros do Animator", headingStyle); search = GUILayout.TextField(search);
            var parameters = driver.Types.Where(p => string.IsNullOrEmpty(search) || p.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            Pager(parameters.Length, ref parameterPage);
            foreach (var pair in parameters.Skip(parameterPage * InspectorPageSize).Take(InspectorPageSize))
                ParameterUi(pair.Key, -1, pair.Value == AnimatorControllerParameterType.Int ? 7 : 1);
            GUILayout.Space(12);
            showBlendShapes = GUILayout.Toggle(showBlendShapes, "Inspecionar blendshapes (" + blendShapes.Count + ")");
            if (showBlendShapes)
            {
                GUILayout.Label("Edição manual sobrepõe a animação do blendshape.", smallStyle);
                if (manualBlendShapes.Count > 0 && GUILayout.Button("Limpar edição manual (" + manualBlendShapes.Count + ")"))
                {
                    foreach (var changed in manualBlendShapes) if (changed.renderer != null) changed.renderer.SetBlendShapeWeight(changed.index, changed.originalWeight);
                    manualBlendShapes.Clear();
                }
                var shapes = blendShapes.Where(b => b.renderer != null && (string.IsNullOrEmpty(search) || b.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                Pager(shapes.Length, ref blendShapePage);
                foreach (var shape in shapes.Skip(blendShapePage * InspectorPageSize).Take(InspectorPageSize))
                {
                    GUILayout.Label(shape.label, smallStyle); float old = shape.renderer.GetBlendShapeWeight(shape.index); float next = GUILayout.HorizontalSlider(old, 0, 100);
                    if (Mathf.Abs(next - old) > .001f)
                    {
                        if (manualBlendShapes.Add(shape)) shape.originalWeight = old;
                        shape.manualWeight = next; shape.renderer.SetBlendShapeWeight(shape.index, next);
                    }
                }
            }
        }
        void Pager(int count, ref int page)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)InspectorPageSize)); page = Mathf.Clamp(page, 0, pages - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("‹", GUILayout.Width(40))) page = Mathf.Max(0, page - 1);
            GUILayout.Label(count + " itens · " + (page + 1) + "/" + pages, smallStyle);
            if (GUILayout.Button("›", GUILayout.Width(40))) page = Mathf.Min(pages - 1, page + 1);
            GUILayout.EndHorizontal();
        }
        void ParameterUi(string name, float min, float max)
        {
            if (string.IsNullOrEmpty(name)) return;
            driver.Values.TryGetValue(name, out var value);
            driver.Types.TryGetValue(name, out var type);
            if (type == AnimatorControllerParameterType.Bool || type == AnimatorControllerParameterType.Trigger)
            {
                bool next = GUILayout.Toggle(value > .5f, name); if (next != (value > .5f)) driver.Set(name, next ? 1 : 0);
            }
            else
            {
                GUILayout.Label(name + "   " + value.ToString("0.00"), smallStyle);
                float next = GUILayout.HorizontalSlider(value, min, max);
                if (Mathf.Abs(next - value) > .001f) driver.Set(name, type == AnimatorControllerParameterType.Int ? Mathf.Round(next) : next);
            }
        }
        void LightUi()
        {
            photo.DrawUI();
            showMirror = GUILayout.Toggle(showMirror, "Espelho de inspeção (imagem mono)"); room.SetMirror(showMirror);
            if (GUILayout.Button("Enquadrar avatar") && currentAvatar != null) room.Frame(currentAvatar);
            GUILayout.Label("Mouse direito: orbitar. Botão do meio: deslocar. Roda: aproximar. Em VR, use o headset; o painel fica na janela do PC.", bodyStyle);
            if (currentAvatar != null)
            {
                float angle = currentAvatar.transform.eulerAngles.y;
                GUILayout.Label("Rotação do avatar"); float next = GUILayout.HorizontalSlider(angle, 0, 360);
                if (Mathf.Abs(next - angle) > .1f) { currentAvatar.transform.rotation = Quaternion.Euler(0, next, 0); if (solver != null) solver.Calibrated = false; }
                foreach (var motion in currentAvatar.GetComponentsInChildren<AvatarSecondaryMotion>(true)) motion.enabled = GUILayout.Toggle(motion.enabled, "Movimento secundário: " + motion.name);
            }
        }
        void TrackingUi()
        {
            GUILayout.Label("PCVR / OpenXR", headingStyle); GUILayout.Label(tracking.Status, bodyStyle);
            GUI.enabled = !tracking.Starting;
            if (GUILayout.Button("Ativar VR")) StartCoroutine(tracking.StartVr()); GUI.enabled = true;
            if (GUILayout.Button("Conectar trackers SteamVR")) tracking.StartTrackers();
            GUILayout.Label(tracking.TrackerStatus, bodyStyle);
            GUILayout.Label("VIVE Pro 2: SteamVR como runtime OpenXR. Quest Pro: PCVR; para misturar Tundra/VIVE, alinhe os espaços de tracking antes de calibrar.", bodyStyle);
            foreach (var p in tracking.points.Values.ToArray())
            {
                GUILayout.Space(6); GUILayout.Label(p.label + (p.Active ? "  ●" : "  (sem sinal)"));
                GUILayout.BeginHorizontal(); GUILayout.Label(p.role.ToString());
                if (GUILayout.Button("Trocar função", GUILayout.Width(140))) { tracking.Assign(p, (BodyRole)(((int)p.role + 1) % Enum.GetValues(typeof(BodyRole)).Length)); if (solver != null) solver.Calibrated = false; }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("CALIBRAR CORPO  [C]")) solver?.Calibrate();
            if (solver != null) GUILayout.Label(solver.Status, bodyStyle);
            GUILayout.Label("Funções: Hips=cintura, Foot=pé, Chest=peito, Elbow=cotovelo, Knee=joelho. Fique na pose do avatar ao calibrar. IK próprio experimental; sem validação física neste PC.", smallStyle);
        }
        void OscUi()
        {
            faces.DrawUI(); GUILayout.Space(12);
            GUILayout.Label("Face / olhos / parâmetros OSC", headingStyle);
            GUILayout.Label(osc.Running ? "Recebendo UDP na porta " + portText : "OSC parado: " + osc.Error, bodyStyle);
            portText = GUILayout.TextField(portText); lan = GUILayout.Toggle(lan, "Aceitar dados da rede local");
            if (GUILayout.Button("Aplicar porta OSC") && int.TryParse(portText, out int port) && port > 0 && port < 65536)
            {
                osc.Start(port, lan); OscPort = port; if (faces.query.Running) { faces.query.Start(port); faces.RefreshQuery(); }
            }
            applyOsc = GUILayout.Toggle(applyOsc, "Aplicar parâmetros recebidos");
            GUILayout.Label("Mensagens: " + osc.Received + "  /  descartadas: " + osc.Dropped, smallStyle);
            GUILayout.Label("Envie /avatar/parameters/NomeDoParametro. O avatar precisa ter parâmetros e animações faciais correspondentes. Receber OSC não cria o rig facial.", bodyStyle);
            GUILayout.Label("VRCFaceTracking: ative a descoberta acima ou configure saída para 127.0.0.1 e esta porta. O módulo do seu hardware precisa estar ativo. Mapeamento de nomes desconhecidos exige ajuste manual.", bodyStyle);
            GUILayout.Label("Não execute outro receptor na mesma porta. O painel do PC permite monitorar parâmetros na aba Expressões.", smallStyle);
        }
        void CompatibilityUi()
        {
            GUILayout.Label("O que esta versão valida", headingStyle);
            GUILayout.Label("Visual de meshes/materiais, animações Unity, parâmetros e menus exportados; entrada OSC; OpenXR; leitura de trackers e IK próprio.", bodyStyle);
            GUILayout.Label("Não é uma réplica do VRChat", headingStyle);
            GUILayout.Label("PhysBones/colliders e constraints são convertidos para solvers próprios. O exportador prepara cópias com VRCFury e converte comandos de parâmetros, pesos e tracking. Contacts e outros comportamentos sem equivalente permanecem limitados e aparecem no relatório. Não há teste de rede ou desempenho de Quest Android.", bodyStyle);
            GUILayout.Label("A física opcional AvatarSecondaryMotion é própria, aproximada e configurada separadamente. Luz e IK podem produzir resultados diferentes do VRChat.", bodyStyle);
            GUILayout.Label("Sem um controlador Base distinto de FX, o corpo usa a pose de repouso do rig antes do IK. Para avaliar animações corporais, informe Base no exportador.", bodyStyle);
            if (driver != null) foreach (var note in driver.Definition.compatibilityNotes) GUILayout.Label("• " + note, smallStyle);
        }
        void ConstraintsUi()
        {
            var runner=currentAvatar!=null?currentAvatar.GetComponent<AvatarConstraintRunner>():null;
            if(runner==null) { GUILayout.Label("Este pacote não contém constraints convertidos. Reexporte com o exportador 0.6."); return; }
            if(runner.constraints==null) runner.Rebuild();
            GUILayout.Label("Constraints offline • "+runner.constraints.Length,headingStyle);
            GUILayout.Label("Execução após tracking e antes dos PhysBones. Dependências ordenadas; ciclos aproximados: "+runner.CyclicDependencies,bodyStyle);
            runner.drawLinks=GUILayout.Toggle(runner.drawLinks,"Mostrar vínculos somente na câmera de edição");
            foreach(var group in runner.constraints.Where(c=>c!=null).GroupBy(c=>c.GetType().Name)) GUILayout.Label(group.Key+": "+group.Count(),smallStyle);
            GUILayout.Space(8);
            foreach(var c in runner.constraints) if(c!=null)
                GUILayout.Label(c.Target.name+" • "+c.GetType().Name.Replace("Avatar","")+" • "+(c.isActiveAndEnabled && c.IsActive?"ativo":"inativo")+" • peso "+c.GlobalWeight.ToString("F2")+(c.FreezeToWorld?" • WORLD FREEZE":""),smallStyle);
            GUILayout.Label("Pesos e Freeze To World podem ser dirigidos pelas animações e toggles importados. Confira os menus na aba Expressões.",bodyStyle);
        }
        void OnDestroy()
        {
            if (viewerMode) return;
            video?.StopOutput(); motor?.Detach(); osc.Dispose(); driver?.Dispose(); driver = null; if (loadedBundle != null) loadedBundle.Unload(true);
        }
    }
}
