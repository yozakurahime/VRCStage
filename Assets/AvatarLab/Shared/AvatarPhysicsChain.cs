using System;
using System.Collections.Generic;
using UnityEngine;
namespace AvatarLab
{
    [DefaultExecutionOrder(320)]
    public sealed class AvatarPhysicsChain : MonoBehaviour
    {
        public Transform rootTransform;
        public Transform[] ignoreTransforms = Array.Empty<Transform>();
        public bool ignoreOtherPhysBones = true;
        public Vector3 endpointPosition;
        public int multiChildType, integrationType, version = 1, immobileType;
        [Range(0,1)] public float pull=.2f,spring=.5f,stiffness=.1f,gravityFalloff,immobile;
        [Range(-1,1)] public float gravity;
        public float radius=.01f,maxAngleX=45,maxAngleZ=45,maxStretch,maxSquish,stretchMotion;
        public int limitType;
        public Vector3 limitRotation;
        public AnimationCurve pullCurve,springCurve,stiffnessCurve,gravityCurve,gravityFalloffCurve,immobileCurve,radiusCurve,maxAngleXCurve,maxAngleZCurve,maxStretchCurve,maxSquishCurve,stretchMotionCurve,limitRotationXCurve,limitRotationYCurve,limitRotationZCurve;
        public AvatarPhysicsCollider[] colliders = Array.Empty<AvatarPhysicsCollider>();
        public bool allowCollision=true,allowGrabbing=true,allowPosing,isAnimated,resetWhenDisabled=true,snapToHand;
        public float grabMovement=1;
        public string parameter="";
        [NonSerialized] public bool simulationEnabled=true,collideWorld;
        // Runtime preview attachment: authored/exported chains keep their original behavior.
        [NonSerialized] public int attachmentDepth;
        [NonSerialized] public bool lockAttachmentRotation;
        public bool IsGrabbed => grabHeld && grabIndex>0;
        public bool IsPosed { get; private set; }
        public float Angle { get; private set; }
        public float Stretch { get; private set; }
        public float Squish { get; private set; }
        public int Count => nodes.Count;
        public int CollisionCorrections { get; private set; }
        public Transform Root => rootTransform!=null?rootTransform:transform;
        public string Diagnostic { get; private set; }="";
        sealed class Node
        {
            public Transform bone;
            public int parent,depth;
            public bool pinned,collapsed;
            public Vector3 restPosition,baseLocalPosition,basePosition,position,previous,virtualOffset;
            public Quaternion restRotation,baseLocalRotation,baseRotation;
            public float length,t,frameRadius,frameLength,frameTension,frameMomentum,frameRigidity,frameGravity,frameFalloff,frameCarry,frameMotion,frameStretch,frameSquish,frameAngleX,frameAngleZ;
            public Vector3 frameRest; public Quaternion frameBasis,frameInverseBasis;
            public readonly List<int> children=new List<int>();
        }
        readonly List<Node> nodes=new List<Node>();
        readonly HashSet<Transform> exclusions=new HashSet<Transform>();
        Vector3 lastRoot;
        Quaternion lastRootRotation;
        bool initialized,wasSimulating;
        string performanceName;
        int grabIndex=-1;
        bool grabHeld;
        Vector3 grabTarget;
        readonly Collider[] overlaps=new Collider[24];
        SphereCollider probe;
        AvatarDefinition owner;
        readonly List<AvatarPhysicsCollider.WorldShape> collisionShapes=new List<AvatarPhysicsCollider.WorldShape>(48);
        readonly HashSet<AvatarPhysicsCollider> listedColliders=new HashSet<AvatarPhysicsCollider>();
        public Vector3 Point(int index) => nodes[index].position;
        public Vector3 ParentPoint(int index) => nodes[nodes[index].parent].position;
        public string PointName(int index) => nodes[index].bone!=null?nodes[index].bone.name:"Endpoint";
        public float PointRadius(int index) => Mathf.Max(0,Sample(radius,radiusCurve,nodes[index].t))*MaxScale(Root);
        public static float Sample(float value,AnimationCurve curve,float t) => value*(curve!=null && curve.length>0?curve.Evaluate(t):1);
        static float MaxScale(Transform t) { var s=t.lossyScale; return Mathf.Max(Mathf.Abs(s.x),Mathf.Max(Mathf.Abs(s.y),Mathf.Abs(s.z))); }
        bool Excluded(Transform t)
        {
            foreach(var ignored in exclusions) if(ignored!=null && (t==ignored || t.IsChildOf(ignored))) return true;
            return false;
        }
        public void Rebuild()
        {
            performanceName="Physics/"+name; Restore(); nodes.Clear(); exclusions.Clear(); ClearPose(); owner=GetComponentInParent<AvatarDefinition>();
            foreach(var t in ignoreTransforms) if(t!=null) exclusions.Add(t);
            if(ignoreOtherPhysBones)
            {
                var scope=owner!=null?owner.transform:transform.root;
                foreach(var other in scope.GetComponentsInChildren<AvatarPhysicsChain>(true))
                    if(other!=this && other.Root!=Root && other.Root.IsChildOf(Root)) exclusions.Add(other.Root);
            }
            Build(Root,-1,0,true); int deepest=1;
            foreach(var n in nodes) deepest=Mathf.Max(deepest,n.depth);
            foreach(var n in nodes) n.t=n.depth/(float)deepest;
            lastRoot=Root.position; lastRootRotation=Root.rotation; initialized=true;
            Diagnostic=nodes.Count>=2?nodes.Count+" pontos (inclui raiz/endpoints)":"Sem segmento: confira filhos/endpoint/ignoreTransforms.";
        }
        void Build(Transform t,int parent,int depth,bool pin)
        {
            if(t==null || depth>128 || nodes.Count>=2048 || Excluded(t)) return;
            int index=nodes.Count;
            var n=new Node {bone=t,parent=parent,depth=depth,pinned=pin || depth<=attachmentDepth,restPosition=t.localPosition,baseLocalPosition=t.localPosition,restRotation=t.localRotation,baseLocalRotation=t.localRotation,baseRotation=t.rotation,basePosition=t.position,position=t.position,previous=t.position};
            nodes.Add(n); if(parent>=0) { nodes[parent].children.Add(index); n.length=Vector3.Distance(nodes[parent].position,n.position); }
            var children=new List<Transform>(); for(int i=0;i<t.childCount;i++) if(!Excluded(t.GetChild(i))) children.Add(t.GetChild(i));
            for(int i=0;i<children.Count;i++) Build(children[i],index,depth+1,children.Count>1 && (multiChildType==0 || multiChildType==1 && i>0));
            if(children.Count==0 && endpointPosition.sqrMagnitude>1e-10f)
            {
                var p=t.TransformPoint(endpointPosition);
                nodes.Add(new Node {parent=index,depth=depth+1,virtualOffset=endpointPosition,basePosition=p,position=p,previous=p,length=Vector3.Distance(p,t.position)}); n.children.Add(nodes.Count-1);
            }
        }
        void Start() { if(!initialized) Rebuild(); }
        void Update() { if(initialized) Restore(); }
        void Restore()
        {
            foreach(var n in nodes) if(n.bone!=null) { if(n.parent>=0) n.bone.localPosition=n.baseLocalPosition; n.bone.localRotation=n.baseLocalRotation; }
        }
        void Capture()
        {
            foreach(var n in nodes)
            {
                if(n.bone!=null)
                {
                    if(!isAnimated) { if(n.parent>=0) n.bone.localPosition=n.restPosition; n.bone.localRotation=n.restRotation; }
                    n.baseLocalPosition=n.bone.localPosition; n.baseLocalRotation=n.bone.localRotation;
                    n.basePosition=n.bone.position; n.baseRotation=n.bone.rotation;
                }
                else n.basePosition=nodes[n.parent].bone.TransformPoint(n.virtualOffset);
            }
            foreach(var n in nodes) if(n.parent>=0)
            {
                n.collapsed=(n.basePosition-nodes[n.parent].basePosition).sqrMagnitude<1e-14f;
                if(n.bone!=null && n.bone.parent!=null) { var s=n.bone.parent.lossyScale; n.collapsed|=Mathf.Abs(s.x)<1e-7f || Mathf.Abs(s.y)<1e-7f || Mathf.Abs(s.z)<1e-7f; }
            }
        }
        public void ResetSimulation()
        {
            Restore(); Capture(); foreach(var n in nodes) n.previous=n.position=n.basePosition;
            lastRoot=Root.position; lastRootRotation=Root.rotation; ClearPose();
            Angle=Stretch=Squish=0;
        }
        public void Grab(int index,Vector3 target)
        {
            if(!allowGrabbing || index<=0 || index>=nodes.Count || nodes[index].pinned) return;
            grabIndex=index; grabTarget=target; grabHeld=true; IsPosed=false;
        }
        public void MoveGrab(Vector3 target) { grabTarget=target; }
        public void Pose() { if(IsGrabbed && allowPosing) IsPosed=true; }
        public void Release() { grabHeld=false; if(!IsPosed) grabIndex=-1; }
        public void ClearPose() { IsPosed=false; grabHeld=false; grabIndex=-1; }
        public void ApplyImpulse(Vector3 velocity)
        {
            foreach(var n in nodes) if(!n.pinned) n.previous=n.position-Vector3.ClampMagnitude(velocity,5)/120f;
        }
        void LateUpdate() { Simulate(Time.deltaTime); }
        public void Simulate(float dt)
        {
            using var timing=LabPerformance.Measure("Physics");
            using var chainTiming=LabPerformance.Measure(performanceName??"Physics/initializing");
            if(!initialized) Rebuild();
            if(!simulationEnabled || nodes.Count<2) { if(wasSimulating) ResetSimulation(); wasSimulating=false; return; }
            Capture();
            PrepareCollisions();
            if(!wasSimulating || (Root.position-lastRoot).sqrMagnitude>.5f*.5f || dt>.15f) { foreach(var n in nodes) n.position=n.previous=n.basePosition; lastRoot=Root.position; lastRootRotation=Root.rotation; }
            wasSimulating=true;
            if(dt<=0) return;
            dt=Mathf.Min(dt,1f/15); int steps=Mathf.Clamp(Mathf.CeilToInt(dt*120),1,8); float h=dt/steps;
            var displacement=Root.position-lastRoot; var turn=Root.rotation*Quaternion.Inverse(lastRootRotation);
            foreach(var n in nodes)
            {
                float carry=n.frameCarry;
                var carried=immobileType==0?Root.position+turn*(n.position-lastRoot):n.position+displacement;
                var carriedPrevious=immobileType==0?Root.position+turn*(n.previous-lastRoot):n.previous+displacement;
                n.position=Vector3.Lerp(n.position,carried,carry); n.previous=Vector3.Lerp(n.previous,carriedPrevious,carry);
            }
            CollisionCorrections=0;
            for(int step=0;step<steps;step++)
            {
                foreach(var n in nodes)
                {
                    if(n.pinned || n.collapsed) { n.previous=n.position=n.basePosition; continue; }
                    float tension=n.frameTension, momentum=n.frameMomentum;
                    float rigidity=n.frameRigidity;
                    Vector3 rest=nodes[n.parent].position+(n.basePosition-nodes[n.parent].basePosition);
                    float g=n.frameGravity, falloff=n.frameFalloff;
                    var old=n.position; float drag=Mathf.Pow(Mathf.Lerp(.4f,.99f,momentum)*(1-rigidity*.2f),h*60);
                    float bend=Vector3.Angle(n.position-nodes[n.parent].position,rest-nodes[n.parent].position)/180;
                    float fall=1-falloff*(1-bend);
                    Vector3 acceleration=(rest-n.position)*(tension*1800+rigidity*600)+Vector3.down*(g*fall*Mathf.Max((rest-nodes[n.parent].position).magnitude,.005f)*400);
                    n.position+=(n.position-n.previous)*drag+acceleration*h*h; n.previous=old;
                }
                for(int iteration=0;iteration<4;iteration++)
                    for(int i=1;i<nodes.Count;i++)
                    {
                        var n=nodes[i]; if(n.pinned) continue;
                        if(n.collapsed) { n.position=nodes[n.parent].position; continue; }
                        if(grabIndex==i) n.position=Vector3.Lerp(n.position,grabTarget,Mathf.Lerp(.15f,1,grabMovement));
                        Constrain(n,grabIndex==i);
                        Collide(n);
                    }
            }
            Angle=Stretch=Squish=0;
            foreach(var n in nodes)
            {
                if(n.parent>=0 && n.length>1e-6f)
                {
                    float length=(n.position-nodes[n.parent].position).magnitude;
                    float referenceLength=(n.basePosition-nodes[n.parent].basePosition).magnitude;
                    Angle=Mathf.Max(Angle,Vector3.Angle(n.basePosition-nodes[n.parent].basePosition,n.position-nodes[n.parent].position)/180);
                    Stretch=Mathf.Max(Stretch,Mathf.Clamp01((length/Mathf.Max(referenceLength,.00001f)-1)/Mathf.Max(.001f,maxStretch)));
                    Squish=Mathf.Max(Squish,Mathf.Clamp01((1-length/Mathf.Max(referenceLength,.00001f))/Mathf.Max(.001f,maxSquish)));
                }
                if(n.bone==null) continue;
                if(lockAttachmentRotation && n.pinned && (n.depth<attachmentDepth || n.children.Count>1)) { n.bone.localRotation=n.baseLocalRotation; continue; }
                if(n.parent>=0 && !n.pinned && !n.collapsed) n.bone.position=n.position;
                if(n.children.Count==0 || n.children.Count>1 && multiChildType==0) continue;
                Vector3 rest=Vector3.zero, simulated=Vector3.zero; int count=multiChildType==1?1:n.children.Count;
                for(int j=0;j<count;j++) { var c=nodes[n.children[j]]; rest+=(c.basePosition-n.basePosition); simulated+=(c.position-n.position); }
                if(rest.sqrMagnitude>1e-10f && simulated.sqrMagnitude>1e-10f) n.bone.rotation=Quaternion.FromToRotation(rest,simulated)*n.baseRotation;
            }
            lastRoot=Root.position; lastRootRotation=Root.rotation;
        }
        void Constrain(Node n,bool grabbed)
        {
            var parent=nodes[n.parent]; var direction=n.position-parent.position;
            float restLength=n.frameLength;
            if(restLength<1e-7f) { n.position=parent.position; return; }
            var rest=n.frameRest;
            if(direction.sqrMagnitude<1e-12f) direction=rest*restLength;
            float motion=n.frameMotion;
            float stretch=n.frameStretch*(grabbed?1:motion);
            float squish=n.frameSquish*(grabbed?1:motion);
            float length=Mathf.Clamp(direction.magnitude,restLength*(1-squish),restLength*(1+stretch));
            direction=LimitCached(direction.normalized,n);
            n.position=parent.position+direction*length;
        }
        Vector3 LimitCached(Vector3 direction,Node n)
        {
            if(limitType==0) return direction;
            return ApplyLimit(n.frameInverseBasis*direction,n.frameBasis,n.frameAngleX,n.frameAngleZ);
        }
        Vector3 ApplyLimit(Vector3 local,Quaternion basis,float x,float z)
        {
            if(limitType==1) local=Vector3.RotateTowards(Vector3.up,local,x*Mathf.Deg2Rad,0);
            else if(limitType==2) { float angle=Mathf.Clamp(Mathf.Atan2(local.z,local.y)*Mathf.Rad2Deg,-x,x)*Mathf.Deg2Rad; local=new Vector3(0,Mathf.Cos(angle),Mathf.Sin(angle)); }
            else { float pitch=Mathf.Clamp(Mathf.Atan2(local.z,local.y)*Mathf.Rad2Deg,-x,x); float yaw=Mathf.Clamp(Mathf.Atan2(local.x,new Vector2(local.y,local.z).magnitude)*Mathf.Rad2Deg,-z,z); local=Quaternion.Euler(pitch,0,-yaw)*Vector3.up; }
            return (basis*local).normalized;
        }
        public Vector3 Limit(Vector3 direction,Vector3 rest,Quaternion reference,float t)
        {
            if(limitType==0) return direction;
            var euler=new Vector3(Sample(limitRotation.x,limitRotationXCurve,t),Sample(limitRotation.y,limitRotationYCurve,t),Sample(limitRotation.z,limitRotationZCurve,t));
            var axis=Quaternion.Euler(euler)*Vector3.up;
            // Rotate authored local limit axes into the animated direction of this segment.
            var basis=Quaternion.FromToRotation(reference*Vector3.up,rest)*reference*Quaternion.Euler(euler);
            var local=Quaternion.Inverse(basis)*direction;
            float x=Mathf.Clamp(Sample(maxAngleX,maxAngleXCurve,t),0,180), z=Mathf.Clamp(Sample(maxAngleZ,maxAngleZCurve,t),0,180);
            return ApplyLimit(local,basis,x,z);
        }
        void Collide(Node n)
        {
            var parent=nodes[n.parent]; float r=n.frameRadius;
            if(r<=0) return;
            foreach(var shape in collisionShapes)
            {
                var projected=shape.ProjectBone(parent.position,n.position,r);
                if((projected-n.position).sqrMagnitude>1e-12f) { n.position=projected; CollisionCorrections++; }
            }
            if(!collideWorld) return;
            if(probe==null) { var go=new GameObject("AvatarLab collision query"){hideFlags=HideFlags.HideAndDontSave}; probe=go.AddComponent<SphereCollider>(); probe.enabled=false; probe.radius=1; }
            int count=Physics.OverlapSphereNonAlloc(n.position,r,overlaps,~(1<<8),QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var c=overlaps[i]; if(c==null || c==probe || owner!=null && c.transform.IsChildOf(owner.transform)) continue;
                probe.radius=r;
                if(Physics.ComputePenetration(probe,n.position,Quaternion.identity,c,c.transform.position,c.transform.rotation,out var direction,out float distance)) { n.position+=direction*distance; CollisionCorrections++; }
            }
        }
        void PrepareCollisions()
        {
            collisionShapes.Clear(); listedColliders.Clear(); float scale=MaxScale(Root);
            foreach(var n in nodes)
            {
                n.frameRadius=Mathf.Max(0,Sample(radius,radiusCurve,n.t))*scale;
                n.frameCarry=Mathf.Clamp01(Sample(immobile,immobileCurve,n.t));
                if(n.parent<0 || n.pinned || n.collapsed) continue;
                n.frameTension=Mathf.Clamp01(Sample(pull,pullCurve,n.t)); n.frameMomentum=Mathf.Clamp01(Sample(spring,springCurve,n.t));
                n.frameRigidity=integrationType==1?Mathf.Clamp01(Sample(stiffness,stiffnessCurve,n.t)):0;
                n.frameGravity=Sample(gravity,gravityCurve,n.t); n.frameFalloff=Mathf.Clamp01(Sample(gravityFalloff,gravityFalloffCurve,n.t));
                n.frameMotion=Mathf.Clamp01(Sample(stretchMotion,stretchMotionCurve,n.t));
                n.frameStretch=Mathf.Max(0,Sample(maxStretch,maxStretchCurve,n.t)); n.frameSquish=Mathf.Clamp01(Sample(maxSquish,maxSquishCurve,n.t));
                var offset=n.basePosition-nodes[n.parent].basePosition; n.frameLength=offset.magnitude; n.frameRest=offset.normalized;
                if(limitType==0) continue;
                var reference=nodes[n.parent].baseRotation;
                var euler=new Vector3(Sample(limitRotation.x,limitRotationXCurve,n.t),Sample(limitRotation.y,limitRotationYCurve,n.t),Sample(limitRotation.z,limitRotationZCurve,n.t));
                n.frameBasis=Quaternion.FromToRotation(reference*Vector3.up,n.frameRest)*reference*Quaternion.Euler(euler); n.frameInverseBasis=Quaternion.Inverse(n.frameBasis);
                n.frameAngleX=Mathf.Clamp(Sample(maxAngleX,maxAngleXCurve,n.t),0,180); n.frameAngleZ=Mathf.Clamp(Sample(maxAngleZ,maxAngleZCurve,n.t),0,180);
            }
            foreach(var c in colliders) if(c!=null && c.isActiveAndEnabled && listedColliders.Add(c)) collisionShapes.Add(c.CaptureWorld());
            if(allowCollision) foreach(var c in AvatarPhysicsCollider.Active)
                if(c!=null && c.isActiveAndEnabled && c.globalCollision && c.allowSelf && !listedColliders.Contains(c) && c.Owner==owner) collisionShapes.Add(c.CaptureWorld());
        }
        public void DrawBones(Color color)
        {
            Gizmos.color=color;
            for(int i=1;i<nodes.Count;i++) { var n=nodes[i]; Gizmos.DrawLine(nodes[n.parent].position,n.position); Gizmos.DrawWireSphere(n.position,PointRadius(i)); }
        }
        void OnDrawGizmosSelected() { DrawBones(Color.yellow); }
        void OnDisable() { ClearPose(); if(resetWhenDisabled) { Restore(); wasSimulating=false; Angle=Stretch=Squish=0; } }
        void OnDestroy() { if(probe!=null) Destroy(probe.gameObject); }
    }
}
