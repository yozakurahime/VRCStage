using System.Collections.Generic;
using UnityEngine;
namespace AvatarLab
{
    public sealed class AvatarPhysicsCollider : MonoBehaviour
    {
        public Transform rootTransform;
        public int shapeType; // sphere, capsule, plane
        public bool insideBounds, bonesAsSpheres;
        public float radius = .05f, height = .2f;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool globalCollision, allowSelf = true;
        AvatarDefinition cachedOwner;
        public AvatarDefinition Owner {get { if(cachedOwner==null) cachedOwner=GetComponentInParent<AvatarDefinition>(); return cachedOwner; }}
        public static readonly List<AvatarPhysicsCollider> Active = new List<AvatarPhysicsCollider>();
        public Transform Root => rootTransform != null ? rootTransform : transform;
        public float Scale { get { var s=Root.lossyScale; return Mathf.Max(Mathf.Abs(s.x),Mathf.Max(Mathf.Abs(s.y),Mathf.Abs(s.z))); } }
        public Vector3 Center => Root.TransformPoint(position);
        public Quaternion Orientation => Root.rotation * rotation;
        public Vector3 Axis => Orientation * Vector3.up;
        public float Radius => Mathf.Max(0,radius)*Scale;
        public float HalfSegment => Mathf.Max(0,height*.5f-radius)*Scale;
        void OnEnable() { cachedOwner=GetComponentInParent<AvatarDefinition>(); if(!Active.Contains(this)) Active.Add(this); }
        void OnTransformParentChanged() { cachedOwner=null; }
        void OnDisable() { Active.Remove(this); }
        public static Vector3 Closest(Vector3 a,Vector3 b,Vector3 p,out float t)
        {
            var d=b-a; t=d.sqrMagnitude>1e-12f?Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude):1; return a+d*t;
        }
        public Vector3 Project(Vector3 p,float boneRadius)
        {
            return CaptureWorld().Project(p,boneRadius);
        }
        public WorldShape CaptureWorld()
        {
            var root=Root; var scale=root.lossyScale; float maximum=Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z))); var orientation=root.rotation*rotation;
            return new WorldShape {center=root.TransformPoint(position),axis=orientation*Vector3.up,right=orientation*Vector3.right,forward=orientation*Vector3.forward,radius=Mathf.Max(0,radius)*maximum,half=Mathf.Max(0,height*.5f-radius)*maximum,kind=shapeType,inside=insideBounds,points=bonesAsSpheres};
        }
        public struct WorldShape
        {
            public Vector3 center,axis,right,forward;
            public float radius,half;
            public int kind;
            public bool inside,points;
            public Vector3 Project(Vector3 p,float boneRadius)
            {
            if(kind==2)
            {
                var n=forward; float distance=Vector3.Dot(p-center,n); float sign=inside?-1:1;
                return distance*sign<boneRadius ? p+n*sign*(boneRadius-distance*sign) : p;
            }
            Vector3 closest=kind==1?Closest(center-axis*half,center+axis*half,p,out _):center;
            var delta=p-closest; float r=inside?Mathf.Max(0,radius-boneRadius):radius+boneRadius;
            float length=delta.magnitude;
            if(inside ? length<=r : length>=r) return p;
            return closest+(length>1e-7f?delta/length:right)*r;
            }
            public Vector3 ProjectBone(Vector3 start,Vector3 end,float boneRadius)
            {
                // Broad phase in world space: most hand/body colliders are far from this bone.
                if(!inside && kind!=2)
                {
                    float expand=radius+boneRadius;
                    var extents=(kind==1?new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z))*half:Vector3.zero)+Vector3.one*expand;
                    var low=Vector3.Min(start,end); var high=Vector3.Max(start,end);
                    if(high.x<center.x-extents.x || low.x>center.x+extents.x || high.y<center.y-extents.y || low.y>center.y+extents.y || high.z<center.z-extents.z || low.z>center.z+extents.z) return end;
                }
                if(points || inside || kind==2) return Project(end,boneRadius);
                Vector3 closest=center,nearest=end; float t=1;
                for(int i=0;i<4;i++) { nearest=Closest(start,end,closest,out t); closest=kind==1?Closest(center-axis*half,center+axis*half,nearest,out _):center; }
                var projected=Project(nearest,boneRadius);
                if((nearest-closest).sqrMagnitude<1e-10f)
                {
                    var normal=Vector3.Cross(end-start,axis); if(normal.sqrMagnitude<1e-10f) normal=Vector3.Cross(end-start,right);
                    if(normal.sqrMagnitude>1e-10f) projected=nearest+normal.normalized*(radius+boneRadius);
                }
                var correction=projected-nearest; if(correction.sqrMagnitude<1e-12f) return end;
                return end+Vector3.ClampMagnitude(correction/Mathf.Max(.15f,t),radius*2+boneRadius*2+.05f);
            }
        }
        public Vector3 ProjectBone(Vector3 start,Vector3 end,float boneRadius)
        {
            return CaptureWorld().ProjectBone(start,end,boneRadius);
        }
        public void Draw(Color color)
        {
            Gizmos.color=color;
            if(shapeType==2) { var old=Gizmos.matrix; Gizmos.matrix=Matrix4x4.TRS(Center,Orientation,Vector3.one*Scale); Gizmos.DrawWireCube(Vector3.zero,new Vector3(.3f,.3f,.001f)); Gizmos.DrawRay(Vector3.zero,Vector3.forward*.15f); Gizmos.matrix=old; return; }
            var a=Center-Axis*HalfSegment; var b=Center+Axis*HalfSegment;
            if(shapeType==0) { Gizmos.DrawWireSphere(Center,Radius); return; }
            Gizmos.DrawWireSphere(a,Radius); Gizmos.DrawWireSphere(b,Radius);
            var x=Orientation*Vector3.right*Radius; var z=Orientation*Vector3.forward*Radius;
            Gizmos.DrawLine(a+x,b+x); Gizmos.DrawLine(a-x,b-x); Gizmos.DrawLine(a+z,b+z); Gizmos.DrawLine(a-z,b-z);
        }
        void OnDrawGizmosSelected() { Draw(insideBounds?Color.cyan:Color.green); }
    }
}
