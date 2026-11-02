using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    [DefaultExecutionOrder(75)]
    public sealed class PoseStudio : MonoBehaviour
    {
        [Serializable] public struct BonePose {public string path;public Quaternion rotation;}
        [Serializable] public sealed class SavedPose
        {
            public int format=2; public string id,name,avatarId;
            public Vector3 bodyLocal; public Quaternion bodyRotation=Quaternion.identity; public float[] muscles;
            public Vector3 hipsLocal,boneBodyLocal;public BonePose[] bones;
        }
        public sealed class Item { public string id,label,category,motionPath; public bool pose,plantFeet; public AnimationClip clip; public SavedPose saved; public RgbMotion motion; }
        public LabApp app;
        public readonly List<Item> Items=new List<Item>();
        public string Status="Escolha uma pose ou animação.";
        public Item Selected {get;private set;}
        public bool Playing {get;private set;}
        public bool loop=true,lockTracking=false;
        public float speed=1;
        public float TimePosition {get;private set;}
        public string PoseDirectory=>Path.Combine(Application.persistentDataPath,"Poses");
        public SavedPose Draft {get;private set;}
        public float Duration=>rgbSampler!=null?rgbDuration:Selected?.clip!=null?Mathf.Max(.001f,Selected.clip.length):1;
        RgbMotionSampler rgbSampler;float rgbDuration;Vector3 neutralBody;
        Transform leftFoot,rightFoot,leftToes,rightToes,hips; float floorLocalHeight; LibraryPoseContacts contacts;
        AvatarDriver attached; Animator animator; HumanPoseHandler handler; HumanPose working; PoseSampler sampler;
        readonly Dictionary<string,Transform> poseBones=new Dictionary<string,Transform>();
        readonly Dictionary<Transform,Quaternion> editedRotations=new Dictionary<Transform,Quaternion>();
        readonly HashSet<int> editedMuscles=new HashSet<int>();Transform[] muscleBones;
        bool startupPreviewUsed;
        string search="",poseName="Minha pose"; int category,page,editGroup;
        static readonly string[] MuscleNames=HumanTrait.MuscleName;
        LocalPosePacks localPacks;
        readonly string[] categories={"Todas","Poses","Animações","Do avatar","Minhas poses","Mocap / vídeo","GoGo Loco"};
        readonly string[] editGroups={"Braços","Pernas","Tronco","Cabeça"};
        void Update()
        {
            if(attached!=app.driver) Attach();
            Advance(Time.unscaledDeltaTime);
        }
        void Attach()
        {
            Stop(); handler?.Dispose(); handler=null; sampler?.Dispose(); sampler=null; attached=app.driver; animator=attached?.Definition.animator; Items.Clear();
            if(animator==null || !animator.isHuman) { Status="A biblioteca de poses requer um avatar Humanoid."; return; }
            leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            leftToes=animator.GetBoneTransform(HumanBodyBones.LeftToes);rightToes=animator.GetBoneTransform(HumanBodyBones.RightToes);
            floorLocalHeight=ContactHeight();contacts=new LibraryPoseContacts(animator);
            poseBones.Clear();var needed=new HashSet<Transform>();
            for(int i=0;i<(int)HumanBodyBones.LastBone;i++)
            {
                if(i==(int)HumanBodyBones.Jaw || i==(int)HumanBodyBones.LeftEye || i==(int)HumanBodyBones.RightEye) continue;
                for(var bone=animator.GetBoneTransform((HumanBodyBones)i);bone!=null && bone!=animator.transform;bone=bone.parent) needed.Add(bone);
            }
            foreach(var bone in needed) poseBones[BonePath(bone,animator.transform)]=bone;
            muscleBones=new Transform[HumanTrait.MuscleCount];for(int i=0;i<muscleBones.Length;i++) {int bone=HumanTrait.BoneFromMuscle(i);if(bone>=0 && bone<(int)HumanBodyBones.LastBone) muscleBones[i]=animator.GetBoneTransform((HumanBodyBones)bone);}
            handler=new HumanPoseHandler(animator.avatar,animator.transform); sampler=new PoseSampler(animator); working=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
            handler.GetHumanPose(ref working);neutralBody=animator.transform.InverseTransformPoint(working.bodyPosition*animator.humanScale)/animator.humanScale;
            if(localPacks==null) localPacks=new LocalPosePacks();
            if(app.motionLibrary!=null) foreach(var p in app.motionLibrary.presets) if(p.clip!=null && !(localPacks.entries.Count>0 && p.category=="Poses")) Items.Add(new Item{id=p.id,label=p.label,category=p.category,pose=p.pose,plantFeet=p.plantFeet,clip=p.clip});
            foreach(var p in localPacks.entries) Items.Add(new Item{id="gogo:"+p.id,label=p.label,category="GoGo Loco",pose=!p.animated,plantFeet=true,clip=p.clip});
            foreach(var clip in attached.AvatarMotionClips().OrderBy(c=>c.name,StringComparer.OrdinalIgnoreCase))
                if(!Items.Any(i=>i.clip==clip)) Items.Add(new Item{id="avatar:"+clip.GetInstanceID(),label=clip.name,category="Do avatar",clip=clip});
            LoadSaved();app.mocap?.RefreshItems(); page=0; Status="Biblioteca pronta: "+Items.Count+" itens. Poses e animações são aplicadas no lugar.";
            if(!startupPreviewUsed)
            {
                startupPreviewUsed=true; var args=Environment.GetCommandLineArgs(); int at=Array.IndexOf(args,"--preview-pose");
                if(at>=0 && at+1<args.Length) {var item=Items.FirstOrDefault(i=>i.id==args[at+1]);if(item!=null) Play(item);}
            }
        }
        public void Refresh() { Attach(); }
        public void Advance(float dt)
        {
            if(attached==null || Selected==null || Draft!=null) return;
            attached.LibraryLocksTracking=lockTracking;
            if(Playing && !Selected.pose)
            {
                float next=TimePosition+Mathf.Max(0,dt)*speed;
                if(loop) TimePosition=Mathf.Repeat(next,Duration);
                else { TimePosition=Mathf.Min(next,Duration); if(next>=Duration) Playing=false; }
            }

        }
        public bool Play(Item item)
        {
            if(item==null || handler==null || attached==null) return false;
            if(item.motionPath!=null && item.motion==null) {app.mocap.RequestPlay(item);return true;}
            RgbMotionSampler incoming=null;
            if(item.motion!=null) {incoming=new RgbMotionSampler(item.motion);rgbDuration=item.motion.Duration;item.motion=null;}
            Stop(); app.motor.SetControl(false); attached.LibraryLocksTracking=lockTracking; Selected=item; TimePosition=0;
            if(incoming!=null) {rgbSampler=incoming;attached.HoldLibraryPose();Playing=true;}
            else if(item.saved!=null) { Draft=Clone(item.saved); attached.HoldLibraryPose(); }
            else { sampler.Play(item.clip); attached.HoldLibraryPose(); Playing=!item.pose; }
            Status=item.label+(item.pose?" • pose fixa":" • reprodução no lugar"); return true;
        }
        public void Pause(bool pause) { if(Selected!=null && Draft==null && !Selected.pose) Playing=!pause; }
        public void Seek(float time) { TimePosition=Mathf.Clamp(time,0,Duration); }
        public void Stop()
        {
            app?.faces?.ClearPlaybackFace();
            app?.mocap?.CancelPendingPlay();attached?.StopLibraryClip(); sampler?.Stop(); rgbSampler?.Dispose();rgbSampler=null;editedMuscles.Clear(); Selected=null; Draft=null; Playing=false; TimePosition=0; Status="Pose normal do avatar restaurada.";
        }
        public SavedPose Capture(string name)
        {
            if(handler==null) throw new InvalidOperationException("Carregue um Humanoid.");
            var pose=new HumanPose(); handler.GetHumanPose(ref pose);
            var local=animator.transform.InverseTransformPoint(pose.bodyPosition*animator.humanScale);
            return new SavedPose{id=Guid.NewGuid().ToString("N"),name=string.IsNullOrWhiteSpace(name)?"Minha pose":name.Trim().Substring(0,Math.Min(80,name.Trim().Length)),avatarId=app.AvatarId,
                bodyLocal=local,boneBodyLocal=local,hipsLocal=hips.localPosition,bodyRotation=Quaternion.Inverse(animator.transform.rotation)*pose.bodyRotation,muscles=(float[])pose.muscles.Clone(),
                bones=poseBones.Select(p=>new BonePose{path=p.Key,rotation=p.Value.localRotation}).ToArray()};
        }
        public SavedPose BeginEdit()
        {
            var captured=Capture(poseName); Stop(); app.motor.SetControl(false); Draft=captured; attached.HoldLibraryPose(); attached.LibraryLocksTracking=lockTracking;
            Selected=new Item{id="draft",label="Pose em edição",pose=true}; Status="Edite a postura e salve como uma nova pose."; return Draft;
        }
        public string SaveCurrent(string name)
        {
            if(Selected!=null) LateUpdate();
            var pose=Capture(name); pose.id=Guid.NewGuid().ToString("N"); pose.name=string.IsNullOrWhiteSpace(name)?"Minha pose":name.Trim().Substring(0,Math.Min(80,name.Trim().Length)); pose.avatarId=app.AvatarId;
            if(!Valid(pose)) throw new InvalidOperationException("Não foi possível salvar: a pose contém valores inválidos.");
            Directory.CreateDirectory(PoseDirectory); string path=Path.Combine(PoseDirectory,pose.id+".json"); File.WriteAllText(path,JsonUtility.ToJson(pose,true));
            Items.Add(new Item{id=pose.id,label=pose.name,category="Minhas poses",pose=true,saved=pose}); Status="Pose salva: "+pose.name; return path;
        }
        static SavedPose Clone(SavedPose pose)=>new SavedPose{format=pose.format,id=pose.id,name=pose.name,avatarId=pose.avatarId,bodyLocal=pose.bodyLocal,boneBodyLocal=pose.boneBodyLocal,hipsLocal=pose.hipsLocal,bodyRotation=pose.bodyRotation,muscles=(float[])pose.muscles.Clone(),bones=pose.bones==null?null:(BonePose[])pose.bones.Clone()};
        static string BonePath(Transform bone,Transform root) {string path=bone.name;while(bone.parent!=root && bone.parent!=null) {bone=bone.parent;path=bone.name+"/"+path;}return path;}
        public void SetMuscle(int index,float value) {if(Draft==null || index<0 || index>=Draft.muscles.Length) return;Draft.muscles[index]=value;editedMuscles.Add(index);}
        void ApplyBonePose()
        {
            foreach(var bone in Draft.bones) if(poseBones.TryGetValue(bone.path,out var target)) target.localRotation=bone.rotation;
            hips.localPosition=Draft.hipsLocal;hips.position+=animator.transform.TransformVector(Draft.bodyLocal-Draft.boneBodyLocal);
        }
        static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        static bool Valid(SavedPose pose)
        {
            return pose!=null && (pose.format==1 || pose.format==2) && pose.muscles!=null && pose.muscles.Length==HumanTrait.MuscleCount && pose.muscles.All(v=>Finite(v) && (pose.format==2 || Mathf.Abs(v)<=2))
                && (pose.format==1 || pose.bones!=null && pose.bones.Length>0 && pose.bones.Length<=512 && pose.bones.All(b=>!string.IsNullOrEmpty(b.path) && Finite(b.rotation.x+b.rotation.y+b.rotation.z+b.rotation.w) && Mathf.Abs(Quaternion.Dot(b.rotation,b.rotation)-1)<.05f))
                && Finite(pose.bodyLocal.x+pose.bodyLocal.y+pose.bodyLocal.z) && pose.bodyLocal.sqrMagnitude<25
                && Finite(pose.bodyRotation.x+pose.bodyRotation.y+pose.bodyRotation.z+pose.bodyRotation.w) && Mathf.Abs(1-(pose.bodyRotation.x*pose.bodyRotation.x+pose.bodyRotation.y*pose.bodyRotation.y+pose.bodyRotation.z*pose.bodyRotation.z+pose.bodyRotation.w*pose.bodyRotation.w))<.05f;
        }
        public void LoadSaved()
        {
            Items.RemoveAll(i=>i.saved!=null); if(!Directory.Exists(PoseDirectory)) return;
            foreach(var path in Directory.GetFiles(PoseDirectory,"*.json"))
                try { var pose=JsonUtility.FromJson<SavedPose>(File.ReadAllText(path)); if(Valid(pose) && pose.avatarId==app.AvatarId) Items.Add(new Item{id=pose.id,label=pose.name??"Pose salva",category="Minhas poses",pose=true,saved=pose}); } catch(Exception ex) { Debug.LogWarning("Pose ignorada: "+Path.GetFileName(path)+" / "+ex.Message); }
        }
        void LateUpdate()
        {
            if(Selected==null || handler==null || attached==null) return;
            attached.LibraryLocksTracking=lockTracking;
            if(rgbSampler!=null)
            {
                if(app.capture.playRecordedFace) rgbSampler.SampleFace(TimePosition,app.faces);else app.faces.ClearPlaybackFace();
                var rgb=rgbSampler.Sample(TimePosition,app.mocap.travel);
                handler.GetHumanPose(ref working);
                for(int i=0;i<working.muscles.Length;i++) if(!MuscleNames[i].Contains("Eye") && !MuscleNames[i].Contains("Jaw")) working.muscles[i]=rgb.muscles[i];
                working.bodyPosition=rgb.bodyPosition+new Vector3(neutralBody.x-rgbSampler.NeutralBody.x,floorLocalHeight/animator.humanScale-rgbSampler.NeutralFoot,neutralBody.z-rgbSampler.NeutralBody.z);
                working.bodyRotation=rgb.bodyRotation;handler.SetHumanPose(ref working);return;
            }
            if(Draft?.bones!=null)
            {
                ApplyBonePose();
                if(editedMuscles.Count>0)
                {
                    handler.GetHumanPose(ref working);foreach(int i in editedMuscles) working.muscles[i]=Draft.muscles[i];
                    working.bodyPosition=Draft.bodyLocal/animator.humanScale;working.bodyRotation=Draft.bodyRotation;handler.SetHumanPose(ref working);
                    editedRotations.Clear();foreach(int i in editedMuscles) if(muscleBones[i]!=null) editedRotations[muscleBones[i]]=muscleBones[i].localRotation;
                    ApplyBonePose();foreach(var bone in editedRotations) bone.Key.localRotation=bone.Value;
                }
                return;
            }
            handler.GetHumanPose(ref working);
            var sample=Draft!=null?default(HumanPose):sampler.Sample(TimePosition);
            var muscles=Draft!=null?Draft.muscles:sample.muscles;
            for(int i=0;i<working.muscles.Length;i++) if(!MuscleNames[i].Contains("Eye") && !MuscleNames[i].Contains("Jaw")) working.muscles[i]=muscles[i];
            // GetHumanPose returns world COM; SetHumanPose expects root-local COM.
            working.bodyPosition=Draft!=null?Draft.bodyLocal/animator.humanScale:sampler.Root.InverseTransformPoint(sample.bodyPosition*sampler.Animator.humanScale)/animator.humanScale;
            working.bodyRotation=Draft!=null?Draft.bodyRotation:Quaternion.Inverse(sampler.Root.rotation)*sample.bodyRotation; handler.SetHumanPose(ref working);
            if(Draft==null && Selected.plantFeet)
            {
                float height=ContactHeight();
                hips.position+=animator.transform.TransformVector(Vector3.up*(floorLocalHeight-height));
                contacts.Apply(Selected.id,TimePosition/Duration);
            }
        }
        float ContactHeight()
        {
            float height=Mathf.Min(animator.transform.InverseTransformPoint(leftFoot.position).y,animator.transform.InverseTransformPoint(rightFoot.position).y);
            if(leftToes!=null) height=Mathf.Min(height,animator.transform.InverseTransformPoint(leftToes.position).y);
            if(rightToes!=null) height=Mathf.Min(height,animator.transform.InverseTransformPoint(rightToes.position).y);
            if(Selected!=null && Selected.category=="GoGo Loco")
                foreach(var part in new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg})
                {var bone=animator.GetBoneTransform(part);if(bone!=null) height=Mathf.Min(height,animator.transform.InverseTransformPoint(bone.position).y);}
            return height;
        }
        static readonly string[,] ArmControls={{"Left Arm Down-Up","E: altura do braço"},{"Right Arm Down-Up","D: altura do braço"},{"Left Arm Front-Back","E: braço frente/trás"},{"Right Arm Front-Back","D: braço frente/trás"},{"Left Forearm Stretch","E: dobrar cotovelo"},{"Right Forearm Stretch","D: dobrar cotovelo"},{"Left Hand Down-Up","E: inclinar punho"},{"Right Hand Down-Up","D: inclinar punho"}};
        static readonly string[,] LegControls={{"Left Upper Leg Front-Back","E: coxa frente/trás"},{"Right Upper Leg Front-Back","D: coxa frente/trás"},{"Left Upper Leg In-Out","E: abrir perna"},{"Right Upper Leg In-Out","D: abrir perna"},{"Left Lower Leg Stretch","E: dobrar joelho"},{"Right Lower Leg Stretch","D: dobrar joelho"},{"Left Foot Up-Down","E: inclinar pé"},{"Right Foot Up-Down","D: inclinar pé"}};
        static readonly string[,] BodyControls={{"Spine Front-Back","Tronco frente/trás"},{"Spine Left-Right","Tronco para os lados"},{"Spine Twist Left-Right","Girar tronco"},{"Chest Front-Back","Inclinar peito"},{"Chest Twist Left-Right","Girar peito"}};
        static readonly string[,] HeadControls={{"Head Nod Down-Up","Cabeça cima/baixo"},{"Head Turn Left-Right","Olhar para os lados"},{"Head Tilt Left-Right","Inclinar cabeça"},{"Neck Nod Down-Up","Pescoço cima/baixo"}};
        public void DrawUI()
        {
            GUILayout.Label("Biblioteca de poses e animações"); GUILayout.Label(Status);
            if(handler==null) return;
            app.mocap.DrawUI();
            if(Selected!=null)
            {
                GUILayout.Label("Selecionada: "+Selected.label);
                GUILayout.BeginHorizontal();
                if(Draft==null && !Selected.pose && GUILayout.Button(Playing?"Pausar":"Continuar")) Pause(Playing);
                if(GUILayout.Button("PARAR / voltar ao avatar")) Stop();
                GUILayout.EndHorizontal();
                if(Selected!=null && Draft==null && !Selected.pose)
                {
                    loop=GUILayout.Toggle(loop,"Repetir animação"); speed=StudioGui.Slider("Velocidade",speed,.1f,2.5f);
                    float next=StudioGui.Slider("Tempo (s)",TimePosition,0,Duration); if(Mathf.Abs(next-TimePosition)>.001f) Seek(next);
                }
            }
            lockTracking=GUILayout.Toggle(lockTracking,"Fixar corpo e mãos durante a prévia");
            GUILayout.Label("Desmarque para combinar com tracking. Face/voz continuam disponíveis. A prévia mantém o avatar no lugar.");
            poseName=GUILayout.TextField(poseName,80);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Salvar pose atual")) try { SaveCurrent(poseName); } catch(Exception ex) {Status=ex.Message;}
            if(GUILayout.Button("Editar pose atual")) try { BeginEdit(); } catch(Exception ex) {Status=ex.Message;}
            GUILayout.EndHorizontal();
            if(Draft!=null)
            {
                editGroup=GUILayout.SelectionGrid(editGroup,editGroups,2);
                var controls=editGroup==0?ArmControls:editGroup==1?LegControls:editGroup==2?BodyControls:HeadControls;
                for(int i=0;i<controls.GetLength(0);i++) { int index=Array.IndexOf(MuscleNames,controls[i,0]); if(index>=0) {float value=StudioGui.Slider(controls[i,1],Draft.muscles[index],Mathf.Min(-1,Draft.muscles[index]),Mathf.Max(1,Draft.muscles[index]));if(Mathf.Abs(value-Draft.muscles[index])>.0001f) SetMuscle(index,value);} }
                Draft.bodyLocal.y=StudioGui.Slider("Altura do corpo",Draft.bodyLocal.y,.1f,2.5f);
            }
            int nextCategory=GUILayout.SelectionGrid(category,categories,2); if(nextCategory!=category) {category=nextCategory;page=0;}
            GUILayout.Label("Buscar"); string nextSearch=GUILayout.TextField(search); if(nextSearch!=search) {search=nextSearch;page=0;}
            var filtered=Items.Where(i=>(category==0 || i.category==categories[category]) && (search.Length==0 || i.label.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
            int pages=Mathf.Max(1,(filtered.Length+11)/12); page=Mathf.Clamp(page,0,pages-1);
            GUILayout.BeginHorizontal(); if(GUILayout.Button("◀")) page=Mathf.Max(0,page-1); GUILayout.Label((page+1)+" / "+pages+" • "+filtered.Length+" itens"); if(GUILayout.Button("▶")) page=Mathf.Min(pages-1,page+1); GUILayout.EndHorizontal();
            foreach(var item in filtered.Skip(page*12).Take(12)) if(GUILayout.Button((item.pose?"POSE • ":"▶ ")+item.label)) Play(item);
            if(GUILayout.Button("Atualizar minhas poses")) LoadSaved();
            if(GUILayout.Button("Abrir pasta de poses")) {Directory.CreateDirectory(PoseDirectory);Application.OpenURL(PoseDirectory);}
            if(GUILayout.Button("Importar biblioteca local (.alposes)")) NativeFilePicker.PickAsync(path=>
            {
                if(string.IsNullOrEmpty(path)) return;
                try
                {
                    Stop();var target=Path.Combine(LocalPosePacks.DirectoryPath,Path.GetFileName(path));
                    if(!string.Equals(Path.GetFullPath(path),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase)) File.Copy(path,target,true);
                    localPacks?.Dispose();localPacks=null;Attach();
                }
                catch(Exception ex) {Status=ex.Message;}
            },"Poses AvatarLab\0*.alposes\0\0","Importar biblioteca local de poses");
            GUILayout.Label(localPacks!=null && localPacks.entries.Count>0?"GoGo Loco • Franada e colaboradores • biblioteca local. As poses substituem as prévias geradas. Use o controle de altura para poses suspensas ou sobre móveis.":"Poses salvas pertencem a este avatar e ficam no computador. Os movimentos incluídos são prévias próprias do AvatarLab.");
        }
        void OnDestroy() {Stop();handler?.Dispose();sampler?.Dispose();localPacks?.Dispose();}
    }
}
