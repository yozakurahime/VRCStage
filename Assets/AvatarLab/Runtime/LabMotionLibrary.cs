using System;
using System.Collections.Generic;
using UnityEngine;
namespace AvatarLab
{
    public sealed class LabMotionLibrary : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string proxy; public AnimationClip clip; }
        [Serializable] public sealed class Preset { public string id,label,category; public bool pose,plantFeet; public AnimationClip clip; }
        public List<Preset> presets=new List<Preset>();
        public RuntimeAnimatorController defaultController;
        public List<Entry> entries=new List<Entry>();
        public RuntimeAnimatorController Resolve(RuntimeAnimatorController source,List<AnimatorOverrideController> owned,out int replaced)
        {
            replaced=0; if(source==null) return defaultController;
            var seen=new HashSet<AnimationClip>();
            var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();
            foreach(var clip in source.animationClips)
            {
                if(clip==null || !seen.Add(clip)) continue;
                // Converted assets may have a suffix added to avoid asset name collisions.
                var key=clip.name; foreach(var e in entries) if(key.Equals(e.proxy,StringComparison.OrdinalIgnoreCase) || key.StartsWith(e.proxy+"_",StringComparison.OrdinalIgnoreCase))
                { overrides.Add(new KeyValuePair<AnimationClip,AnimationClip>(clip,e.clip)); replaced++; break; }
            }
            if(replaced==0) return source;
            var controller=new AnimatorOverrideController(source); controller.ApplyOverrides(overrides); owned.Add(controller); return controller;
        }
    }
}
