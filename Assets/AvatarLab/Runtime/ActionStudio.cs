using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarLab
{
    public enum StudioActionKind {Event,Hotkey,Delay,SetParameter,ToggleParameter,FaceChannel,PlayMotion,StopMotion,Camera,Light,Throw,Drop,ClearProps,RecordStart,RecordStop,VideoStart,VideoStop,SendVNyan}
    [Serializable] public sealed class StudioActionNode
    {
        public int id;public StudioActionKind kind;public string text="";public float value=1;public bool eventValue;
        public Vector2 position;public int[] next=Array.Empty<int>();
    }
    [Serializable] public sealed class StudioActionGraph
    {
        public int version=1;public string id=Guid.NewGuid().ToString("N"),name="Minhas ações";public bool enabled=true;
        public List<StudioActionNode> nodes=new List<StudioActionNode>();
    }
    [DefaultExecutionOrder(30)]
    public sealed class ActionStudio : MonoBehaviour
    {
        [Serializable] sealed class EventCommand {public string @event,command;public float value=1;}
        struct Job {public StudioActionGraph graph;public int node;public float value,time;public int hops;}
        public LabApp app;public readonly LocalActionSocket socket=new LocalActionSocket();
        public readonly List<StudioActionGraph> Graphs=new List<StudioActionGraph>();
        public bool GraphOpen;public string Status="Ações e animações locais do AvatarLab.";bool showConnections;
        public int port=8010;public string vnEndpoint="ws://127.0.0.1:8000/vnyan";
        public int Executed {get;private set;}public int Dropped {get;private set;}public int Pending=>jobs.Count;
        public string DirectoryPath=>Path.Combine(Application.persistentDataPath,"Actions");
        readonly List<Job> jobs=new List<Job>();Task outbound;int selected,nodeSelected=-1,linkFrom=-1;string portText="8010",testEvent="wave";float testValue=1;
        Vector2 canvasScroll;int draggedNode=-1;Rect window=new Rect(455,45,900,750);readonly Dictionary<int,Rect> rectangles=new Dictionary<int,Rect>();
        static readonly string[] KindLabels={"Evento","Atalho","Aguardar","Definir parâmetro","Alternar parâmetro","Canal facial","Reproduzir animação","Parar animação","Escolher câmera","Luz","Arremessar objetos","Soltar objetos","Limpar objetos","Iniciar gravação","Salvar gravação","Iniciar vídeo","Parar vídeo","Comando externo (legado)"};
        static readonly string[] LocalKindLabels=KindLabels.Take(17).ToArray();
        StudioActionGraph Current=>Graphs.Count==0?null:Graphs[Mathf.Clamp(selected,0,Graphs.Count-1)];
        void Awake() {Load();if(Graphs.Count==0) Examples();}
        public void Examples()
        {
            var wave=new StudioActionGraph{name="Acenar por evento"};
            wave.nodes.Add(new StudioActionNode{id=0,kind=StudioActionKind.Event,text="wave",position=new Vector2(30,30),next=new[]{1}});
            wave.nodes.Add(new StudioActionNode{id=1,kind=StudioActionKind.PlayMotion,text="wave",position=new Vector2(300,30),next=new[]{2}});
            wave.nodes.Add(new StudioActionNode{id=2,kind=StudioActionKind.Delay,value=2,position=new Vector2(570,30),next=new[]{3}});
            wave.nodes.Add(new StudioActionNode{id=3,kind=StudioActionKind.StopMotion,position=new Vector2(840,30)});Graphs.Add(wave);
            var party=new StudioActionGraph{name="Festa local"};
            party.nodes.Add(new StudioActionNode{id=0,kind=StudioActionKind.Event,text="party",position=new Vector2(30,180),next=new[]{1,2}});
            party.nodes.Add(new StudioActionNode{id=1,kind=StudioActionKind.Light,value=2,position=new Vector2(300,120)});
            party.nodes.Add(new StudioActionNode{id=2,kind=StudioActionKind.Drop,value=8,position=new Vector2(300,260)});Graphs.Add(party);
        }
        public static void Validate(StudioActionGraph graph)
        {
            if(graph==null || graph.version!=1 || graph.nodes==null || graph.nodes.Count>128 || string.IsNullOrWhiteSpace(graph.name) || graph.name.Length>100) throw new InvalidDataException("Grafo inválido.");
            var ids=new HashSet<int>();
            foreach(var n in graph.nodes) if(n==null || n.id<0 || !ids.Add(n.id) || !Enum.IsDefined(typeof(StudioActionKind),n.kind) || n.text==null || n.text.Length>512 || float.IsNaN(n.value) || float.IsInfinity(n.value) || n.next==null || n.next.Length>16 || float.IsNaN(n.position.x+n.position.y) || float.IsInfinity(n.position.x+n.position.y)) throw new InvalidDataException("Nó inválido.");
            foreach(var n in graph.nodes) if(n.next.Any(i=>!ids.Contains(i))) throw new InvalidDataException("Ligação para nó inexistente.");
            // Cycles are allowed only if they pass through a timed node.
            var visiting=new HashSet<int>();var done=new HashSet<int>();
            void Walk(int id)
            {
                if(done.Contains(id)) return;if(!visiting.Add(id)) throw new InvalidDataException("Ciclo sem espera: adicione Aguardar.");
                var n=graph.nodes.First(p=>p.id==id);if(n.kind!=StudioActionKind.Delay) foreach(int next in n.next) Walk(next);
                visiting.Remove(id);done.Add(id);
            }
            foreach(var n in graph.nodes) Walk(n.id);
        }
        public string Save(StudioActionGraph graph)
        {
            Validate(graph);if(!Guid.TryParseExact(graph.id,"N",out _)) graph.id=Guid.NewGuid().ToString("N");Directory.CreateDirectory(DirectoryPath);
            string path=Path.Combine(DirectoryPath,graph.id+".algraph.json"),temp=path+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(graph,true));AvatarArchive.CommitFile(temp,path);Status="Grafo salvo: "+graph.name;return path;
        }
        public void Import(string path)
        {
            if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException("Grafo excede 1 MiB.");
            var graph=JsonUtility.FromJson<StudioActionGraph>(File.ReadAllText(path));Validate(graph);graph.id=Guid.NewGuid().ToString("N");Save(graph);Graphs.Add(graph);selected=Graphs.Count-1;
        }
        public void Load()
        {
            Cancel();Graphs.Clear();if(!Directory.Exists(DirectoryPath)) return;
            foreach(var path in Directory.EnumerateFiles(DirectoryPath,"*.algraph.json").Take(128))
                try {if(new FileInfo(path).Length>1024*1024) continue;var graph=JsonUtility.FromJson<StudioActionGraph>(File.ReadAllText(path));Validate(graph);Graphs.Add(graph);}catch(Exception ex) {Status=Path.GetFileName(path)+": "+ex.Message;}
            selected=0;
        }
        public void Fire(string name,float value=1)
        {
            if(string.IsNullOrWhiteSpace(name) || name.Length>128 || float.IsNaN(value) || float.IsInfinity(value)) {Dropped++;return;}
            foreach(var graph in Graphs) if(graph.enabled) foreach(var node in graph.nodes) if(node.kind==StudioActionKind.Event && node.text==name) Enqueue(new Job{graph=graph,node=node.id,value=value,time=Time.unscaledTime});
        }
        public void Command(string command)
        {
            try
            {
                if(command.StartsWith("{",StringComparison.Ordinal)) {var data=JsonUtility.FromJson<EventCommand>(command);Fire(data.@event??data.command,data.value);}
                else Fire(command.Trim());
            }
            catch(Exception ex) {Dropped++;Status=ex.Message;}
        }
        void Enqueue(Job job) {if(jobs.Count>=256 || job.hops>512) {Dropped++;return;}jobs.Add(job);}
        public void Cancel() {jobs.Clear();}
        void Update()
        {
            int limit=64;while(limit-->0 && socket.TryRead(out string command)) Command(command);
            if(outbound!=null && outbound.IsCompleted) {if(outbound.IsFaulted) Status=outbound.Exception.GetBaseException().Message;outbound=null;}
            if(GUIUtility.keyboardControl==0 && !GraphOpen) foreach(var graph in Graphs) if(graph.enabled) foreach(var node in graph.nodes)
                if(node.kind==StudioActionKind.Hotkey && Enum.TryParse(node.text,true,out KeyCode key) && Input.GetKeyDown(key)) Enqueue(new Job{graph=graph,node=node.id,value=1,time=Time.unscaledTime});
            Step(Time.unscaledTime);
        }
        public void Step(float now)
        {
            int budget=128;
            while(budget-->0)
            {
                int index=jobs.FindIndex(j=>j.time<=now);if(index<0) break;var job=jobs[index];jobs.RemoveAt(index);if(!job.graph.enabled) continue;
                var node=job.graph.nodes.FirstOrDefault(n=>n.id==job.node);if(node==null) continue;
                try {Execute(node,node.eventValue?job.value:node.value);Executed++;Status="Executado: "+KindLabels[(int)node.kind];}
                catch(Exception ex) {Status=ex.Message;continue;}
                float wait=node.kind==StudioActionKind.Delay?Mathf.Clamp(node.value,.05f,3600):0;
                foreach(int next in node.next) Enqueue(new Job{graph=job.graph,node=next,value=job.value,time=now+wait,hops=job.hops+1});
            }
        }
        void Execute(StudioActionNode node,float value)
        {
            switch(node.kind)
            {
                case StudioActionKind.Event:case StudioActionKind.Hotkey:case StudioActionKind.Delay:break;
                case StudioActionKind.SetParameter:case StudioActionKind.ToggleParameter:
                    if(app.driver==null || !app.driver.Types.ContainsKey(node.text)) throw new InvalidOperationException("Parâmetro não encontrado: "+node.text);
                    app.driver.SetValue(node.text,node.kind==StudioActionKind.ToggleParameter?app.driver.GetValue(node.text)>.5f?0:1:value);break;
                case StudioActionKind.FaceChannel:app.faces.profile.enabled=true;app.faces.ReceiveOsc(node.text,Mathf.Clamp01(value));break;
                case StudioActionKind.PlayMotion:
                    var item=app.poses.Items.FirstOrDefault(i=>i.id==node.text || i.label.Equals(node.text,StringComparison.OrdinalIgnoreCase));if(item==null) throw new InvalidOperationException("Animação não encontrada: "+node.text);app.poses.Play(item);break;
                case StudioActionKind.StopMotion:app.poses.Stop();break;
                case StudioActionKind.Camera:
                    int camera=node.text.Length>0?app.cinema.shots.FindIndex(s=>s.name==node.text):Mathf.RoundToInt(value);if(camera<0 || camera>=app.cinema.shots.Count) throw new InvalidOperationException("Câmera não encontrada.");app.cinema.selected=camera;app.cinema.playhead=0;break;
                case StudioActionKind.Light:app.room.Preset(Mathf.Clamp(Mathf.RoundToInt(value),0,2));break;
                case StudioActionKind.Throw:case StudioActionKind.Drop:app.props.Spawn(Mathf.RoundToInt(value),node.kind==StudioActionKind.Drop);break;
                case StudioActionKind.ClearProps:app.props.Clear();break;
                case StudioActionKind.RecordStart:if(node.text.Length>0) app.capture.takeName=node.text;if(!app.capture.StartRecording()) throw new InvalidOperationException(app.capture.Status);break;
                case StudioActionKind.RecordStop:app.capture.StopRecording();break;
                case StudioActionKind.VideoStart:app.video.StartOutput(value>.5f);break;
                case StudioActionKind.VideoStop:app.video.StopOutput();break;
                case StudioActionKind.SendVNyan:
                    if(outbound!=null) throw new InvalidOperationException("Aguarde o envio WebSocket anterior.");outbound=LocalActionSocket.Send(vnEndpoint,node.text);break;
            }
        }
        public void DrawUI()
        {
            GUILayout.Label("Ações e eventos • "+Status);GUILayout.Label("Executadas "+Executed+" • agendadas "+Pending+" • descartadas "+Dropped);
            GUILayout.Label("Execute pelos botões abaixo ou crie sequências no editor de nós. Configure os objetos em Objetos físicos.");
            foreach(var eventName in Graphs.Where(g=>g.enabled).SelectMany(g=>g.nodes).Where(n=>n.kind==StudioActionKind.Event).Select(n=>n.text).Distinct().Take(24).ToArray())
                if(GUILayout.Button("EXECUTAR • "+eventName)) Fire(eventName);
            showConnections=GUILayout.Toggle(showConnections,"Mostrar conexões externas opcionais");
            if(showConnections)
            {
                GUILayout.Label("WebSocket local: ws://127.0.0.1:"+port+"/avatarlab");portText=GUILayout.TextField(portText,5);if(int.TryParse(portText,out int p) && p>1024 && p<65536) port=p;
                if(GUILayout.Button(socket.Running?"PARAR WEBSOCKET":"RECEBER EVENTOS WEBSOCKET")) {if(socket.Running) socket.Dispose();else socket.Start(port);Status=socket.Error??"WebSocket pronto.";}
                GUILayout.Label("Destino de comandos externos (compatibilidade)");vnEndpoint=GUILayout.TextField(vnEndpoint);
            }
            testEvent=GUILayout.TextField(testEvent,128);testValue=StudioGui.Slider("Valor do evento",testValue,0,20);if(GUILayout.Button("DISPARAR EVENTO")) Fire(testEvent,testValue);
            if(GUILayout.Button("Cancelar ações agendadas")) Cancel();
            GUILayout.BeginHorizontal();if(GUILayout.Button("Novo grafo")) {var graph=new StudioActionGraph();graph.nodes.Add(new StudioActionNode{id=0,kind=StudioActionKind.Event,text="meu_evento",position=new Vector2(30,30)});Graphs.Add(graph);selected=Graphs.Count-1;nodeSelected=0;}
            if(GUILayout.Button("Importar .algraph.json")) NativeFilePicker.PickAsync(path=>{try {Import(path);}catch(Exception ex) {Status=ex.Message;}},"Ações AvatarLab\0*.algraph.json\0\0","Importar grafo de ações");GUILayout.EndHorizontal();
            for(int i=0;i<Graphs.Count;i++) if(GUILayout.Button((selected==i?"● ":"")+Graphs[i].name)) {selected=i;nodeSelected=-1;linkFrom=-1;rectangles.Clear();}
            var current=Current;if(current==null) return;current.name=GUILayout.TextField(current.name,100);current.enabled=GUILayout.Toggle(current.enabled,"Grafo ativo");
            if(GUILayout.Button(GraphOpen?"FECHAR EDITOR VISUAL":"ABRIR EDITOR VISUAL DE NÓS")) GraphOpen=!GraphOpen;
            if(GUILayout.Button("Salvar grafo")) try {Save(current);}catch(Exception ex) {Status=ex.Message;}
            GUILayout.Label("Selecione um nó, configure-o abaixo e ligue Saída → Entrada. Conectores duplos criam ramos paralelos.");
            var node=current.nodes.FirstOrDefault(n=>n.id==nodeSelected);
            if(node!=null)
            {
                GUILayout.Label("Nó "+node.id);node.kind=(StudioActionKind)GUILayout.SelectionGrid((int)node.kind,showConnections || node.kind==StudioActionKind.SendVNyan?KindLabels:LocalKindLabels,2);GUILayout.Label("Evento, parâmetro, animação ou câmera");node.text=GUILayout.TextField(node.text,512);
                node.value=StudioGui.Slider("Valor / segundos / quantidade",node.value,0,60);node.eventValue=GUILayout.Toggle(node.eventValue,"Usar valor recebido pelo evento");
                if(GUILayout.Button("Remover ligações de saída")) node.next=Array.Empty<int>();
                if(GUILayout.Button("Excluir nó")) {current.nodes.Remove(node);foreach(var n in current.nodes) n.next=n.next.Where(id=>id!=node.id).ToArray();nodeSelected=-1;}
            }
            if(GUILayout.Button("Adicionar nó após o selecionado"))
            {
                if(current.nodes.Count<128) {int id=current.nodes.Count==0?0:current.nodes.Max(n=>n.id)+1;var added=new StudioActionNode{id=id,kind=StudioActionKind.Delay,value=1,position=node!=null?node.position+new Vector2(260,0):new Vector2(30,30)};current.nodes.Add(added);if(node!=null) node.next=node.next.Concat(new[]{id}).ToArray();nodeSelected=id;}
            }
            GUILayout.Label("As ações funcionam sem conexão com outro aplicativo. Salve seus grafos para reutilizar ao reabrir o AvatarLab.");
            if(GUILayout.Button("Abrir pasta de ações")) {Directory.CreateDirectory(DirectoryPath);Application.OpenURL(DirectoryPath);}
        }
        static void Line(Vector2 a,Vector2 b)
        {
            var color=GUI.color;GUI.color=new Color(.1f,.8f,.9f);float middle=(a.x+b.x)*.5f;
            GUI.DrawTexture(new Rect(Mathf.Min(a.x,middle),a.y,Mathf.Abs(middle-a.x)+2,2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(middle,Mathf.Min(a.y,b.y),2,Mathf.Abs(b.y-a.y)+2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Mathf.Min(middle,b.x),b.y,Mathf.Abs(b.x-middle)+2,2),Texture2D.whiteTexture);GUI.color=color;
        }
        void OnGUI()
        {
            if(!GraphOpen || Current==null) return;
            float scale=Mathf.Clamp(Screen.height/900f,.7f,1.5f);window.width=Mathf.Min(950,Screen.width/scale-465);window.height=Mathf.Min(720,Screen.height/scale-70);
            if(window.width<300) return;var previous=GUI.matrix;GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            window=GUI.Window(8000,window,DrawGraph,"AvatarLab • "+Current.name+" • "+(linkFrom>=0?"Escolha Entrada":"arraste os nós / selecione para editar"));GUI.matrix=previous;
        }
        void DrawGraph(int id)
        {
            var color=GUI.color;GUI.color=new Color(.04f,.055f,.07f,1);GUI.DrawTexture(new Rect(2,20,window.width-4,window.height-22),Texture2D.whiteTexture);GUI.color=color;
            if(GUI.Button(new Rect(window.width-80,20,70,24),"Fechar")) GraphOpen=false;
            canvasScroll=GUI.BeginScrollView(new Rect(8,50,window.width-16,window.height-60),canvasScroll,new Rect(0,0,2400,1600));
            foreach(var node in Current.nodes) rectangles[node.id]=new Rect(node.position,new Vector2(225,110));
            foreach(var node in Current.nodes) foreach(int next in node.next) if(rectangles.TryGetValue(next,out var to)) Line(new Vector2(node.position.x+225,node.position.y+90),new Vector2(to.x,to.y+90));
            foreach(var node in Current.nodes)
            {
                var rect=rectangles[node.id];var e=Event.current;
                if(e.type==EventType.MouseDown && e.button==0 && new Rect(rect.x,rect.y,rect.width,22).Contains(e.mousePosition)) {draggedNode=node.id;nodeSelected=node.id;e.Use();}
                if(e.type==EventType.MouseDrag && draggedNode==node.id) {node.position+=e.delta;node.position=new Vector2(Mathf.Clamp(node.position.x,0,2150),Mathf.Clamp(node.position.y,0,1480));e.Use();}
                GUI.Box(rect,GUIContent.none);GUI.BeginGroup(rect);DrawNode(10000+node.id);GUI.EndGroup();
            }
            if(Event.current.rawType==EventType.MouseUp) draggedNode=-1;
            GUI.EndScrollView();GUI.DragWindow(new Rect(0,0,window.width,20));
        }
        void DrawNode(int id)
        {
            var node=Current.nodes.First(n=>n.id==id-10000);
            GUI.Label(new Rect(6,1,213,22),"#"+node.id+" • "+KindLabels[(int)node.kind]);
            GUI.Label(new Rect(6,25,213,24),node.text.Length>0?node.text:KindLabels[(int)node.kind]);
            if(GUI.Button(new Rect(6,50,213,24),nodeSelected==node.id?"Selecionado":"Configurar")) nodeSelected=node.id;
            if(GUI.Button(new Rect(6,79,103,24),"Entrada") && linkFrom>=0)
            {
                var from=Current.nodes.FirstOrDefault(n=>n.id==linkFrom);if(from!=null && from.id!=node.id && !from.next.Contains(node.id))
                {var previous=from.next;from.next=from.next.Concat(new[]{node.id}).ToArray();try {Validate(Current);}catch(Exception ex) {from.next=previous;Status=ex.Message;}}
                linkFrom=-1;
            }
            if(GUI.Button(new Rect(115,79,104,24),"Saída")) linkFrom=node.id;
        }
        void OnDestroy() {Cancel();socket.Dispose();}
    }
}
