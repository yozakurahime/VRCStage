using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace AvatarLab
{
    public sealed class IfacialReceiver : IDisposable
    {
        const string Handshake = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719|sendDataVersion=v2";
        readonly ConcurrentQueue<FacialFrame> frames = new ConcurrentQueue<FacialFrame>();
        UdpClient socket;
        Thread worker;
        volatile bool active;
        int pending;
        public string Error { get; private set; }
        public int Received;
        public int Datagrams, Invalid;
        public string LastSender, DecodeError;
        long lastValid;
        public double Age => lastValid == 0 ? double.PositiveInfinity : (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastValid)) / (double)Stopwatch.Frequency;
        public bool Running => active;
        public void Start(string phoneIp, int port = 49983, bool localOnly = false)
        {
            Dispose(); Error = DecodeError = LastSender = null; Received = Datagrams = Invalid = 0; lastValid = 0;
            try
            {
                socket = new UdpClient(new IPEndPoint(localOnly ? IPAddress.Loopback : IPAddress.Any, port)); socket.Client.ReceiveTimeout = 250;
                if (!string.IsNullOrWhiteSpace(phoneIp))
                {
                    if (!IPAddress.TryParse(phoneIp.Trim(), out var ip)) throw new ArgumentException("Informe o IP local do iPhone.");
                    byte[] bytes = Encoding.UTF8.GetBytes(Handshake); socket.Send(bytes, bytes.Length, new IPEndPoint(ip, 49983));
                }
                active = true; worker = new Thread(Receive) { IsBackground = true, Name = "AvatarLab iFacial" }; worker.Start();
            }
            catch (Exception ex) { Error = ex.Message; socket?.Close(); socket = null; }
        }
        void Receive()
        {
            while (active)
            {
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0); byte[] data = socket.Receive(ref endpoint);
                    Interlocked.Increment(ref Datagrams); LastSender = endpoint.Address.ToString();
                    if (data.Length >= 4 && data[0] == 'F' && data[1] == 'M' && data[2] == 'V' && data[3] == '3')
                    { Interlocked.Increment(ref Invalid); DecodeError = "Face Motion v3 detectado. Selecione v3 UDP no formato de entrada."; continue; }
                    var frame = IfacialCodec.Decode(Encoding.UTF8.GetString(data));
                    Interlocked.Increment(ref Received); Interlocked.Exchange(ref lastValid, Stopwatch.GetTimestamp()); DecodeError = null;
                    if (Interlocked.Increment(ref pending) > 64) { Interlocked.Decrement(ref pending); continue; } frames.Enqueue(frame);
                }
                catch (SocketException ex) { if (active && ex.SocketErrorCode != SocketError.TimedOut) Error = ex.Message; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Interlocked.Increment(ref Invalid); DecodeError = ex.Message; }
            }
        }
        public bool TryRead(out FacialFrame frame) { if (!frames.TryDequeue(out frame)) return false; Interlocked.Decrement(ref pending); return true; }
        public void Dispose() { active = false; socket?.Close(); worker?.Join(1000); socket = null; worker = null; while (frames.TryDequeue(out _)) { } pending = 0; }
    }
}
