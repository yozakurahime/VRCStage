using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarLab
{
    [DefaultExecutionOrder(400)]
    public sealed class VideoOutput : MonoBehaviour
    {
        public const int Magic = 0x414C5632, Header = 64, Capacity = Header + 1920 * 1080 * 4;
        public LabApp app;
        public RenderTexture Texture { get; private set; }
        public bool Running => writer != null;
        public bool OutputWindowRunning=>viewer!=null && !viewer.HasExited;
        public int width = 1280, height = 720, frameRate = 30;
        public bool avatarOnly, greenScreen;
        public bool useSpout = true, transparent;
        public bool cpuReadback=true;
        public bool studioBackground=true;
        bool showPost;
        public bool OutputAlpha=>transparent || !greenScreen && studioBackground && app.photo.settings.background==2;
        public string SpoutName = "AvatarLab Studio";
        Klak.Spout.SpoutSender spout;
        public readonly ObsVirtualCamera obs = new ObsVirtualCamera();
        public string Status = "Saída local limpa para captura de janela/jogo no OBS.";
        public int Frames;
        public int RenderedFrames {get;private set;}
        public string Channel { get; private set; }
        MemoryMappedFile memory;
        MemoryMappedViewAccessor writer;
        byte[] buffer;
        bool pending;
        AsyncGPUReadbackRequest request;
        Texture2D fallback;
        float next;
        int sequence;
        Process viewer;
        public void StartOutput(bool launchWindow = true)
        {
            StopOutput();
            try
            {
                width = width >= 1920 ? 1920 : 1280; height = width == 1920 ? 1080 : 720; frameRate = Mathf.Clamp(frameRate, 15, 60);
                Channel = "AvatarLab.Video." + Guid.NewGuid().ToString("N");
                memory = MemoryMappedFile.CreateOrOpen(Channel, Capacity, MemoryMappedFileAccess.ReadWrite);
                writer = memory.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
                Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "AvatarLab Program Output" }; Texture.Create();
                buffer = new byte[width * height * 4]; Frames = RenderedFrames = 0; sequence = 0;
                writer.Write(0, Magic); writer.Write(4, width); writer.Write(8, height); writer.Write(16, buffer.Length);
                writer.Write(32,frameRate);
                app.cinema.Camera.targetTexture = Texture;
                if (useSpout)
                {
                    spout = gameObject.AddComponent<Klak.Spout.SpoutSender>(); spout.SetResources(app.spoutResources);
                    spout.captureMethod = Klak.Spout.CaptureMethod.Texture; spout.sourceTexture = Texture; spout.spoutName = SpoutName; spout.keepAlpha = OutputAlpha;
                }
                if (launchWindow && cpuReadback) OpenViewer();
                Status = "Saída ativa: " + width + "×" + height + " / " + frameRate + " FPS. No OBS escolha a janela AvatarLab VIDEO.";
            }
            catch (Exception ex) { StopOutput(); Status = ex.Message; }
        }
        public void OpenViewer()
        {
            if (!Running) return;
            cpuReadback=true;
            try
            {
                if (viewer != null && !viewer.HasExited) return;
                string executable = StageBrand.Executable;
                if (!File.Exists(executable)) { Status = "A janela de vídeo é aberta no build Windows."; return; }
                string log = Path.Combine(Application.persistentDataPath, "video-viewer.log");
                viewer = Process.Start(new ProcessStartInfo(executable, "--video-viewer --video-channel " + Channel + " -screen-fullscreen 0 -screen-width " + width + " -screen-height " + height + " -logFile \"" + log + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { Status = ex.Message; }
        }
        void LateUpdate()
        {
            using var timing=LabPerformance.Measure("Video");
            if (!Running || pending || Time.unscaledTime < next || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            next = Time.unscaledTime + 1f / frameRate;
            var camera = app.cinema.Camera;
            if (spout != null) spout.keepAlpha = OutputAlpha;
            app.photo.RenderTo(Texture,avatarOnly,transparent?2:greenScreen?3:studioBackground?app.photo.settings.background:0,app.photo.settings.color);
            RenderedFrames++;
            if(!cpuReadback) return;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                pending = true;
                request = AsyncGPUReadback.Request(Texture, 0, TextureFormat.RGBA32, result =>
                {
                    try { if (!result.hasError && writer != null) { result.GetData<byte>().CopyTo(buffer); Publish(buffer); } }
                    catch (Exception ex) { Status = ex.Message; }
                    finally { pending = false; }
                });
            }
            else
            {
                if (fallback == null) fallback = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var old = RenderTexture.active;
                try { RenderTexture.active = Texture; fallback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); fallback.Apply(false); fallback.GetRawTextureData<byte>().CopyTo(buffer); Publish(buffer); }
                finally { RenderTexture.active = old; }
            }
        }
        public void Publish(byte[] pixels)
        {
            using var timing=LabPerformance.Measure("VideoCopy");
            if (writer == null || pixels.Length != width * height * 4) return;
            sequence += 2; writer.Write(12, sequence - 1); System.Threading.Thread.MemoryBarrier();
            CopyPixels(writer,pixels,true); writer.Write(20, ++Frames);
            writer.Write(24, DateTime.UtcNow.Ticks); System.Threading.Thread.MemoryBarrier(); writer.Write(12, sequence);
        }
        public static void CopyPixels(MemoryMappedViewAccessor view,byte[] pixels,bool write)
        {
            bool retained=false; var handle=view.SafeMemoryMappedViewHandle;
            try
            {
                handle.DangerousAddRef(ref retained);
                var pointer=IntPtr.Add(handle.DangerousGetHandle(),checked((int)view.PointerOffset+Header));
                if(write) Marshal.Copy(pixels,0,pointer,pixels.Length); else Marshal.Copy(pointer,pixels,0,pixels.Length);
            }
            finally { if(retained) handle.DangerousRelease(); }
        }
        public void StopOutput()
        {
            if (pending && !request.done) request.WaitForCompletion(); pending = false;
            if (writer != null) { try { writer.Write(0, 0); } catch { } writer.Dispose(); writer = null; }
            memory?.Dispose(); memory = null;
            if (app != null && app.cinema != null && app.cinema.Camera != null) app.cinema.Camera.targetTexture = null;
            if (spout != null) { spout.enabled = false; Destroy(spout); spout = null; }
            if (Texture != null) { Texture.Release(); Destroy(Texture); Texture = null; }
            if (fallback != null) { Destroy(fallback); fallback = null; }
            if (viewer != null) { try { if (!viewer.HasExited) viewer.CloseMainWindow(); } catch { } viewer.Dispose(); viewer = null; }
        }
        public void DrawUI()
        {
            GUILayout.Label("Vídeo para livestream"); GUILayout.Label(Status);
            if (!Running)
            {
                useSpout = GUILayout.Toggle(useSpout, "Ativar Spout nativo: AvatarLab Studio");
                cpuReadback=GUILayout.Toggle(cpuReadback,"Janela de vídeo para captura no OBS");
                GUILayout.Label("Desmarque a janela se usar somente um receptor Spout.");
            }
            if (!Running)
            {
                int choice = GUILayout.SelectionGrid(width == 1920 ? 1 : 0, new[] { "1280 × 720", "1920 × 1080" }, 2); width = choice == 1 ? 1920 : 1280;
                frameRate = Mathf.RoundToInt(StudioGui.Slider("FPS", frameRate, 15, 60));
                if (GUILayout.Button("INICIAR SAÍDA / abrir janela OBS")) StartOutput();
            }
            else
            {
                GUILayout.Label("Quadros renderizados: " + RenderedFrames);
                if (GUILayout.Button("Abrir janela de vídeo")) OpenViewer();
                if (GUILayout.Button("Parar saída")) { StopOutput(); Status = "Saída parada."; }
            }
            avatarOnly = GUILayout.Toggle(avatarOnly, "Mostrar somente o avatar na saída"); greenScreen = GUILayout.Toggle(greenScreen, "Fundo verde para chroma key");
            studioBackground=GUILayout.Toggle(studioBackground,"Usar fundo configurado em Luz / foto");
            transparent = GUILayout.Toggle(transparent, "Fundo transparente / preservar alpha no Spout");
            if(GUILayout.Button(showPost?"Ocultar pós-processamento":"Configurar pós-processamento da câmera")) showPost=!showPost;
            if(showPost) app.photo.DrawPostUI();
            GUILayout.Label("No OBS: adicione Captura de janela ou Captura de jogo e escolha AvatarLab VIDEO. Os painéis e o cursor do editor não fazem parte dessa imagem. Escolha a câmera na aba Cinema.");
            GUILayout.Label("Áudio: adicione seu microfone/dispositivo virtual como fonte no OBS. Esta saída transporta vídeo local, sem iniciar uma transmissão nem enviar dados à internet.");
            GUILayout.Label("Spout: selecione AvatarLab Studio em um receptor Spout. Câmera virtual: use OBS Virtual Camera ou o SpoutCam instalado. O programa não registra drivers automaticamente.");
            if (GUILayout.Button("Preparar OBS e iniciar câmera virtual"))
            {
                if (!Running) StartOutput(); else OpenViewer();
                obs.PrepareAndStart(width, height, frameRate);
            }
            if (GUILayout.Button("Parar câmera virtual do OBS")) obs.Stop();
            GUILayout.Label(obs.Status);
            if (Texture != null) { var rect = GUILayoutUtility.GetAspectRect(16f / 9f); GUI.DrawTexture(rect, Texture, ScaleMode.ScaleToFit, false); }
        }
        void OnDestroy() { StopOutput(); }
    }
    public sealed class CleanVideoViewer : MonoBehaviour
    {
        string channel;
        MemoryMappedFile memory;
        MemoryMappedViewAccessor reader;
        Texture2D texture;
        byte[] buffer;
        int lastSequence = -1;
        float retry;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr handle, string title);
        public void Configure(string name) { channel = name; }
        IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 30; QualitySettings.vSyncCount=0;
            var camera = new GameObject("Video Viewer Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 0;
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            try { SetWindowText(Process.GetCurrentProcess().MainWindowHandle, "AvatarLab VIDEO - OBS"); } catch { }
        }
        void Update()
        {
            if (reader == null)
            {
                if (Time.unscaledTime < retry) return; retry = Time.unscaledTime + .5f;
                try { memory = MemoryMappedFile.OpenExisting(channel, MemoryMappedFileRights.Read); reader = memory.CreateViewAccessor(0, VideoOutput.Capacity, MemoryMappedFileAccess.Read); }
                catch { memory?.Dispose(); memory = null; return; }
            }
            try
            {
                if (reader.ReadInt32(0) != VideoOutput.Magic) return;
                int sequence = reader.ReadInt32(12); if (sequence == lastSequence || sequence % 2 != 0) return;
                int width = reader.ReadInt32(4), height = reader.ReadInt32(8), length = reader.ReadInt32(16);
                if (width < 1 || width > 1920 || height < 1 || height > 1080 || length != width * height * 4) return;
                if (texture == null || texture.width != width || texture.height != height)
                {
                    if (texture != null) Destroy(texture); texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false); buffer = new byte[length];
                }
                VideoOutput.CopyPixels(reader,buffer,false); System.Threading.Thread.MemoryBarrier(); if (reader.ReadInt32(12) != sequence) return;
                int fps=reader.ReadInt32(32); Application.targetFrameRate=fps>=15 && fps<=60?fps:30;
                texture.LoadRawTextureData(buffer); texture.Apply(false, false); lastSequence = sequence;
            }
            catch { reader?.Dispose(); reader = null; memory?.Dispose(); memory = null; }
        }
        void OnGUI()
        {
            GUI.matrix = Matrix4x4.identity;
            if (texture != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texture, ScaleMode.ScaleToFit, false);
        }
        void OnDestroy() { reader?.Dispose(); memory?.Dispose(); if (texture != null) Destroy(texture); }
    }
}
