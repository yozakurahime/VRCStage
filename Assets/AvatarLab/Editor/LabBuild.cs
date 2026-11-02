using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Object = UnityEngine.Object;

namespace AvatarLab.Editor
{
    public static class LabBuild
    {
        public const string Generated = "Assets/AvatarLabGenerated";
        [MenuItem("Tools/AvatarLab/Projeto do player/Preparar laboratório")]
        public static void Prepare()
        {
            if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder("Assets", "AvatarLabGenerated");
            PlayerSettings.companyName = "AvatarLab"; PlayerSettings.productName = "AvatarLab Offline";
            PlayerSettings.bundleVersion = StageBrand.Version;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "local.avatarlab.offline");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1440; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").First());
            var input = player.FindProperty("activeInputHandler"); if (input != null) { input.intValue = 2; player.ApplyModifiedPropertiesWithoutUndo(); }
            QualitySettings.antiAliasing = 4; QualitySettings.pixelLightCount = 8;
            QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowDistance = 25;
            ConfigureXr();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = new GameObject("AvatarLab").AddComponent<LabApp>();
            app.motionLibrary=StudioMotionBuilder.Build();
            app.roomMaterial = Template("Room", "Standard"); app.mirrorMaterial = Template("Mirror", "Unlit/Texture");
            var spout = AssetDatabase.LoadAssetAtPath<Klak.Spout.SpoutResources>(Generated + "/SpoutResources.asset");
            if (spout == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Klak.Spout.SpoutResources>("Packages/jp.keijiro.klak.spout/Editor/SpoutResources.asset");
                if (source == null) throw new Exception("Recursos Spout não encontrados.");
                spout = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(spout, Generated + "/SpoutResources.asset");
            }
            app.spoutResources = spout;
            new GameObject("Validation (command line only)").AddComponent<LabSmokeTest>();
            new GameObject("Studio validation (command line only)").AddComponent<StudioSmokeTest>();
            new GameObject("Hand validation (command line only)").AddComponent<HandSmokeTest>();
            new GameObject("iFacial validation (command line only)").AddComponent<IfacialSmokeTest>();
            new GameObject("Physics validation (command line only)").AddComponent<PhysicsSmokeTest>();
            new GameObject("Leap service validation (command line only)").AddComponent<LeapSmokeTest>();
            new GameObject("Desk calibration validation (command line only)").AddComponent<DeskSmokeTest>();
            new GameObject("Game camera validation (command line only)").AddComponent<GameCameraSmokeTest>();
            new GameObject("Public release validation (command line only)").AddComponent<ReleaseSmokeTest>();
            new GameObject("VRCFury validation (command line only)").AddComponent<FurySmokeTest>();
            new GameObject("Performance probe (command line only)").AddComponent<PerformanceProbe>();
            new GameObject("Hair/video validation (command line only)").AddComponent<HairVideoSmokeTest>();
            new GameObject("Locomotion validation (command line only)").AddComponent<LocomotionSmokeTest>();
            new GameObject("Pose library validation (command line only)").AddComponent<PoseLibrarySmokeTest>();
            new GameObject("Pose geometry validation (command line only)").AddComponent<PoseGeometryTest>();
            new GameObject("RGB motion validation (command line only)").AddComponent<RgbMotionSmokeTest>();
            new GameObject("Integration bridge validation (command line only)").AddComponent<BridgeSmokeTest>();
            new GameObject("Photo validation (command line only)").AddComponent<PhotoSmokeTest>();
            new GameObject("World validation (command line only)").AddComponent<WorldSmokeTest>();
            new GameObject("GoGo pose validation (command line only)").AddComponent<GoGoPoseSmokeTest>();
            new GameObject("World lighting validation (command line only)").AddComponent<WorldLightingSmokeTest>();
            new GameObject("Post processing validation (command line only)").AddComponent<PostProcessSmokeTest>();
            string path = Generated + "/Lab.unity"; EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            AssetDatabase.SaveAssets();
        }
        static Material Template(string name, string shader)
        {
            string path = Generated + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
        static void ConfigureXr()
        {
            string path = Generated + "/XRSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings == null) { settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); AssetDatabase.CreateAsset(settings, path); }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Standalone)) settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var general = settings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            general.InitManagerOnStart = false;
            if (!AssetDatabase.Contains(general)) AssetDatabase.AddObjectToAsset(general, settings);
            if (!AssetDatabase.Contains(general.Manager)) AssetDatabase.AddObjectToAsset(general.Manager, settings);
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone)) throw new Exception("Falha ao configurar loader OpenXR.");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (xr == null) throw new Exception("OpenXRSettings não foi criado.");
            xr.renderMode = OpenXRSettings.RenderMode.MultiPass;
            foreach (var feature in xr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
                feature.enabled = feature is HTCViveControllerProfile || feature is OculusTouchControllerProfile || feature is MetaQuestTouchProControllerProfile || feature is ValveIndexControllerProfile || feature is KHRSimpleControllerProfile;
            EditorUtility.SetDirty(xr); EditorUtility.SetDirty(general); EditorUtility.SetDirty(settings);
        }
        [MenuItem("Tools/AvatarLab/Projeto do player/Atualizar pacote do exportador")]
        public static void ExportExporterOnly()
        {
            try
            {
                Directory.CreateDirectory("Build");
                AssetDatabase.ExportPackage(new[] { "Assets/AvatarLab/Shared", "Assets/AvatarLab/Editor/Exporter" }, "Build/AvatarLab-Exportador.unitypackage", ExportPackageOptions.Recurse);
                Debug.Log("AVATARLAB_EXPORTER_PACKAGE_SUCCESS");
                if(Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch(Exception ex) { Debug.LogException(ex); if(Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }
        [MenuItem("Tools/AvatarLab/Projeto do player/Compilar Windows e exportador")]
        public static void Build()
        {
            try
            {
                Prepare();
                Directory.CreateDirectory("Build/AvatarLabOffline/Exemplos");
                var sample = CreateSample();
                AvatarExporter.Export(sample, Path.GetFullPath("Build/AvatarLabOffline/Exemplos/Robot.alavatar"));
                Object.DestroyImmediate(sample);
                var physicsSample = CreatePhysicsSample();
                AvatarExporter.Export(physicsSample, Path.GetFullPath("Build/AvatarLabOffline/Exemplos/PhysicsChain.alavatar"));
                Object.DestroyImmediate(physicsSample);
                var stage = CreateStageExample();
                StageExporter.Export(stage, false, true, Path.GetFullPath("Build/AvatarLabOffline/Exemplos/Studio.alworld"));
                Object.DestroyImmediate(stage);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = EditorBuildSettings.scenes.Select(x => x.path).ToArray(), locationPathName = "Build/AvatarLabOffline/AvatarLab Offline.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build falhou: " + report.summary.result + " / " + report.summary.totalErrors + " erros.");
                AssetDatabase.ExportPackage(new[] { "Assets/AvatarLab/Shared", "Assets/AvatarLab/Editor/Exporter" }, "Build/AvatarLab-Exportador.unitypackage", ExportPackageOptions.Recurse);
                File.WriteAllText("Build/build-result.txt", "SUCCESS\nUnity " + Application.unityVersion + "\nBytes " + report.summary.totalSize + "\n");
                Debug.Log("AVATARLAB_BUILD_SUCCESS");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }
        public static GameObject CreatePhysicsSample()
        {
            var root=new GameObject("Física • cadeia e collider"); var definition=root.AddComponent<AvatarDefinition>(); definition.eyeHeight=1.6f;
            var temporary=GameObject.CreatePrimitive(PrimitiveType.Sphere); var mesh=Object.Instantiate(temporary.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(temporary);
            var vertices=mesh.vertices; for(int i=0;i<vertices.Length;i++) vertices[i]*=.08f; mesh.vertices=vertices; mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(Generated+"/PhysicsBead.asset"));
            var material=Template("Physics beads","Standard"); material.color=new Color(1,.4f,.1f);
            Transform parent=root.transform,anchor=null;
            for(int i=0;i<7;i++)
            {
                var bone=new GameObject("Bone "+i).transform; bone.SetParent(parent,false); bone.localPosition=i==0?new Vector3(0,1.7f,0):new Vector3(0,-.18f,0);
                bone.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh; bone.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material; if(i==0) anchor=bone; parent=bone;
            }
            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Sphere); obstacle.name="Collision sphere"; obstacle.transform.SetParent(root.transform,false); obstacle.transform.localPosition=new Vector3(.13f,1.12f,0); obstacle.transform.localScale=Vector3.one*.3f; Object.DestroyImmediate(obstacle.GetComponent<Collider>());
            var obstacleMaterial=Template("Physics obstacle","Standard"); obstacleMaterial.color=new Color(.1f,.5f,.8f); obstacle.GetComponent<Renderer>().sharedMaterial=obstacleMaterial;
            var collider=obstacle.AddComponent<AvatarPhysicsCollider>(); collider.radius=.5f; collider.bonesAsSpheres=false;
            var chain=anchor.gameObject.AddComponent<AvatarPhysicsChain>(); chain.pull=.12f; chain.spring=.85f; chain.gravity=.25f; chain.radius=.025f; chain.allowPosing=true; chain.parameter="DemoPhysics"; chain.colliders=new[]{collider};
            AssetDatabase.SaveAssets(); return root;
        }
        public static GameObject CreateStageExample()
        {
            var root = new GameObject("Studio de demonstração");
            Cube(root.transform, "Stage floor", new Vector3(0, -.15f, 0), new Vector3(16, .3f, 16), new Color(.18f, .22f, .28f)).AddComponent<BoxCollider>();
            Cube(root.transform, "Stage wall", new Vector3(0, 2, -5), new Vector3(12, 4, .3f), new Color(.15f, .3f, .38f)).AddComponent<BoxCollider>();
            Cube(root.transform, "Stage pedestal", new Vector3(3, .3f, 1), new Vector3(2, .6f, 2), new Color(.5f, .3f, .18f)).AddComponent<BoxCollider>();
            var lamp = new GameObject("Studio key light"); lamp.transform.SetParent(root.transform); lamp.transform.rotation = Quaternion.Euler(45, -30, 0);
            var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.Soft;
            AssetDatabase.SaveAssets(); return root;
        }
        public static GameObject CreateSample()
        {
            var root = new GameObject("Robot de demonstração");
            var animator = root.AddComponent<Animator>();
            Cube(root.transform, "Torso", new Vector3(0, 1, 0), new Vector3(.5f, .6f, .3f), new Color(.1f, .65f, .7f));
            Cube(root.transform, "Head", new Vector3(0, 1.58f, 0), new Vector3(.43f, .4f, .35f), new Color(.72f, .8f, .87f));
            Cube(root.transform, "Eye L", new Vector3(-.1f, 1.63f, .19f), new Vector3(.06f, .07f, .03f), Color.black);
            Cube(root.transform, "Eye R", new Vector3(.1f, 1.63f, .19f), new Vector3(.06f, .07f, .03f), Color.black);
            Cube(root.transform, "Leg L", new Vector3(-.15f, .36f, 0), new Vector3(.18f, .7f, .22f), new Color(.24f, .32f, .4f));
            Cube(root.transform, "Leg R", new Vector3(.15f, .36f, 0), new Vector3(.18f, .7f, .22f), new Color(.24f, .32f, .4f));
            Cube(root.transform, "Arm L", new Vector3(-.4f, 1.05f, 0), new Vector3(.2f, .58f, .22f), new Color(.24f, .32f, .4f));
            Cube(root.transform, "Arm R", new Vector3(.4f, 1.05f, 0), new Vector3(.2f, .58f, .22f), new Color(.24f, .32f, .4f));
            Cube(root.transform, "Hat", new Vector3(0, 1.85f, 0), new Vector3(.6f, .12f, .5f), new Color(1, .35f, .1f));
            var jaw = Cube(root.transform, "Jaw", new Vector3(0, 1.48f, .19f), new Vector3(.18f, .025f, .035f), Color.black);
            var mesh = Object.Instantiate(jaw.GetComponent<MeshFilter>().sharedMesh); mesh.name = "DemoJaw";
            var delta = new Vector3[mesh.vertexCount]; var verts = mesh.vertices;
            for (int i = 0; i < delta.Length; i++) delta[i] = verts[i].y < 0 ? new Vector3(0, -3, 0) : Vector3.zero;
            mesh.AddBlendShapeFrame("JawOpen", 100, delta, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
            var weights = new BoneWeight[mesh.vertexCount]; for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            mesh.boneWeights = weights; mesh.bindposes = new[] { root.transform.worldToLocalMatrix * jaw.transform.localToWorldMatrix };
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Generated + "/Jaw.asset"));
            var material = jaw.GetComponent<Renderer>().sharedMaterial;
            Object.DestroyImmediate(jaw.GetComponent<MeshRenderer>()); Object.DestroyImmediate(jaw.GetComponent<MeshFilter>());
            var renderer = jaw.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.bones = new[] { root.transform }; renderer.rootBone = root.transform; renderer.sharedMaterial = material; renderer.updateWhenOffscreen = true;
            var controller = AnimatorController.CreateAnimatorControllerAtPath(AssetDatabase.GenerateUniqueAssetPath(Generated + "/Robot.controller"));
            controller.AddParameter("HatVisible", AnimatorControllerParameterType.Bool); controller.AddParameter("JawOpen", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            var off = sm.AddState("Hat off"); var on = sm.AddState("Hat on"); sm.defaultState = off;
            off.motion = HatClip(false); on.motion = HatClip(true);
            var enter = off.AddTransition(on); enter.hasExitTime = false; enter.duration = 0; enter.AddCondition(AnimatorConditionMode.If, 0, "HatVisible");
            var exit = on.AddTransition(off); exit.hasExitTime = false; exit.duration = 0; exit.AddCondition(AnimatorConditionMode.IfNot, 0, "HatVisible");
            var definition = root.AddComponent<AvatarDefinition>(); definition.animator = animator; definition.fxController = controller;
            definition.controls.Add(new ExpressionControl { label = "Chapéu", parameter = "HatVisible", type = 1, value = 1 });
            definition.controls.Add(new ExpressionControl { label = "Abrir boca", parameter = "JawOpen", type = 5, axes = new[] { "JawOpen" } });
            definition.faceBindings.Add(new FaceBinding { parameter = "JawOpen", rendererPath = "Jaw", blendShape = "JawOpen" });
            AssetDatabase.SaveAssets(); return root;
        }
        static AnimationClip HatClip(bool visible)
        {
            var clip = new AnimationClip { name = visible ? "Hat on" : "Hat off" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, visible ? 1 : 0));
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(Generated + "/" + clip.name + ".anim")); return clip;
        }
        static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent); go.transform.localPosition = position; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Standard")) { color = color }; mat.SetFloat("_Glossiness", .3f);
            AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(Generated + "/" + name + ".mat")); go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
