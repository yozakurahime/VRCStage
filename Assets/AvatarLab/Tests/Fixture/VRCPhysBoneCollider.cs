#if UNITY_EDITOR
// Synthetic serialized fixture, not a VRChat SDK implementation.
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    public class VRCPhysBoneCollider : MonoBehaviour
    {
        public Transform rootTransform;
        public int shapeType;
        public bool insideBounds,bonesAsSpheres;
        public float radius=.1f,height=.4f;
        public Vector3 position;
        public Quaternion rotation=Quaternion.identity;
        public int globalCollision;
        public bool globalCollisionAllowSelf=true;
    }
}
#endif
