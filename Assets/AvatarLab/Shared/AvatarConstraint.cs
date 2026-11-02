using System;
using UnityEngine;
namespace AvatarLab
{
    [Serializable] public struct AvatarConstraintSource
    {
        public Transform SourceTransform;
        public float Weight;
        public Vector3 ParentPositionOffset,ParentRotationOffset;
    }
    [Serializable] public struct AvatarConstraintSources
    {
        public AvatarConstraintSource source0,source1,source2,source3,source4,source5,source6,source7,source8,source9,source10,source11,source12,source13,source14,source15;
        public int totalLength;
        public AvatarConstraintSource[] overflowList;
        public AvatarConstraintSource Get(int i)
        {
            switch(i) { case 0:return source0;case 1:return source1;case 2:return source2;case 3:return source3;case 4:return source4;case 5:return source5;case 6:return source6;case 7:return source7;case 8:return source8;case 9:return source9;case 10:return source10;case 11:return source11;case 12:return source12;case 13:return source13;case 14:return source14;case 15:return source15;default:return overflowList[i-16]; }
        }
        public void Set(int i,AvatarConstraintSource s)
        {
            switch(i) {case 0:source0=s;break;case 1:source1=s;break;case 2:source2=s;break;case 3:source3=s;break;case 4:source4=s;break;case 5:source5=s;break;case 6:source6=s;break;case 7:source7=s;break;case 8:source8=s;break;case 9:source9=s;break;case 10:source10=s;break;case 11:source11=s;break;case 12:source12=s;break;case 13:source13=s;break;case 14:source14=s;break;case 15:source15=s;break;default:overflowList[i-16]=s;break;}
        }
        public int Count=>Mathf.Clamp(totalLength,0,16+(overflowList?.Length??0));
    }
    // Independent offline solver. Serialized names preserve animation bindings after conversion.
    public abstract class AvatarConstraint : MonoBehaviour
    {
        public bool IsActive=true,Locked=true,SolveInLocalSpace,FreezeToWorld,RebakeOffsetsWhenUnfrozen;
        public float GlobalWeight=1;
        public Transform TargetTransform;
        public AvatarConstraintSources Sources=new AvatarConstraintSources();
        public Vector3 PositionAtRest,PositionOffset,RotationAtRest,RotationOffset,ScaleAtRest=Vector3.one,ScaleOffset=Vector3.one;
        public bool AffectsPositionX=true,AffectsPositionY=true,AffectsPositionZ=true,AffectsRotationX=true,AffectsRotationY=true,AffectsRotationZ=true,AffectsScaleX=true,AffectsScaleY=true,AffectsScaleZ=true;
        public Vector3 AimAxis=Vector3.forward,UpAxis=Vector3.up,WorldUpVector=Vector3.up;
        public Transform WorldUpTransform;
        public int WorldUp;
        public bool UseUpTransform;
        public float Roll;
        public abstract int Kind {get;} // parent, position, rotation, scale, aim, look-at
        public Transform Target=>TargetTransform!=null?TargetTransform:transform;
        bool frozen;
        Vector3 frozenPosition,frozenScale;
        Quaternion frozenRotation;
        public int Evaluations {get;private set;}
        void OnDisable() { frozen=false; }
        public void Evaluate()
        {
            if(!isActiveAndEnabled || !IsActive) return;
            var t=Target; var parent=t.parent;
            bool local=SolveInLocalSpace;
            if(FreezeToWorld && !frozen) { frozenPosition=t.position; frozenRotation=t.rotation; frozenScale=t.lossyScale; }
            if(!FreezeToWorld && frozen && RebakeOffsetsWhenUnfrozen) Rebake();
            frozen=FreezeToWorld;
            Vector3 p=Vector3.zero,s=Vector3.zero; Quaternion q=Quaternion.identity; float sum=0;
            for(int i=0;i<Sources.Count;i++)
            {
                var src=Sources.Get(i); var source=src.SourceTransform; if(source==null || src.Weight<=0) continue;
                Vector3 nextP=local?source.localPosition:source.position;
                Quaternion nextQ=local?source.localRotation:source.rotation;
                if(Kind==0) { nextP+=nextQ*Vector3.Scale(local?source.localScale:source.lossyScale,src.ParentPositionOffset); nextQ*=Quaternion.Euler(src.ParentRotationOffset); }
                p+=nextP*src.Weight; s+=(local?source.localScale:source.lossyScale)*src.Weight;
                q=sum==0?nextQ:Quaternion.Slerp(q,nextQ,src.Weight/(sum+src.Weight)); sum+=src.Weight;
            }
            if(sum>0) { p/=sum; s/=sum; }
            else { p=local?PositionAtRest:parent!=null?parent.TransformPoint(PositionAtRest):PositionAtRest; q=(local || parent==null)?Quaternion.Euler(RotationAtRest):parent.rotation*Quaternion.Euler(RotationAtRest); s=local?ScaleAtRest:Vector3.Scale(parent!=null?parent.lossyScale:Vector3.one,ScaleAtRest); }
            if(Kind==1) p+=PositionOffset;
            if(Kind==2) q*=Quaternion.Euler(RotationOffset);
            if(Kind==3) s=Vector3.Scale(s,ScaleOffset);
            if(Kind>=4)
            {
                Vector3 origin=local?t.localPosition:t.position; var direction=p-origin;
                if(direction.sqrMagnitude>1e-10f)
                {
                    Vector3 up=WorldUpVector;
                    if(Kind==5) up=UseUpTransform && WorldUpTransform!=null?(local?WorldUpTransform.localPosition:WorldUpTransform.position)-origin:Quaternion.AngleAxis(Roll,direction.normalized)*Vector3.up;
                    else if(WorldUp==0) up=Vector3.up;
                    else if(WorldUp==1 && WorldUpTransform!=null) up=(local?WorldUpTransform.localPosition:WorldUpTransform.position)-origin;
                    else if(WorldUp==2 && WorldUpTransform!=null) up=(local?WorldUpTransform.localRotation:WorldUpTransform.rotation)*WorldUpVector;
                    else if(WorldUp==4) up=(local?t.localRotation:t.rotation)*UpAxis;
                    if(Vector3.Cross(direction,up).sqrMagnitude<1e-10f) up=Mathf.Abs(direction.normalized.y)<.9f?Vector3.up:Vector3.right;
                    var axis=Kind==5?Quaternion.identity:Quaternion.LookRotation(AimAxis.sqrMagnitude>1e-8f?AimAxis:Vector3.forward,UpAxis.sqrMagnitude>1e-8f?UpAxis:Vector3.up);
                    q=Quaternion.LookRotation(direction,up)*Quaternion.Inverse(axis)*Quaternion.Euler(RotationOffset);
                }
                else q=local?t.localRotation:t.rotation;
            }
            if(frozen) { p=frozenPosition; q=frozenRotation; s=frozenScale; local=false; }
            if(!local) { p=parent!=null?parent.InverseTransformPoint(p):p; q=parent!=null?Quaternion.Inverse(parent.rotation)*q:q; s=Divide(s,parent!=null?parent.lossyScale:Vector3.one); }
            if(Kind<=1)
            {
                var result=Vector3.LerpUnclamped(PositionAtRest,p,GlobalWeight); var old=t.localPosition;
                t.localPosition=new Vector3(AffectsPositionX?result.x:old.x,AffectsPositionY?result.y:old.y,AffectsPositionZ?result.z:old.z);
            }
            if(Kind==0 || Kind==2 || Kind>=4)
            {
                var result=Quaternion.SlerpUnclamped(Quaternion.Euler(RotationAtRest),q,GlobalWeight);
                if(AffectsRotationX && AffectsRotationY && AffectsRotationZ) t.localRotation=result;
                else { var e=result.eulerAngles; var old=t.localEulerAngles; t.localRotation=Quaternion.Euler(AffectsRotationX?e.x:old.x,AffectsRotationY?e.y:old.y,AffectsRotationZ?e.z:old.z); }
            }
            if(Kind==3)
            {
                var result=Vector3.LerpUnclamped(ScaleAtRest,s,GlobalWeight); var old=t.localScale;
                t.localScale=new Vector3(AffectsScaleX?result.x:old.x,AffectsScaleY?result.y:old.y,AffectsScaleZ?result.z:old.z);
            }
            Evaluations++;
        }
        void Rebake()
        {
            var t=Target; bool local=SolveInLocalSpace; var targetPosition=local?t.localPosition:t.position; var targetRotation=local?t.localRotation:t.rotation;
            Vector3 p=Vector3.zero,s=Vector3.zero; Quaternion q=Quaternion.identity; float sum=0;
            for(int i=0;i<Sources.Count;i++)
            {
                var src=Sources.Get(i); var source=src.SourceTransform; if(source==null || src.Weight<=0) continue;
                var sp=local?source.localPosition:source.position; var sq=local?source.localRotation:source.rotation; var ss=local?source.localScale:source.lossyScale;
                if(Kind==0) { src.ParentPositionOffset=Divide(Quaternion.Inverse(sq)*(targetPosition-sp),ss); src.ParentRotationOffset=(Quaternion.Inverse(sq)*targetRotation).eulerAngles; Sources.Set(i,src); }
                p+=sp*src.Weight; s+=ss*src.Weight; q=sum==0?sq:Quaternion.Slerp(q,sq,src.Weight/(sum+src.Weight)); sum+=src.Weight;
            }
            if(sum>0) { PositionOffset=targetPosition-p/sum; RotationOffset=(Quaternion.Inverse(q)*targetRotation).eulerAngles; ScaleOffset=Divide(local?t.localScale:t.lossyScale,s/sum); }
        }
        static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(Mathf.Abs(b.x)>1e-8f?a.x/b.x:0,Mathf.Abs(b.y)>1e-8f?a.y/b.y:0,Mathf.Abs(b.z)>1e-8f?a.z/b.z:0);
    }
}
