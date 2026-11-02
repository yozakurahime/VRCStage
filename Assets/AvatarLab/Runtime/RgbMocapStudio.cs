using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarLab
{
    [DefaultExecutionOrder(80)]
    public sealed class RgbMocapStudio : MonoBehaviour
    {
        [Serializable] sealed class Settings {public string rgbRoot="";}
        public LabApp app;public bool travel;
        public string Status="Importe uma captura .almotion ou converta uma prévia do RGB MoCap.";
        public string MotionDirectory=>Path.Combine(Application.persistentDataPath,"Motions");
        string SettingsPath=>Path.Combine(Application.persistentDataPath,"Profiles","rgb-mocap.json");
        public string RgbRoot="";
        string metadata="",takeName="Minha captura",start="0",end="";
        bool expanded,startupUsed;Task<Loaded> loading;Task<string> conversion;Process converter;
        Task<string[]> scanning;string projectRoot="";string[] previews=Array.Empty<string>();int previewIndex;
        int playGeneration,pendingGeneration,conversionGeneration;PoseStudio.Item pendingItem;string pendingAvatar,conversionAvatar;
        sealed class Loaded {public string path;public RgbMotion data;}
        public bool Busy=>loading!=null || conversion!=null || scanning!=null;
        public string BridgePath=>Path.Combine(Path.GetDirectoryName(Application.dataPath),"MocapBridge","AvatarLab RGB Motion.exe");
        void Awake()
        {
            try {if(File.Exists(SettingsPath)) RgbRoot=JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)).rgbRoot;} catch(Exception ex) {Status=ex.Message;}
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--rgb-root");if(at>=0 && at+1<args.Length) {RgbRoot=args[at+1];SaveSettings();}
            if(string.IsNullOrEmpty(RgbRoot))
            {
                string local=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../External/RGBMoCap"));
                if(File.Exists(Path.Combine(local,"RGBMoCap.cmd"))) RgbRoot=local;
            }
        }
        void Update()
        {
            if(scanning!=null && scanning.IsCompleted)
            {
                var done=scanning;scanning=null;
                if(done.IsFaulted) Status=done.Exception.GetBaseException().Message;
                else {previews=done.Result;previewIndex=0;metadata=previews.FirstOrDefault()??"";Status=previews.Length>0?"Escolha a captura e o resultado que deseja adicionar.":"Nenhuma captura preparada neste projeto. Use Reconstruct 3D e Prepare Preview no RGB MoCap.";}
            }
            if(!startupUsed && app.driver!=null && app.poses.Items.Count>0)
            {
                startupUsed=true;var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--motion");
                if(at>=0 && at+1<args.Length) Import(args[at+1]);
            }
            if(conversion!=null && conversion.IsCompleted)
            {
                var done=conversion;conversion=null;converter?.Dispose();converter=null;
                if(done.IsFaulted) Status=done.Exception.GetBaseException().Message;
                else if(!done.IsCanceled) {ImportInternal(done.Result,conversionGeneration==playGeneration && conversionAvatar==app.AvatarId);Status="Conversão concluída; validando a captura...";}
            }
            if(loading!=null && loading.IsCompleted)
            {
                var done=loading;loading=null;
                if(done.IsFaulted) {Status=done.Exception.GetBaseException().Message;return;}
                if(done.IsCanceled) return;
                var loaded=done.Result;RefreshItems();
                if(pendingGeneration!=playGeneration || pendingAvatar!=app.AvatarId) {Status="Captura disponível na biblioteca.";return;}
                var item=pendingItem??app.poses.Items.FirstOrDefault(i=>i.motionPath==loaded.path);
                if(item==null) return;
                item.label=loaded.data.Info.name;item.motion=loaded.data;
                try {if(app.poses.Play(item)) Status="Captura pronta: "+item.label+" • "+loaded.data.Info.frameCount+" quadros";}
                catch(Exception ex) {item.motion=null;Status=ex.Message;}
                pendingItem=null;
            }
        }
        public void CancelPendingPlay() {playGeneration++;}
        public void RefreshItems()
        {
            app.poses.Items.RemoveAll(i=>i.motionPath!=null && i!=app.poses.Selected && !File.Exists(i.motionPath));
            if(!Directory.Exists(MotionDirectory)) return;
            foreach(string file in Directory.GetFiles(MotionDirectory,"*.almotion").OrderBy(p=>p))
                if(!app.poses.Items.Any(i=>i.motionPath==file)) app.poses.Items.Add(new PoseStudio.Item{id="motion:"+Path.GetFileNameWithoutExtension(file),label=Path.GetFileNameWithoutExtension(file),category="Mocap / vídeo",motionPath=file});
        }
        public void RequestPlay(PoseStudio.Item item)
        {
            if(Busy) {Status="Aguarde a operação atual.";return;}
            pendingItem=item;pendingGeneration=++playGeneration;pendingAvatar=app.AvatarId;Status="Carregando captura...";
            loading=Task.Run(()=>new Loaded{path=item.motionPath,data=RgbMotion.Read(item.motionPath)});
        }
        public void Import(string path)=>ImportInternal(path,true);
        void ImportInternal(string path,bool autoPlay)
        {
            if(Busy) {Status="Aguarde a operação atual.";return;}
            string directory=MotionDirectory;pendingItem=null;pendingGeneration=autoPlay?++playGeneration:-1;pendingAvatar=app.AvatarId;Status="Validando movimento...";
            loading=Task.Run(()=>
            {
                Directory.CreateDirectory(directory);string temp=Path.Combine(directory,"incoming-"+Guid.NewGuid().ToString("N")+".tmp");
                try
                {
                    if(new FileInfo(path).Length>64*1024*1024) throw new InvalidDataException("Movimento excede 64 MiB.");
                    File.Copy(path,temp);var data=RgbMotion.Read(temp);string hash;
                    using(var input=File.OpenRead(temp)) using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant().Substring(0,16);
                    string name=new string((data.Info.name??"Captura").Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray()).Trim();if(name.Length==0) name="Captura";
                    name=name.Substring(0,Math.Min(80,name.Length));string destination=Path.Combine(directory,name+"-"+hash+".almotion");
                    AvatarArchive.CommitFile(temp,destination);return new Loaded{path=destination,data=data};
                }
                finally {if(File.Exists(temp)) File.Delete(temp);}
            });
        }
        static string Quote(string value)
        {
            // All values are arguments to an executable; no command shell is used.
            if(value.IndexOf('"')>=0 || value.IndexOf('\n')>=0 || value.IndexOf('\r')>=0) throw new ArgumentException("Valor contém aspas ou quebra de linha.");
            return "\""+value+(value.EndsWith("\\")?"\\":"")+"\"";
        }
        public void Convert()
        {
            if(Busy) return;
            try
            {
                if(!File.Exists(BridgePath)) throw new FileNotFoundException("Ponte RGB Motion ausente; extraia o pacote completo.");
                if(!File.Exists(Path.Combine(RgbRoot,"mocap","mixamo_data.py"))) throw new DirectoryNotFoundException("Escolha a instalação do RGB MoCap.");
                if(!File.Exists(metadata)) throw new FileNotFoundException("Escolha um arquivo *_mixamo.json de uma prévia preparada.");
                if(!int.TryParse(start,out int first) || first<0 || end.Length>0 && (!int.TryParse(end,out int last) || last<=first)) throw new ArgumentException("Informe quadros de corte válidos; deixe o fim vazio para usar até o final.");
                string output=Path.Combine(Application.temporaryCachePath,"rgb-"+Guid.NewGuid().ToString("N")+".almotion");
                var info=new ProcessStartInfo(BridgePath,"--rgb-root "+Quote(RgbRoot)+" --metadata "+Quote(metadata)+" --output "+Quote(output)+" --start "+first+(end.Length>0?" --end "+int.Parse(end):"")+" --name "+Quote(takeName)) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
                converter=Process.Start(info);var process=converter;Status="Convertendo captura em processo separado...";
                conversionGeneration=playGeneration;conversionAvatar=app.AvatarId;
                conversion=Task.Run(async()=>
                {
                    var error=process.StandardError.ReadToEndAsync();var result=process.StandardOutput.ReadToEndAsync();
                    await Task.Run(()=>process.WaitForExit());string detail=await error;await result;
                    if(process.ExitCode!=0) throw new InvalidOperationException(detail.Length>1200?detail.Substring(detail.Length-1200):detail);
                    return output;
                });
            }
            catch(Exception ex) {Status=ex.Message;}
        }
        public void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));string temp=SettingsPath+".tmp";
            File.WriteAllText(temp,JsonUtility.ToJson(new Settings{rgbRoot=RgbRoot}));AvatarArchive.CommitFile(temp,SettingsPath);
        }
        void ScanProject(string folder)
        {
            if(Busy) return;projectRoot=folder;Status="Lendo capturas do projeto...";
            scanning=Task.Run(()=>Directory.EnumerateFiles(folder,"*_mixamo.json",SearchOption.AllDirectories).Take(257).ToArray());
        }
        void LaunchRgb()
        {
            try
            {
                string launcher=Path.Combine(RgbRoot,"RGBMoCap.cmd");if(!File.Exists(launcher)) throw new FileNotFoundException("Escolha a instalação do RGB MoCap.");
                Process.Start(new ProcessStartInfo(launcher){UseShellExecute=true,WorkingDirectory=RgbRoot});Status="RGB MoCap aberto. Prepare a captura e depois escolha a prévia aqui.";
            }
            catch(Exception ex) {Status=ex.Message;}
        }
        public void DrawUI()
        {
            expanded=GUILayout.Toggle(expanded,"Mocap de vídeo • RGB MoCap");
            if(!expanded) return;
            GUILayout.Label(Status);
            if(GUILayout.Button("Importar animação .almotion")) NativeFilePicker.PickAsync(Import,"Movimento AvatarLab\0*.almotion\0\0","Importar captura de movimento");
            travel=GUILayout.Toggle(travel,"Aplicar deslocamento visual da captura");
            GUILayout.Label("RGB MoCap • instalação");RgbRoot=GUILayout.TextField(RgbRoot);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Localizar RGB MoCap")) NativeFilePicker.PickAsync(p=>{RgbRoot=Path.GetDirectoryName(p);SaveSettings();},"RGB MoCap\0RGBMoCap.cmd\0\0","Localizar RGBMoCap.cmd");
            if(GUILayout.Button("Abrir RGB MoCap")) {SaveSettings();LaunchRgb();}
            GUILayout.EndHorizontal();
            GUILayout.Label("Prepare o vídeo no RGB MoCap e use Prepare Preview / Apply Correction. Escolha a prévia do ator e resultado desejados.");
            if(GUILayout.Button("Abrir projeto RGB MoCap")) NativeFilePicker.PickAsync(p=>ScanProject(Path.GetDirectoryName(p)),"Projeto RGB MoCap\0project.json\0\0","Abrir projeto RGB MoCap salvo");
            if(previews.Length>0)
            {
                var labels=previews.Select(p=>p.Substring(Math.Min(p.Length,projectRoot.Length+1)).Replace("_mixamo.json","")).ToArray();
                int next=GUILayout.SelectionGrid(previewIndex,labels,1);if(next!=previewIndex) {previewIndex=next;metadata=previews[next];}
                if(GUILayout.Button("Atualizar capturas do projeto")) ScanProject(projectRoot);
            }
            if(GUILayout.Button("Escolher prévia individual (avançado)")) NativeFilePicker.PickAsync(p=>{metadata=p;previews=Array.Empty<string>();},"Prévia RGB MoCap\0*_mixamo.json\0\0","Escolher prévia do ator / resultado");
            if(!string.IsNullOrEmpty(metadata)) GUILayout.Label("Captura: "+Path.GetFileName(metadata).Replace("_mixamo.json",""));
            GUILayout.Label("Nome da animação");takeName=GUILayout.TextField(takeName,80);
            GUILayout.BeginHorizontal();GUILayout.Label("Quadro inicial");start=GUILayout.TextField(start,8);GUILayout.Label("Final");end=GUILayout.TextField(end,8);GUILayout.EndHorizontal();
            GUI.enabled=!Busy;
            if(GUILayout.Button("Converter e adicionar à biblioteca")) Convert();
            GUI.enabled=true;
            if(GUILayout.Button("Abrir pasta de animações")) {Directory.CreateDirectory(MotionDirectory);Application.OpenURL(MotionDirectory);}
            GUILayout.Label("A IA roda no RGB MoCap. Esta ponte lê movimentos já preparados, sem FBX. A preparação dos modelos é feita no Model Manager.");
        }
        void OnDestroy()
        {
            CancelPendingPlay();if(converter!=null) {try {if(!converter.HasExited) converter.Kill();} catch(InvalidOperationException) {} converter.Dispose();converter=null;}
        }
    }
}
