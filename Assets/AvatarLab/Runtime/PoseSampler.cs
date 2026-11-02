using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
namespace AvatarLab
{
    // Sample only a hidden Humanoid skeleton, then copy its body pose. Imported clips
    // cannot animate live avatar materials, objects, parameters or physics components.
    public sealed class PoseSampler : IDisposable
    {
        public readonly Transform Root;
        public readonly Animator Animator;
        readonly HumanPoseHandler handler;
        readonly HumanPose pose;
        readonly List<(Transform bone,Vector3 position,Quaternion rotation,Vector3 scale)> rest=new List<(Transform,Vector3,Quaternion,Vector3)>();
        readonly Dictionary<string,SkeletonBone> reference=new Dictionary<string,SkeletonBone>();
        PlayableGraph graph; AnimationClipPlayable playable;
        public PoseSampler(Animator source)
        {
            foreach(var bone in (source.avatar.humanDescription.skeleton??Array.Empty<SkeletonBone>())) if(!reference.ContainsKey(bone.name)) reference.Add(bone.name,bone);
            var needed=new HashSet<Transform>();
            for(int i=0;i<(int)HumanBodyBones.LastBone;i++)
            {
                var bone=source.GetBoneTransform((HumanBodyBones)i);
                while(bone!=null && bone!=source.transform) {needed.Add(bone);bone=bone.parent;}
            }
            Root=new GameObject("AvatarLab hidden pose skeleton"){hideFlags=HideFlags.HideAndDontSave}.transform;
            Copy(source.transform,Root,needed);
            Animator=Root.gameObject.AddComponent<Animator>();Animator.avatar=source.avatar;Animator.applyRootMotion=false;Animator.fireEvents=false;Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            handler=new HumanPoseHandler(source.avatar,Root);pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
        }
        void Copy(Transform source,Transform target,HashSet<Transform> needed)
        {
            foreach(Transform child in source) if(needed.Contains(child))
            {
                var copy=new GameObject(child.name){hideFlags=HideFlags.HideAndDontSave}.transform;copy.SetParent(target,false);copy.localPosition=child.localPosition;copy.localRotation=child.localRotation;copy.localScale=child.localScale;
                if(reference.TryGetValue(child.name,out var bind)) {copy.localPosition=bind.position;copy.localRotation=bind.rotation;copy.localScale=bind.scale;}
                rest.Add((copy,copy.localPosition,copy.localRotation,copy.localScale));Copy(child,copy,needed);
            }
        }
        public void Play(AnimationClip clip)
        {
            Stop(); foreach(var bone in rest) {bone.bone.localPosition=bone.position;bone.bone.localRotation=bone.rotation;bone.bone.localScale=bone.scale;}
            Animator.Rebind();
            graph=PlayableGraph.Create("AvatarLab pose sample");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);playable.SetSpeed(0);
            var output=AnimationPlayableOutput.Create(graph,"Pose only",Animator);output.SetSourcePlayable(playable);graph.Play();
        }
        public HumanPose Sample(float time)
        {
            playable.SetTime(Mathf.Max(0,time));graph.Evaluate(0);var result=pose;handler.GetHumanPose(ref result);return result;
        }
        public void Stop() {if(graph.IsValid()) graph.Destroy();}
        public void Dispose() {Stop();handler.Dispose();if(Root!=null) UnityEngine.Object.Destroy(Root.gameObject);}
    }
}
