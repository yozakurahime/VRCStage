using System;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class StudioLamp
    {
        public bool enabled=true,shadows;public float intensity=3,kelvin=5500,azimuth=-35,elevation=25,distance=2.5f,angle=90;
        public bool temperature=true;public Color color=Color.white;
    }
    [Serializable] public sealed class StudioLightingSettings
    {
        public bool enabled,cameraRelative=true;public Color ambient=new Color(.12f,.13f,.16f);
        public StudioLamp[] lamps={new StudioLamp{shadows=true},new StudioLamp{intensity=1,azimuth=45,kelvin=6500},new StudioLamp{intensity=3,azimuth=155,kelvin=4500}};
    }
    // Reuses the three inspection lamps; does not add three more shadow casters.
    public sealed class StudioLighting : IDisposable
    {
        public StudioLightingSettings settings=new StudioLightingSettings();public string preset="Manual";
        readonly LabApp app;readonly Light[] lights;readonly Transform[] parents;readonly Vector3[] positions;readonly Quaternion[] rotations;
        readonly Color[] colors;readonly float[] intensities,angles;readonly LightShadows[] shadows;
        readonly GameObject rig;bool active;Color ambient;UnityEngine.Rendering.AmbientMode ambientMode;int selected;
        Color ambientEquator,ambientGround;float ambientIntensity;UnityEngine.Rendering.SphericalHarmonicsL2 ambientProbe;
        public StudioLighting(LabApp app)
        {
            this.app=app;lights=new[]{app.room.key,app.room.fill,app.room.rim};parents=new Transform[3];positions=new Vector3[3];rotations=new Quaternion[3];
            colors=new Color[3];intensities=new float[3];angles=new float[3];shadows=new LightShadows[3];rig=new GameObject("AvatarLab portrait lights");
        }
        public static float Safe(float value,float fallback,float min,float max)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);
        public void Preset(int index)
        {
            settings=new StudioLightingSettings{enabled=true};preset=new[]{"Beleza","Rembrandt","Dramático","Neon","Suave"}[Mathf.Clamp(index,0,4)];
            var k=settings.lamps[0];var f=settings.lamps[1];var r=settings.lamps[2];
            if(index==0) {k.azimuth=-15;k.elevation=18;k.intensity=1.4f;f.intensity=.6f;r.intensity=1.3f;settings.ambient=new Color(.1f,.11f,.13f);}
            if(index==1) {k.azimuth=-55;k.elevation=38;f.intensity=.35f;r.intensity=2;settings.ambient=new Color(.055f,.06f,.075f);}
            if(index==2) {k.azimuth=-65;k.kelvin=4000;k.intensity=4;f.intensity=.15f;f.kelvin=10000;r.intensity=3;settings.ambient=new Color(.025f,.03f,.045f);}
            if(index==3) {k.temperature=f.temperature=r.temperature=false;k.color=new Color(1,.12f,.45f);f.color=new Color(.08f,.65f,1);r.color=new Color(.2f,.9f,1);k.intensity=2.5f;f.intensity=1.2f;r.intensity=2.2f;settings.ambient=new Color(.035f,.025f,.07f);}
            if(index==4) {k.azimuth=-25;k.intensity=2;f.intensity=1.5f;r.intensity=1.5f;settings.ambient=new Color(.3f,.3f,.32f);}
            Apply();
        }
        public void Restore()
        {
            if(!active) return;
            for(int i=0;i<3;i++) {var l=lights[i];if(l==null) continue;l.transform.SetParent(parents[i],false);l.transform.localPosition=positions[i];l.transform.localRotation=rotations[i];l.color=colors[i];l.intensity=intensities[i];l.spotAngle=angles[i];l.shadows=shadows[i];l.enabled=true;}
            RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;active=false;
            RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;RenderSettings.ambientIntensity=ambientIntensity;RenderSettings.ambientProbe=ambientProbe;
        }
        public void Apply()
        {
            if(settings==null || !settings.enabled) {Restore();return;}
            if(settings.lamps==null || settings.lamps.Length!=3) settings.lamps=new StudioLightingSettings().lamps;
            if(!active)
            {
                ambient=RenderSettings.ambientLight;ambientMode=RenderSettings.ambientMode;
                ambientEquator=RenderSettings.ambientEquatorColor;ambientGround=RenderSettings.ambientGroundColor;ambientIntensity=RenderSettings.ambientIntensity;ambientProbe=RenderSettings.ambientProbe;
                for(int i=0;i<3;i++) {var l=lights[i];parents[i]=l.transform.parent;positions[i]=l.transform.localPosition;rotations[i]=l.transform.localRotation;colors[i]=l.color;intensities[i]=l.intensity;angles[i]=l.spotAngle;shadows[i]=l.shadows;l.transform.SetParent(rig.transform,true);}
                active=true;
            }
            Vector3 actor=app.currentAvatar!=null?app.currentAvatar.transform.position:Vector3.zero;
            Vector3 target=actor+Vector3.up*app.photo.AvatarHeight*.65f;
            var camera=app.cinema.Camera;var direction=camera.transform.position-target;direction.y=0;
            Quaternion basis=settings.cameraRelative && direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction):app.currentAvatar!=null?Quaternion.Euler(0,app.currentAvatar.transform.eulerAngles.y,0):Quaternion.identity;
            for(int i=0;i<3;i++)
            {
                var s=settings.lamps[i]??(settings.lamps[i]=new StudioLamp());var l=lights[i];l.enabled=s.enabled;
                s.intensity=Safe(s.intensity,3,0,8);s.kelvin=Safe(s.kelvin,5500,2000,12000);s.distance=Safe(s.distance,2.5f,.5f,8);s.elevation=Safe(s.elevation,25,-20,85);s.azimuth=Safe(s.azimuth,0,-180,180);s.angle=Safe(s.angle,90,20,140);
                l.transform.position=target+basis*Quaternion.Euler(-s.elevation,s.azimuth,0)*Vector3.forward*s.distance;l.transform.LookAt(target);
                l.color=s.temperature?Mathf.CorrelatedColorTemperatureToRGB(s.kelvin):s.color;l.intensity=s.intensity;l.spotAngle=s.angle;
                l.shadows=s.shadows?LightShadows.Soft:LightShadows.None;
            }
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=settings.ambient;
        }
        public void DrawUI()
        {
            GUILayout.Label("Iluminação de estúdio • "+preset);int choice=GUILayout.SelectionGrid(-1,new[]{"Beleza","Rembrandt","Dramático","Neon","Suave"},3);if(choice>=0) Preset(choice);
            settings.enabled=GUILayout.Toggle(settings.enabled,"Usar iluminação de estúdio");settings.cameraRelative=GUILayout.Toggle(settings.cameraRelative,"Posicionar luzes em relação à câmera de saída");
            selected=GUILayout.SelectionGrid(selected,new[]{"Principal","Preenchimento","Recorte"},3);var s=settings.lamps[selected];
            s.enabled=GUILayout.Toggle(s.enabled,"Luz ativa");s.intensity=StudioGui.Slider("Intensidade",s.intensity,0,8);
            s.azimuth=StudioGui.Slider("Direção lateral (graus)",s.azimuth,-180,180);s.elevation=StudioGui.Slider("Elevação (graus)",s.elevation,-20,85);s.distance=StudioGui.Slider("Distância (m)",s.distance,.5f,8);s.angle=StudioGui.Slider("Abertura do foco",s.angle,20,140);
            s.temperature=GUILayout.Toggle(s.temperature,"Cor por temperatura");if(s.temperature) s.kelvin=StudioGui.Slider("Temperatura (K)",s.kelvin,2000,12000);else s.color=StudioGui.Color("Cor da luz",s.color);
            s.shadows=GUILayout.Toggle(s.shadows,"Sombras suaves (custo adicional)");settings.ambient=StudioGui.Color("Luz ambiente",settings.ambient);
            GUILayout.Label("Principal modela o rosto; preenchimento reduz contraste; recorte separa o cabelo do fundo. Materiais com luz própria podem responder de outra forma.");
        }
        public void Dispose() {Restore();UnityEngine.Object.Destroy(rig);}
    }
}
