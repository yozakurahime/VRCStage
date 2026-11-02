using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
namespace AvatarLab.Editor
{
    public static class StudioMotionBuilder
    {
        const string Folder=LabBuild.Generated+"/Locomotion";
        public static LabMotionLibrary Build()
        {
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(LabBuild.Generated,"Locomotion");
            var library=AssetDatabase.LoadAssetAtPath<LabMotionLibrary>(Folder+"/Library.asset");
            if(library==null) { library=ScriptableObject.CreateInstance<LabMotionLibrary>(); AssetDatabase.CreateAsset(library,Folder+"/Library.asset"); }
            library.entries.Clear();
            var names=new[]{"stand_still","stand_still2","stand_still3","idle","idle2","idle3","walk_forward","walk_backward","strafe_right","strafe_right_45","strafe_right_135","run_forward","run_backward","run_strafe_right","run_strafe_right_45","run_strafe_right_135","sprint_forward","crouch_still","crouch_walk_forward","crouch_walk_right","crouch_walk_right_45","crouch_walk_right_135","fall_short","fall_long","landing","land_quick","low_crawl_idle","low_crawl_still","low_crawl_forward","low_crawl_right"};
            foreach(var name in names) library.entries.Add(new LabMotionLibrary.Entry{proxy="proxy_"+name,clip=Clip(name)});
            PoseLibraryBuilder.Build(library);
            foreach(var preset in library.presets)
            {
                if(preset.id=="walk") preset.clip=Get(library,"walk_forward");
                if(preset.id=="run") preset.clip=Get(library,"run_forward");
                if(preset.id=="land") preset.clip=Get(library,"landing");
                if(preset.id=="crouch") library.entries.First(e=>e.proxy=="proxy_crouch_still").clip=preset.clip;
                if(preset.id=="crouchwalk") library.entries.First(e=>e.proxy=="proxy_crouch_walk_forward").clip=preset.clip;
            }
            string path=Folder+"/Default.controller"; var existing=AssetDatabase.LoadAssetAtPath<AnimatorController>(path); if(existing!=null) AssetDatabase.DeleteAsset(path);
            var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("VelocityMagnitude",AnimatorControllerParameterType.Float); controller.AddParameter("VelocityY",AnimatorControllerParameterType.Float); controller.AddParameter("Upright",AnimatorControllerParameterType.Float); controller.AddParameter("Grounded",AnimatorControllerParameterType.Bool);
            controller.AddParameter("VRCStageGait",AnimatorControllerParameterType.Float);controller.AddParameter("VRCStageRate",AnimatorControllerParameterType.Float);
            controller.parameters=controller.parameters.Select(p=>{ if(p.name=="Grounded") p.defaultBool=true; if(p.name=="Upright" || p.name=="VRCStageRate") p.defaultFloat=1; return p; }).ToArray();
            var machine=controller.layers[0].stateMachine;
            var standing=machine.AddState("Standing / walk / run"); standing.motion=Tree(controller,"Standing","VRCStageGait",new[]{Get(library,"stand_still"),Get(library,"walk_forward"),Get(library,"run_forward")},new[]{0f,1.8f,3.6f});
            var crouch=machine.AddState("Crouch / crouch walk"); crouch.motion=Tree(controller,"Crouch","VRCStageGait",new[]{Get(library,"crouch_still"),Get(library,"crouch_walk_forward")},new[]{0f,.9f});
            foreach(var state in new[]{standing,crouch}) {state.speedParameter="VRCStageRate";state.speedParameterActive=true;}
            // Authored muscle-only crouch clips have no foot IK goals; enabling
            // Unity's clip IK on them pulls feet toward a missing/default goal.
            standing.iKOnFeet=true;
            var jump=machine.AddState("Jump ascent"); jump.motion=Tree(controller,"Jump momentum","VRCStageGait",new[]{Get(library,"fall_short"),Official("RunForwardsJump_Frame02")},new[]{0f,3.6f});
            var falling=machine.AddState("Falling");falling.motion=Get(library,"fall_long");
            var land=machine.AddState("Landing"); land.motion=Get(library,"landing");land.speed=1.6f;land.iKOnFeet=true;
            Transition(standing,crouch,AnimatorConditionMode.Less,.7f,"Upright"); Transition(crouch,standing,AnimatorConditionMode.Greater,.7f,"Upright");
            foreach(var state in new[]{standing,crouch,land})
            {
                Transition(state,jump,AnimatorConditionMode.IfNot,0,"Grounded").AddCondition(AnimatorConditionMode.Greater,0,"VelocityY");
                Transition(state,falling,AnimatorConditionMode.IfNot,0,"Grounded").AddCondition(AnimatorConditionMode.Less,.01f,"VelocityY");
            }
            Transition(jump,falling,AnimatorConditionMode.Less,0,"VelocityY");
            Transition(falling,jump,AnimatorConditionMode.Greater,.1f,"VelocityY").AddCondition(AnimatorConditionMode.IfNot,0,"Grounded");
            Transition(jump,land,AnimatorConditionMode.If,0,"Grounded");Transition(falling,land,AnimatorConditionMode.If,0,"Grounded");
            Transition(land,crouch,AnimatorConditionMode.Less,.7f,"Upright");
            Transition(land,standing,AnimatorConditionMode.Greater,.15f,"VRCStageGait").AddCondition(AnimatorConditionMode.Greater,.7f,"Upright");
            var finish=land.AddTransition(standing); finish.hasExitTime=true; finish.exitTime=.35f; finish.hasFixedDuration=true;finish.duration=.1f;finish.AddCondition(AnimatorConditionMode.Greater,.7f,"Upright");
            machine.defaultState=standing; library.defaultController=controller; EditorUtility.SetDirty(library); AssetDatabase.SaveAssets(); return library;
        }
        static AnimationClip Get(LabMotionLibrary l,string name)=>l.entries.First(e=>e.proxy=="proxy_"+name).clip;
        static AnimatorStateTransition Transition(AnimatorState a,AnimatorState b,AnimatorConditionMode mode,float threshold,string parameter) { var t=a.AddTransition(b); t.hasExitTime=false; t.hasFixedDuration=true;t.duration=.1f; t.AddCondition(mode,threshold,parameter); return t; }
        static BlendTree Tree(AnimatorController owner,string name,string parameter,AnimationClip[] clips,float[] thresholds)
        {
            var tree=new BlendTree{name=name,blendType=BlendTreeType.Simple1D,blendParameter=parameter,useAutomaticThresholds=false}; AssetDatabase.AddObjectToAsset(tree,owner);
            for(int i=0;i<clips.Length;i++) tree.AddChild(clips[i],thresholds[i]); return tree;
        }
        static AnimationClip Clip(string name)
        {
            string file=name=="stand_still" || name.StartsWith("idle") || name.StartsWith("stand_still")?"Idle":name=="walk_forward"?"WalkForwards":name=="run_forward"?"RunForwards":name=="sprint_forward"?"SprintForwards":name=="fall_long"?"Falling":name=="fall_short"?"StandingJump_Frame02":name=="landing" || name=="land_quick"?"FallLand":name=="walk_backward"?"StrafeRightWalkBackwards":name.StartsWith("strafe_right")?"StrafeRight":null;
            if(file!=null)
                return Official(file);
            string path=Folder+"/"+name+".anim"; var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null) { clip=new AnimationClip(); AssetDatabase.CreateAsset(clip,path); }
            clip.ClearCurves(); clip.name="AvatarLab "+name; clip.frameRate=60;
            bool walk=name.Contains("walk") || name.Contains("strafe") || name.Contains("run") || name.Contains("sprint") || name.Contains("crawl_forward") || name.Contains("crawl_right");
            bool run=name.Contains("run") || name.Contains("sprint"), crouch=name.Contains("crouch") || name.Contains("crawl"), air=name.Contains("fall"), landing=name=="landing" || name=="land_quick";
            float duration=landing?.28f:run?.55f:walk?.9f:1;
            var curves=new Dictionary<string,AnimationCurve>();
            Action<string,float,float> add=(property,time,value)=>{ if(!curves.TryGetValue(property,out var curve)) curves[property]=curve=new AnimationCurve(); curve.AddKey(time,value); };
            for(int sample=0;sample<=32;sample++)
            {
                float t=sample/32f, phase=t*Mathf.PI*2, gait=walk?Mathf.Sin(phase):0, bend=crouch?.55f:0;
                float land=landing?Mathf.Sin(t*Mathf.PI)*.3f:0;
                add("RootT.x",t*duration,0); add("RootT.y",t*duration,1-(crouch?.28f:0)-land*.2f+(walk?Mathf.Cos(phase*2)*.012f:0)); add("RootT.z",t*duration,0);
                add("RootQ.x",t*duration,0); add("RootQ.y",t*duration,0); add("RootQ.z",t*duration,0); add("RootQ.w",t*duration,1);
                add("Spine Front-Back",t*duration,crouch?.18f:run?.1f:0);
                foreach(string side in new[]{"Left","Right"})
                {
                    float swing=(side=="Left"?1:-1)*gait*(run?.55f:.35f); if(name.Contains("backward")) swing=-swing;
                    add(side+" Upper Leg Front-Back",t*duration,bend+swing+land+(air?.2f:0));
                    add(side+" Lower Leg Stretch",t*duration,1-(crouch?.85f:0)-Mathf.Max(0,-swing)*1.5f-land-(air?.4f:0));
                    add(side+" Foot Up-Down",t*duration,-swing*.25f);
                    add(side+" Arm Down-Up",t*duration,-.75f+(air?.2f:0)); add(side+" Arm Front-Back",t*duration,-swing*.55f);
                    add(side+" Forearm Stretch",t*duration,run?.2f:.85f);
                }
            }
            foreach(var pair in curves)
            {
                for(int k=0;k<pair.Value.length;k++) AnimationUtility.SetKeyBroken(pair.Value,k,false);
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),pair.Key),pair.Value);
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=!landing; settings.loopBlend=false; settings.keepOriginalPositionY=true; settings.keepOriginalOrientation=true; AnimationUtility.SetAnimationClipSettings(clip,settings); EditorUtility.SetDirty(clip); return clip;
        }
        static AnimationClip Official(string file)
        {
            var clips=AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/UnityCharacterMotion/f@"+file+".fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__") && !c.name.EndsWith("_Mirror")).ToArray();
            if(clips.Length!=1 || !clips[0].isHumanMotion || clips[0].length<=0) throw new InvalidOperationException("Animação oficial Unity ausente/inválida: "+file);
            return clips[0];
        }
    }
}
