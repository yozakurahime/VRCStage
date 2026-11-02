using System;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
namespace AvatarLab
{
    [DefaultExecutionOrder(330)]
    public sealed class PhysicsStudio : MonoBehaviour
    {
        public LabApp app;
        public bool simulate=true,worldCollisions,handCollisions=true,showBones,showColliders;
        public int Selected;
        public AvatarPhysicsChain[] Chains { get; private set; }=Array.Empty<AvatarPhysicsChain>();
        AvatarPhysicsCollider[] colliders=Array.Empty<AvatarPhysicsCollider>();
        AvatarPhysicsCollider[] standards=Array.Empty<AvatarPhysicsCollider>();
        GameObject attached;
        struct Grab { public AvatarPhysicsChain chain; public int point; public float depth; }
        Grab mouse,left,right;
        Material lineMaterial;
        string search="";
        public void Refresh()
        {
            attached=app.currentAvatar; mouse=left=right=default;
            Chains=attached!=null?attached.GetComponentsInChildren<AvatarPhysicsChain>(true):Array.Empty<AvatarPhysicsChain>();
            colliders=attached!=null?attached.GetComponentsInChildren<AvatarPhysicsCollider>(true):Array.Empty<AvatarPhysicsCollider>();
            standards=colliders.Where(c=>c.name.StartsWith("AvatarLab standard ",StringComparison.Ordinal)).ToArray(); Selected=0;
        }
        void Update()
        {
            if(attached!=app.currentAvatar)
            {
                Refresh();
            }
            foreach(var chain in Chains) { chain.simulationEnabled=simulate; chain.collideWorld=worldCollisions; }
            foreach(var c in standards) if(c!=null) c.enabled=handCollisions;
            if(!simulate) { Release(ref mouse); Release(ref left); Release(ref right); return; }
            if(!app.tracking.VrActive)
            {
                if(Input.GetKey(KeyCode.G) && Input.GetMouseButtonDown(0) && Input.mousePosition.x>470)
                {
                    Release(ref mouse); var ray=app.room.view.ScreenPointToRay(Input.mousePosition); float best=.08f;
                    foreach(var chain in Chains) if(chain.isActiveAndEnabled && chain.allowGrabbing)
                        for(int i=1;i<chain.Count;i++)
                        {
                            var p=chain.Point(i); float along=Vector3.Dot(p-ray.origin,ray.direction); if(along<0) continue;
                            float distance=Vector3.Distance(p,ray.GetPoint(along));
                            if(distance<Mathf.Max(best,chain.PointRadius(i))) { best=distance; mouse=new Grab{chain=chain,point=i,depth=app.room.view.WorldToScreenPoint(p).z}; }
                        }
                    if(mouse.chain!=null) mouse.chain.Grab(mouse.point,mouse.chain.Point(mouse.point));
                }
                if(mouse.chain!=null)
                {
                    var m=Input.mousePosition; mouse.chain.MoveGrab(app.room.view.ScreenToWorldPoint(new Vector3(m.x,m.y,mouse.depth)));
                    if(Input.GetKeyDown(KeyCode.P)) mouse.chain.Pose();
                    if(!Input.GetMouseButton(0) || !Application.isFocused) Release(ref mouse);
                }
            }
            else
            {
                Vr(ref left,app.tracking.leftHand.position,app.tracking.LeftTracked,app.tracking.LeftGrip,XRNode.LeftHand);
                Vr(ref right,app.tracking.rightHand.position,app.tracking.RightTracked,app.tracking.RightGrip,XRNode.RightHand);
            }
        }
        static void Release(ref Grab grab) { if(grab.chain!=null) grab.chain.Release(); grab=default; }
        void Vr(ref Grab grab,Vector3 p,bool valid,float grip,XRNode node)
        {
            if(!valid || grip<.4f) { Release(ref grab); return; }
            if(grab.chain==null && grip>.65f)
            {
                float best=.1f;
                foreach(var chain in Chains) if(chain.isActiveAndEnabled && chain.allowGrabbing && !chain.IsGrabbed)
                    for(int i=1;i<chain.Count;i++) { float d=Vector3.Distance(p,chain.Point(i)); if(d<best+chain.PointRadius(i)) { best=d; grab=new Grab{chain=chain,point=i}; } }
                if(grab.chain!=null) grab.chain.Grab(grab.point,p);
            }
            if(grab.chain!=null)
            {
                grab.chain.MoveGrab(p);
                if(InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(CommonUsages.triggerButton,out bool pressed) && pressed) grab.chain.Pose();
            }
        }
        void LateUpdate()
        {
            foreach(var chain in Chains) if(!string.IsNullOrEmpty(chain.parameter))
            {
                app.driver?.Set(chain.parameter+"_IsGrabbed",chain.IsGrabbed?1:0); app.driver?.Set(chain.parameter+"_IsPosed",chain.IsPosed?1:0);
                app.driver?.Set(chain.parameter+"_Angle",chain.Angle); app.driver?.Set(chain.parameter+"_Stretch",chain.Stretch); app.driver?.Set(chain.parameter+"_Squish",chain.Squish);
            }
        }
        public void DrawUI()
        {
            GUILayout.Label("PhysBones convertidos • simulação offline");
            GUILayout.Label(Chains.Length+" cadeias / "+colliders.Length+" colliders ("+standards.Length+" padrão de mãos/dedos)");
            if(Chains.Length==0) GUILayout.Label("Este pacote não contém física convertida. Reexporte usando o exportador 0.4. Pacotes antigos removeram os PhysBones e precisam ser reexportados.");
            simulate=GUILayout.Toggle(simulate,"Simular cadeias importadas");
            handCollisions=GUILayout.Toggle(handCollisions,"Colisão com mãos e dedos do avatar");
            worldCollisions=GUILayout.Toggle(worldCollisions,"Colisão adicional com colliders Unity do cenário");
            showBones=GUILayout.Toggle(showBones,"Desenhar cadeias na câmera de edição"); showColliders=GUILayout.Toggle(showColliders,"Desenhar colliders na câmera de edição");
            if(GUILayout.Button("Resetar simulação / soltar poses")) foreach(var c in Chains) c.ResetSimulation();
            if(GUILayout.Button("Aplicar impulso de teste")) foreach(var c in Chains) c.ApplyImpulse(new Vector3(.5f,.1f,0));
            app.hair?.DrawUI();
            GUILayout.Label("Desktop: segure G e arraste com botão esquerdo sobre a cadeia. P durante o arraste fixa a pose quando permitido. VR: grip perto do osso agarra; trigger fixa a pose. Os controles de permissão do componente importado são respeitados.");
            GUILayout.Label("Forças/limites/colisão usam solver próprio. Curvas, multi-child e stretch são aproximados; ainda precisam de comparação com o cliente VRChat. Não há sincronização multiplayer.");
            search=GUILayout.TextField(search);
            for(int i=0;i<Chains.Length;i++) if(string.IsNullOrEmpty(search) || Chains[i].name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)
                if(GUILayout.Button((Selected==i?"● ":"")+Chains[i].name+" / "+Chains[i].Count+" pontos")) Selected=i;
            if(Chains.Length>0)
            {
                Selected=Mathf.Clamp(Selected,0,Chains.Length-1); var c=Chains[Selected]; GUILayout.Space(8); GUILayout.Label(c.name+" • "+c.Diagnostic);
                c.enabled=GUILayout.Toggle(c.enabled,"Cadeia habilitada"); c.allowGrabbing=GUILayout.Toggle(c.allowGrabbing,"Permitir agarrar localmente"); c.allowPosing=GUILayout.Toggle(c.allowPosing,"Permitir fixar pose localmente");
                c.pull=StudioGui.Slider("Pull",c.pull,0,1); c.spring=StudioGui.Slider("Spring / momentum",c.spring,0,1); c.stiffness=StudioGui.Slider("Stiffness (advanced)",c.stiffness,0,1); c.gravity=StudioGui.Slider("Gravity",c.gravity,-1,1); c.radius=StudioGui.Slider("Radius (m locais)",c.radius,0,.2f);
                GUILayout.Label("Correções de colisão neste frame: "+c.CollisionCorrections+" / angle "+c.Angle.ToString("0.00"));
                if(GUILayout.Button("Recriar cadeia selecionada")) c.Rebuild();
                GUILayout.Label("Ajustes deste painel são temporários. Configure no projeto Unity e reexporte para guardar os valores.");
            }
        }
        void OnRenderObject()
        {
            if((!showBones && !showColliders) || app==null || app.room==null || Camera.current!=app.room.view) return;
            if(lineMaterial==null) { lineMaterial=new Material(Shader.Find("Hidden/Internal-Colored")){hideFlags=HideFlags.HideAndDontSave}; lineMaterial.SetInt("_ZWrite",0); lineMaterial.SetInt("_ZTest",(int)UnityEngine.Rendering.CompareFunction.Always); lineMaterial.SetInt("_Cull",0); }
            lineMaterial.SetPass(0); GL.PushMatrix(); GL.MultMatrix(Matrix4x4.identity); GL.Begin(GL.LINES);
            if(showBones) foreach(var chain in Chains) for(int i=1;i<chain.Count;i++) { GL.Color(chain.IsGrabbed?Color.magenta:Color.yellow); Cross(chain.Point(i),Mathf.Max(.003f,chain.PointRadius(i))); Line(chain.ParentPoint(i),chain.Point(i)); }
            if(showColliders) foreach(var c in colliders) if(c!=null && c.isActiveAndEnabled)
            {
                GL.Color(c.insideBounds?Color.cyan:Color.green);
                if(c.shapeType==2) { var x=c.Orientation*Vector3.right*.15f; var y=c.Orientation*Vector3.up*.15f; Line(c.Center-x-y,c.Center+x-y); Line(c.Center+x-y,c.Center+x+y); Line(c.Center+x+y,c.Center-x+y); Line(c.Center-x+y,c.Center-x-y); Line(c.Center,c.Center+c.Orientation*Vector3.forward*.1f); }
                else { var a=c.shapeType==1?c.Center-c.Axis*c.HalfSegment:c.Center; var b=c.shapeType==1?c.Center+c.Axis*c.HalfSegment:c.Center; Circle(a,c.Orientation*Vector3.right,c.Axis,c.Radius); Circle(a,c.Orientation*Vector3.forward,c.Axis,c.Radius); Circle(a,c.Orientation*Vector3.right,c.Orientation*Vector3.forward,c.Radius); if(c.shapeType==1) { Circle(b,c.Orientation*Vector3.right,c.Orientation*Vector3.forward,c.Radius); for(int j=0;j<4;j++) { var d=c.Orientation*Quaternion.Euler(0,j*90,0)*Vector3.right*c.Radius; Line(a+d,b+d); } } }
            }
            GL.End(); GL.PopMatrix();
        }
        static void Line(Vector3 a,Vector3 b) { GL.Vertex(a); GL.Vertex(b); }
        static void Cross(Vector3 p,float r) { Line(p-Vector3.right*r,p+Vector3.right*r); Line(p-Vector3.up*r,p+Vector3.up*r); Line(p-Vector3.forward*r,p+Vector3.forward*r); }
        static void Circle(Vector3 p,Vector3 x,Vector3 y,float radius) { for(int i=0;i<24;i++) { float a=i*Mathf.PI*2/24,b=(i+1)*Mathf.PI*2/24; Line(p+(x*Mathf.Cos(a)+y*Mathf.Sin(a))*radius,p+(x*Mathf.Cos(b)+y*Mathf.Sin(b))*radius); } }
        void OnDestroy() { Release(ref mouse); Release(ref left); Release(ref right); if(lineMaterial!=null) Destroy(lineMaterial); }
    }
}
