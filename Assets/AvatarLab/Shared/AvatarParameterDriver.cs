using System;
using UnityEngine;
namespace AvatarLab
{
    [Serializable] public sealed class AvatarParameterOperation
    {
        public int type; // Set, Add, Random, Copy
        public string name,source;
        public float value,valueMin,valueMax=1,chance=1,sourceMin,sourceMax,destMin,destMax;
        public bool preventRepeats,convertRange;
    }
    public sealed class AvatarParameterDriver : StateMachineBehaviour
    {
        public AvatarParameterOperation[] parameters=Array.Empty<AvatarParameterOperation>();
        public bool localOnly;
        public override void OnStateEnter(Animator animator,AnimatorStateInfo state,int layer)
        {
            if(AvatarLayerControl.Hosts.TryGetValue(animator,out var host)) Apply(host);
        }
        public void Apply(IAvatarAnimationHost host)
        {
            // Every avatar instance in this offline player is local.
            foreach(var p in parameters)
            {
                if(string.IsNullOrEmpty(p.name)) continue;
                float value=p.value;
                if(p.type==1) value=host.GetValue(p.name)+p.value;
                if(p.type==3)
                {
                    value=host.GetValue(p.source);
                    if(p.convertRange) value=Mathf.Lerp(p.destMin,p.destMax,Mathf.InverseLerp(p.sourceMin,p.sourceMax,value));
                }
                if(p.type==2)
                {
                    var type=host.ParameterType(p.name); var old=host.GetValue(p.name);
                    for(int attempt=0;attempt<8;attempt++)
                    {
                        value=type==AnimatorControllerParameterType.Bool?(UnityEngine.Random.value<p.chance?1:0):type==AnimatorControllerParameterType.Int?UnityEngine.Random.Range(Mathf.RoundToInt(p.valueMin),Mathf.RoundToInt(p.valueMax)+1):UnityEngine.Random.Range(p.valueMin,p.valueMax);
                        if(!p.preventRepeats || !Mathf.Approximately(value,old)) break;
                    }
                }
                host.SetValue(p.name,value);
            }
        }
    }
}
