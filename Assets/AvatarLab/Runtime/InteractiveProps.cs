using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class InteractivePropSettings
    {
        public int shape,amount=5;public float size=.12f,mass=.08f,speed=5,lifetime=8;
        public bool gravity=true;public Color color=new Color(.15f,.75f,.9f);
    }
    public sealed class InteractiveProps : MonoBehaviour
    {
        public LabApp app;readonly List<(GameObject go,float end)> spawned=new List<(GameObject,float)>();
        Material material;public InteractivePropSettings settings=new InteractivePropSettings();
        public string Status="Objetos físicos locais: configure, solte ou arremesse.";
        static readonly PrimitiveType[] Shapes={PrimitiveType.Sphere,PrimitiveType.Cube,PrimitiveType.Capsule};
        string SettingsPath=>Path.Combine(Application.persistentDataPath,"InteractiveProps.json");
        void Awake()
        {
            try {if(File.Exists(SettingsPath) && new FileInfo(SettingsPath).Length<16384) settings=JsonUtility.FromJson<InteractivePropSettings>(File.ReadAllText(SettingsPath))??new InteractivePropSettings();Normalize();}
            catch(Exception ex) {settings=new InteractivePropSettings();Status="Configuração ignorada: "+ex.Message;}
        }
        static float Safe(float value,float fallback,float min,float max)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);
        void Normalize()
        {
            settings.shape=Mathf.Clamp(settings.shape,0,2);settings.amount=Mathf.Clamp(settings.amount,1,20);
            settings.size=Safe(settings.size,.12f,.04f,.6f);settings.mass=Safe(settings.mass,.08f,.01f,2);
            settings.speed=Safe(settings.speed,5,.5f,12);settings.lifetime=Safe(settings.lifetime,8,1,30);
            settings.color=new Color(Safe(settings.color.r,.15f,0,1),Safe(settings.color.g,.75f,0,1),Safe(settings.color.b,.9f,0,1));
        }
        public void SaveSettings()
        {
            try {Normalize();Directory.CreateDirectory(Application.persistentDataPath);string temp=SettingsPath+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(settings,true));AvatarArchive.CommitFile(temp,SettingsPath);Status="Configuração dos objetos salva.";}
            catch(Exception ex) {Status="Falha ao salvar: "+ex.Message;}
        }
        public int Count=>spawned.Count;
        public void Spawn(int amount,bool drop,float lifetime=-1)
        {
            Normalize();amount=Mathf.Clamp(amount,1,20);lifetime=lifetime<0?settings.lifetime:Safe(lifetime,8,1,30);
            if(material==null) {material=new Material(app.roomMaterial);material.color=Color.white;}
            var tint=new MaterialPropertyBlock();tint.SetColor("_Color",settings.color);tint.SetColor("_BaseColor",settings.color);
            Vector3 actor=app.currentAvatar!=null?app.currentAvatar.transform.position:Vector3.zero;
            for(int i=0;i<amount && spawned.Count<64;i++)
            {
                var ball=GameObject.CreatePrimitive(Shapes[settings.shape]);ball.name="AvatarLab interactive prop";ball.transform.localScale=Vector3.one*settings.size;
                Vector3 target=actor+Vector3.up*1.2f;
                ball.transform.position=drop?actor+new Vector3(UnityEngine.Random.Range(-.5f,.5f),2.8f,UnityEngine.Random.Range(-.3f,.3f)):target+new Vector3(UnityEngine.Random.Range(-1f,1f),UnityEngine.Random.Range(-.1f,.4f),2.5f);
                var renderer=ball.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.SetPropertyBlock(tint);
                var body=ball.AddComponent<Rigidbody>();body.mass=settings.mass;body.useGravity=settings.gravity;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                if(!drop) body.velocity=(target-ball.transform.position).normalized*settings.speed+Vector3.up*.8f;
                spawned.Add((ball,Time.unscaledTime+lifetime));
            }
            Status="Objetos criados; a duração configurada controla a remoção.";
        }
        public void DrawUI()
        {
            GUILayout.Label("Objetos interativos • "+Status);GUILayout.Label("Ativos: "+Count+" / 64 • física calculada no AvatarLab.");
            settings.shape=GUILayout.SelectionGrid(settings.shape,new[]{"Esfera","Cubo","Cápsula"},3);
            settings.amount=Mathf.RoundToInt(StudioGui.Slider("Quantidade",settings.amount,1,20));
            GUILayout.BeginHorizontal();if(GUILayout.Button("SOLTAR OBJETOS")) Spawn(settings.amount,true);if(GUILayout.Button("ARREMESSAR")) Spawn(settings.amount,false);GUILayout.EndHorizontal();
            if(GUILayout.Button("LIMPAR OBJETOS")) Clear();
            settings.size=StudioGui.Slider("Largura (metros)",settings.size,.04f,.6f);settings.mass=StudioGui.Slider("Massa (kg)",settings.mass,.01f,2);
            settings.speed=StudioGui.Slider("Velocidade de arremesso (m/s)",settings.speed,.5f,12);settings.lifetime=StudioGui.Slider("Desaparecer após (segundos)",settings.lifetime,1,30);
            settings.gravity=GUILayout.Toggle(settings.gravity,"Usar gravidade");
            settings.color.r=StudioGui.Slider("Cor • vermelho",settings.color.r,0,1);settings.color.g=StudioGui.Slider("Cor • verde",settings.color.g,0,1);settings.color.b=StudioGui.Slider("Cor • azul",settings.color.b,0,1);
            if(GUILayout.Button("Salvar configuração dos objetos")) SaveSettings();
            GUILayout.Label("Os nós Arremessar / Soltar usam esta configuração. Colisões dependem dos colliders Unity presentes; não há collider completo da malha do avatar. Para objetos de cenário personalizados, use a aba Cenário.");
        }
        void Update() {for(int i=spawned.Count-1;i>=0;i--) if(spawned[i].go==null || Time.unscaledTime>spawned[i].end) {if(spawned[i].go!=null) Destroy(spawned[i].go);spawned.RemoveAt(i);}}
        public void Clear() {foreach(var item in spawned) if(item.go!=null) Destroy(item.go);spawned.Clear();}
        void OnDestroy() {Clear();if(material!=null) Destroy(material);}
    }
}
