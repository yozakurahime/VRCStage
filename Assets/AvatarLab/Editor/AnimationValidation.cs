using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object=UnityEngine.Object;
namespace AvatarLab.Editor
{
    public static class AnimationValidation
    {
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        static void Evaluate(AvatarDriver driver) { var graph=(PlayableGraph)typeof(AvatarDriver).GetField("graph",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(driver); graph.Evaluate(.02f); }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory("TestOutput"); LabBuild.Prepare();
                var root=new GameObject("Animation fixture"); var target=new GameObject("Target"); target.transform.SetParent(root.transform,false);
                var def=root.AddComponent<AvatarDefinition>(); def.animator=root.AddComponent<Animator>();
                var descriptor=root.AddComponent<Fixture.VRCAvatarDescriptor>();
                var layers=new Fixture.VRCAvatarDescriptor.Layer[8];
                for(int i=0;i<8;i++)
                {
                    string name=AvatarDriver.LayerNames[i];
                    string path=AssetDatabase.GenerateUniqueAssetPath(LabBuild.Generated+"/Test"+name+".controller");
                    var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
                    controller.AddParameter("TestParameter",AnimatorControllerParameterType.Float);
                    var clip=new AnimationClip(); AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Target",typeof(Transform),"m_LocalPosition.x"),AnimationCurve.Constant(0,1,i+1));
                    AssetDatabase.CreateAsset(clip,AssetDatabase.GenerateUniqueAssetPath(LabBuild.Generated+"/Test"+name+".anim"));
                    var state=controller.layers[0].stateMachine.AddState("TestState"); controller.layers[0].stateMachine.defaultState=state; state.motion=clip;
                    if(i==3) { var command=state.AddStateMachineBehaviour<Fixture.VRCPlayableLayerControl>(); command.goalWeight=.8f; command.blendDuration=0; }
                    layers[i]=new Fixture.VRCAvatarDescriptor.Layer {type=(Fixture.VRCAvatarDescriptor.LayerType)i,animatorController=controller};
                }
                descriptor.baseAnimationLayers=layers.Take(5).ToArray(); descriptor.specialAnimationLayers=layers.Skip(5).ToArray(); AssetDatabase.SaveAssets();
                string output=Path.GetFullPath("TestOutput/AnimationFixture.alavatar"); AvatarExporter.Export(root,output);
                Check(root.GetComponent<Fixture.VRCAvatarDescriptor>()==descriptor,"Source descriptor changed");
                var manifest=AvatarArchive.Read(output,out var bytes); var bundle=AssetBundle.LoadFromMemory(bytes); var prefab=bundle.LoadAsset<GameObject>(manifest.prefab);
                Check(prefab.GetComponent<AvatarDefinition>().animationLayers.Count==8,"Eight descriptor layers were not exported");
                Check(prefab.GetComponent<Fixture.VRCAvatarDescriptor>()==null,"SDK fixture leaked into export");
                var avatar=Object.Instantiate(prefab); var definition=avatar.GetComponent<AvatarDefinition>(); var point=avatar.transform.Find("Target");
                using(var driver=new AvatarDriver(definition))
                {
                    Check(AvatarDriver.LayerNames.All(driver.HasLayer),"Missing controller playable");
                    Check(driver.Weight("Action")==0 && !driver.PoseOverride,"Initial Action/pose weights");
                    // Allow Action's state-enter behaviour to run, then explicitly isolate each test.
                    Evaluate(driver);
                    Check(Mathf.Abs(driver.Weight("Action")-.8f)<.001f,"Converted state-enter layer control did not execute");
                    Check(manifest.notes.Any(n=>n.Contains("Controle de peso convertido")),"Layer-control command not converted");
                    foreach(string name in AvatarDriver.LayerNames) driver.BlendLayer(name,0,0);
                    driver.BlendLayer("Base",1,0); Evaluate(driver); Check(Mathf.Abs(point.localPosition.x-1)<.01f,"Base clip did not execute");
                    driver.BlendLayer("Gesture",1,0); Evaluate(driver); Check(Mathf.Abs(point.localPosition.x-3)<.01f,"Gesture did not override Base");
                    driver.BlendLayer("FX",1,0); Evaluate(driver); Check(Mathf.Abs(point.localPosition.x-5)<.01f,"FX did not override Gesture");
                    driver.PreviewPose("Sitting"); Evaluate(driver); Check(driver.PoseOverride && Mathf.Abs(point.localPosition.x-6)<.01f,"Sitting preview failed");
                    driver.PreviewPose("TPose"); Evaluate(driver); Check(driver.Weight("Sitting")==0 && Mathf.Abs(point.localPosition.x-7)<.01f,"T-Pose selection failed");
                    driver.PreviewPose(""); Check(!driver.PoseOverride && driver.Values["Seated"]==0,"Standing did not release pose");
                    driver.Set("TestParameter",.75f);
                    var mixer=(AnimationLayerMixerPlayable)typeof(AvatarDriver).GetField("mixer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(driver);
                    Check(mixer.IsLayerAdditive(1),"Additive mixer flag missing");
                    for(int i=0;i<8;i++) Check(Mathf.Abs(((AnimatorControllerPlayable)mixer.GetInput(i)).GetFloat("TestParameter")-.75f)<.001f,"Shared parameter missing from controller "+i);
                    var command=definition.animationLayers.Find(l=>l.type=="Action").controller as AnimatorController;
                    // Bundle controllers are RuntimeAnimatorController; use runtime host command directly too.
                    driver.BlendLayer("Action",.6f,0); Check(Mathf.Abs(driver.Weight("Action")-.6f)<.001f,"Layer host command failed");
                }
                Check(!AvatarLayerControl.Hosts.ContainsKey(definition.animator),"Disposed driver remained registered");
                Object.DestroyImmediate(avatar); bundle.Unload(true); Object.DestroyImmediate(root);
                File.WriteAllText("TestOutput/animation-editor-result.txt","PASS\nEight descriptor slots exported/imported; original descriptor preserved; no SDK fixture in prefab; Base/Gesture/FX priority; Sitting/T-Pose/Standing selection; Additive blending flag; shared parameters in eight playables; converted layer-control command; host cleanup.\n");
                Debug.Log("AVATARLAB_ANIMATION_PASS"); EditorApplication.Exit(0);
            }
            catch(Exception ex) { File.WriteAllText("TestOutput/animation-editor-result.txt","FAIL\n"+ex); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
