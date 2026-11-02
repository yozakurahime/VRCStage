#if UNITY_EDITOR
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    public sealed class VRCAvatarParameterDriver : StateMachineBehaviour
    {
        public AvatarParameterOperation[] parameters=new AvatarParameterOperation[0];
        public bool localOnly;
    }
}
#endif
