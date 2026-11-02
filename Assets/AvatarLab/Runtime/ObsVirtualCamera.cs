using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarLab
{
    public sealed class ObsVirtualCamera
    {
        public string Status = "Câmera virtual usa o dispositivo do OBS já instalado, ou SpoutCam como receptor Spout.";
        public string Executable = File.Exists(@"D:\Obs\obs-studio\bin\64bit\obs64.exe") ? @"D:\Obs\obs-studio\bin\64bit\obs64.exe" : @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
        const string Collection = "AvatarLab Offline", Scene = "AvatarLab VIDEO";
        public bool Busy;
        [Serializable] sealed class WsConfig { public bool server_enabled; public int server_port = 4455; public bool auth_required = true; public string server_password; }
        [Serializable] sealed class Auth { public string challenge, salt; }
        [Serializable] sealed class RequestStatus { public bool result; public int code; public string comment; }
        [Serializable] sealed class Response { public bool outputActive; public string currentSceneCollectionName; }
        [Serializable] sealed class Data { public Auth authentication; public RequestStatus requestStatus; public Response responseData; public string requestId; }
        [Serializable] sealed class Message { public int op; public Data d; }
        public static string ObsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
        public void PrepareAndStart(int width, int height, int fps)
        {
            if (Busy) return;
            try
            {
                if (!File.Exists(Executable)) { Status = "OBS não encontrado. Instale o OBS ou use SpoutCam; a saída Spout já pode ser usada."; return; }
                WriteCollection(width, height, fps);
                if (Process.GetProcessesByName("obs64").Length > 0) { _ = StartExisting(); return; }
                Process.Start(new ProcessStartInfo(Executable, "--profile \"" + Collection + "\" --collection \"" + Collection + "\" --scene \"" + Scene + "\" --startvirtualcam") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Executable) });
                Status = "OBS iniciado com coleção própria e câmera virtual. Selecione OBS Virtual Camera no aplicativo de destino.";
            }
            catch (Exception ex) { Status = ex.Message; }
        }
        public static void WriteCollection(int width, int height, int fps)
        {
            string scenes = Path.Combine(ObsDirectory, "basic", "scenes"), profiles = Path.Combine(ObsDirectory, "basic", "profiles", "AvatarLabOffline");
            Directory.CreateDirectory(scenes); Directory.CreateDirectory(profiles);
            string file = Path.Combine(scenes, "AvatarLab_Offline.json");
            if (!File.Exists(file))
            {
                string sourceId = Guid.NewGuid().ToString(), sceneId = Guid.NewGuid().ToString();
                string source = "{\"name\":\"AvatarLab Video Source\",\"uuid\":" + OscQueryBridge.Quote(sourceId) + ",\"id\":\"window_capture\",\"versioned_id\":\"window_capture\",\"settings\":{\"window\":"+OscQueryBridge.Quote("AvatarLab VIDEO - OBS:UnityWndClass:"+Path.GetFileName(StageBrand.Executable))+",\"method\":2,\"client_area\":true,\"cursor\":false,\"priority\":0,\"capture_audio\":false},\"mixers\":0,\"volume\":1.0,\"enabled\":true}";
                string item = "{\"name\":\"AvatarLab Video Source\",\"source_uuid\":" + OscQueryBridge.Quote(sourceId) + ",\"visible\":true,\"locked\":true,\"rot\":0.0,\"pos\":{\"x\":0.0,\"y\":0.0},\"scale\":{\"x\":1.0,\"y\":1.0},\"align\":5,\"bounds_type\":2,\"bounds_align\":0,\"bounds\":{\"x\":" + width + ",\"y\":" + height + "},\"id\":1}";
                string scene = "{\"name\":" + OscQueryBridge.Quote(Scene) + ",\"uuid\":" + OscQueryBridge.Quote(sceneId) + ",\"id\":\"scene\",\"versioned_id\":\"scene\",\"settings\":{\"id_counter\":1,\"custom_size\":false,\"items\":[" + item + "]},\"mixers\":0}";
                string json = "{\"name\":" + OscQueryBridge.Quote(Collection) + ",\"current_scene\":" + OscQueryBridge.Quote(Scene) + ",\"current_program_scene\":" + OscQueryBridge.Quote(Scene) + ",\"sources\":[" + source + "," + scene + "],\"scene_order\":[{\"name\":" + OscQueryBridge.Quote(Scene) + "}],\"groups\":[],\"transitions\":[],\"quick_transitions\":[]}";
                File.WriteAllText(file, json, new UTF8Encoding(false));
            }
            string ini = Path.Combine(profiles, "basic.ini");
            if (!File.Exists(ini)) File.WriteAllText(ini, "[General]\nName=" + Collection + "\n\n[Video]\nBaseCX=" + width + "\nBaseCY=" + height + "\nOutputCX=" + width + "\nOutputCY=" + height + "\nFPSType=0\nFPSCommon=" + fps + "\nScaleType=bicubic\nColorFormat=NV12\nColorSpace=709\nColorRange=Partial\n\n[Audio]\nSampleRate=48000\nChannelSetup=Stereo\n\n[Output]\nMode=Simple\n", new UTF8Encoding(false));
        }
        async Task StartExisting()
        {
            Busy = true;
            try
            {
                using (var client = await Connect())
                {
                    var stream = await Request(client, "GetStreamStatus"); var recording = await Request(client, "GetRecordStatus");
                    if (stream.outputActive || recording.outputActive) { Status = "OBS está transmitindo/gravando. A coleção atual foi preservada; escolha AvatarLab Offline manualmente para usar esta câmera."; return; }
                    await Request(client, "SetCurrentSceneCollection", "{\"sceneCollectionName\":" + OscQueryBridge.Quote(Collection) + "}");
                    await Request(client, "SetCurrentProgramScene", "{\"sceneName\":" + OscQueryBridge.Quote(Scene) + "}");
                    await Request(client, "StartVirtualCam"); Status = "OBS Virtual Camera iniciada com a cena AvatarLab VIDEO.";
                }
            }
            catch (Exception ex) { Status = "OBS já está aberto: selecione a coleção AvatarLab Offline e clique em Iniciar câmera virtual. Controle automático: " + ex.Message; }
            finally { Busy = false; }
        }
        public async void Stop()
        {
            if (Busy) return; Busy = true;
            try { using (var client = await Connect()) { await Request(client, "StopVirtualCam"); Status = "Câmera virtual parada."; } }
            catch (Exception ex) { Status = "No OBS, clique em Parar câmera virtual. Controle automático: " + ex.Message; }
            finally { Busy = false; }
        }
        static async Task<ClientWebSocket> Connect()
        {
            string configPath = Path.Combine(ObsDirectory, "plugin_config", "obs-websocket", "config.json");
            if (!File.Exists(configPath)) throw new Exception("servidor WebSocket do OBS não configurado");
            var config = JsonUtility.FromJson<WsConfig>(File.ReadAllText(configPath));
            if (config == null || !config.server_enabled) throw new Exception("ative o servidor WebSocket nas ferramentas do OBS, se quiser controle automático");
            var client = new ClientWebSocket();
            try
            {
                using (var timeout = new CancellationTokenSource(4000)) await client.ConnectAsync(new Uri("ws://127.0.0.1:" + config.server_port), timeout.Token);
                var hello = await Receive(client); string auth = "";
                if (hello.d != null && hello.d.authentication != null)
                {
                    string secret = Digest((config.server_password ?? "") + hello.d.authentication.salt);
                    auth = ",\"authentication\":" + OscQueryBridge.Quote(Digest(secret + hello.d.authentication.challenge));
                }
                await Send(client, "{\"op\":1,\"d\":{\"rpcVersion\":1,\"eventSubscriptions\":0" + auth + "}}");
                if ((await Receive(client)).op != 2) throw new Exception("autenticação OBS não concluída"); return client;
            }
            catch { client.Dispose(); throw; }
        }
        static string Digest(string value) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static async Task<Response> Request(ClientWebSocket client, string type, string data = "{}")
        {
            string id = Guid.NewGuid().ToString("N");
            await Send(client, "{\"op\":6,\"d\":{\"requestType\":" + OscQueryBridge.Quote(type) + ",\"requestId\":" + OscQueryBridge.Quote(id) + ",\"requestData\":" + data + "}}");
            for (int i = 0; i < 30; i++)
            {
                var response = await Receive(client); if (response.op != 7 || response.d == null || response.d.requestId != id) continue;
                if (response.d.requestStatus == null || !response.d.requestStatus.result) throw new Exception(response.d.requestStatus?.comment ?? "requisição OBS recusada");
                return response.d.responseData ?? new Response();
            }
            throw new Exception("OBS não respondeu");
        }
        static async Task Send(ClientWebSocket client, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json); using (var timeout = new CancellationTokenSource(4000)) await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
        }
        static async Task<Message> Receive(ClientWebSocket client)
        {
            using (var memory = new MemoryStream()) using (var timeout = new CancellationTokenSource(4000))
            {
                var buffer = new byte[4096]; WebSocketReceiveResult result;
                do { result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token); if (result.MessageType == WebSocketMessageType.Close) throw new Exception("OBS encerrou a conexão"); memory.Write(buffer, 0, result.Count); if (memory.Length > 1024 * 1024) throw new Exception("resposta OBS muito grande"); } while (!result.EndOfMessage);
                return JsonUtility.FromJson<Message>(Encoding.UTF8.GetString(memory.ToArray()));
            }
        }
    }
}
