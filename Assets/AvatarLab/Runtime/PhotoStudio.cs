using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class PhotoLook
    {
        public float exposure,contrast=1,saturation=1,warmth,vignette,sharpness;public bool film;
    }
    [Serializable] public sealed class PhotoSettings
    {
        public int resolution,background,delay;public Color color=new Color(.025f,.04f,.065f);public bool avatarOnly;
        public PhotoLook look=new PhotoLook();
        public CameraPostSettings post=new CameraPostSettings();
    }
    [DefaultExecutionOrder(390)] public sealed class PhotoStudio : MonoBehaviour
    {
        public LabApp app;public PhotoSettings settings=new PhotoSettings();public StudioLighting lighting;
        public bool guides=true,preview=true,mainPreview;public string Status="PNG limpo da câmera de saída • F12";public string LastPath {get;private set;}
        public CameraPostSettings Post=>settings.post??(settings.post=new CameraPostSettings());
        public StudioColorEffect Effect {get;private set;}
        public bool Saving=>write!=null;public float Countdown=>deadline<0?0:Mathf.Max(0,deadline-Time.unscaledTime);
        public float AvatarHeight
        {
            get {var a=app.driver?.Definition.animator;if(a!=null && a.isHuman) {var head=a.GetBoneTransform(HumanBodyBones.Head);var left=a.GetBoneTransform(HumanBodyBones.LeftFoot);if(head!=null && left!=null) return Mathf.Clamp(head.position.y-left.position.y+.18f,.3f,8);}return 1.7f;}
        }
        public static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(2160,2700),new Vector2Int(2160,2160),new Vector2Int(2160,3840)};
        public Vector2Int Size=>Sizes[Mathf.Clamp(settings.resolution,0,Sizes.Length-1)];
        RenderTexture texture;float next,lastDraw=-10,deadline=-1;int section;Task write;string pendingPath;
        public void Initialize() {lighting=new StudioLighting(app);Effect=app.cinema.Camera.gameObject.AddComponent<StudioColorEffect>();Effect.studio=this;}
        public void Frame(int mode)
        {
            Vector3 actor=app.currentAvatar!=null?app.currentAvatar.transform.position:Vector3.zero;float height=AvatarHeight;
            var a=app.driver?.Definition.animator;var head=a!=null && a.isHuman?a.GetBoneTransform(HumanBodyBones.Head):null;
            Vector3 target=mode==0?actor+Vector3.up*height*.55f:mode==1?actor+Vector3.up*height*.76f:head!=null?head.position+Vector3.up*height*.025f:actor+Vector3.up*height*.9f;
            float focal=mode==0?28:mode==1?50:85;float fov=2*Mathf.Atan(12/focal)*Mathf.Rad2Deg;
            float aspect=(float)Size.x/Size.y;float span=height*(mode==0?1.3f:mode==1?.85f:.48f);float width=height*(mode==0?.65f:mode==1?.55f:.35f);
            if(mode==0 && a!=null && a.isHuman && app.poses?.Selected!=null)
            {
                var bounds=new Bounds(a.GetBoneTransform(HumanBodyBones.Hips).position,Vector3.zero);
                for(int bone=0;bone<(int)HumanBodyBones.LastBone;bone++) {var t=a.GetBoneTransform((HumanBodyBones)bone);if(t!=null) bounds.Encapsulate(t.position);}
                bounds.Expand(a.humanScale*.25f);target=bounds.center;span=bounds.size.y*1.2f;width=Mathf.Max(bounds.size.x,bounds.size.z)*1.2f;
            }
            float distance=Mathf.Max(span,width/aspect)/(2*Mathf.Tan(fov*Mathf.Deg2Rad*.5f));
            var direction=app.room.view.transform.position-target;direction.y=0;if(direction.sqrMagnitude<.001f) direction=Vector3.forward;direction.Normalize();
            var shot=app.cinema.shots[app.cinema.selected];shot.mode=1;shot.lookAt=true;shot.position=target-actor+direction*distance;shot.targetOffset=target-actor;shot.fov=fov;shot.roll=0;
            app.cinema.playing=false;app.cinema.Evaluate(app.cinema.playhead);Status="Enquadramento: "+new[]{"corpo inteiro","meio corpo","retrato"}[mode];
        }
        public void RenderTo(RenderTexture target,bool avatarOnly,int background,Color color,bool still=false)
        {
            var c=app.cinema.Camera;var rt=c.targetTexture;float aspect=c.aspect;var clear=c.clearFlags;var bg=c.backgroundColor;int mask=c.cullingMask;
            bool wasStill=Effect.StillCapture,wasTransparent=Effect.TransparentOutput;
            try {Effect.StillCapture=still;Effect.TransparentOutput=background==2;c.targetTexture=target;c.aspect=(float)target.width/target.height;c.cullingMask=avatarOnly?1<<8:~((1<<30)|(1<<31));c.clearFlags=background==0 && RenderSettings.skybox!=null?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;c.backgroundColor=background==2?Color.clear:background==3?Color.green:color;c.Render();}
            finally {Effect.StillCapture=wasStill;Effect.TransparentOutput=wasTransparent;c.targetTexture=rt;c.aspect=aspect;c.clearFlags=clear;c.backgroundColor=bg;c.cullingMask=mask;}
        }
        public void RequestCapture()
        {
            if(Saving || deadline>=0) return;deadline=Time.unscaledTime+Mathf.Clamp(settings.delay,0,10);Status="Preparando foto...";
        }
        public void Capture(string path=null)
        {
            if(Saving) return;RenderTexture rt=null;Texture2D pixels=null;var previous=RenderTexture.active;
            try
            {
                if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) throw new InvalidOperationException("Captura requer uma GPU.");
                var size=Size;rt=RenderTexture.GetTemporary(size.x,size.y,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB,4);
                RenderTo(rt,settings.avatarOnly,settings.background,settings.color,true);RenderTexture.active=rt;pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0,false);pixels.Apply(false);var rgba=pixels.GetRawTextureData();var format=pixels.graphicsFormat;
                if(path==null) {string directory=Path.Combine(Application.persistentDataPath,"Fotos");Directory.CreateDirectory(directory);path=Path.Combine(directory,"Foto-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".png");}
                pendingPath=path;var shot=JsonUtility.ToJson(app.cinema.shots[app.cinema.selected],true);string final=path;
                write=Task.Run(()=>{var png=ImageConversion.EncodeArrayToPNG(rgba,format,(uint)size.x,(uint)size.y);string temp=final+".tmp";File.WriteAllBytes(temp,png);AvatarArchive.CommitFile(temp,final);File.WriteAllText(Path.ChangeExtension(final,"camera.json"),shot);});Status="Salvando foto...";
            }
            catch(Exception ex) {Status="Foto não salva: "+ex.Message;}
            finally {RenderTexture.active=previous;if(pixels!=null) Destroy(pixels);if(rt!=null) RenderTexture.ReleaseTemporary(rt);}
        }
        void Update()
        {
            if(write!=null && write.IsCompleted) {Status=write.IsFaulted?"Falha: "+write.Exception.GetBaseException().Message:"Foto salva: "+pendingPath;if(!write.IsFaulted) LastPath=pendingPath;write=null;}
            float panel=app.PanelVisible?440*Mathf.Clamp(Screen.height/900f,.7f,1.5f):0;
            if(!mainPreview || !app.StudioInputAllowed || Input.mousePosition.x<panel || app.cinema.shots.Count==0) return;
            var s=app.cinema.shots[app.cinema.selected];if(s.mode>1) return;
            bool orbit=Input.GetMouseButton(1),pan=Input.GetMouseButton(2);float wheel=Input.mouseScrollDelta.y;if(!orbit && !pan && wheel==0) return;
            Vector3 actor=app.currentAvatar!=null?app.currentAvatar.transform.position:Vector3.zero,target=actor+s.targetOffset,position=s.position+(s.mode==1?actor:Vector3.zero),offset=position-target;
            if(orbit) {offset=Quaternion.AngleAxis(Input.GetAxis("Mouse X")*3,Vector3.up)*offset;var tilted=Quaternion.AngleAxis(-Input.GetAxis("Mouse Y")*3,app.cinema.Camera.transform.right)*offset;if(Mathf.Abs(tilted.normalized.y)<.98f) offset=tilted;}
            if(wheel!=0) offset=offset.normalized*Mathf.Clamp(offset.magnitude*Mathf.Exp(-wheel*.1f),.2f,20);
            if(pan) {var delta=(-app.cinema.Camera.transform.right*Input.GetAxis("Mouse X")-app.cinema.Camera.transform.up*Input.GetAxis("Mouse Y"))*offset.magnitude*.008f;s.targetOffset+=delta;target+=delta;}
            s.position=target+offset-(s.mode==1?actor:Vector3.zero);s.lookAt=true;app.cinema.playing=false;
        }
        void LateUpdate()
        {
            lighting?.Apply();
            if(deadline>=0 && Time.unscaledTime>=deadline) {deadline=-1;Capture();}
            if(app.video.Running) return;
            if((!preview && !mainPreview) || Time.unscaledTime-lastDraw>.4f || Time.unscaledTime<next || SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            int edge=mainPreview && !app.video.Running?1280:640;next=Time.unscaledTime+1f/(edge==1280?30:15);float aspect=(float)Size.x/Size.y;int width=aspect>=1?edge:Mathf.RoundToInt(edge*aspect),height=aspect>=1?Mathf.RoundToInt(edge/aspect):edge;
            if(texture==null || texture.width!=width || texture.height!=height) {if(texture!=null) {texture.Release();Destroy(texture);}texture=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);texture.Create();}
            RenderTo(texture,settings.avatarOnly,settings.background,settings.color);
        }
        public void DrawPreview(bool broadcast=false)
        {
            broadcast=broadcast || app.video.Running;var image=broadcast && app.video.Texture!=null?app.video.Texture:texture;
            if(!broadcast || app.video.Texture==null) lastDraw=Time.unscaledTime;
            if(image==null) {GUILayout.Label("Preparando prévia da câmera...");return;}
            GUILayout.Label(broadcast && app.video.Texture!=null?"Prévia da transmissão":"Prévia da foto • "+Size.x+" × "+Size.y);
            float aspect=(float)image.width/image.height;var outer=GUILayoutUtility.GetRect(10,220,GUILayout.ExpandWidth(true));float width=Mathf.Min(outer.width,outer.height*aspect);var rect=new Rect(outer.center.x-width*.5f,outer.center.y-width/aspect*.5f,width,width/aspect);GUI.DrawTexture(rect,image,ScaleMode.ScaleToFit,false);
            if(guides) {var color=GUI.color;GUI.color=new Color(1,.9f,.55f,.6f);for(int i=1;i<=2;i++) {GUI.DrawTexture(new Rect(rect.x+rect.width*i/3,rect.y,1,rect.height),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(rect.x,rect.y+rect.height*i/3,rect.width,1),Texture2D.whiteTexture);}GUI.color=color;}
            if(Countdown>0) GUI.Label(rect,Mathf.CeilToInt(Countdown).ToString());
        }
        void OnGUI()
        {
            if(!mainPreview) return;var image=app.video.Running?app.video.Texture:texture;if(!app.video.Running) lastDraw=Time.unscaledTime;if(image==null) return;
            var old=GUI.matrix;int depth=GUI.depth;var color=GUI.color;GUI.depth=20;GUI.matrix=Matrix4x4.identity;
            float left=app.PanelVisible?440*Mathf.Clamp(Screen.height/900f,.7f,1.5f):0;var outer=new Rect(left,0,Screen.width-left,Screen.height);GUI.color=Color.black;GUI.DrawTexture(outer,Texture2D.whiteTexture);GUI.color=Color.white;
            float aspect=(float)image.width/image.height,width=Mathf.Min(outer.width,outer.height*aspect);var rect=new Rect(outer.center.x-width*.5f,outer.center.y-width/aspect*.5f,width,width/aspect);GUI.DrawTexture(rect,image,ScaleMode.ScaleToFit,false);
            if(guides) {GUI.color=new Color(1,.9f,.55f,.5f);for(int i=1;i<=2;i++) {GUI.DrawTexture(new Rect(rect.x+rect.width*i/3,rect.y,1,rect.height),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(rect.x,rect.y+rect.height*i/3,rect.width,1),Texture2D.whiteTexture);}}
            GUI.color=Color.white;GUI.Label(new Rect(left+12,10,700,25),"Câmera de saída • F3: voltar à inspeção • direito: orbitar • meio: deslocar • roda: distância");GUI.color=color;GUI.matrix=old;GUI.depth=depth;
        }
        public void LookPreset(int i)
        {
            settings.look=i==0?new PhotoLook():i==1?new PhotoLook{exposure=-.35f,contrast=1.02f,saturation=.95f,warmth=.05f,vignette=.15f,sharpness=.05f}:i==2?new PhotoLook{contrast=1.18f,saturation=.8f,warmth=.05f,vignette=.35f,film=true}:i==3?new PhotoLook{exposure=-.3f,contrast=1.05f,saturation=1.15f,vignette=.25f,film=true}:new PhotoLook{contrast=1.15f,saturation=0,vignette=.25f};
        }
        public void DrawUI()
        {
            GUILayout.Label("Estúdio de luz e fotografia");GUILayout.Label(Status+(Countdown>0?" • "+Mathf.CeilToInt(Countdown)+" s":""));
            GUILayout.BeginHorizontal();if(GUILayout.Button("Corpo inteiro")) Frame(0);if(GUILayout.Button("Meio corpo")) Frame(1);if(GUILayout.Button("Retrato")) Frame(2);GUILayout.EndHorizontal();
            if(GUILayout.Button(Saving?"Salvando...":"TIRAR FOTO PNG • F12")) RequestCapture();
            if(deadline>=0 && GUILayout.Button("Cancelar temporizador")) {deadline=-1;Status="Temporizador cancelado.";}
            mainPreview=GUILayout.Toggle(mainPreview,"Ver câmera de saída na área principal • F3");
            guides=GUILayout.Toggle(guides,"Guias de composição (só na prévia)");preview=GUILayout.Toggle(preview,"Prévia de foto • 15 FPS");if(preview) DrawPreview();
            section=GUILayout.SelectionGrid(section,new[]{"Luzes","Look / cor","Foto / fundo","Pós-processamento"},2);
            if(section==3) DrawPostUI();
            if(section==0) lighting.DrawUI();
            if(section==1)
            {
                int choice=GUILayout.SelectionGrid(-1,new[]{"Neutro","Beleza","Cinema","Neon","Preto e branco"},3);if(choice>=0) LookPreset(choice);
                var l=settings.look;l.exposure=StudioGui.Slider("Exposição (EV)",l.exposure,-2,2);l.contrast=StudioGui.Slider("Contraste",l.contrast,.5f,1.8f);l.saturation=StudioGui.Slider("Saturação",l.saturation,0,1.8f);l.warmth=StudioGui.Slider("Frio / quente",l.warmth,-1,1);l.vignette=StudioGui.Slider("Vinheta",l.vignette,0,.8f);l.sharpness=StudioGui.Slider("Nitidez",l.sharpness,0,.5f);l.film=GUILayout.Toggle(l.film,"Compressão de altas luzes (filme)");
                GUILayout.Label("O look afeta fotos e transmissão; a vista de inspeção não recebe o filtro de cor.");
            }
            if(section==2)
            {
                GUILayout.Label("Resolução da foto (independente da transmissão)");settings.resolution=GUILayout.SelectionGrid(settings.resolution,new[]{"Full HD 16:9","4K 16:9","Retrato 4:5","Quadrado 1:1","Vertical 9:16"},2);
                settings.background=GUILayout.SelectionGrid(settings.background,new[]{"Cenário / céu","Cor sólida","Transparente","Chroma verde"},2);settings.avatarOnly=GUILayout.Toggle(settings.avatarOnly,"Somente avatar na foto / prévia");
                if(settings.background==1) settings.color=StudioGui.Color("Fundo",settings.color);
                settings.delay=Mathf.RoundToInt(StudioGui.Slider("Temporizador (s)",settings.delay,0,10));
                if(GUILayout.Button("Abrir pasta de fotos")) {string directory=Path.Combine(Application.persistentDataPath,"Fotos");Directory.CreateDirectory(directory);Application.OpenURL(directory);}
                GUILayout.Label("PNG sem painéis, cursor, espelho ou guias. Cabelo transparente depende do shader do avatar. Fotos não incluem áudio nem marca d’água. Ajuste a lente e a inclinação em Cinema; salve luzes e look em Cenário → Salvar estúdio.");
            }
        }
        void OnDestroy() {lighting?.Dispose();if(texture!=null) {texture.Release();Destroy(texture);}}
        public void DrawPostUI()
        {
            var s=Post;GUILayout.Label("Pós-processamento da câmera de saída • fotos e transmissão");s.enabled=GUILayout.Toggle(s.enabled,"Ativar pós-processamento");
            int preset=GUILayout.SelectionGrid(-1,new[]{"Limpo","Suave","Cinema","Neon"},4);if(preset>=0)
            {
                settings.post=new CameraPostSettings{enabled=preset>0,bloom=preset>0,bloomIntensity=preset==3?1:.3f,bloomThreshold=preset==3?.6f:.9f,bloomRadius=preset==3?3:2,motionBlur=preset==2,shutter=.35f,chromatic=preset==3?.2f:0,grain=preset==2?.035f:0};s=Post;
            }
            s.quality=GUILayout.SelectionGrid(s.quality,new[]{"Econômico","Alta qualidade"},2);
            s.bloom=GUILayout.Toggle(s.bloom,"Bloom • brilho ao redor das luzes");if(s.bloom) {s.bloomIntensity=StudioGui.Slider("Intensidade do bloom",s.bloomIntensity,0,3);s.bloomThreshold=StudioGui.Slider("Limiar de brilho",s.bloomThreshold,0,2);s.bloomRadius=StudioGui.Slider("Raio do bloom",s.bloomRadius,.5f,6);s.bloomTint=StudioGui.Color("Cor do bloom",s.bloomTint);s.bloomAlpha=GUILayout.Toggle(s.bloomAlpha,"Incluir halo no alpha da saída transparente");}
            s.motionBlur=GUILayout.Toggle(s.motionBlur,"Motion blur • desfoque de movimento");if(s.motionBlur) {s.shutter=StudioGui.Slider("Obturador / intensidade",s.shutter,0,1);s.maxMotionPixels=StudioGui.Slider("Limite do rastro (pixels)",s.maxMotionPixels,1,48);if(!SystemInfo.supportsMotionVectors) GUILayout.Label("GPU sem suporte a vetores de movimento.");}
            s.depthOfField=GUILayout.Toggle(s.depthOfField,"Profundidade de campo • desfoque por distância");if(s.depthOfField) {if(GUILayout.Button("Focar no rosto do avatar")) {var a=app.driver?.Definition.animator;var head=a!=null && a.isHuman?a.GetBoneTransform(HumanBodyBones.Head):null;if(head!=null) s.focusDistance=Mathf.Max(.1f,app.cinema.Camera.transform.InverseTransformPoint(head.position).z);}s.focusDistance=StudioGui.Slider("Distância de foco (m)",s.focusDistance,.1f,20);s.focusRange=StudioGui.Slider("Faixa de foco (m)",s.focusRange,.1f,10);s.focusBlur=StudioGui.Slider("Desfoque (pixels)",s.focusBlur,0,16);}
            s.chromatic=StudioGui.Slider("Aberração cromática",s.chromatic,0,1);s.grain=StudioGui.Slider("Grão de filme",s.grain,0,.3f);
            GUILayout.Label("Efeitos locais na GPU. Salve em Cenário → Salvar estúdio. Motion blur depende de vetores de movimento do material; transparências podem ter resposta limitada. Fotos PNG usam os efeitos com motion blur desligado. A prévia acompanha o vídeo enquanto a saída estiver ativa.");
        }
    }
    [RequireComponent(typeof(Camera))] public sealed class StudioColorEffect : MonoBehaviour
    {
        public PhotoStudio studio;Material material;
        public bool StillCapture,TransparentOutput;
        readonly CameraPostEffects post=new CameraPostEffects();Camera camera;DepthTextureMode initialDepth;
        int lastFrame=-1,width,height;float lastTime,fov;Vector3 position;Quaternion rotation;
        RenderTexture history;
        SkinnedMeshRenderer[] skinned;bool[] previousMotion;GameObject previousAvatar;
        void Awake() {camera=GetComponent<Camera>();initialDepth=camera.depthTextureMode;}
        void OnPreCull()
        {
            if(studio==null) return;var s=studio.Post;bool motion=s.enabled && s.motionBlur && SystemInfo.supportsMotionVectors && !StillCapture;
            camera.depthTextureMode=initialDepth | (s.enabled && s.depthOfField || motion?DepthTextureMode.Depth:DepthTextureMode.None) | (motion?DepthTextureMode.MotionVectors:DepthTextureMode.None);
            var avatar=studio.app.currentAvatar;
            if(!motion || avatar!=previousAvatar)
            {
                if(skinned!=null) for(int i=0;i<skinned.Length;i++) if(skinned[i]!=null) skinned[i].skinnedMotionVectors=previousMotion[i];skinned=null;previousAvatar=null;
            }
            if(motion && skinned==null && avatar!=null) {previousAvatar=avatar;skinned=avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);previousMotion=new bool[skinned.Length];for(int i=0;i<skinned.Length;i++) {previousMotion[i]=skinned[i].skinnedMotionVectors;skinned[i].skinnedMotionVectors=true;}}
        }
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            if(material==null) {var shader=Resources.Load<Shader>("AvatarLabGrade");if(shader!=null && shader.isSupported) material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
            if(material==null || studio==null) {Graphics.Blit(source,destination);return;}
            var s=studio.settings.look;material.SetVector("_Look",new Vector4(s.exposure,s.contrast,s.saturation,s.warmth));material.SetVector("_Style",new Vector4(s.vignette,s.sharpness,s.film?1:0,0));
            RenderTexture processed=null;var previousActive=RenderTexture.active;
            try
            {
                if(studio.Post.enabled)
                {
                    bool motion=!StillCapture && history!=null && SystemInfo.supportsMotionVectors && lastFrame>=0 && lastFrame!=Time.frameCount && width==source.width && height==source.height && Time.unscaledTime-lastTime<.25f && Vector3.Distance(position,camera.transform.position)<2 && Quaternion.Angle(rotation,camera.transform.rotation)<60 && Mathf.Abs(fov-camera.fieldOfView)<8;
                    processed=RenderTexture.GetTemporary(source.width,source.height,0,source.format);post.Render(source,processed,studio.Post,TransparentOutput,motion,history);
                }
                Graphics.Blit(processed!=null?processed:source,destination,material);
                if(!StillCapture)
                {
                    if(studio.Post.enabled && studio.Post.motionBlur && SystemInfo.supportsMotionVectors)
                    {
                        if(history==null || history.width!=source.width || history.height!=source.height || history.format!=source.format) {ReleaseHistory();history=new RenderTexture(source.width,source.height,0,source.format){name="AvatarLab motion change reference",filterMode=FilterMode.Bilinear};history.Create();}
                        Graphics.Blit(source,history);
                    }
                    else ReleaseHistory();
                    lastFrame=Time.frameCount;lastTime=Time.unscaledTime;width=source.width;height=source.height;position=camera.transform.position;rotation=camera.transform.rotation;fov=camera.fieldOfView;
                }
            }
            finally {RenderTexture.active=previousActive;if(processed!=null) RenderTexture.ReleaseTemporary(processed);}
        }
        void ReleaseHistory() {if(history!=null) {history.Release();Destroy(history);history=null;}}
        void OnDestroy() {ReleaseHistory();if(material!=null) Destroy(material);post.Dispose();if(camera!=null) camera.depthTextureMode=initialDepth;if(skinned!=null) for(int i=0;i<skinned.Length;i++) if(skinned[i]!=null) skinned[i].skinnedMotionVectors=previousMotion[i];}
    }
}
