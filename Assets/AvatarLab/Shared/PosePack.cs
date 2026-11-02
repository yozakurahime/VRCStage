using System;
using UnityEngine;
namespace AvatarLab
{
    public sealed class PosePack : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string id,label; public AnimationClip clip; public bool animated; }
        public int format=1;
        public string author,source;
        public Entry[] entries=Array.Empty<Entry>();
    }
}
