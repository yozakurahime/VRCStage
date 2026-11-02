using System;
using UnityEngine;
namespace AvatarLab
{
    public sealed class AvatarTrackingControl : StateMachineBehaviour
    {
        public string[] parts=Array.Empty<string>();
        public int[] modes=Array.Empty<int>(); // NoChange=0, Tracking=1, Animation=2
        public override void OnStateEnter(Animator animator,AnimatorStateInfo state,int layer)
        {
            if(AvatarLayerControl.Hosts.TryGetValue(animator,out var host)) for(int i=0;i<Math.Min(parts.Length,modes.Length);i++) if(modes[i]!=0) host.SetTracking(parts[i],modes[i]);
        }
    }
}
