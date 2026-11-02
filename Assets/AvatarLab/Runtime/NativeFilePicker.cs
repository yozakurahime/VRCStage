using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

namespace AvatarLab
{
    public static class NativeFilePicker
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int size;
            public IntPtr owner, instance;
            public string filter;
            public IntPtr customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public string initialDir, title;
            public int flags; // explorer, file/path exist, no cwd change
            public short fileOffset, fileExtension;
            public string defaultExtension;
            public IntPtr userData, hook;
            public string templateName;
            public IntPtr reserved;
            public int reserved2, flagsEx;
        }
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileName(ref OpenFileName data);
        static readonly ConcurrentQueue<Action> completed = new ConcurrentQueue<Action>();
        static int busy;
        public static void Pump() { while (completed.TryDequeue(out var action)) action(); }
        public static void PickAsync(Action<string> callback, string filter = null, string title = null)
        {
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            string result = null; Exception error = null;
            var owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            // Shell extensions require an STA and more stack than Unity's player thread.
            var thread = new Thread(() =>
            {
                try
                {
                    var data = new OpenFileName { size = Marshal.SizeOf(typeof(OpenFileName)), owner = owner, filterIndex = 1, maxFile = 32768, file = Marshal.AllocHGlobal(65536), flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008, filter = "AvatarLab (*.alavatar)\0*.alavatar\0\0", title = "Importar avatar para a biblioteca local" };
                    if (filter != null) data.filter = filter;
                    if (title != null) data.title = title;
                    try { Marshal.WriteInt16(data.file, 0); if (GetOpenFileName(ref data)) result = Marshal.PtrToStringUni(data.file); } finally { Marshal.FreeHGlobal(data.file); }
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                    completed.Enqueue(() => { if (error != null) UnityEngine.Debug.LogException(error); else if (result != null) callback(result); });
                }
            }, 8 * 1024 * 1024);
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
}

