using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AvatarLab
{
    public sealed class AvatarDriver : IDisposable, IAvatarAnimationHost
    {
        public readonly AvatarDefinition Definition;
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        public readonly Dictionary<string, AnimatorControllerParameterType> Types = new Dictionary<string, AnimatorControllerParameterType>();
        sealed class ControllerBinding
        {
            public string type;
            public int slot;
            public RuntimeAnimatorController asset;
            public AnimatorControllerPlayable playable;
            public readonly Dictionary<string, AnimatorControllerParameter> parameters = new Dictionary<string, AnimatorControllerParameter>();
        }
        readonly List<ControllerBinding> controllers = new List<ControllerBinding>();
        readonly Dictionary<string,ControllerBinding> byLayer=new Dictionary<string,ControllerBinding>();
        readonly Dictionary<string,int> trackingModes=new Dictionary<string,int>();
        public void SetTracking(string part,int mode) { if(mode!=0) trackingModes[part]=mode; }
        public bool AllowsTracking(string part)=>!trackingModes.TryGetValue(part,out var mode) || mode!=2;
        public AnimatorControllerParameterType ParameterType(string name)=>Types.TryGetValue(name??"",out var type)?type:AnimatorControllerParameterType.Float;
        public float GetValue(string name)
        {
            foreach(var c in controllers) if(c.parameters.TryGetValue(name??"",out var p))
                return p.type==AnimatorControllerParameterType.Bool?(c.playable.GetBool(p.nameHash)?1:0):p.type==AnimatorControllerParameterType.Int?c.playable.GetInteger(p.nameHash):p.type==AnimatorControllerParameterType.Float?c.playable.GetFloat(p.nameHash):Values.TryGetValue(name??"",out var trigger)?trigger:0;
            return Values.TryGetValue(name??"",out var value)?value:0;
        }
        public void SetValue(string name,float value)
        {
            var type=ParameterType(name);
            Set(name,type==AnimatorControllerParameterType.Bool?(value>.5f?1:0):type==AnimatorControllerParameterType.Int?Mathf.Round(value):value);
        }
        PlayableGraph graph;
        AnimationLayerMixerPlayable mixer;
        public bool ManualEvaluation {get;private set;}
        public bool AdvanceAnimation=true;
        public void UseManualEvaluation() { graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); ManualEvaluation=true; }
        public void EvaluateManual(float dt)
        {
            if(!ManualEvaluation || !AdvanceAnimation) return;
            using var timing=LabPerformance.Measure("AnimationGraph"); graph.Evaluate(dt);
        }
        readonly List<AnimatorOverrideController> ownedControllers=new List<AnimatorOverrideController>();
        public int ResolvedMotionProxies {get;private set;}
        readonly List<AvatarMask> generatedMasks=new List<AvatarMask>();
        readonly List<Blend> blends=new List<Blend>();
        sealed class Blend { public string type; public int layer; public float from,to,start,duration; }
        public static readonly string[] LayerNames={"Base","Additive","Gesture","Action","FX","Sitting","TPose","IKPose"};
        public bool ExternalBodyOverride;
        public bool PoseOverride => ExternalBodyOverride || LibraryActive && LibraryLocksTracking || Weight("Sitting")>.001f || Weight("TPose")>.001f || Weight("IKPose")>.001f;
        public bool BodyAnimation => LibraryActive || HasBaseController || Weight("Gesture")>.001f || Weight("Action")>.001f || PoseOverride;
        Transform neutralHips;
        Vector3 neutralHipPosition;
        readonly List<KeyValuePair<Transform, Quaternion>> neutralRotations = new List<KeyValuePair<Transform, Quaternion>>();
        public bool HasBaseController { get; private set; }
        RuntimeAnimatorController gameController;
        ControllerBinding originalBase;
        bool gameLocomotion;
        readonly Dictionary<string,float> gameLayerWeights=new Dictionary<string,float>();
        public void SetGameLocomotion(bool active)
        {
            if(gameLocomotion==active || gameController==null || !graph.IsValid()) return;
            gameLocomotion=active;var previous=Find("Base");
            if(previous!=null) {graph.Disconnect(mixer,0);controllers.Remove(previous);byLayer.Remove("Base");}
            if(active)
            {
                originalBase=previous;AddController(gameController,mixer,0,"Base");
                foreach(var layer in new[]{"Additive","Gesture","Action","Sitting","TPose","IKPose"}) {gameLayerWeights[layer]=Weight(layer);if(Find(layer)!=null) mixer.SetInputWeight(Find(layer).slot,0);}
            }
            else
            {
                if(previous!=null) graph.DestroySubgraph(previous.playable);
                if(originalBase!=null) {controllers.Add(originalBase);byLayer["Base"]=originalBase;graph.Connect(originalBase.playable,0,mixer,0);mixer.SetInputWeight(0,1);}
                foreach(var pair in gameLayerWeights) if(Find(pair.Key)!=null) mixer.SetInputWeight(Find(pair.Key).slot,pair.Value);
                gameLayerWeights.Clear();originalBase=null;
            }
            foreach(var pair in Values.ToArray()) Set(pair.Key,pair.Value);
        }
        public AvatarDriver(AvatarDefinition definition,LabMotionLibrary motionLibrary=null)
        {
            Definition = definition;
            gameController=motionLibrary?.defaultController;
            var animator = definition.animator;
            if (animator == null) return;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            var baseController = definition.baseController != null ? definition.baseController : animator.runtimeAnimatorController;
            if (baseController == definition.fxController) baseController = null;
            if(motionLibrary!=null && animator.isHuman) { baseController=motionLibrary.Resolve(baseController,ownedControllers,out int count); ResolvedMotionProxies+=count; }
            HasBaseController = baseController != null;
            if (baseController == null && animator.isHuman)
            {
                neutralHips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (neutralHips != null) neutralHipPosition = neutralHips.localPosition;
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var which = (HumanBodyBones)i;
                    if (which == HumanBodyBones.Jaw || which == HumanBodyBones.LeftEye || which == HumanBodyBones.RightEye) continue;
                    var bone = animator.GetBoneTransform(which);
                    if (bone != null) neutralRotations.Add(new KeyValuePair<Transform, Quaternion>(bone, bone.localRotation));
                }
            }
            animator.runtimeAnimatorController = null;
            graph = PlayableGraph.Create("AvatarLab Avatar");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationLayerMixerPlayable.Create(graph, LayerNames.Length);
            var output = AnimationPlayableOutput.Create(graph, "Avatar", animator); output.SetSourcePlayable(mixer);
            AvatarLayerControl.Hosts[animator]=this;
            for(int i=0;i<LayerNames.Length;i++)
            {
                string type=LayerNames[i]; var layer=definition.animationLayers.Find(l=>l.type==type);
                var asset=type=="Base"?baseController:type=="FX"?definition.fxController:layer?.controller;
                if(asset==null) continue;
                if(type!="Base" && type!="FX" && motionLibrary!=null && animator.isHuman) { asset=motionLibrary.Resolve(asset,ownedControllers,out int count); ResolvedMotionProxies+=count; }
                AddController(asset,mixer,i,type);
                mixer.SetInputWeight(i,type=="Action" || i>=5?0:1);
                if(type=="Additive") mixer.SetLayerAdditive((uint)i,true);
                var mask=layer?.mask;
                if(mask==null && type=="FX" && animator.isHuman)
                {
                    mask=new AvatarMask(); generatedMasks.Add(mask);
                    mask.AddTransformPath(definition.transform,true);
                    for(int part=0;part<(int)AvatarMaskBodyPart.LastBodyPart;part++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part,false);
                }
                if(mask!=null) mixer.SetLayerMaskFromAvatarMask((uint)i,mask);
            }
            graph.Play();
            foreach (var p in definition.defaults) Set(p.name, p.value);
            Set("Upright", 1); Set("Grounded", 1); Set("Seated", 0); Set("AFK", 0);
        }
        public bool LibraryActive {get;private set;}
        public bool LibraryLocksTracking=true;
        public IEnumerable<AnimationClip> AvatarMotionClips()
        {
            var seen=new HashSet<AnimationClip>();
            foreach(var c in controllers) if(c.type!="FX") foreach(var clip in c.asset.animationClips)
                if(clip!=null && clip.isHumanMotion && seen.Add(clip) && IsBodyLibraryClip(clip.name)) yield return clip;
        }
        static bool IsBodyLibraryClip(string name)
        {
            return !name.StartsWith("go_",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("proxy_",StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("AvatarLab ",StringComparison.OrdinalIgnoreCase) && name.IndexOf("reference",StringComparison.OrdinalIgnoreCase)<0
                && name.IndexOf("smooth",StringComparison.OrdinalIgnoreCase)<0 && name.IndexOf("default",StringComparison.OrdinalIgnoreCase)<0;
        }
        public void HoldLibraryPose() {LibraryActive=true;}
        public void StopLibraryClip() {LibraryActive=false;}
        public void ApplyNeutralRoot()
        {
            // FX-only controllers don't provide a base humanoid pose. Use the rig's rest pose before tracking IK.
            if(!BodyAnimation)
            {
                if (neutralHips != null) neutralHips.localPosition = neutralHipPosition;
                foreach (var bone in neutralRotations) if (bone.Key != null) bone.Key.localRotation = bone.Value;
            }
        }
        void AddController(RuntimeAnimatorController asset, AnimationLayerMixerPlayable mixer, int layer, string type)
        {
            var controller = AnimatorControllerPlayable.Create(graph, asset);
            graph.Connect(controller, 0, mixer, layer); mixer.SetInputWeight(layer, 1);
            var binding = new ControllerBinding { playable = controller, type=type,slot=layer,asset=asset }; controllers.Add(binding);
            byLayer[type]=binding;
            for (int i = 0; i < controller.GetParameterCount(); i++)
            {
                var p = controller.GetParameter(i); Types[p.name] = p.type;
                binding.parameters[p.name] = p;
                if (!Values.ContainsKey(p.name)) Values[p.name] = p.type == AnimatorControllerParameterType.Bool ? (p.defaultBool ? 1 : 0) : p.type == AnimatorControllerParameterType.Int ? p.defaultInt : p.defaultFloat;
            }
        }
        ControllerBinding Find(string type) => byLayer.TryGetValue(type,out var binding)?binding:null;
        public bool HasLayer(string type) => Find(type)!=null;
        public string[] ActiveClipNames(string type)
        {
            var c=Find(type); if(c==null) return Array.Empty<string>(); var result=new List<string>();
            for(int i=0;i<c.playable.GetLayerCount();i++) foreach(var clip in c.playable.GetCurrentAnimatorClipInfo(i)) if(clip.clip!=null && clip.weight>.01f) result.Add(clip.clip.name);
            return result.ToArray();
        }
        public string ControllerName(string type) { var layer=Definition.animationLayers.Find(l=>l.type==type); return (type=="Base"?Definition.baseController:type=="FX"?Definition.fxController:layer?.controller)?.name??(type=="Base" && HasBaseController?"AvatarLab locomoção padrão":"não importado"); }
        public float Weight(string type) { var c=Find(type); return c==null?0:mixer.GetInputWeight(c.slot); }
        public void BlendLayer(string type,float weight,float seconds,int animatorLayer=-1)
        {
            var c=Find(type); if(c==null) return;
            if(animatorLayer>=c.playable.GetLayerCount()) return;
            blends.RemoveAll(b=>b.type==type && b.layer==animatorLayer);
            blends.Add(new Blend {type=type,layer=animatorLayer,from=animatorLayer<0?Weight(type):c.playable.GetLayerWeight(animatorLayer),to=Mathf.Clamp01(weight),duration=Mathf.Max(0,seconds),start=Time.time});
            UpdateLayers();
        }
        public void UpdateLayers()
        {
            for(int i=blends.Count-1;i>=0;i--)
            {
                var b=blends[i]; var c=Find(b.type); float t=b.duration<=0?1:Mathf.Clamp01((Time.time-b.start)/b.duration), value=Mathf.Lerp(b.from,b.to,t);
                if(b.layer<0) mixer.SetInputWeight(c.slot,value); else c.playable.SetLayerWeight(b.layer,value);
                if(t>=1) blends.RemoveAt(i);
            }
        }
        public void PreviewPose(string type)
        {
            foreach(var pose in new[]{"Sitting","TPose","IKPose"}) BlendLayer(pose,pose==type?1:0,0);
            Set("Seated",type=="Sitting"?1:0);
        }
        public void DrawAnimationUI()
        {
            GUILayout.Label("Controladores e camadas de animação");
            GUILayout.Label("Standing / em pé usa Base (Seated = false). Sitting, T-Pose e IK Pose são prévias; não recalibram o IK VR.");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Standing / em pé")) PreviewPose("");
            if(GUILayout.Button("Sitting")) PreviewPose("Sitting");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("T-Pose")) PreviewPose("TPose");
            if(GUILayout.Button("IK Pose")) PreviewPose("IKPose");
            GUILayout.EndHorizontal();
            foreach(string type in LayerNames)
            {
                GUILayout.Label(type+" • "+ControllerName(type));
                if(!HasLayer(type)) continue;
                float old=Weight(type),value=GUILayout.HorizontalSlider(old,0,1);
                if(Mathf.Abs(value-old)>.001f) BlendLayer(type,value,0);
                var c=Find(type);
                for(int j=0;j<c.playable.GetLayerCount();j++)
                {
                    var state=c.playable.GetCurrentAnimatorStateInfo(j); var clips=c.playable.GetCurrentAnimatorClipInfo(j);
                    string clip=clips.Length>0 && clips[0].clip!=null?clips[0].clip.name:"sem clip";
                    GUILayout.Label("  "+c.playable.GetLayerName(j)+": "+clip+" • "+state.normalizedTime.ToString("F2"));
                }
            }
            GUILayout.Label("Estados, transições, Blend Trees e máscaras vêm do pacote. Comportamentos SDK sem equivalente são informados na compatibilidade.");
        }
        public void Set(string name, float value)
        {
            if (string.IsNullOrEmpty(name) || float.IsNaN(value) || float.IsInfinity(value)) return;
            Values[name] = value;
            foreach (var c in controllers)
            {
                if (c.parameters.TryGetValue(name, out var p))
                {
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Bool: c.playable.SetBool(p.nameHash, value > .5f); break;
                        case AnimatorControllerParameterType.Int: c.playable.SetInteger(p.nameHash, Mathf.RoundToInt(value)); break;
                        case AnimatorControllerParameterType.Float: c.playable.SetFloat(p.nameHash, value); break;
                        case AnimatorControllerParameterType.Trigger: if (value > .5f) c.playable.SetTrigger(p.nameHash); else c.playable.ResetTrigger(p.nameHash); break;
                    }
                }
            }
        }
        public void ApplyFaceBindings()
        {
            foreach (var binding in Definition.faceBindings)
            {
                if (!Values.TryGetValue(binding.parameter, out var value)) continue;
                var t = string.IsNullOrEmpty(binding.rendererPath) ? Definition.transform : Definition.transform.Find(binding.rendererPath);
                var r = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                int index = r != null && r.sharedMesh != null ? r.sharedMesh.GetBlendShapeIndex(binding.blendShape) : -1;
                if (index >= 0) r.SetBlendShapeWeight(index, Mathf.Clamp(value * binding.scale, 0, 100));
            }
        }
        public void Dispose() { LibraryActive=false; if(Definition.animator!=null) AvatarLayerControl.Hosts.Remove(Definition.animator); if (graph.IsValid()) graph.Destroy(); foreach(var mask in generatedMasks) UnityEngine.Object.Destroy(mask); foreach(var controller in ownedControllers) UnityEngine.Object.Destroy(controller); }
    }
}
