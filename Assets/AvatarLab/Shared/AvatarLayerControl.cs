using System.Collections.Generic;
using UnityEngine;

namespace AvatarLab
{
    public interface IAvatarAnimationHost
    {
        void BlendLayer(string type, float weight, float seconds, int animatorLayer);
        float GetValue(string name);
        void SetValue(string name,float value);
        AnimatorControllerParameterType ParameterType(string name);
        void SetTracking(string part,int mode);
    }
    // Independent replacement for serialized layer-weight commands in exported states.
    public sealed class AvatarLayerControl : StateMachineBehaviour
    {
        public static readonly Dictionary<Animator,IAvatarAnimationHost> Hosts = new Dictionary<Animator,IAvatarAnimationHost>();
        public string target="Action";
        public int animatorLayer=-1;
        public float weight=1, duration;
        public override void OnStateEnter(Animator animator, AnimatorStateInfo state, int layer)
        {
            if(Hosts.TryGetValue(animator,out var host)) host.BlendLayer(target,weight,duration,animatorLayer);
        }
    }
}
