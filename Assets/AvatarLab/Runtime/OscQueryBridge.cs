using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AvatarLab
{
    public sealed class OscQueryBridge : IDisposable
    {
        sealed class Node { public string path, type, value; public readonly Dictionary<string, Node> children = new Dictionary<string, Node>(); }
        volatile Dictionary<string, string> documents = new Dictionary<string, string>();
        volatile bool running;
        TcpListener http;
        UdpClient mdns;
        Thread httpThread, dnsThread;
        int udpPort = 9000, remotePort = 9001;
        string id = "avtr_00000000-0000-0000-0000-000000000000", displayName = "AvatarLab", instance, hostname;
        public string Status = "Descoberta VRCFaceTracking desligada.";
        public bool Running => running;
        public int HttpPort { get; private set; }
        public static string Quote(string value)
        {
            var output = new StringBuilder("\"");
            foreach (char c in value ?? "")
                switch (c) { case '\\': output.Append("\\\\"); break; case '"': output.Append("\\\""); break; case '\r': output.Append("\\r"); break; case '\n': output.Append("\\n"); break; case '\t': output.Append("\\t"); break; default: if (c < 32) output.Append("\\u" + ((int)c).ToString("x4")); else output.Append(c); break; }
            return output.Append('"').ToString();
        }
        public void UpdateAvatar(string avatarId, string name, Dictionary<string, string> parameters)
        {
            id = Guid.TryParseExact(avatarId, "N", out var parsed) ? "avtr_" + parsed.ToString("D") : "avtr_00000000-0000-0000-0000-000000000000";
            displayName = name;
            var root = new Node { path = "/" }; var avatar = Add(root, "avatar");
            var change = Add(avatar, "change"); change.type = "s"; change.value = Quote(id);
            var native = Add(avatar, "avatarfullfacetracking"); native.type = "F"; native.value = "false";
            var list = Add(avatar, "parameters");
            foreach (var p in parameters)
            {
                if (string.IsNullOrEmpty(p.Key) || p.Key.Length > 256) continue;
                var node = list; foreach (var part in p.Key.Split('/')) if (part.Length > 0) node = Add(node, part);
                node.type = p.Value; node.value = p.Value == "T" || p.Value == "F" ? "false" : "0";
            }
            var generated = new Dictionary<string, string>(); Serialize(root, generated); documents = generated;
            if (running) SendControl("/avatar/change", id);
        }
        static Node Add(Node parent, string name)
        {
            if (!parent.children.TryGetValue(name, out var node)) { node = new Node { path = parent.path == "/" ? "/" + name : parent.path + "/" + name }; parent.children[name] = node; } return node;
        }
        static string Serialize(Node n, Dictionary<string, string> documents)
        {
            var b = new StringBuilder("{\"FULL_PATH\":" + Quote(n.path));
            if (n.type != null) b.Append(",\"TYPE\":").Append(Quote(n.type)).Append(",\"ACCESS\":3,\"VALUE\":[").Append(n.value).Append(']');
            if (n.children.Count > 0 || n.type == null)
            {
                b.Append(",\"CONTENTS\":{"); bool first = true;
                foreach (var child in n.children) { if (!first) b.Append(','); first = false; b.Append(Quote(child.Key)).Append(':').Append(Serialize(child.Value, documents)); }
                b.Append('}');
            }
            string json = b.Append('}').ToString(); documents[n.path] = json; return json;
        }
        public string Document(string path) => documents.TryGetValue(path, out var data) ? data : null;
        public string HostInfo() => "{\"NAME\":" + Quote("AvatarLab - " + displayName) + ",\"OSC_IP\":\"127.0.0.1\",\"OSC_PORT\":" + udpPort + ",\"OSC_TRANSPORT\":\"UDP\",\"EXTENSIONS\":{\"ACCESS\":true,\"TYPE\":true,\"VALUE\":true}}";
        public void Start(int inputPort)
        {
            Dispose(); udpPort = inputPort;
            try
            {
                instance = "VRChat-Client-AvatarLab-" + Guid.NewGuid().ToString("N").Substring(0, 8); hostname = "avatarlab-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".local";
                http = new TcpListener(IPAddress.Loopback, 0); http.Start(); HttpPort = ((IPEndPoint)http.LocalEndpoint).Port;
                mdns = new UdpClient(AddressFamily.InterNetwork); mdns.ExclusiveAddressUse = false;
                mdns.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); mdns.Client.Bind(new IPEndPoint(IPAddress.Any, 5353)); mdns.Client.ReceiveTimeout = 250;
                mdns.MulticastLoopback = true; mdns.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"));
                try { mdns.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"), IPAddress.Loopback); } catch { }
                running = true; httpThread = new Thread(HttpLoop) { IsBackground = true, Name = "AvatarLab OSCQuery HTTP" }; dnsThread = new Thread(DnsLoop) { IsBackground = true, Name = "AvatarLab OSCQuery mDNS" };
                httpThread.Start(); dnsThread.Start(); Status = "OSCQuery ativo em 127.0.0.1:" + HttpPort + ". Abra o módulo de hardware no VRCFaceTracking.";
                SendControl("/avatar/change", id);
            }
            catch (Exception ex) { Dispose(); Status = "OSCQuery: " + ex.Message; }
        }
        void HttpLoop()
        {
            while (running)
            {
                try
                {
                    if (!http.Pending()) { Thread.Sleep(20); continue; }
                    using (var client = http.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 1000; client.SendTimeout = 1000;
                        using (var stream = client.GetStream())
                        {
                            var header = new StringBuilder(); var buffer = new byte[1024];
                            while (header.Length < 8192 && !header.ToString().Contains("\r\n\r\n")) { int read = stream.Read(buffer, 0, buffer.Length); if (read == 0) break; header.Append(Encoding.ASCII.GetString(buffer, 0, read)); }
                            var parts = header.ToString().Split('\n')[0].Trim().Split(' '); string path = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "/";
                            string payload = path.IndexOf("HOST_INFO", StringComparison.OrdinalIgnoreCase) >= 0 ? HostInfo() : Document(path.Split('?')[0]);
                            byte[] body = Encoding.UTF8.GetBytes(payload ?? "{}");
                            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 " + (payload != null ? "200 OK" : "404 Not Found") + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                            stream.Write(response, 0, response.Length); stream.Write(body, 0, body.Length);
                        }
                    }
                }
                catch (Exception) { if (!running) break; }
            }
        }
        void DnsLoop()
        {
            DateTime announce = DateTime.MinValue;
            while (running)
            {
                try
                {
                    if (DateTime.UtcNow >= announce) { Multicast(Advertisement()); Multicast(Query()); announce = DateTime.UtcNow.AddSeconds(5); }
                    var endpoint = new IPEndPoint(IPAddress.Any, 0); byte[] packet = mdns.Receive(ref endpoint);
                    if (packet.Length < 12) continue;
                    if ((packet[2] & 0x80) == 0) { string text = Encoding.ASCII.GetString(packet); if (text.Contains("oscjson") || text.Contains("_services")) Multicast(Advertisement()); }
                    else DiscoverVrcft(packet);
                }
                catch (SocketException ex) { if (ex.SocketErrorCode != SocketError.TimedOut && !running) break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
        }
        void Multicast(byte[] packet)
        {
            if (mdns == null) return;
            var destination = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
            try { mdns.Send(packet, packet.Length, destination); } catch { }
            try
            {
                mdns.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Loopback.GetAddressBytes()); mdns.Send(packet, packet.Length, destination);
                mdns.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Any.GetAddressBytes());
            }
            catch { }
        }
        static void U16(Stream s, int n) { s.WriteByte((byte)(n >> 8)); s.WriteByte((byte)n); }
        static void U32(Stream s, int n) { U16(s, n >> 16); U16(s, n & 65535); }
        static void Name(Stream s, string name) { foreach (string part in name.Split('.')) { byte[] bytes = Encoding.UTF8.GetBytes(part); if (bytes.Length > 63) throw new FormatException(); s.WriteByte((byte)bytes.Length); s.Write(bytes, 0, bytes.Length); } s.WriteByte(0); }
        static byte[] Data(Action<Stream> write) { using (var memory = new MemoryStream()) { write(memory); return memory.ToArray(); } }
        static void Record(Stream s, string name, int type, byte[] data)
        {
            Name(s, name); U16(s, type); U16(s, type == 12 ? 1 : 0x8001); U32(s, 120); U16(s, data.Length); s.Write(data, 0, data.Length);
        }
        byte[] Advertisement() => Data(s =>
        {
            U16(s, 0); U16(s, 0x8400); U16(s, 0); U16(s, 1); U16(s, 0); U16(s, 3);
            string service = instance + "._oscjson._tcp.local";
            Record(s, "_oscjson._tcp.local", 12, Data(x => Name(x, service)));
            Record(s, hostname, 1, new byte[] { 127, 0, 0, 1 });
            Record(s, service, 33, Data(x => { U16(x, 0); U16(x, 0); U16(x, HttpPort); Name(x, hostname); }));
            Record(s, service, 16, new byte[] { 0 });
        });
        static byte[] Query() => Data(s => { U16(s, 0); U16(s, 0); U16(s, 1); U16(s, 0); U16(s, 0); U16(s, 0); Name(s, "_oscjson._tcp.local"); U16(s, 12); U16(s, 1); });
        static int Read16(byte[] b, int p) { if (p + 2 > b.Length) throw new FormatException(); return (b[p] << 8) | b[p + 1]; }
        static string ReadName(byte[] b, ref int p, int depth = 0)
        {
            if (depth > 12) throw new FormatException(); var parts = new List<string>();
            while (p < b.Length)
            {
                int length = b[p++]; if (length == 0) return string.Join(".", parts);
                if ((length & 0xC0) == 0xC0) { if (p >= b.Length) throw new FormatException(); int target = ((length & 63) << 8) | b[p++]; parts.Add(ReadName(b, ref target, depth + 1)); return string.Join(".", parts); }
                if (length > 63 || p + length > b.Length) throw new FormatException(); parts.Add(Encoding.UTF8.GetString(b, p, length)); p += length;
            }
            throw new FormatException();
        }
        void DiscoverVrcft(byte[] packet)
        {
            int p = 12, questions = Read16(packet, 4), records = Read16(packet, 6) + Read16(packet, 8) + Read16(packet, 10);
            if (questions > 128 || records > 512) return;
            for (int i = 0; i < questions; i++) { ReadName(packet, ref p); p += 4; }
            for (int i = 0; i < records; i++)
            {
                string name = ReadName(packet, ref p); int type = Read16(packet, p), length = Read16(packet, p + 8); p += 10;
                if (p + length > packet.Length) throw new FormatException();
                if (type == 33 && length >= 6 && name.StartsWith("VRCFT-", StringComparison.OrdinalIgnoreCase))
                {
                    int port = Read16(packet, p + 4);
                    var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/?HOST_INFO"); request.Timeout = 750;
                    using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string json = reader.ReadToEnd(); var match = Regex.Match(json, "\"OSC_PORT\"\\s*:\\s*(\\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int destination) && destination > 0 && destination < 65536)
                        { bool changed = remotePort != destination; remotePort = destination; Status = "VRCFaceTracking descoberto; OSCQuery " + HttpPort + ", receptor VRCFT " + remotePort; if (changed) SendControl("/avatar/change", id); }
                    }
                }
                p += length;
            }
        }
        public void SendControl(string address, string value)
        {
            try
            {
                var bytes = Data(s => { OscString(s, address); OscString(s, ",s"); OscString(s, value); });
                using (var sender = new UdpClient()) sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, remotePort));
            }
            catch { }
        }
        static void OscString(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); stream.Write(bytes, 0, bytes.Length); stream.WriteByte(0); while (stream.Position % 4 != 0) stream.WriteByte(0); }
        public void Dispose()
        {
            running = false; http?.Stop(); mdns?.Close(); httpThread?.Join(1000); dnsThread?.Join(1000); http = null; mdns = null; httpThread = dnsThread = null;
            Status = "Descoberta VRCFaceTracking desligada.";
        }
    }
}
