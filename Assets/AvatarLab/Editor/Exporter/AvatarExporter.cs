using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AvatarLab.Editor
{
    public sealed class AvatarExporter : EditorWindow
    {
        GameObject avatar;
        bool inbox = true;
        Vector2 scroll;
        string lastReport = "";
        const string TempRoot = "Assets/AvatarLabExport";
        static readonly Dictionary<Motion,Motion> physicsMotions = new Dictionary<Motion,Motion>();
        public static string Inbox => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "AvatarLab", "AvatarLab Offline", "Inbox");
        [MenuItem("Tools/AvatarLab/Exportar avatar local")]
        [MenuItem("Tools/VRCStage/Exportar avatar local")]
        static void Open()
        {
            var w = GetWindow<AvatarExporter>("VRCStage • envio local");
            w.minSize = new Vector2(470, 450); w.avatar = Selection.activeGameObject;
        }
        void OnGUI()
        {
            GUILayout.Label("Enviar para VRCStage", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Exportação local para Windows. Não publica nada no VRChat ou na internet. O programa usa sistemas próprios; leia o relatório de compatibilidade.", MessageType.Info);
            avatar = (GameObject)EditorGUILayout.ObjectField("Raiz do avatar", avatar, typeof(GameObject), true);
            EditorGUILayout.HelpBox("Detecção automática: Base, Additive, Gesture, Action, FX, Sitting, T-Pose e IK Pose; menus, parâmetros, física e constraints configurados no avatar. Não é necessário escolher controladores manualmente.", MessageType.Info);
            EditorGUILayout.HelpBox("Avatares com VRCFury são preparados em uma cópia pelos preprocessadores locais do SDK antes da exportação. SDK/VRCFury devem estar instalados neste projeto. Consulte o relatório para limites de runtime.", MessageType.Info);
            inbox = EditorGUILayout.Toggle("Enviar direto ao programa", inbox);
            GUILayout.Label("Unity exigida pelo player: 2022.3.22f1", EditorStyles.miniLabel);
            GUI.enabled = avatar != null && !EditorApplication.isPlaying;
            if (GUILayout.Button("EXPORTAR / ENVIAR LOCAL", GUILayout.Height(38)))
            {
                string output = "";
                try
                {
                    if (inbox) { Directory.CreateDirectory(Inbox); output = Path.Combine(Inbox, SafeName(avatar.name) + ".alavatar"); }
                    else output = EditorUtility.SaveFilePanel("Exportar avatar local", "", SafeName(avatar.name), "alavatar");
                    if (!string.IsNullOrEmpty(output))
                    {
                        var manifest = Export(avatar, output);
                        lastReport = "Salvo em: " + output + "\n\n" + string.Join("\n", manifest.notes);
                        EditorUtility.RevealInFinder(output);
                    }
                }
                catch (Exception ex) { lastReport = ex.ToString(); Debug.LogException(ex); EditorUtility.DisplayDialog("AvatarLab", ex.Message, "OK"); }
            }
            GUI.enabled = true;
            scroll = EditorGUILayout.BeginScrollView(scroll); EditorGUILayout.TextArea(lastReport, GUILayout.ExpandHeight(true)); EditorGUILayout.EndScrollView();
        }
        static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Avatar" : name;
        }
        public static AvatarManifest Export(GameObject source, string output, RuntimeAnimatorController baseOverride = null, RuntimeAnimatorController fxOverride = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Application.unityVersion != "2022.3.22f1") throw new InvalidOperationException("Use Unity 2022.3.22f1 para exportar para este player.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("Mude o projeto para PC, Mac & Linux Standalone / Windows x86_64 antes de exportar.");
            // Never overwrite an existing user folder. The reserved folder is created only for this transaction.
            if (AssetDatabase.IsValidFolder(TempRoot) || Directory.Exists(TempRoot)) throw new IOException("A pasta temporária " + TempRoot + " já existe. Renomeie-a antes de exportar.");
            AssetDatabase.CreateFolder("Assets", "AvatarLabExport");
            Scene preview = default; GameObject clone = null;
            string archiveTemp = output + ".tmp-" + Guid.NewGuid().ToString("N");
            var notes = new List<string>();
            var controllerCopies = new Dictionary<RuntimeAnimatorController, RuntimeAnimatorController>();
            physicsMotions.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("AvatarLab", "Preparando cópia do avatar...", .1f);
                preview = EditorSceneManager.NewPreviewScene();
                clone = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(clone, preview);
                clone.name = source.name; clone.transform.position = Vector3.zero; clone.transform.rotation = Quaternion.identity; clone.SetActive(true);
                AvatarPreprocessor.Run(clone,notes);
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                var definition = clone.GetComponent<AvatarDefinition>() ?? clone.AddComponent<AvatarDefinition>();
                definition.displayName = source.name;
                definition.animator = clone.GetComponent<Animator>() ?? clone.GetComponentInChildren<Animator>(true);
                if (definition.animator != null && definition.animator.transform != clone.transform)
                    throw new InvalidOperationException("O Animator principal precisa estar na raiz selecionada. Selecione a raiz do avatar.");
                ReadDeclarativeSettings(clone, definition, notes);
                PhysicsConverter.Convert(clone, notes);
                ConstraintConverter.Convert(clone,notes);
                if (baseOverride != null) definition.baseController = baseOverride;
                if (fxOverride != null) definition.fxController = fxOverride;
                if (definition.baseController == null && definition.animator != null) definition.baseController = definition.animator.runtimeAnimatorController;
                definition.baseController = CopyController(definition.baseController, controllerCopies, notes);
                definition.fxController = CopyController(definition.fxController, controllerCopies, notes);
                foreach(var layer in definition.animationLayers) layer.controller=CopyController(layer.controller,controllerCopies,notes);
                foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
                    animator.runtimeAnimatorController = CopyController(animator.runtimeAnimatorController, controllerCopies, notes);
                var unsupported = new Dictionary<string, int>();
                var components = clone.GetComponentsInChildren<Component>(true);
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    var c = components[i]; if (c == null) continue;
                    var type = c.GetType();
                    bool supported = c is AvatarDefinition || c is AvatarSecondaryMotion || c is AvatarPhysicsChain || c is AvatarPhysicsCollider || c is AvatarConstraint || c is AvatarConstraintRunner || type.Assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal);
                    if (c is Camera || c is AudioListener) supported = false;
                    if (supported) continue;
                    string label = type.FullName;
                    unsupported[label] = unsupported.TryGetValue(label, out int count) ? count + 1 : 1;
                    Object.DestroyImmediate(c);
                }
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                foreach (var c in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (c != null && !(c is AvatarDefinition) && !(c is AvatarSecondaryMotion) && !(c is AvatarPhysicsChain) && !(c is AvatarPhysicsCollider) && !(c is AvatarConstraint) && !(c is AvatarConstraintRunner)) throw new InvalidOperationException("Não foi possível remover o componente não suportado: " + c.GetType().FullName);
                foreach (var pair in unsupported) notes.Add("Removido: " + pair.Key + " (" + pair.Value + "). O comportamento não é simulado.");
                notes.Add("Contacts do SDK ainda não executam. Constraints e física usam conversores/solvers próprios; ciclos, Aim/LookAt e escala não uniforme podem diferir do VRChat.");
                notes.Add("Camadas Base/Additive/Gesture/Action/FX e poses importadas; comandos de pesos, parâmetros e tracking convertidos. Outros comportamentos são reportados individualmente.");
                if (definition.animator == null || !definition.animator.isHuman) notes.Add("Rig não Humanoid: visual e controles disponíveis; calibração corporal desativada.");
                if (definition.baseController == null && definition.fxController == null) notes.Add("Sem controladores Base/FX. Use blendshapes manuais ou informe um controlador no exportador.");
                definition.compatibilityNotes = notes;
                var prefab = PrefabUtility.SaveAsPrefabAsset(clone, TempRoot + "/avatar.prefab");
                if (prefab == null) throw new IOException("Não foi possível salvar o prefab temporário.");
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayProgressBar("AvatarLab", "Compilando materiais, texturas e animações...", .45f);
                string buildFolder = Path.Combine(Path.GetTempPath(), "AvatarLab-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(buildFolder);
                try
                {
                    string bundleName = "avatar-" + Guid.NewGuid().ToString("N") + ".bundle";
                    var result = BuildPipeline.BuildAssetBundles(buildFolder, new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { TempRoot + "/avatar.prefab" } } }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
                    if (result == null) throw new IOException("Falha ao compilar o avatar. Consulte o Console do Unity.");
                    string identity = GlobalObjectId.GetGlobalObjectIdSlow(source).ToString();
                    if (identity.EndsWith("-0-0") || identity.Contains("-00000000000000000000000000000000-")) identity = "unsaved:" + source.name;
                    var manifest = new AvatarManifest { id = AvatarArchive.Hash(Encoding.UTF8.GetBytes(identity)).Substring(0, 32), name = source.name, unityVersion = Application.unityVersion, notes = notes.ToArray() };
                    AvatarArchive.Write(archiveTemp, manifest, File.ReadAllBytes(Path.Combine(buildFolder, bundleName)));
                    AvatarArchive.CommitFile(archiveTemp, output);
                    File.WriteAllText(output + ".report.txt", string.Join(Environment.NewLine, notes), new UTF8Encoding(false));
                    return manifest;
                }
                finally
                {
                    // buildFolder is a newly created transaction directory under the system temp folder.
                    Directory.Delete(buildFolder, true);
                }
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                AssetDatabase.DeleteAsset(TempRoot);
                if (File.Exists(archiveTemp)) File.Delete(archiveTemp);
                EditorUtility.ClearProgressBar();
            }
        }
        static RuntimeAnimatorController CopyController(RuntimeAnimatorController source, Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> copies, List<string> notes)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out var existing)) return existing;
            if (source is AnimatorOverrideController overrides)
            {
                var clone = new AnimatorOverrideController(CopyController(overrides.runtimeAnimatorController, copies, notes));
                var values = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(values);
                var keys = new List<KeyValuePair<AnimationClip, AnimationClip>>(); clone.GetOverrides(keys);
                for(int i=0;i<keys.Count;i++) { var match=values.FirstOrDefault(v=>v.Key==keys[i].Key || v.Key.name==keys[i].Key.name); keys[i]=new KeyValuePair<AnimationClip,AnimationClip>(keys[i].Key,PhysicsMotion(match.Value,notes) as AnimationClip); }
                clone.ApplyOverrides(keys);
                AssetDatabase.CreateAsset(clone, TempRoot + "/override-" + copies.Count + ".overrideController"); copies[source] = clone; return clone;
            }
            string original = AssetDatabase.GetAssetPath(source);
            string path = TempRoot + "/controller-" + copies.Count + ".controller";
            if (string.IsNullOrEmpty(original) || !AssetDatabase.CopyAsset(original, path)) throw new IOException("Controlador precisa estar salvo como asset: " + source.name);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) throw new IOException("Tipo de controlador não suportado: " + source.name);
            copies[source] = controller;
            var originalBehaviours=AssetDatabase.LoadAllAssetsAtPath(path).OfType<StateMachineBehaviour>().ToArray();
            foreach (var layer in controller.layers) StripBehaviours(layer.stateMachine, path, notes);
            foreach (var behaviour in originalBehaviours) Object.DestroyImmediate(behaviour, true);
            EditorUtility.SetDirty(controller); return controller;
        }
        static void StripBehaviours(AnimatorStateMachine machine, string owner, List<string> notes)
        {
            if (AssetDatabase.GetAssetPath(machine) != owner) throw new IOException("StateMachine externo não suportado; exportação cancelada para proteger os assets originais.");
            var machineBehaviours=machine.behaviours; machine.behaviours=Array.Empty<StateMachineBehaviour>();
            foreach(var b in machineBehaviours) ConvertBehaviour(b,t=>machine.AddStateMachineBehaviour(t),notes);
            foreach (var child in machine.states)
            {
                if (AssetDatabase.GetAssetPath(child.state) != owner) throw new IOException("AnimatorState externo não suportado.");
                var stateBehaviours=child.state.behaviours; child.state.behaviours=Array.Empty<StateMachineBehaviour>();
                foreach(var b in stateBehaviours) ConvertBehaviour(b,t=>child.state.AddStateMachineBehaviour(t),notes);
                child.state.motion=PhysicsMotion(child.state.motion,notes);
                EditorUtility.SetDirty(child.state);
            }
            foreach (var child in machine.stateMachines) StripBehaviours(child.stateMachine, owner, notes);
            EditorUtility.SetDirty(machine);
        }
        static void ConvertBehaviour(StateMachineBehaviour source,Func<Type,StateMachineBehaviour> create,List<string> notes)
        {
            if(source==null) return;
            string name=source.GetType().Name;
            if(source is AvatarLayerControl || source is AvatarParameterDriver || source is AvatarTrackingControl)
            { EditorUtility.CopySerialized(source,create(source.GetType())); return; }
            if(name=="VRCAvatarParameterDriver")
            {
                var d=new SerializedObject(source); var replacement=(AvatarParameterDriver)create(typeof(AvatarParameterDriver));
                replacement.localOnly=d.FindProperty("localOnly")?.boolValue??false;
                var list=d.FindProperty("parameters");
                if(list!=null && list.isArray)
                {
                    replacement.parameters=new AvatarParameterOperation[list.arraySize];
                    for(int i=0;i<list.arraySize;i++)
                    {
                        var p=list.GetArrayElementAtIndex(i); var op=new AvatarParameterOperation();
                        foreach(var f in typeof(AvatarParameterOperation).GetFields())
                        {
                            var value=p.FindPropertyRelative(f.Name); if(value==null) continue;
                            if(f.FieldType==typeof(string)) f.SetValue(op,value.stringValue);
                            else if(f.FieldType==typeof(bool)) f.SetValue(op,value.boolValue);
                            else if(f.FieldType==typeof(int)) f.SetValue(op,value.intValue);
                            else if(f.FieldType==typeof(float)) f.SetValue(op,value.floatValue);
                        }
                        replacement.parameters[i]=op;
                    }
                }
                EditorUtility.SetDirty(replacement); notes.Add("Parameter Driver convertido: "+replacement.parameters.Length+" operações locais."); return;
            }
            if(name=="VRCAnimatorTrackingControl")
            {
                var d=new SerializedObject(source); var replacement=(AvatarTrackingControl)create(typeof(AvatarTrackingControl)); var parts=new List<string>(); var modes=new List<int>();
                foreach(string part in new[]{"Head","Hip","LeftHand","RightHand","LeftFoot","RightFoot","LeftFingers","RightFingers","Eyes","Mouth"})
                { var p=d.FindProperty("tracking"+part); if(p!=null) { parts.Add(part); modes.Add(p.intValue); } }
                replacement.parts=parts.ToArray(); replacement.modes=modes.ToArray(); EditorUtility.SetDirty(replacement); notes.Add("Tracking Control convertido (tracking/animação por parte do corpo)."); return;
            }
            if(name=="VRCPlayableLayerControl" || name=="VRCAnimatorLayerControl")
            {
                var data=new SerializedObject(source); var type=data.FindProperty(name=="VRCAnimatorLayerControl"?"playable":"layer");
                if(type!=null && type.propertyType==SerializedPropertyType.Enum)
                {
                    var replacement=(AvatarLayerControl)create(typeof(AvatarLayerControl)); replacement.target=type.enumNames[type.enumValueIndex];
                    replacement.weight=data.FindProperty("goalWeight")?.floatValue??1;
                    replacement.duration=data.FindProperty("blendDuration")?.floatValue??0;
                    replacement.animatorLayer=name=="VRCAnimatorLayerControl"?(data.FindProperty("layer")?.intValue??0):-1;
                    if(name=="VRCAnimatorLayerControl") { var playable=data.FindProperty("playable"); if(playable!=null) replacement.target=playable.enumNames[playable.enumValueIndex]; }
                    EditorUtility.SetDirty(replacement); notes.Add("Controle de peso convertido: "+name+" / "+replacement.target); return;
                }
            }
            notes.Add("StateMachineBehaviour sem equivalente removido: "+source.GetType().FullName);
        }
        static Motion PhysicsMotion(Motion motion,List<string> notes,int depth=0)
        {
            if(motion==null) return null;
            if(physicsMotions.TryGetValue(motion,out var cached)) return cached;
            if(depth>32) throw new InvalidOperationException("BlendTree recursivo/profundo não suportado.");
            if(motion is AnimationClip clip)
            {
                var bindings=AnimationUtility.GetCurveBindings(clip).Where(b=>NeedsRemap(b.type)).ToArray();
                var objectBindings=AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(b=>NeedsRemap(b.type)).ToArray();
                if(bindings.Length==0 && objectBindings.Length==0) { physicsMotions[motion]=motion; return motion; }
                var copy=Object.Instantiate(clip); copy.name=clip.name;
                foreach(var binding in bindings)
                {
                    var curve=AnimationUtility.GetEditorCurve(copy,binding); AnimationUtility.SetEditorCurve(copy,binding,null);
                    var target=ConvertedType(binding.type);
                    string field=binding.propertyName.Split('.')[0];
                    if(target!=null && (field=="m_Enabled" || target.GetField(field)!=null))
                    {
                        var rewritten=binding; rewritten.type=target; AnimationUtility.SetEditorCurve(copy,rewritten,curve);
                    }
                    else notes.Add("Curva SDK sem equivalente removida: "+binding.type.Name+" / "+binding.propertyName);
                }
                foreach(var binding in objectBindings)
                {
                    var values=AnimationUtility.GetObjectReferenceCurve(copy,binding); AnimationUtility.SetObjectReferenceCurve(copy,binding,null);
                    var target=ConvertedType(binding.type); string field=binding.propertyName.Split('.')[0];
                    if(target!=null && target.GetField(field)!=null) { var rewritten=binding; rewritten.type=target; AnimationUtility.SetObjectReferenceCurve(copy,rewritten,values); }
                    else notes.Add("Referência animada SDK sem equivalente removida: "+binding.type.Name+" / "+binding.propertyName);
                }
                string folder="motion-"+Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder(TempRoot,folder);
                AssetDatabase.CreateAsset(copy,TempRoot+"/"+folder+"/"+SafeName(clip.name)+".anim"); physicsMotions[motion]=copy; notes.Add("Curvas de componentes remapeadas: "+clip.name+" ("+(bindings.Length+objectBindings.Length)+")."); return copy;
            }
            if(motion is BlendTree tree)
            {
                var children=tree.children; bool changed=false;
                for(int i=0;i<children.Length;i++) { var next=PhysicsMotion(children[i].motion,notes,depth+1); if(next!=children[i].motion) { children[i].motion=next; changed=true; } }
                if(changed)
                {
                    var copy=new BlendTree {name=tree.name,blendType=tree.blendType,blendParameter=tree.blendParameter,blendParameterY=tree.blendParameterY,minThreshold=tree.minThreshold,maxThreshold=tree.maxThreshold,useAutomaticThresholds=tree.useAutomaticThresholds,children=children};
                    var src=new SerializedObject(tree); var dst=new SerializedObject(copy); var normalize=src.FindProperty("m_NormalizedBlendValues"); var destNormalize=dst.FindProperty("m_NormalizedBlendValues");
                    if(normalize!=null && destNormalize!=null) { destNormalize.boolValue=normalize.boolValue; dst.ApplyModifiedPropertiesWithoutUndo(); }
                    AssetDatabase.CreateAsset(copy,TempRoot+"/physics-"+Guid.NewGuid().ToString("N")+".asset"); physicsMotions[motion]=copy; return copy;
                }
            }
            physicsMotions[motion]=motion; return motion;
        }
        static Type ConvertedType(Type type)=>type?.Name=="VRCPhysBone"?typeof(AvatarPhysicsChain):type?.Name=="VRCPhysBoneCollider"?typeof(AvatarPhysicsCollider):ConstraintConverter.TargetType(type);
        static bool NeedsRemap(Type type)=>type!=null && (ConvertedType(type)!=null || (type.Namespace??"").StartsWith("VRC.",StringComparison.Ordinal));
        static void ReadDeclarativeSettings(GameObject root, AvatarDefinition definition, List<string> notes)
        {
            // Read only the user's serialized configuration. No SDK type references or SDK runtime code.
            foreach (var component in root.GetComponents<Component>())
            {
                if (component == null || component.GetType().Name != "VRCAvatarDescriptor") continue;
                var data = new SerializedObject(component);
                definition.animationLayers.Clear();
                foreach(var property in new[]{"baseAnimationLayers","specialAnimationLayers"})
                {
                var layers = data.FindProperty(property);
                if (layers != null && layers.isArray)
                    for (int i = 0; i < layers.arraySize; i++)
                    {
                        var layer = layers.GetArrayElementAtIndex(i);
                        var type = layer.FindPropertyRelative("type");
                        var controller = layer.FindPropertyRelative("animatorController")?.objectReferenceValue as RuntimeAnimatorController;
                        if (type == null) continue;
                        string name = type.enumNames[type.enumValueIndex];
                        if(layer.FindPropertyRelative("isDefault")?.boolValue==true)
                        {
                            string standard=DefaultControllerName(name);
                            string asset=AssetDatabase.GetAllAssetPaths().FirstOrDefault(p=>p.EndsWith("/"+standard+".controller",StringComparison.OrdinalIgnoreCase));
                            controller=asset!=null?AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(asset):null;
                            if(controller==null) notes.Add("Camada padrão não encontrada no projeto: "+name+". Use um controlador próprio para testar.");
                        }
                        if (name == "FX") definition.fxController = controller;
                        else if (name == "Base") definition.baseController = controller;
                        if(controller!=null) { definition.animationLayers.Add(new AvatarAnimationLayer {type=name,controller=controller,mask=layer.FindPropertyRelative("mask")?.objectReferenceValue as AvatarMask}); notes.Add("Camada de animação importada: "+name+" / "+controller.name); }
                    }
                }
                var parameters = data.FindProperty("expressionParameters")?.objectReferenceValue;
                if (parameters != null)
                {
                    var list = new SerializedObject(parameters).FindProperty("parameters");
                    if (list != null && list.isArray)
                        for (int i = 0; i < list.arraySize; i++)
                        {
                            var p = list.GetArrayElementAtIndex(i);
                            definition.defaults.Add(new ParameterDefault { name = p.FindPropertyRelative("name").stringValue, value = p.FindPropertyRelative("defaultValue").floatValue });
                        }
                }
                var menu = data.FindProperty("expressionsMenu")?.objectReferenceValue;
                if (menu != null) { definition.controls.Clear(); ReadMenu(menu, "", definition.controls, new HashSet<Object>(), 0, notes); }
                var view = data.FindProperty("ViewPosition"); if (view != null) definition.eyeHeight = view.vector3Value.y;
            }
        }
        static string DefaultControllerName(string type)
        {
            switch(type)
            {
                case "Base": return "vrc_AvatarV3LocomotionLayer";
                case "Additive": return "vrc_AvatarV3IdleLayer";
                case "Gesture": return "vrc_AvatarV3HandsLayer";
                case "Action": return "vrc_AvatarV3ActionLayer";
                case "FX": return "vrc_AvatarV3FaceLayer";
                case "Sitting": return "vrc_AvatarV3SittingLayer";
                case "TPose": return "vrc_AvatarV3UtilityTPose";
                case "IKPose": return "vrc_AvatarV3UtilityIKPose";
                default: return "__none__";
            }
        }
        static void ReadMenu(Object menu, string parent, List<ExpressionControl> output, HashSet<Object> ancestry, int depth, List<string> notes)
        {
            if (menu == null || depth > 10 || !ancestry.Add(menu)) return;
            var controls = new SerializedObject(menu).FindProperty("controls");
            if (controls == null || !controls.isArray) { ancestry.Remove(menu); return; }
            for (int i = 0; i < controls.arraySize; i++)
            {
                var c = controls.GetArrayElementAtIndex(i);
                var typeProperty = c.FindPropertyRelative("type");
                int typeIndex = typeProperty != null && typeProperty.propertyType == SerializedPropertyType.Enum ? typeProperty.enumValueIndex : -1;
                string typeName = typeProperty != null && typeIndex >= 0 && typeIndex < typeProperty.enumNames.Length ? typeProperty.enumNames[typeIndex] : "";
                int type;
                switch (typeName)
                {
                    case "Button": type = 0; break;
                    case "Toggle": type = 1; break;
                    case "SubMenu": type = 2; break;
                    case "TwoAxisPuppet": type = 3; break;
                    case "FourAxisPuppet": type = 4; break;
                    case "RadialPuppet": type = 5; break;
                    default: notes.Add("Controle de menu vazio ou não reconhecido: " + typeName + " (" + (typeProperty != null ? typeProperty.intValue.ToString() : "ausente") + ")"); continue;
                }
                var control = new ExpressionControl { label = System.Text.RegularExpressions.Regex.Replace(c.FindPropertyRelative("name")?.stringValue ?? "Control", "<[^>]+>", ""), type = type, parent = parent, parameter = c.FindPropertyRelative("parameter")?.FindPropertyRelative("name")?.stringValue ?? "", value = c.FindPropertyRelative("value")?.floatValue ?? 1 };
                var subParameters = c.FindPropertyRelative("subParameters");
                if (subParameters != null && subParameters.isArray)
                {
                    control.axes = new string[subParameters.arraySize];
                    for (int a = 0; a < control.axes.Length; a++) control.axes[a] = subParameters.GetArrayElementAtIndex(a).FindPropertyRelative("name")?.stringValue ?? "";
                }
                if (control.type == 2)
                {
                    control.axes = string.IsNullOrEmpty(control.parameter) ? Array.Empty<string>() : new[] { control.parameter };
                    string child = parent + "/" + i; control.parameter = child;
                    ReadMenu(c.FindPropertyRelative("subMenu")?.objectReferenceValue, child, output, ancestry, depth + 1, notes);
                }
                output.Add(control);
            }
            ancestry.Remove(menu);
        }
    }
}
