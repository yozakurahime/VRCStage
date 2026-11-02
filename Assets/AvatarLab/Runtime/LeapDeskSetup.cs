using System;
using System.IO;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class LeapDeskPose
    {
        public int version=2;
        public bool followHeight=true;
        public float referenceHipHeight, referenceScale=1;
        public Vector3 position=new Vector3(0,.85f,.35f), euler=new Vector3(0,180,0);
        public float scale=1;
        public bool show=true, edit;
    }
    // Sensor extrinsics in metres, relative to the actor's origin and rotation, without rig scale.
    public sealed class LeapDeskSetup : MonoBehaviour
    {
        public OpticalHands optical;
        public LeapDeskPose pose=new LeapDeskPose();
        public string Status="";
        string profile;
        bool dirty, dragging;
        float changedAt, mouseY;
        Vector3 dragStart, hitStart;
        Plane dragPlane;
        Material lines;
        Transform referenceActor;
        float initialHipHeight, initialScale;
        LabApp App=>optical.app;
        float HipHeight()
        {
            var a=App?.driver?.Definition.animator;
            var hip=a!=null && a.isHuman?a.GetBoneTransform(HumanBodyBones.Hips):null;
            return hip!=null?App.currentAvatar.transform.InverseTransformPoint(hip.position).y:0;
        }
        public float HeightOffset=>pose.followHeight?(HipHeight()-pose.referenceHipHeight)*pose.referenceScale:0;
        public Vector3 Position=>App.currentAvatar.transform.position+App.currentAvatar.transform.rotation*(pose.position+Vector3.up*HeightOffset);
        public Quaternion Rotation=>App.currentAvatar.transform.rotation*Quaternion.Euler(pose.euler);
        public static Vector3 TransformPoint(Vector3 origin,Quaternion rotation,float scale,Vector3 point)=>origin+rotation*(point*scale);
        public Vector3 Map(Vector3 point)=>TransformPoint(Position,Rotation,pose.scale,point);
        public Vector3 LocalFromWorld(Vector3 point)=>Quaternion.Inverse(App.currentAvatar.transform.rotation)*(point-App.currentAvatar.transform.position)-Vector3.up*HeightOffset;
        public void Attach(string avatarId)
        {
            if(dirty) Save();
            string folder=Path.Combine(Application.persistentDataPath,"LeapDesk");
            string id=Path.GetFileName(avatarId??"default");
            profile=Path.Combine(folder,string.IsNullOrEmpty(id)?"default.json":id+".json");
            if(referenceActor!=App.currentAvatar.transform)
            {
                referenceActor=App.currentAvatar.transform;
                initialHipHeight=HipHeight(); initialScale=Mathf.Abs(referenceActor.lossyScale.y);
            }
            Defaults();
            if(File.Exists(profile)) try
            {
                var saved=JsonUtility.FromJson<LeapDeskPose>(File.ReadAllText(profile));
                if(Valid(saved))
                {
                    bool migrate=saved.version==1;
                    if(migrate) { saved.version=2; saved.followHeight=true; saved.referenceHipHeight=initialHipHeight; saved.referenceScale=initialScale; }
                    pose=saved; if(migrate) Save(); Status="Posição da mesa carregada para este avatar.";
                }
                else Status="Perfil de mesa inválido; usando posição inicial.";
            }
            catch(Exception ex) { Status="Não foi possível ler a mesa: "+ex.Message; }
            dirty=false; dragging=false;
        }
        public static bool Valid(LeapDeskPose p)=>p!=null && (p.version==1 || p.version==2) && Finite(p.position) && p.position.sqrMagnitude<100 && Finite(p.euler) && p.euler.sqrMagnitude<10000000 && !float.IsNaN(p.scale) && !float.IsInfinity(p.scale) && p.scale>=.2f && p.scale<=3 && (p.version==1 || Finite(new Vector3(p.referenceHipHeight,p.referenceScale,0)) && p.referenceScale>0 && p.referenceScale<100);
        static bool Finite(Vector3 v)=>!float.IsNaN(v.x+v.y+v.z) && !float.IsInfinity(v.x+v.y+v.z);
        public void Defaults()
        {
            pose=new LeapDeskPose {referenceHipHeight=initialHipHeight,referenceScale=initialScale>0?initialScale:1};
            var a=App?.driver?.Definition.animator;
            if(a!=null && a.isHuman)
            {
                var left=a.GetBoneTransform(HumanBodyBones.LeftUpperArm); var right=a.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if(left!=null && right!=null) pose.position.y=Mathf.Clamp(LocalFromWorld((left.position+right.position)*.5f).y-.45f,.25f,2);
            }
            Status="Posição inicial para braços dobrados. Ajuste para sua mesa.";
        }
        public bool AlignGuide(OpticalHand left,OpticalHand right,Animator a)
        {
            if(a==null || !a.isHuman || left==null || right==null) return false;
            var l=a.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r=a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lh=a.GetBoneTransform(HumanBodyBones.LeftHand); var rh=a.GetBoneTransform(HumanBodyBones.RightHand);
            if(l==null || r==null || lh==null || rh==null) return false;
            var rawSide=Vector3.ProjectOnPlane(right.joints[0]-left.joints[0],Vector3.up);
            var bodySide=Vector3.ProjectOnPlane(Quaternion.Inverse(App.currentAvatar.transform.rotation)*(r.position-l.position),Vector3.up);
            if(rawSide.magnitude<.08f || bodySide.sqrMagnitude<.001f) { Status="Separe as duas mãos, sem cruzar os braços."; return false; }
            float yaw=Vector3.SignedAngle(rawSide,bodySide,Vector3.up);
            pose.euler=new Vector3(0,yaw,0);
            Vector3 shoulders=LocalFromWorld((l.position+r.position)*.5f);
            // A reproducible reference pose, rather than fitting to the avatar's lowered wrists.
            Vector3 target=shoulders+new Vector3(0,-.22f,.30f);
            pose.position=target-Quaternion.Euler(pose.euler)*((left.joints[0]+right.joints[0])*.5f*pose.scale);
            Changed(); Status="Pose guia alinhada. Refine a altura, distância e rotação para coincidir com sua mesa.";
            return true;
        }
        public void Changed() { dirty=true; changedAt=Time.unscaledTime; }
        public void Save()
        {
            if(string.IsNullOrEmpty(profile) || !Valid(pose)) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(profile)); File.WriteAllText(profile,JsonUtility.ToJson(pose,true)); dirty=false; }
            catch(Exception ex) { Status="Erro ao salvar mesa: "+ex.Message; }
        }
        void Update()
        {
            if(optical==null || App==null || App.currentAvatar==null || optical.source!=0 || optical.mounted) return;
            if(dirty && !dragging && Time.unscaledTime-changedAt>.5f) Save();
            if(!pose.edit || !pose.show || App.motor.ControlActive || App.tracking.VrActive) { dragging=false; return; }
            var view=App.room.view;
            float panel=440*Mathf.Clamp(Screen.height/900f,.7f,1.5f);
            if(Input.GetMouseButtonDown(0) && Input.mousePosition.x>panel)
            {
                var screen=view.WorldToScreenPoint(Position);
                if(screen.z>0 && Vector2.Distance(new Vector2(screen.x,screen.y),Input.mousePosition)<22)
                {
                    dragPlane=new Plane(Vector3.up,Position);
                    if(dragPlane.Raycast(view.ScreenPointToRay(Input.mousePosition),out float d))
                    { dragging=true; dragStart=pose.position; mouseY=Input.mousePosition.y; hitStart=view.ScreenPointToRay(Input.mousePosition).GetPoint(d); }
                }
            }
            if(dragging)
            {
                if(!Input.GetMouseButton(0) || !Application.isFocused) { dragging=false; Save(); return; }
                if(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    float metres=2*Vector3.Distance(view.transform.position,Position)*Mathf.Tan(view.fieldOfView*.5f*Mathf.Deg2Rad)/Screen.height;
                    pose.position=dragStart+Vector3.up*((Input.mousePosition.y-mouseY)*metres);
                }
                else if(dragPlane.Raycast(view.ScreenPointToRay(Input.mousePosition),out float d))
                    pose.position=dragStart+Quaternion.Inverse(App.currentAvatar.transform.rotation)*(view.ScreenPointToRay(Input.mousePosition).GetPoint(d)-hitStart);
                Changed();
            }
        }
        public void DrawUI()
        {
            GUILayout.Label("LEAP NA MESA • posição em relação ao personagem");
            GUILayout.Label("Metros: X lateral, Y altura de referência em pé, Z à frente. Sem herdar a escala do rig.");
            var before=JsonUtility.ToJson(pose);
            pose.followHeight=GUILayout.Toggle(pose.followHeight,"Acompanhar altura do corpo ao sentar / agachar");
            pose.show=GUILayout.Toggle(pose.show,"Mostrar sensor, mesa e mãos na câmera de edição");
            pose.edit=GUILayout.Toggle(pose.edit,"Arrastar o sensor na cena (Shift: altura)");
            GUILayout.Label("Posição do sensor (m)");
            pose.position.x=StudioGui.Number("Lateral X",pose.position.x,-3,3,.01f);
            pose.position.y=StudioGui.Number("Altura Y",pose.position.y,-1,3,.01f);
            pose.position.z=StudioGui.Number("Frente Z",pose.position.z,-3,3,.01f);
            GUILayout.Label("Rotação do sensor (graus)");
            pose.euler.x=StudioGui.Number("Inclinar X",pose.euler.x,-180,180,5);
            pose.euler.y=StudioGui.Number("Girar Y",pose.euler.y,-180,180,5);
            pose.euler.z=StudioGui.Number("Inclinar Z",pose.euler.z,-180,180,5);
            if(GUILayout.Button("Girar sensor 180°")) pose.euler.y=Mathf.DeltaAngle(0,pose.euler.y+180);
            pose.scale=StudioGui.Slider("Escala dos movimentos",pose.scale,.2f,3);
            if(before!=JsonUtility.ToJson(pose)) Changed();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Salvar mesa")) { Save(); Status="Posição da mesa salva para este avatar."; }
            if(GUILayout.Button("Posição inicial")) { Defaults(); Changed(); }
            GUILayout.EndHorizontal();
            GUILayout.Label(Status);
            GUILayout.Label("A pose guia é uma referência de braços dobrados. O Leap não mede a localização do tronco: refine os controles para reproduzir sua postura real. O cotovelo continua estimado.");
        }
        void OnRenderObject()
        {
            if(optical==null || App==null || App.currentAvatar==null || optical.source!=0 || optical.mounted || !pose.show || Camera.current!=App.room.view) return;
            if(lines==null) { var shader=Shader.Find("Hidden/Internal-Colored"); if(shader==null) return; lines=new Material(shader){hideFlags=HideFlags.HideAndDontSave}; lines.SetInt("_ZWrite",0); lines.SetInt("_ZTest",(int)UnityEngine.Rendering.CompareFunction.Always); lines.SetInt("_Cull",0); }
            lines.SetPass(0); GL.PushMatrix(); GL.MultMatrix(Matrix4x4.identity); GL.Begin(GL.LINES);
            var center=Position; var rotation=Rotation;
            GL.Color(Color.cyan); Box(center,rotation,new Vector3(.08f,.015f,.03f));
            GL.Color(new Color(.25f,.65f,.75f)); Box(center-Vector3.up*.025f,App.currentAvatar.transform.rotation,new Vector3(.6f,.005f,.32f));
            GL.Color(Color.red); Line(center,center+rotation*Vector3.right*.18f);
            GL.Color(Color.green); Line(center,center+rotation*Vector3.up*.25f);
            GL.Color(Color.blue); Line(center,center+rotation*Vector3.forward*.18f);
            for(int side=0;side<2;side++) if(optical.TryGetHand(side,out var h))
            {
                GL.Color(side==0?Color.yellow:Color.magenta);
                var wrist=Map(h.joints[0]); Cross(wrist,.015f);
                for(int finger=0;finger<5;finger++) { int start=1+finger*4; Line(wrist,Map(h.joints[start])); for(int j=0;j<3;j++) Line(Map(h.joints[start+j]),Map(h.joints[start+j+1])); }
                if(h.elbowValid) Line(wrist,Map(h.elbow));
            }
            GL.End(); GL.PopMatrix();
        }
        static void Line(Vector3 a,Vector3 b) { GL.Vertex(a); GL.Vertex(b); }
        static void Cross(Vector3 p,float s) { Line(p-Vector3.right*s,p+Vector3.right*s); Line(p-Vector3.up*s,p+Vector3.up*s); Line(p-Vector3.forward*s,p+Vector3.forward*s); }
        static void Box(Vector3 p,Quaternion q,Vector3 half)
        {
            var v=new Vector3[8]; for(int i=0;i<8;i++) v[i]=p+q*Vector3.Scale(half,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            for(int i=0;i<8;i++) for(int bit=1;bit<=4;bit*=2) if((i&bit)==0) Line(v[i],v[i|bit]);
        }
        void OnDestroy() { if(dirty) Save(); if(lines!=null) Destroy(lines); }
    }
}
