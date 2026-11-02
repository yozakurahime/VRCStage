using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvatarLab
{
    // Small local WebSocket endpoint; no HTTP.sys URL ACL, firewall rule or admin required.
    public sealed class LocalActionSocket : IDisposable
    {
        TcpListener listener;CancellationTokenSource cancel;int clients,pending;
        readonly ConcurrentQueue<string> commands=new ConcurrentQueue<string>();
        readonly object queueGate=new object();
        public bool Running=>listener!=null;public string Error {get;private set;}public int Received,Dropped;
        public void Start(int port)
        {
            Dispose();Error=null;cancel=new CancellationTokenSource();
            try {listener=new TcpListener(IPAddress.Loopback,port);listener.Start(8);var source=listener;var token=cancel.Token;Task.Run(()=>Accept(source,token));}
            catch(Exception ex) {Error=ex.Message;Dispose();}
        }
        async Task Accept(TcpListener source,CancellationToken token)
        {
            try
            {
                while(!token.IsCancellationRequested)
                {
                    var client=await source.AcceptTcpClientAsync();
                    if(Interlocked.Increment(ref clients)>8) {Interlocked.Decrement(ref clients);client.Close();continue;}
                    _=Serve(client,token);
                }
            }
            catch(Exception ex) {if(!token.IsCancellationRequested) Error=ex.Message;}
        }
        async Task Serve(TcpClient client,CancellationToken token)
        {
            try
            {
                using(client) using(var stream=client.GetStream()) using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(5000);var header=new MemoryStream();var single=new byte[1];
                    while(header.Length<8192)
                    {
                        if(await stream.ReadAsync(single,0,1,timeout.Token)!=1) return;header.WriteByte(single[0]);
                        if(header.Length>=4) {var a=header.GetBuffer();int n=(int)header.Length;if(a[n-4]==13 && a[n-3]==10 && a[n-2]==13 && a[n-1]==10) break;}
                    }
                    string text=Encoding.ASCII.GetString(header.ToArray());var lines=text.Split(new[]{"\r\n"},StringSplitOptions.None);
                    if(lines.Length<2 || lines[0]!="GET /vnyan HTTP/1.1" && lines[0]!="GET /avatarlab HTTP/1.1") return;
                    string key=null,origin=null;bool upgrade=false;
                    foreach(string line in lines)
                    {
                        int at=line.IndexOf(':');if(at<0) continue;string name=line.Substring(0,at).Trim(),value=line.Substring(at+1).Trim();
                        if(name.Equals("Sec-WebSocket-Key",StringComparison.OrdinalIgnoreCase)) key=value;
                        if(name.Equals("Origin",StringComparison.OrdinalIgnoreCase)) origin=value;
                        if(name.Equals("Upgrade",StringComparison.OrdinalIgnoreCase)) upgrade=value.Equals("websocket",StringComparison.OrdinalIgnoreCase);
                    }
                    if(!upgrade || key==null || Convert.FromBase64String(key).Length!=16) return;
                    if(origin!=null && origin!="null" && (!Uri.TryCreate(origin,UriKind.Absolute,out var uri) || !(uri.Host=="localhost" || IPAddress.TryParse(uri.Host,out var address) && IPAddress.IsLoopback(address)))) return;
                    string accept;using(var sha=SHA1.Create()) accept=Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    var response=Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "+accept+"\r\n\r\n");await stream.WriteAsync(response,0,response.Length,timeout.Token);
                    using(var socket=WebSocket.CreateFromStream(stream,true,null,TimeSpan.FromSeconds(20)))
                    {
                        var buffer=new byte[8192];
                        while(!token.IsCancellationRequested && socket.State==WebSocketState.Open)
                        {
                            int count=0;WebSocketReceiveResult received;
                            do {received=await socket.ReceiveAsync(new ArraySegment<byte>(buffer,count,buffer.Length-count),token);if(received.MessageType==WebSocketMessageType.Close) return;count+=received.Count;if(count>=buffer.Length && !received.EndOfMessage) return;} while(!received.EndOfMessage);
                            if(received.MessageType!=WebSocketMessageType.Text) continue;
                            string command=new UTF8Encoding(false,true).GetString(buffer,0,count);
                            lock(queueGate)
                            {
                                if(token.IsCancellationRequested) return;
                                Interlocked.Increment(ref Received);
                                if(pending>=256) {Interlocked.Increment(ref Dropped);continue;}
                                commands.Enqueue(command);pending++;
                            }
                        }
                    }
                }
            }
            catch(Exception ex) {if(!token.IsCancellationRequested && !(ex is WebSocketException) && !(ex is OperationCanceledException)) Error=ex.Message;}
            finally {Interlocked.Decrement(ref clients);}
        }
        public bool TryRead(out string command) {lock(queueGate) {if(!commands.TryDequeue(out command)) return false;pending--;return true;}}
        public static async Task Send(string endpoint,string command)
        {
            var uri=new Uri(endpoint);if(uri.Scheme!="ws" || !(uri.Host=="localhost" || IPAddress.TryParse(uri.Host,out var address) && IPAddress.IsLoopback(address))) throw new InvalidOperationException("WebSocket de saída deve apontar para localhost.");
            using(var socket=new ClientWebSocket()) using(var timeout=new CancellationTokenSource(5000))
            {await socket.ConnectAsync(uri,timeout.Token);var data=Encoding.UTF8.GetBytes(command);if(data.Length>8192) throw new InvalidOperationException("Comando muito grande.");await socket.SendAsync(new ArraySegment<byte>(data),WebSocketMessageType.Text,true,timeout.Token);await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"",timeout.Token);}
        }
        public void Dispose() {cancel?.Cancel();listener?.Stop();listener=null;cancel?.Dispose();cancel=null;lock(queueGate) {while(commands.TryDequeue(out _)) {}pending=0;}}
    }
}
