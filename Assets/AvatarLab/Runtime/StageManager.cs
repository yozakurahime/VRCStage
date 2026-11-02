using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class StageItemData
    {
        public string name, source, primitive;
        public bool world;
        public Vector3 position, euler, scale = Vector3.one;
        public Color color = Color.gray;
    }
    [Serializable] public sealed class StudioSession
    {
        public int version = 1;
        public bool showLab = true;
        public StageItemData[] objects;
        public Vector3 avatarPosition, avatarEuler;
        public CameraShot[] cameras;
        public int selectedCamera;
        public StudioLightingSettings lighting;
        public PhotoSettings photo;
        public float cameraTransition=.35f;
    }
    public sealed class StageManager : MonoBehaviour
    {
        public LabApp app;
        public string Status = "Adicione objetos ou importe um cenário.";
        public bool Busy, showLab = true;
        sealed class Item { public GameObject go; public StageItemData data; }
        sealed class Asset { public AssetBundle bundle; public GameObject prefab; public StageManifest manifest; }
        readonly List<Item> items = new List<Item>();
        readonly Dictionary<string, Asset> assets = new Dictionary<string, Asset>();
        int selected = -1;
        string sessionName = "Meu estudio";
        readonly List<string> saved = new List<string>();
        public string SessionDirectory => Path.Combine(Application.persistentDataPath, "Studios");
        public int ObjectCount => items.Count;
        public Transform Selected => selected >= 0 && selected < items.Count ? items[selected].go.transform : null;
        public void AddPrimitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type); go.name = type.ToString() + " " + (items.Count + 1);
            go.GetComponent<Renderer>().sharedMaterial = new Material(app.roomMaterial) { color = new Color(.35f, .55f, .65f) };
            var position = app.currentAvatar != null ? app.currentAvatar.transform.position : Vector3.zero;
            go.transform.position = position + Vector3.forward * 1.5f + Vector3.up * .5f;
            items.Add(new Item { go = go, data = new StageItemData { name = go.name, primitive = type.ToString(), color = go.GetComponent<Renderer>().sharedMaterial.color } }); selected = items.Count - 1;
        }
        public void Import(string path, bool world = false) { if (!Busy) StartCoroutine(ImportRoutine(path, world, null)); }
        IEnumerator ImportRoutine(string path, bool world, StageItemData placement)
        {
            Busy = true; Status = "Carregando cenário/objeto..."; yield return null;
            Asset asset = null; GameObject candidate = null; Exception failure = null;
            byte[] bytes = null; StageManifest manifest = null;
            bool obj = Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase);
            if (obj)
            {
                try { candidate = ObjLoader.Load(path, app.roomMaterial); }
                catch (Exception ex) { failure = ex; }
            }
            else
            {
                try { manifest = StageArchive.Read(path, out bytes); assets.TryGetValue(manifest.bundleSha256, out asset); }
                catch (Exception ex) { failure = ex; }
                if (failure == null && asset == null)
                {
                    var request = AssetBundle.LoadFromMemoryAsync(bytes); yield return request;
                    if (request.assetBundle == null) failure = new InvalidDataException("Não foi possível carregar o bundle de cenário.");
                    else
                    {
                        var prefab = request.assetBundle.LoadAsset<GameObject>(manifest.prefab);
                        if (prefab == null || prefab.GetComponent<StageDefinition>() == null) { request.assetBundle.Unload(true); failure = new InvalidDataException("Definição de cenário ausente."); }
                        else { asset = new Asset { bundle = request.assetBundle, prefab = prefab, manifest = manifest }; assets[manifest.bundleSha256] = asset; }
                    }
                }
                if (failure == null) candidate = Instantiate(asset.prefab);
            }
            if (failure != null) { Status = "Falha: " + failure.Message; Busy = false; yield break; }
            candidate.name = placement != null ? placement.name : (manifest != null ? manifest.name : Path.GetFileNameWithoutExtension(path));
            candidate.transform.position = world ? Vector3.zero : (app.currentAvatar != null ? app.currentAvatar.transform.position + Vector3.forward * 1.5f : Vector3.forward * 1.5f);
            if (placement != null) ApplyTransform(candidate.transform, placement);
            if (world)
            {
                app.photo.lighting.Restore();
                foreach (var old in items.Where(i => i.data.world).ToArray()) { DestroyImmediate(old.go); items.Remove(old); }
                var def = candidate.GetComponent<StageDefinition>(); if (def != null) ApplyLighting(def);
                showLab = false; SetLab(false);
            }
            items.Add(new Item { go = candidate, data = placement ?? new StageItemData { name = candidate.name, source = Path.GetFullPath(path), world = world } }); selected = items.Count - 1;
            Status = candidate.name + " carregado. Objetos importados não executam scripts de mundo do VRChat."; Busy = false;
        }
        static void ApplyLighting(StageDefinition d)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = d.ambient; RenderSettings.skybox = d.skybox;
            if(d.hasEnvironmentSettings)
            {
                RenderSettings.ambientMode=d.ambientMode;RenderSettings.ambientSkyColor=d.ambientSky;RenderSettings.ambientEquatorColor=d.ambientEquator;RenderSettings.ambientGroundColor=d.ambientGround;
                RenderSettings.ambientIntensity=d.ambientIntensity;RenderSettings.reflectionIntensity=d.reflectionIntensity;
                RenderSettings.defaultReflectionMode=d.reflectionMode;RenderSettings.customReflectionTexture=d.customReflectionTexture;
                if(d.ambientSH!=null && d.ambientSH.Length==27)
                {
                    var sh=new UnityEngine.Rendering.SphericalHarmonicsL2();
                    for(int channel=0;channel<3;channel++) for(int coefficient=0;coefficient<9;coefficient++) sh[channel,coefficient]=d.ambientSH[channel*9+coefficient];
                    RenderSettings.ambientProbe=sh;
                }
                else if(d.ambientMode==UnityEngine.Rendering.AmbientMode.Skybox) DynamicGI.UpdateEnvironment();
            }
            LightmapSettings.lightProbes=d.lightProbes;
            if(d.lightProbes!=null && d.lightProbes.count>0) LightProbes.Tetrahedralize();
            var maps = new LightmapData[d.lightmapColors.Length];
            for (int i = 0; i < maps.Length; i++) maps[i] = new LightmapData { lightmapColor = d.lightmapColors[i], lightmapDir = i < d.lightmapDirections.Length ? d.lightmapDirections[i] : null, shadowMask = i < d.shadowMasks.Length ? d.shadowMasks[i] : null };
            LightmapSettings.lightmapsMode = d.lightmapsMode; LightmapSettings.lightmaps = maps;
            for (int i = 0; i < d.lightmappedRenderers.Length; i++) if (d.lightmappedRenderers[i] != null && i < d.lightmapIndices.Length && i < d.lightmapOffsets.Length)
            { d.lightmappedRenderers[i].lightmapIndex = d.lightmapIndices[i]; d.lightmappedRenderers[i].lightmapScaleOffset = d.lightmapOffsets[i]; }
        }
        static void ApplyTransform(Transform t, StageItemData d) { t.SetPositionAndRotation(d.position, Quaternion.Euler(d.euler)); t.localScale = d.scale; }
        public void SetLab(bool active)
        {
            showLab = active; app.room.gameObject.SetActive(active); app.room.clampToRoom = active;
            if (active) { LightmapSettings.lightProbes=null;LightmapSettings.lightmaps = Array.Empty<LightmapData>(); RenderSettings.skybox = null; app.room.Preset(0); }
            else {var world=items.LastOrDefault(i=>i.data.world && i.go!=null);if(world!=null) ApplyLighting(world.go.GetComponent<StageDefinition>());}
            app.room.view.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        }
        public void Clear()
        {
            LightmapSettings.lightProbes=null;
            foreach (var item in items) if (item.go != null) DestroyImmediate(item.go); items.Clear(); selected = -1;
            foreach (var asset in assets.Values) asset.bundle.Unload(true); assets.Clear();
            SetLab(true);
        }
        public void Save(string path)
        {
            foreach (var item in items) { var t = item.go.transform; item.data.position = t.position; item.data.euler = t.eulerAngles; item.data.scale = t.localScale; item.data.name = item.go.name; }
            var session = new StudioSession { showLab = showLab, objects = items.Select(i => i.data).ToArray(), cameras = app.cinema.shots.ToArray(), selectedCamera = app.cinema.selected };
            session.lighting=app.photo.lighting.settings;session.photo=app.photo.settings;session.cameraTransition=app.cinema.transition;
            if (app.motor.Root != null) { session.avatarPosition = app.motor.Root.position; session.avatarEuler = app.motor.Root.eulerAngles; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp"; File.WriteAllText(temp, JsonUtility.ToJson(session, true)); AvatarArchive.CommitFile(temp, path); Status = "Estúdio salvo: " + path;
        }
        public IEnumerator Load(string path)
        {
            StudioSession session;
            try
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Arquivo de estúdio muito grande.");
                session = JsonUtility.FromJson<StudioSession>(File.ReadAllText(path));
                if (session == null || session.version != 1 || session.objects == null || session.objects.Length > 1000) throw new InvalidDataException("Estúdio incompatível.");
                foreach (var o in session.objects) if (string.IsNullOrEmpty(o.primitive) && !File.Exists(o.source)) throw new FileNotFoundException("Asset do estúdio não encontrado: " + o.source);
            }
            catch (Exception ex) { Status = ex.Message; yield break; }
            app.photo.lighting.Restore();app.photo.lighting.settings=new StudioLightingSettings();Clear();
            foreach (var o in session.objects)
            {
                if (!string.IsNullOrEmpty(o.primitive) && Enum.TryParse(o.primitive, out PrimitiveType type))
                {
                    AddPrimitive(type); var item = items[items.Count - 1]; item.data = o; item.go.name = o.name; item.go.GetComponent<Renderer>().sharedMaterial.color = o.color; ApplyTransform(item.go.transform, o);
                }
                else yield return ImportRoutine(o.source, o.world, o);
            }
            SetLab(session.showLab);
            if (session.cameras != null && session.cameras.Length > 0) { app.cinema.shots.Clear(); app.cinema.shots.AddRange(session.cameras); app.cinema.selected = Mathf.Clamp(session.selectedCamera, 0, session.cameras.Length - 1); }
            if(session.lighting!=null) app.photo.lighting.settings=session.lighting;
            if(session.photo!=null) {app.photo.settings=session.photo;if(app.photo.settings.look==null) app.photo.settings.look=new PhotoLook();}
            app.cinema.transition=StudioLighting.Safe(session.cameraTransition,.35f,0,2);app.photo.lighting.Apply();
            app.motor.Teleport(session.avatarPosition, Quaternion.Euler(session.avatarEuler)); Status = "Estúdio carregado.";
        }
        public void DrawUI()
        {
            GUILayout.Label(Status);
            GUI.enabled = !Busy;
            if (GUILayout.Button("Importar mundo (.alworld)")) NativeFilePicker.PickAsync(p => Import(p, true), "Mundo AvatarLab\0*.alworld\0\0", "Importar mundo 3D");
            if (GUILayout.Button("Importar objeto (.alprop / .obj)")) NativeFilePicker.PickAsync(p => Import(p), "Objetos 3D\0*.alprop;*.obj\0\0", "Importar objeto 3D");
            GUILayout.Label("No Unity: Tools > AvatarLab > Exportar mundo ou objeto. OBJ simples importa geometria, UVs e normais; use .alprop para materiais e texturas.");
            GUILayout.BeginHorizontal(); foreach (var type in new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Cylinder }) if (GUILayout.Button(type.ToString())) AddPrimitive(type); GUILayout.EndHorizontal();
            if (GUILayout.Button("Adicionar piso")) { AddPrimitive(PrimitiveType.Cube); Selected.position = Vector3.zero + Vector3.down * .05f; Selected.localScale = new Vector3(10, .1f, 10); }
            bool lab = GUILayout.Toggle(showLab, "Exibir sala e luzes do laboratório"); if (lab != showLab) SetLab(lab);
            GUILayout.Space(8); GUILayout.Label("Objetos (" + items.Count + ")");
            for (int i = 0; i < items.Count; i++) if (GUILayout.Button((selected == i ? "● " : "") + items[i].go.name)) selected = i;
            if (Selected != null)
            {
                Selected.name = GUILayout.TextField(Selected.name);
                Selected.position = StudioGui.Vector("Posição", Selected.position);
                Selected.eulerAngles = StudioGui.Vector("Rotação", Selected.eulerAngles);
                var scale = StudioGui.Vector("Escala", Selected.localScale); Selected.localScale = new Vector3(Mathf.Max(.001f, scale.x), Mathf.Max(.001f, scale.y), Mathf.Max(.001f, scale.z));
                if (GUILayout.Button("Duplicar objeto") && !items[selected].data.world)
                {
                    var original = items[selected]; var copy = Instantiate(original.go); copy.name = original.go.name + " cópia"; copy.transform.position += Vector3.right * .3f;
                    items.Add(new Item { go = copy, data = JsonUtility.FromJson<StageItemData>(JsonUtility.ToJson(original.data)) }); selected = items.Count - 1;
                }
                if (GUILayout.Button("Remover objeto selecionado")) { DestroyImmediate(items[selected].go); items.RemoveAt(selected); selected = -1; }
            }
            GUILayout.Space(10); GUILayout.Label("Salvar / carregar estúdio"); sessionName = GUILayout.TextField(sessionName);
            string safe = string.Concat(sessionName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))); if (string.IsNullOrWhiteSpace(safe)) safe = "Estudio";
            Directory.CreateDirectory(SessionDirectory);
            if (GUILayout.Button("Salvar estúdio")) { try { Save(Path.Combine(SessionDirectory, safe + ".json")); } catch (Exception ex) { Status = ex.Message; } }
            if (GUILayout.Button("Listar estúdios salvos")) { saved.Clear(); saved.AddRange(Directory.GetFiles(SessionDirectory, "*.json")); }
            foreach (var file in saved) if (GUILayout.Button("Abrir " + Path.GetFileNameWithoutExtension(file))) StartCoroutine(Load(file));
            GUI.enabled = true;
        }
        void OnDestroy()
        {
            foreach (var item in items) if (item.go != null) DestroyImmediate(item.go);
            foreach (var asset in assets.Values) if (asset.bundle != null) asset.bundle.Unload(true);
        }
    }
}
