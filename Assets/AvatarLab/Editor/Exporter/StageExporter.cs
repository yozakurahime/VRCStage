using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AvatarLab.Editor
{
    public sealed class StageExporter : EditorWindow
    {
        GameObject selectedRoot;
        bool wholeScene = true, world = true;
        string status = "";
        [MenuItem("Tools/AvatarLab/Exportar mundo ou objeto")]
        [MenuItem("Tools/VRCStage/Exportar mundo ou objeto")]
        static void Open() { var w = GetWindow<StageExporter>("AvatarLab • cenário"); w.selectedRoot = Selection.activeGameObject; }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Exporta geometria, materiais, luzes, lightmaps e colliders para Windows. Scripts de mundo/Udon não são executados no player. Use Built-in e Unity 2022.3.22f1.", MessageType.Info);
            world = EditorGUILayout.Toggle("Exportar como mundo", world);
            wholeScene = EditorGUILayout.Toggle("Usar cena inteira", wholeScene && world);
            selectedRoot = (GameObject)EditorGUILayout.ObjectField("Raiz selecionada", selectedRoot, typeof(GameObject), true);
            if (GUILayout.Button("Exportar cenário / objeto"))
            {
                string p = EditorUtility.SaveFilePanel("Exportar cenário", "", world ? "Meu mundo" : "Meu objeto", world ? "alworld" : "alprop");
                if (!string.IsNullOrEmpty(p)) try { Export(selectedRoot, wholeScene, world, p); status = "Exportado: " + p; } catch (Exception ex) { status = ex.Message; Debug.LogException(ex); }
            }
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        }
        public static void Export(GameObject source, bool entireScene, bool isWorld, string output)
        {
            const string temporaryFolder = "Assets/AvatarLabWorldExport";
            if (Application.unityVersion != "2022.3.22f1" || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64) throw new InvalidOperationException("Use Unity 2022.3.22f1 e Windows x86_64.");
            if (!entireScene && source == null) throw new ArgumentException("Selecione a raiz do objeto.");
            if (AssetDatabase.IsValidFolder(temporaryFolder) || Directory.Exists(temporaryFolder)) throw new IOException("A pasta " + temporaryFolder + " já existe; renomeie-a antes de exportar.");
            AssetDatabase.CreateFolder("Assets", "AvatarLabWorldExport");
            var sourceScene = SceneManager.GetActiveScene();
            var preview = EditorSceneManager.NewPreviewScene(); GameObject root = null;
            string build = Path.Combine(Path.GetTempPath(), "AvatarLabStage-" + Guid.NewGuid().ToString("N"));
            string archive = output + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var scene = sourceScene; var originalMaps = LightmapSettings.lightmaps;
                var originalRenderers = entireScene ? scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray() : source.GetComponentsInChildren<Renderer>(true);
                root = new GameObject(entireScene ? scene.name : source.name); SceneManager.MoveGameObjectToScene(root, preview);
                if (entireScene)
                {
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        if (go == root) continue;
                        var clone = Object.Instantiate(go); SceneManager.MoveGameObjectToScene(clone, preview); clone.name = go.name; clone.transform.SetParent(root.transform, true);
                    }
                }
                else { var clone = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(clone, preview); clone.name = source.name; clone.transform.SetParent(root.transform, true); }
                var definition = root.AddComponent<StageDefinition>(); definition.kind = isWorld ? "world" : "prop";
                definition.ambient = RenderSettings.ambientLight; definition.skybox = RenderSettings.skybox;
                definition.hasEnvironmentSettings=true;definition.ambientMode=RenderSettings.ambientMode;
                definition.ambientSky=RenderSettings.ambientSkyColor;definition.ambientEquator=RenderSettings.ambientEquatorColor;definition.ambientGround=RenderSettings.ambientGroundColor;
                definition.ambientIntensity=RenderSettings.ambientIntensity;definition.reflectionIntensity=RenderSettings.reflectionIntensity;
                definition.reflectionMode=RenderSettings.defaultReflectionMode;definition.customReflectionTexture=RenderSettings.customReflectionTexture;
                var sh=RenderSettings.ambientProbe;var coefficients=new float[27];bool finite=true;
                for(int channel=0;channel<3;channel++) for(int coefficient=0;coefficient<9;coefficient++) {float value=sh[channel,coefficient];coefficients[channel*9+coefficient]=value;finite &= !float.IsNaN(value) && !float.IsInfinity(value);}
                if(finite) definition.ambientSH=coefficients;
                if(LightmapSettings.lightProbes!=null && LightmapSettings.lightProbes.count>0)
                {
                    definition.lightProbes=Object.Instantiate(LightmapSettings.lightProbes);
                    definition.lightProbes.name="World baked light probes";
                    AssetDatabase.CreateAsset(definition.lightProbes,temporaryFolder+"/light-probes.asset");
                    Debug.Log("AVATARLAB_EXPORTED_LIGHT_PROBES "+definition.lightProbes.count);
                }
                definition.lightmapsMode = LightmapSettings.lightmapsMode;
                definition.lightmapColors = originalMaps.Select(x => x.lightmapColor).ToArray(); definition.lightmapDirections = originalMaps.Select(x => x.lightmapDir).ToArray(); definition.shadowMasks = originalMaps.Select(x => x.shadowMask).ToArray();
                var clonedRenderers = root.GetComponentsInChildren<Renderer>(true);
                definition.lightmappedRenderers = clonedRenderers;
                definition.lightmapIndices = originalRenderers.Select(x => x.lightmapIndex).ToArray(); definition.lightmapOffsets = originalRenderers.Select(x => x.lightmapScaleOffset).ToArray();
                var removed = new Dictionary<string, int>();
                foreach (var c in root.GetComponentsInChildren<Component>(true).Reverse())
                {
                    if (c == null || c is StageDefinition) continue;
                    bool keep = !(c is MonoBehaviour) && c.GetType().Assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal);
                    if (c is Camera || c is AudioListener || c is Animator || c is Animation) keep = false;
                    if (keep) continue;
                    string type = c.GetType().FullName; removed[type] = removed.TryGetValue(type, out int n) ? n + 1 : 1; Object.DestroyImmediate(c);
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 0; GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject); }
                var remaining=root.GetComponentsInChildren<MonoBehaviour>(true).Where(x=>x!=null && !(x is StageDefinition)).ToArray();
                if (remaining.Length>0) throw new IOException("Scripts dependentes não removidos: "+string.Join(", ",remaining.Select(x=>x.GetType().FullName).Distinct()));
                definition.notes = removed.Select(x => "Removido: " + x.Key + " (" + x.Value + ")").ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, temporaryFolder + "/stage.prefab"); AssetDatabase.SaveAssets(); Directory.CreateDirectory(build);
                string bundleName = "stage-" + Guid.NewGuid().ToString("N") + ".bundle";
                var result = BuildPipeline.BuildAssetBundles(build, new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { temporaryFolder + "/stage.prefab" } } }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
                if (result == null) throw new IOException("Falha ao compilar o cenário.");
                var manifest = new StageManifest { id = Guid.NewGuid().ToString("N"), name = root.name, kind = definition.kind, unityVersion = Application.unityVersion, notes = definition.notes };
                StageArchive.Write(archive, manifest, File.ReadAllBytes(Path.Combine(build, bundleName))); AvatarArchive.CommitFile(archive, output);
                File.WriteAllText(output + ".report.txt", string.Join(Environment.NewLine, definition.notes));
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); AssetDatabase.DeleteAsset(temporaryFolder);
                if (File.Exists(archive)) File.Delete(archive);
                string resolved = Path.GetFullPath(build), tempBase = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolved.StartsWith(tempBase, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("AvatarLabStage-", StringComparison.Ordinal) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }
    }
}
