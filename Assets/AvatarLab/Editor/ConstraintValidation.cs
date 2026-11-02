using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using Object=UnityEngine.Object;
namespace AvatarLab.Editor
{
    public static class ConstraintValidation
    {
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        static Transform Child(Transform p,string name) { var t=new GameObject(name).transform; t.SetParent(p,false); return t; }
        static void Source(AvatarConstraint c,Transform t) { c.Sources.totalLength=1; c.Sources.source0=new AvatarConstraintSource {SourceTransform=t,Weight=1}; }
        static void Close(Vector3 a,Vector3 b,string message) { Check(Vector3.Distance(a,b)<.001f,message+": "+a+" != "+b); }
        public static void RunAndBuild()
        {
            Directory.CreateDirectory("TestOutput");
            try
            {
                var root=new GameObject("Constraint mathematics"); var source=Child(root.transform,"Source"); var target=Child(root.transform,"Target"); source.position=new Vector3(2,1,3); source.rotation=Quaternion.Euler(0,90,0);
                var parent=target.gameObject.AddComponent<AvatarParentConstraint>(); Source(parent,source);
                parent.Sources.source0.ParentPositionOffset=Vector3.right*.25f; parent.Sources.source0.ParentRotationOffset=new Vector3(10,0,0); parent.Evaluate();
                Close(target.position,source.TransformPoint(Vector3.right*.25f),"Parent position offset"); Check(Quaternion.Angle(target.rotation,source.rotation*Quaternion.Euler(10,0,0))<.01f,"Parent rotation offset");
                parent.FreezeToWorld=true; parent.Evaluate(); var frozen=target.position; source.position+=Vector3.right; root.transform.position+=Vector3.forward; parent.Evaluate(); Close(target.position,frozen,"World freeze under moving source and root");
                parent.RebakeOffsetsWhenUnfrozen=true; parent.FreezeToWorld=false; parent.Evaluate(); Close(target.position,frozen,"Unfreeze rebake jumped"); parent.enabled=false;
                var rotation=target.gameObject.AddComponent<AvatarRotationConstraint>(); Source(rotation,source); source.rotation=Quaternion.Euler(0,90,0); rotation.GlobalWeight=.5f; rotation.Evaluate(); Check(Quaternion.Angle(target.localRotation,Quaternion.Euler(0,45,0))<.01f,"Rotation global weight"); rotation.enabled=false;
                var position=target.gameObject.AddComponent<AvatarPositionConstraint>(); Source(position,source); position.PositionAtRest=new Vector3(.1f,.2f,.3f); position.GlobalWeight=0; position.Evaluate(); Close(target.localPosition,position.PositionAtRest,"At rest at zero weight");
                position.GlobalWeight=1; position.SolveInLocalSpace=true; source.localPosition=new Vector3(.4f,.5f,.6f); position.PositionOffset=Vector3.one*.1f; position.Evaluate(); Close(target.localPosition,source.localPosition+Vector3.one*.1f,"Local space position");
                position.AffectsPositionY=false; var oldY=target.localPosition.y; source.localPosition+=Vector3.up; position.Evaluate(); Check(Mathf.Abs(target.localPosition.y-oldY)<.001f,"Position axis mask"); position.enabled=false;
                var scale=target.gameObject.AddComponent<AvatarScaleConstraint>(); Source(scale,source); source.localScale=new Vector3(2,3,4); scale.ScaleOffset=new Vector3(.5f,2,1); scale.Evaluate(); Close(target.lossyScale,new Vector3(1,6,4),"Multiplicative scale offset"); scale.enabled=false;
                var aim=target.gameObject.AddComponent<AvatarAimConstraint>(); Source(aim,source); source.position=target.position+Vector3.right; aim.Evaluate(); Check(Vector3.Angle(target.forward,Vector3.right)<.01f,"Aim alignment"); aim.enabled=false;
                var look=target.gameObject.AddComponent<AvatarLookAtConstraint>(); Source(look,source); source.position=target.position+Vector3.back; look.Roll=30; look.Evaluate(); Check(Vector3.Angle(target.forward,Vector3.back)<.01f,"LookAt alignment"); look.enabled=false;
                var b=Child(root.transform,"B").gameObject.AddComponent<AvatarPositionConstraint>(); var a=Child(root.transform,"A").gameObject.AddComponent<AvatarPositionConstraint>(); Source(a,source); Source(b,a.transform);
                var runner=root.AddComponent<AvatarConstraintRunner>(); runner.Rebuild(); source.position=new Vector3(5,2,1); runner.Evaluate(); Close(b.transform.position,source.position,"Dependency order"); Source(a,b.transform); runner.Rebuild(); Check(runner.CyclicDependencies>0,"Cycle not diagnosed"); runner.Evaluate(); Check(!float.IsNaN(a.transform.position.x),"Cycle produced nonfinite output"); Object.DestroyImmediate(root);

                LabBuild.Prepare(); var fixture=new GameObject("Constraint export fixture"); var def=fixture.AddComponent<AvatarDefinition>(); def.animator=fixture.AddComponent<Animator>();
                var sourceTransform=Child(fixture.transform,"Source"); sourceTransform.localPosition=Vector3.up;
                var raw=Child(fixture.transform,"Constrained").gameObject.AddComponent<Fixture.VRCParentConstraint>(); raw.Sources.totalLength=1; raw.Sources.source0=new AvatarConstraintSource {SourceTransform=sourceTransform,Weight=1,ParentPositionOffset=Vector3.right*.1f};
                string controllerPath=AssetDatabase.GenerateUniqueAssetPath(LabBuild.Generated+"/ConstraintValidation.controller"); var controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath); def.fxController=controller;
                foreach(string name in new[]{"Set","Add","Copy","RandomInt"}) controller.AddParameter(name,name=="RandomInt"?AnimatorControllerParameterType.Int:AnimatorControllerParameterType.Float);
                var clip=new AnimationClip(); AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Constrained",typeof(Fixture.VRCParentConstraint),"FreezeToWorld"),AnimationCurve.Constant(0,1,1));
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Constrained",typeof(Fixture.VRCParentConstraint),"Sources.source0.Weight"),AnimationCurve.Constant(0,1,.25f));
                string clipPath=AssetDatabase.GenerateUniqueAssetPath(LabBuild.Generated+"/ConstraintAnimated.anim"); AssetDatabase.CreateAsset(clip,clipPath);
                var state=controller.layers[0].stateMachine.AddState("Driver"); controller.layers[0].stateMachine.defaultState=state; state.motion=clip;
                var driver=state.AddStateMachineBehaviour<Fixture.VRCAvatarParameterDriver>(); driver.localOnly=true;
                driver.parameters=new[]{new AvatarParameterOperation {name="Set",value=.25f},new AvatarParameterOperation {name="Add",type=1,value=.5f},new AvatarParameterOperation {name="Copy",type=3,source="Set",convertRange=true,sourceMin=0,sourceMax=1,destMin=0,destMax=2},new AvatarParameterOperation {name="RandomInt",type=2,valueMin=2,valueMax=2}};
                state.AddStateMachineBehaviour<Fixture.VRCAnimatorTrackingControl>(); AssetDatabase.SaveAssets(); string original=AvatarArchive.Hash(File.ReadAllBytes(controllerPath)); string clipHash=AvatarArchive.Hash(File.ReadAllBytes(clipPath));
                var manifest=AvatarExporter.Export(fixture,Path.GetFullPath("TestOutput/ConstraintFixture.alavatar"));
                Check(fixture.GetComponentInChildren<Fixture.VRCParentConstraint>()==raw && raw.Sources.source0.SourceTransform==sourceTransform,"Source component mutated"); Check(original==AvatarArchive.Hash(File.ReadAllBytes(controllerPath)) && clipHash==AvatarArchive.Hash(File.ReadAllBytes(clipPath)),"Source controller/clip mutated");
                AvatarArchive.Read("TestOutput/ConstraintFixture.alavatar",out var bytes); var bundle=AssetBundle.LoadFromMemory(bytes); var prefab=bundle.LoadAsset<GameObject>(manifest.prefab); var instance=Object.Instantiate(prefab); var imported=instance.GetComponentInChildren<AvatarParentConstraint>();
                Check(imported!=null && imported.Sources.source0.SourceTransform==instance.transform.Find("Source"),"Constraint source reference not preserved");
                var importedClip=instance.GetComponent<AvatarDefinition>().fxController.animationClips.First(c=>c.name.StartsWith("ConstraintAnimated")); importedClip.SampleAnimation(instance,.5f); Check(imported.FreezeToWorld && Mathf.Abs(imported.Sources.source0.Weight-.25f)<.001f,"Animated constraint fields were not remapped: freeze="+imported.FreezeToWorld+", weight="+imported.Sources.source0.Weight+", bindings="+string.Join(";",AnimationUtility.GetCurveBindings(importedClip).Select(b=>b.type.Name+":"+b.propertyName)));
                using(var playback=new AvatarDriver(instance.GetComponent<AvatarDefinition>()))
                {
                    var graph=(PlayableGraph)typeof(AvatarDriver).GetField("graph",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(playback); graph.Evaluate(.02f);
                    Check(Mathf.Abs(playback.GetValue("Set")-.25f)<.001f && Mathf.Abs(playback.GetValue("Add")-.5f)<.001f && Mathf.Abs(playback.GetValue("Copy")-.5f)<.001f && playback.GetValue("RandomInt")==2,"Converted parameter driver operations failed");
                    Check(!playback.AllowsTracking("LeftHand") && !playback.AllowsTracking("LeftFingers") && playback.AllowsTracking("RightHand"),"Converted tracking control failed");
                }
                Check(instance.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c==null || !c.GetType().Name.StartsWith("VRC")),"SDK fixture component leaked"); Object.DestroyImmediate(instance); bundle.Unload(true); Object.DestroyImmediate(fixture);
                File.WriteAllText("TestOutput/constraint-editor-result.txt","PASS\nParent offsets; world freeze/unfreeze rebake; rotation/global weight; at-rest/local position and axis masks; multiplicative scale; Aim/LookAt; dependency ordering/cycle diagnostics; export/import source references and animated freeze/source weight; source component, controller and clip preserved; converted Set/Add/Random/Copy and tracking controls executed through real Playables; no SDK fixture in prefab.\n");
                Debug.Log("AVATARLAB_CONSTRAINTS_PASS"); LabBuild.Build();
            }
            catch(Exception ex) { File.WriteAllText("TestOutput/constraint-editor-result.txt","FAIL\n"+ex); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
