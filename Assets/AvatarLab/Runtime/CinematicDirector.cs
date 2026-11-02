using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class CameraKeyframe
    {
        public Vector3 position, euler;
        public float fov = 50;
    }
    [Serializable] public sealed class CameraShot
    {
        public string name = "Câmera";
        public int mode; // 0 fixed, 1 follow, 2 orbit, 3 path
        public Vector3 position = new Vector3(0, 1.6f, 3), euler = new Vector3(0, 180, 0);
        public Vector3 targetOffset = new Vector3(0, 1.3f, 0);
        public bool lookAt = true, loop = true;
        public float fov = 50, duration = 10, orbitRadius = 3, orbitHeight = 1.5f, orbitSpeed = 12,roll;
        public List<CameraKeyframe> keys = new List<CameraKeyframe>();
    }
    [DefaultExecutionOrder(350)]
    public sealed class CinematicDirector : MonoBehaviour
    {
        public LabApp app;
        public List<CameraShot> shots = new List<CameraShot>();
        public int selected;
        public Camera Camera { get; private set; }
        public bool playing;
        public float playhead;
        public float transition=.35f;int lastSelected=-1;float blendStart;Vector3 blendPosition;Quaternion blendRotation;float blendFov;
        int guiMode;
        void Awake()
        {
            Camera = new GameObject("Program Camera").AddComponent<Camera>(); Camera.enabled = false;
            Camera.nearClipPlane = .03f; Camera.farClipPlane = 500; Camera.stereoTargetEye = StereoTargetEyeMask.None;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.03f, .04f, .06f);
            shots.Add(new CameraShot { name = "Frontal" });
        }
        public void Snapshot()
        {
            var c = app.room.view;
            shots.Add(new CameraShot { name = "Plano " + (shots.Count + 1), position = c.transform.position, euler = c.transform.eulerAngles, fov = c.fieldOfView, lookAt = false }); selected = shots.Count - 1;
        }
        public void Evaluate(float elapsed)
        {
            if (shots.Count == 0) return; selected = Mathf.Clamp(selected, 0, shots.Count - 1); var s = shots[selected];
            Vector3 actor = app.currentAvatar != null ? app.currentAvatar.transform.position : Vector3.zero;
            Vector3 target = actor + s.targetOffset;
            var position = s.position; var rotation = Quaternion.Euler(s.euler); float fov = s.fov;
            if (s.mode == 1) position = actor + s.position;
            if (s.mode == 2)
            {
                float angle = s.euler.y + elapsed * s.orbitSpeed;
                position = actor + Quaternion.Euler(0, angle, 0) * Vector3.forward * s.orbitRadius + Vector3.up * s.orbitHeight;
            }
            if (s.mode == 3 && s.keys.Count > 0)
            {
                float phase = s.loop ? Mathf.Repeat(elapsed / Mathf.Max(.1f, s.duration), 1) : Mathf.Clamp01(elapsed / Mathf.Max(.1f, s.duration));
                float segment = phase * (s.keys.Count - 1); int left = Mathf.Min(Mathf.FloorToInt(segment), s.keys.Count - 1); int right = Mathf.Min(left + 1, s.keys.Count - 1);
                float blend = Mathf.SmoothStep(0, 1, segment - left);
                position = Vector3.Lerp(s.keys[left].position, s.keys[right].position, blend);
                rotation = Quaternion.Slerp(Quaternion.Euler(s.keys[left].euler), Quaternion.Euler(s.keys[right].euler), blend);
                fov = Mathf.Lerp(s.keys[left].fov, s.keys[right].fov, blend);
            }
            Camera.transform.SetPositionAndRotation(position, rotation); Camera.fieldOfView = Mathf.Clamp(fov, 10, 120);
            if (s.lookAt && (target - position).sqrMagnitude > .0001f) Camera.transform.LookAt(target);
            Camera.transform.rotation*=Quaternion.Euler(0,0,StudioLighting.Safe(s.roll,0,-90,90));
        }
        void LateUpdate()
        {
            if(lastSelected!=selected) {blendPosition=Camera.transform.position;blendRotation=Camera.transform.rotation;blendFov=Camera.fieldOfView;blendStart=lastSelected<0?-100:Time.unscaledTime;lastSelected=selected;}
            if (playing) playhead += Time.unscaledDeltaTime;Evaluate(playhead);
            float t=transition<=0?1:Mathf.Clamp01((Time.unscaledTime-blendStart)/transition);if(t<1) {t=Mathf.SmoothStep(0,1,t);Camera.transform.position=Vector3.Lerp(blendPosition,Camera.transform.position,t);Camera.transform.rotation=Quaternion.Slerp(blendRotation,Camera.transform.rotation,t);Camera.fieldOfView=Mathf.Lerp(blendFov,Camera.fieldOfView,t);}
        }
        public void DrawUI()
        {
            GUILayout.Label("Câmeras de cinema");
            GUILayout.BeginHorizontal();if(GUILayout.Button("Corpo inteiro")) app.photo.Frame(0);if(GUILayout.Button("Meio corpo")) app.photo.Frame(1);if(GUILayout.Button("Retrato")) app.photo.Frame(2);GUILayout.EndHorizontal();
            if(GUILayout.Button("FOTO PNG • câmera selecionada")) app.photo.RequestCapture();
            if (GUILayout.Button("Criar câmera da vista atual")) Snapshot();
            for (int i = 0; i < shots.Count; i++) if (GUILayout.Button((selected == i ? "● " : "") + shots[i].name)) { selected = i; playhead = 0; }
            if (shots.Count == 0) return; selected = Mathf.Clamp(selected, 0, shots.Count - 1); var s = shots[selected];
            s.name = GUILayout.TextField(s.name); s.mode = GUILayout.SelectionGrid(s.mode, new[] { "Fixa", "Seguir", "Órbita", "Trajetória" }, 2);
            s.lookAt = GUILayout.Toggle(s.lookAt, "Olhar para o avatar"); s.targetOffset = StudioGui.Vector("Alvo relativo ao avatar", s.targetOffset);
            s.fov = StudioGui.Slider("Lente / campo de visão", s.fov, 10, 100);
            float mm=12/Mathf.Tan(s.fov*Mathf.Deg2Rad*.5f);float nextMm=StudioGui.Slider("Lente (mm • sensor vertical 24 mm)",mm,15,135);if(Mathf.Abs(nextMm-mm)>.01f) s.fov=2*Mathf.Atan(12/nextMm)*Mathf.Rad2Deg;
            s.roll=StudioGui.Slider("Inclinação / Dutch angle",s.roll,-45,45);transition=StudioGui.Slider("Troca suave de câmera (s)",transition,0,2);
            if (s.mode == 2) { s.orbitRadius = StudioGui.Slider("Raio", s.orbitRadius, .2f, 20); s.orbitHeight = StudioGui.Slider("Altura", s.orbitHeight, .1f, 8); s.orbitSpeed = StudioGui.Slider("Graus por segundo", s.orbitSpeed, -60, 60); }
            else if (s.mode != 3)
            {
                s.position = StudioGui.Vector(s.mode == 1 ? "Posição relativa ao avatar" : "Posição", s.position);
                if (!s.lookAt) s.euler = StudioGui.Vector("Rotação", s.euler);
            }
            if (s.mode == 3)
            {
                s.duration = StudioGui.Slider("Duração (s)", s.duration, .5f, 120); s.loop = GUILayout.Toggle(s.loop, "Repetir trajetória");
                if (GUILayout.Button("Adicionar ponto da vista atual")) s.keys.Add(new CameraKeyframe { position = app.room.view.transform.position, euler = app.room.view.transform.eulerAngles, fov = app.room.view.fieldOfView });
                for (int i = 0; i < s.keys.Count; i++)
                {
                    GUILayout.BeginHorizontal(); GUILayout.Label("Ponto " + (i + 1) + "  " + s.keys[i].position.ToString("F1"));
                    if (GUILayout.Button("×", GUILayout.Width(35))) { s.keys.RemoveAt(i); GUILayout.EndHorizontal(); break; } GUILayout.EndHorizontal();
                }
            }
            playing = GUILayout.Toggle(playing, "Reproduzir câmera");
            playhead = StudioGui.Slider("Tempo", playhead, 0, Mathf.Max(1, s.duration));
            if (GUILayout.Button("Reiniciar movimento")) playhead = 0;
            GUILayout.Label("A câmera selecionada é a saída da transmissão. O editor continua com sua própria câmera. Salve as câmeras junto com o estúdio na aba Cenário.");
            app.photo.DrawPreview(true);
            if (shots.Count > 1 && GUILayout.Button("Remover câmera")) { shots.RemoveAt(selected); selected = Mathf.Clamp(selected, 0, shots.Count - 1); }
        }
        void OnDestroy() { if (Camera != null) Destroy(Camera.gameObject); }
    }
}
