#if UNITY_EDITOR
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    // Serialized test data only; no SDK runtime implementation.
    public abstract class FixtureConstraint : MonoBehaviour
    {
        public bool IsActive=true,Locked=true,SolveInLocalSpace,FreezeToWorld,RebakeOffsetsWhenUnfrozen;
        public float GlobalWeight=1;
        public Transform TargetTransform;
        public AvatarConstraintSources Sources=new AvatarConstraintSources();
        public Vector3 PositionAtRest,PositionOffset,RotationAtRest,RotationOffset,ScaleAtRest=Vector3.one,ScaleOffset=Vector3.one;
        public bool AffectsPositionX=true,AffectsPositionY=true,AffectsPositionZ=true,AffectsRotationX=true,AffectsRotationY=true,AffectsRotationZ=true,AffectsScaleX=true,AffectsScaleY=true,AffectsScaleZ=true;
    }
}
#endif
