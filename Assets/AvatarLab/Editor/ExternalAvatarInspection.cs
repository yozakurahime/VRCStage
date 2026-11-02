using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarLab.Editor
{
    public static class ExternalAvatarInspection
    {
        [Serializable] sealed class Inspection
        {
            public string name, id, error;
            public long packageBytes;
            public bool humanoid;
            public int skinnedMeshes, blendshapes, menuControls, expressionDefaults, faceBindings;
            public string baseController, fxController;
            public string[] unsupportedScripts, shaders;
            public ExpressionControl[] controls;
        }
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--inspect-avatar");
            var result = new Inspection(); AssetBundle bundle = null;
            try
            {
                if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("Informe --inspect-avatar arquivo.alavatar");
                var manifest = AvatarArchive.Read(args[at + 1], out var bytes);
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) throw new InvalidDataException("Não foi possível carregar o bundle.");
                var avatar = bundle.LoadAsset<GameObject>(manifest.prefab);
                var definition = avatar.GetComponent<AvatarDefinition>();
                if (definition == null) throw new InvalidDataException("AvatarDefinition ausente.");
                var skins = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                result.name = manifest.name; result.id = manifest.id; result.packageBytes = new FileInfo(args[at + 1]).Length;
                result.humanoid = definition.animator != null && definition.animator.isHuman;
                result.skinnedMeshes = skins.Length;
                result.blendshapes = skins.Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.blendShapeCount);
                result.menuControls = definition.controls.Count; result.expressionDefaults = definition.defaults.Count;
                result.faceBindings = definition.faceBindings.Count; result.controls = definition.controls.ToArray();
                result.baseController = definition.baseController != null ? definition.baseController.name : "";
                result.fxController = definition.fxController != null ? definition.fxController.name : "";
                result.unsupportedScripts = avatar.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(s => s == null || (!(s is AvatarDefinition) && !(s is AvatarSecondaryMotion)))
                    .Select(s => s == null ? "Missing MonoBehaviour" : s.GetType().FullName).ToArray();
                result.shaders = avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct().OrderBy(s => s).ToArray();
                if (result.unsupportedScripts.Length > 0) throw new InvalidDataException("Scripts externos presentes no pacote.");
                if (result.controls.Any(c => c.type < 0 || c.type > 5)) throw new InvalidDataException("Tipo de controle não normalizado.");
                File.WriteAllText("TestOutput/external-avatar-inspection.json", JsonUtility.ToJson(result, true));
                Debug.Log("AVATARLAB_EXTERNAL_INSPECTION_PASS " + result.name); bundle.Unload(true); EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                result.error = ex.ToString(); Directory.CreateDirectory("TestOutput");
                File.WriteAllText("TestOutput/external-avatar-inspection.json", JsonUtility.ToJson(result, true));
                Debug.LogException(ex); if (bundle != null) bundle.Unload(true); EditorApplication.Exit(1);
            }
        }
    }
}
