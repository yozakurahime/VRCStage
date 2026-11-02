using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AvatarLab
{
    [DefaultExecutionOrder(50)]
    public sealed class AvatarLocomotion : MonoBehaviour
    {
        public LabApp app;
        public bool ControlActive, follow = true, animateWalk = true;
        public float speed = 1.8f, runMultiplier = 2, jumpHeight = .7f;
        public Transform Root => root;
        public CharacterController Controller => controller;
        Transform root, model;
        CharacterController controller;
        float vertical, gait, movement,standingHeight;
        public bool Crouched {get;private set;}
        public bool Grounded=>controller!=null && controller.isGrounded;
        public Vector3 Velocity {get;private set;}
        bool requestJump;
        bool savedPhotoPreview,gameCameraSaved;
        Vector3 editFocus;
        float editYaw,editPitch,editDistance;
        public float cameraSensitivity=2.5f, cameraDistance=2.5f;
        float cameraYaw,cameraPitch=15;
        readonly RaycastHit[] cameraHits=new RaycastHit[64];
        readonly Collider[] headroom=new Collider[32];
        readonly Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();
        public void Attach(GameObject avatar)
        {
            Detach(); model = avatar.transform;
            root = new GameObject("Avatar Motor").transform; root.SetPositionAndRotation(model.position, Quaternion.identity);
            model.SetParent(root, true); controller = root.gameObject.AddComponent<CharacterController>();
            float height = Mathf.Clamp(avatar.GetComponent<AvatarDefinition>().eyeHeight * Mathf.Abs(model.lossyScale.y) + .12f, .5f, 4);
            standingHeight=height; controller.height = height; controller.radius = height * .16f; controller.center = Vector3.up * height * .5f;
            controller.stepOffset = Mathf.Min(.3f, height * .25f); controller.skinWidth = .02f; controller.slopeLimit = 45;
            var a = avatar.GetComponent<AvatarDefinition>().animator;
            if (a != null && a.isHuman)
                foreach (var which in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
                { var bone = a.GetBoneTransform(which); if (bone != null) bones[which] = bone; }
            vertical = 0; movement = 0;
        }
        public void Detach()
        {
            SetControl(false); bones.Clear();
            if (model != null) model.SetParent(null, true);
            if (root != null) { root.gameObject.SetActive(false); DestroyImmediate(root.gameObject); }
            model = null; root = null; controller = null;
        }
        public void SetControl(bool active)
        {
            active=active && root!=null;
            if(active && ControlActive) return;
            if(active) app?.poses?.Stop();
            if(active && !app.tracking.VrActive)
            {
                editFocus=app.room.focus; editYaw=app.room.yaw; editPitch=app.room.pitch; editDistance=app.room.distance;
                savedPhotoPreview=app.photo.mainPreview; app.photo.mainPreview=false;
                gameCameraSaved=true;
                cameraYaw=model.eulerAngles.y; cameraPitch=15;
                app.room.focus=model.position+Vector3.up*controller.height*.65f;
            }
            else if(!active && gameCameraSaved)
            {
                app.room.focus=editFocus; app.room.yaw=editYaw; app.room.pitch=editPitch; app.room.distance=editDistance;
                app.photo.mainPreview=savedPhotoPreview;
                gameCameraSaved=false;
            }
            ControlActive = active && root != null;
            app?.driver?.SetGameLocomotion(ControlActive && animateWalk && !app.tracking.VrActive);
            if (!ControlActive) { vertical = 0; app?.driver?.Set("VelocityX", 0); app?.driver?.Set("VelocityZ", 0); app?.driver?.Set("VelocityMagnitude", 0); app?.driver?.Set("VelocityY",0); app?.driver?.Set("AngularY",0); Velocity=Vector3.zero; if(controller!=null) Resize(false); app?.driver?.Set("Upright",Crouched?.6f:1); }
            Cursor.lockState = ControlActive ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !ControlActive;
            if (app != null && app.room != null) app.room.gameLook = ControlActive;
        }
        public CollisionFlags Move(Vector3 displacement)
        {
            if (controller == null || !controller.enabled) return CollisionFlags.None;
            var previous = root.position; var flags = controller.Move(displacement);
            if (app.tracking.VrActive) app.tracking.origin.position += root.position - previous;
            return flags;
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2)) SetControl(!ControlActive);
            if (Input.GetKeyDown(KeyCode.Escape) || !Application.isFocused) SetControl(false);
            movement = 0;
            if (root == null || controller == null) return;
            if (!ControlActive) return;
            var input = Vector2.zero;
            if (ControlActive && Application.isFocused)
            {
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                requestJump = Input.GetKeyDown(KeyCode.Space);
                if (app.tracking.VrActive)
                {
                    var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                    if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis)) input += axis;
                }
            }
            StepInput(input,Input.GetKey(KeyCode.LeftShift),requestJump,Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C),Time.deltaTime);
            requestJump=false;
        }
        void Resize(bool crouch)
        {
            if(controller==null) return;
            if(!crouch && Crouched)
            {
                var bottom=root.position+Vector3.up*(controller.radius+.03f);
                var top=root.position+Vector3.up*(standingHeight-controller.radius);
                int count=Physics.OverlapCapsuleNonAlloc(bottom,top,controller.radius,headroom,~(1<<8),QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++) if(headroom[i]!=controller && headroom[i]!=null && !headroom[i].transform.IsChildOf(model)) { crouch=true; break; }
                if(count==headroom.Length) crouch=true;
            }
            Crouched=crouch; float height=crouch?standingHeight*.6f:standingHeight;
            if(Mathf.Abs(controller.height-height)>.0001f) { controller.height=height; controller.center=Vector3.up*height*.5f; }
        }
        // Same input path for desktop/VR input and deterministic movement validation.
        public void StepInput(Vector2 input,bool run,bool jump,bool crouch,float deltaTime)
        {
            if(root==null || controller==null) return;
            app.driver?.SetGameLocomotion(animateWalk && !app.tracking.VrActive);
            float dt=Mathf.Clamp(deltaTime,.0001f,.05f); input=Vector2.ClampMagnitude(input,1);
            Resize(crouch);
            var view=app.tracking.VrActive?app.tracking.head:app.room.view.transform;
            var forward=Vector3.ProjectOnPlane(view.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward).normalized;
            var direction=forward*input.y+right*input.x;
            float currentSpeed=speed*(Crouched?.5f:run?runMultiplier:1);
            if(controller.isGrounded && vertical<0) vertical=-2;
            if(jump && controller.isGrounded) vertical=Mathf.Sqrt(2*Mathf.Abs(Physics.gravity.y)*jumpHeight);
            vertical+=Physics.gravity.y*dt;
            var previous=root.position; float previousYaw=root.eulerAngles.y;
            var flags=Move((direction*currentSpeed+Vector3.up*vertical)*dt);
            if((flags & CollisionFlags.Above)!=0 && vertical>0) vertical=0;
            if(controller.isGrounded && vertical<0) vertical=-2;
            if(direction.sqrMagnitude>.01f && !app.tracking.VrActive) root.rotation=Quaternion.Slerp(root.rotation,Quaternion.LookRotation(direction),1-Mathf.Exp(-12*dt));
            Velocity=(root.position-previous)/dt; if(controller.isGrounded) Velocity=new Vector3(Velocity.x,0,Velocity.z);
            var local=root.InverseTransformDirection(Velocity); movement=Vector3.ProjectOnPlane(Velocity,Vector3.up).magnitude;
            app.driver?.Set("VelocityX",local.x); app.driver?.Set("VelocityZ",local.z); app.driver?.Set("VelocityY",Velocity.y);
            app.driver?.Set("VelocityMagnitude",Velocity.magnitude); app.driver?.Set("Grounded",controller.isGrounded?1:0);
            app.driver?.Set("Upright",Crouched?.6f:1); app.driver?.Set("AngularY",Mathf.DeltaAngle(previousYaw,root.eulerAngles.y)*Mathf.Deg2Rad/dt);
            gait+=dt*9*movement/1.8f;
        }

        void LateUpdate()
        {
            if(ControlActive && !app.tracking.VrActive)
                StepCamera(new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y")),Input.mouseScrollDelta.y,Time.unscaledDeltaTime);
        }
        public void StepCamera(Vector2 mouse,float zoom,float dt)
        {
            if(model==null || app.tracking.VrActive) return;
            cameraYaw+=mouse.x*cameraSensitivity;
            cameraPitch=Mathf.Clamp(cameraPitch-mouse.y*cameraSensitivity,-30,75);
            cameraDistance=Mathf.Clamp(cameraDistance-zoom*.25f,.45f,10);
            var room=app.room;
            if(follow) room.focus=Vector3.Lerp(room.focus,model.position+Vector3.up*controller.height*.65f,1-Mathf.Exp(-16*Mathf.Clamp(dt,0,.1f)));
            var rotation=Quaternion.Euler(cameraPitch,cameraYaw,0);
            var backwards=rotation*Vector3.back;
            float actual=cameraDistance;
            int count=Physics.SphereCastNonAlloc(room.focus,.12f,backwards,cameraHits,cameraDistance,~(1<<8),QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var hit=cameraHits[i];
                if(hit.collider==controller || hit.collider.transform.IsChildOf(model)) continue;
                actual=Mathf.Min(actual,Mathf.Max(.05f,hit.distance-.08f));
            }
            room.orbit.SetPositionAndRotation(room.focus+backwards*actual,rotation);
        }
        void Swing(HumanBodyBones which, float degrees) { if (bones.TryGetValue(which, out var t)) t.localRotation *= Quaternion.Euler(degrees, 0, 0); }
        public void DrawUI()
        {
            GUILayout.Label("Controle do avatar");
            GUILayout.Label("F2: jogar/editar · WASD: mover · Shift: correr · Espaço: pular · Ctrl/C: agachar · Esc: liberar mouse");
            if (GUILayout.Button(ControlActive ? "Liberar controle" : "Controlar avatar [F2]")) SetControl(!ControlActive);
            speed = StudioGui.Slider("Velocidade (m/s)", speed, .3f, 6); runMultiplier = StudioGui.Slider("Corrida", runMultiplier, 1, 3);
            jumpHeight = StudioGui.Slider("Altura do pulo", jumpHeight, .1f, 2);
            follow = GUILayout.Toggle(follow, "Câmera acompanha o avatar"); animateWalk = GUILayout.Toggle(animateWalk, "Locomoção Humanoid Unity no modo jogo");
            cameraSensitivity=StudioGui.Slider("Sensibilidade do mouse",cameraSensitivity,.3f,6);
            cameraDistance=StudioGui.Slider("Distância da câmera de jogo",cameraDistance,.45f,10);
            GUILayout.Label("Câmera em terceira pessoa: mouse gira, roda aproxima/afasta. Evita paredes com colliders. Esc restaura a câmera de edição / foto; a câmera de transmissão mantém seu enquadramento.");
            GUILayout.Label("No desktop, o modo jogo usa clipes oficiais Unity de repouso, caminhada, corrida, queda e aterrissagem. O controlador Base importado volta ao sair. O agachamento ainda usa a adaptação local. Em VR o tracking corporal tem prioridade. Cenários precisam de colliders.");
            if (root != null)
            {
                if (GUILayout.Button("Voltar à origem")) Teleport(Vector3.zero, Quaternion.identity);
                if (GUILayout.Button("Trazer avatar à frente da câmera")) Teleport(app.room.view.transform.position + Vector3.ProjectOnPlane(app.room.view.transform.forward, Vector3.up).normalized * 2, Quaternion.identity);
            }
        }
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (root == null) return;
            controller.enabled = false; root.SetPositionAndRotation(position, rotation); controller.enabled = true; vertical = 0;
            if (app.solver != null) app.solver.Calibrated = false;
        }
        void OnDestroy() { Detach(); }
    }
}
