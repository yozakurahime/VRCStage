using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace AvatarLab.Editor
{
    public static class PoseLibraryBuilder
    {
        const string Folder=LabBuild.Generated+"/Poses";
        public static void Build(LabMotionLibrary library)
        {
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(LabBuild.Generated,"Poses");library.presets.Clear();
            using(var rig=new ReferenceMotionRig())
            {
                Add(library,rig,"relaxed","Relaxada",true);Add(library,rig,"tpose","T-Pose",true);Add(library,rig,"apose","A-Pose",true);
                Add(library,rig,"hips","Mãos na cintura",true);Add(library,rig,"crossed","Braços cruzados",true);Add(library,rig,"point","Apontar",true);
                Add(library,rig,"victory","Braços para cima",true);Add(library,rig,"photo","Pose para foto",true);Add(library,rig,"sit","Sentada",true);Add(library,rig,"kneel","Ajoelhada",true);
                Add(library,rig,"crouch","Agachada",true);Add(library,rig,"wavepose","Mão levantada",true);
                Add(library,rig,"wave","Acenar",false);Add(library,rig,"clap","Bater palmas",false);Add(library,rig,"bow","Reverência",false);Add(library,rig,"nod","Concordar com a cabeça",false);
                Add(library,rig,"shake","Negar com a cabeça",false);Add(library,rig,"dance","Dança simples",false);Add(library,rig,"breathe","Respiração suave",false);Add(library,rig,"stretch","Alongar braços",false);
                Add(library,rig,"walk","Caminhar",false);Add(library,rig,"run","Correr",false);Add(library,rig,"crouchwalk","Caminhar agachada",false);Add(library,rig,"land","Aterrissar",false);
            }
            EditorUtility.SetDirty(library);
        }
        static void Add(LabMotionLibrary library,ReferenceMotionRig rig,string id,string label,bool pose)
        {
            string path=Folder+"/"+id+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(clip==null) {clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
            clip.ClearCurves();clip.name="AvatarLab • "+label;clip.frameRate=60;
            float duration=pose?1:id=="dance"?3:id=="breathe"?4:id=="walk" || id=="crouchwalk"?.9f:id=="run"?.6f:id=="land"?.4f:2;
            int frames=pose?1:Mathf.CeilToInt(duration*60);var curves=new Dictionary<string,List<float>>();var names=HumanTrait.MuscleName;
            Action<string,float> add=(name,value)=>{if(!curves.TryGetValue(name,out var values)) curves[name]=values=new List<float>();values.Add(value);};Quaternion previous=Quaternion.identity;
            for(int frame=0;frame<=frames;frame++)
            {
                rig.Apply(id,frame/(float)frames);var human=rig.Capture();
                for(int i=0;i<names.Length;i++) if(!names[i].Contains("Eye") && !names[i].Contains("Jaw")) add(names[i],Mathf.Clamp(human.muscles[i],-1,1));
                add("RootT.x",human.bodyPosition.x);add("RootT.y",human.bodyPosition.y);add("RootT.z",human.bodyPosition.z);
                var q=human.bodyRotation;if(frame>0 && Quaternion.Dot(q,previous)<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous=q;
                add("RootQ.x",q.x);add("RootQ.y",q.y);add("RootQ.z",q.z);add("RootQ.w",q.w);
            }
            foreach(var pair in curves)
            {
                var values=pair.Value;var keys=new Keyframe[values.Count];float dt=duration/frames;
                for(int i=0;i<keys.Length;i++)
                {
                    float tangent=i>0 && i<frames?(values[i+1]-values[i-1])/(2*dt):!pose && Mathf.Abs(values[0]-values[frames])<.0001f?(values[1]-values[frames-1])/(2*dt):0;
                    keys[i]=new Keyframe(i*dt,values[i],tangent,tangent);
                }
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),pair.Key),new AnimationCurve(keys));
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=!pose && id!="land";settings.keepOriginalPositionY=true;settings.keepOriginalOrientation=true;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
            library.presets.Add(new LabMotionLibrary.Preset{id=id,label=label,category=pose?"Poses":"Animações",pose=pose,plantFeet=true,clip=clip});
        }
    }
}
