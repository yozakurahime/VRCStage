using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AvatarLab
{
    public sealed class OscMessage
    {
        public string address;
        public object[] values;
        public float Number(int index = 0)
        {
            var value = values[index];
            return value is bool b ? (b ? 1 : 0) : Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    // Bounded OSC 1.0 decoder: recursive bundles, big endian floats/ints, bools and strings.
    public static class OscCodec
    {
        public static byte[] Encode(string address,params object[] values)
        {
            using(var data=new MemoryStream())
            {
                Action<string> write=value=>{var bytes=Encoding.UTF8.GetBytes(value);data.Write(bytes,0,bytes.Length);data.WriteByte(0);while(data.Length%4!=0) data.WriteByte(0);};
                write(address);var tags=new StringBuilder(",");foreach(var v in values) tags.Append(v is string?'s':v is bool b?b?'T':'F':v is int?'i':'f');write(tags.ToString());
                foreach(var v in values)
                {
                    if(v is string s) {write(s);continue;}if(v is bool) continue;
                    byte[] bytes=v is int n?BitConverter.GetBytes(n):BitConverter.GetBytes(Convert.ToSingle(v));if(BitConverter.IsLittleEndian) Array.Reverse(bytes);data.Write(bytes,0,4);
                }
                return data.ToArray();
            }
        }
        public static byte[] Bundle(IEnumerable<byte[]> messages)
        {
            using(var data=new MemoryStream())
            {
                data.Write(Encoding.ASCII.GetBytes("#bundle\0"),0,8);data.Write(new byte[]{0,0,0,0,0,0,0,1},0,8);
                foreach(var message in messages) {var size=BitConverter.GetBytes(message.Length);if(BitConverter.IsLittleEndian) Array.Reverse(size);data.Write(size,0,4);data.Write(message,0,message.Length);}
                if(data.Length>65000) throw new InvalidDataException("Bundle OSC excede o limite UDP.");return data.ToArray();
            }
        }
        static int Int(byte[] data, ref int p, int end)
        {
            if (p + 4 > end) throw new InvalidDataException();
            var n = (data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3]; p += 4; return n;
        }
        static string String(byte[] data, ref int p, int end)
        {
            int start = p;
            while (p < end && data[p] != 0) p++;
            if (p == end) throw new InvalidDataException();
            string value = Encoding.UTF8.GetString(data, start, p - start);
            p = (p + 4) & ~3;
            if (p > end) throw new InvalidDataException();
            return value;
        }
        public static List<OscMessage> Decode(byte[] bytes)
        {
            var output = new List<OscMessage>(); Parse(bytes, 0, bytes.Length, output, 0); return output;
        }
        static void Parse(byte[] data, int p, int end, List<OscMessage> output, int depth)
        {
            if (depth > 8 || output.Count > 256 || p >= end) throw new InvalidDataException();
            string address = String(data, ref p, end);
            if (address == "#bundle")
            {
                if (p + 8 > end) throw new InvalidDataException(); p += 8; // immediate application; future timetags aren't scheduled
                while (p < end)
                {
                    int length = Int(data, ref p, end);
                    if (length <= 0 || length > end - p) throw new InvalidDataException();
                    Parse(data, p, p + length, output, depth + 1); p += length;
                }
                return;
            }
            if (!address.StartsWith("/", StringComparison.Ordinal)) throw new InvalidDataException();
            string tags = String(data, ref p, end);
            if (!tags.StartsWith(",", StringComparison.Ordinal)) throw new InvalidDataException();
            var values = new object[tags.Length - 1];
            for (int i = 1; i < tags.Length; i++)
            {
                switch (tags[i])
                {
                    case 'i': values[i - 1] = Int(data, ref p, end); break;
                    case 'f': var bits = Int(data, ref p, end); values[i - 1] = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0); break;
                    case 'T': values[i - 1] = true; break;
                    case 'F': values[i - 1] = false; break;
                    case 's': values[i - 1] = String(data, ref p, end); break;
                    default: throw new InvalidDataException("OSC type não suportado.");
                }
            }
            if (p != end) throw new InvalidDataException();
            output.Add(new OscMessage { address = address, values = values });
        }
    }
    public sealed class OscReceiver : IDisposable
    {
        readonly ConcurrentQueue<OscMessage> queue = new ConcurrentQueue<OscMessage>();
        UdpClient socket;
        Thread thread;
        volatile bool running;
        int pending, received, dropped;
        public int Received => received;
        public int Dropped => dropped;
        public string Error { get; private set; }
        public bool Running => running;
        public void Start(int port, bool allowLan)
        {
            Dispose(); Error = null;
            try
            {
                socket = new UdpClient(new IPEndPoint(allowLan ? IPAddress.Any : IPAddress.Loopback, port));
                socket.Client.ReceiveTimeout = 250;
                running = true;
                thread = new Thread(Receive) { IsBackground = true, Name = "AvatarLab OSC" }; thread.Start();
            }
            catch (Exception ex) { Error = ex.Message; socket?.Close(); socket = null; }
        }
        void Receive()
        {
            while (running)
            {
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    foreach (var message in OscCodec.Decode(socket.Receive(ref endpoint)))
                    {
                        Interlocked.Increment(ref received);
                        if (Interlocked.Increment(ref pending) > 2048) { Interlocked.Decrement(ref pending); Interlocked.Increment(ref dropped); continue; }
                        queue.Enqueue(message);
                    }
                }
                catch (SocketException ex) { if (running && ex.SocketErrorCode != SocketError.TimedOut) Error = ex.Message; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { Interlocked.Increment(ref dropped); }
            }
        }
        public bool TryDequeue(out OscMessage message)
        {
            if (!queue.TryDequeue(out message)) return false;
            Interlocked.Decrement(ref pending); return true;
        }
        public void Dispose()
        {
            running = false; socket?.Close(); thread?.Join(1000); socket = null; thread = null;
            while (queue.TryDequeue(out _)) { } pending = 0;
        }
    }
}
