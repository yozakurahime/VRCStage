using System;
using System.IO;
using System.Runtime.InteropServices;
using LeapInternal;

namespace AvatarLab
{
    // Bind to the user's installed runtime, whose IPC version matches its service.
    // Keep delegates and module alive until the polling thread has stopped.
    public sealed class LeapNativeClient : IDisposable
    {
        [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate eLeapRS CreateFn(IntPtr config, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate eLeapRS OpenFn(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate eLeapRS PollFn(IntPtr handle, uint timeout, ref LEAP_CONNECTION_MESSAGE message);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate eLeapRS ModeFn(IntPtr handle, eLeapTrackingMode mode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate eLeapRS PolicyFn(IntPtr handle, ulong set, ulong clear);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CloseFn(IntPtr handle);
        IntPtr module, connection;
        CreateFn create; OpenFn open; PollFn poll; ModeFn mode; PolicyFn policy; CloseFn close, destroy;
        public string Library { get; private set; }
        T Bind<T>(string name) where T : Delegate
        {
            var address=GetProcAddress(module,name);
            if(address==IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        public LeapNativeClient()
        {
            string root=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates={Path.Combine(root,"Ultraleap","LeapSDK","lib","x64","LeapC.dll"), Path.Combine(root,"Leap Motion","Core Services","LeapC.dll")};
            foreach(var path in candidates) if(File.Exists(path)) { Library=path; break; }
            if(Library==null) Library=Path.Combine(UnityEngine.Application.dataPath,"Plugins","x86_64","LeapC.dll");
            try
            {
                module=LoadLibraryEx(Library,IntPtr.Zero,0x100|0x1000);
                if(module==IntPtr.Zero) throw new Exception("Não foi possível carregar LeapC: "+Marshal.GetLastWin32Error());
                create=Bind<CreateFn>("LeapCreateConnection"); open=Bind<OpenFn>("LeapOpenConnection"); poll=Bind<PollFn>("LeapPollConnection");
                mode=Bind<ModeFn>("LeapSetTrackingMode"); policy=Bind<PolicyFn>("LeapSetPolicyFlags"); close=Bind<CloseFn>("LeapCloseConnection"); destroy=Bind<CloseFn>("LeapDestroyConnection");
                var result=create(IntPtr.Zero,out connection); if(result!=eLeapRS.eLeapRS_Success) throw new Exception("LeapCreateConnection: "+result);
                result=open(connection); if(result!=eLeapRS.eLeapRS_Success) throw new Exception("LeapOpenConnection: "+result);
            }
            catch { Dispose(); throw; }
        }
        public eLeapRS Poll(ref LEAP_CONNECTION_MESSAGE message) => poll(connection,100,ref message);
        public void Configure(eLeapTrackingMode value) { mode(connection,value); policy(connection,1,0); }
        public void Dispose()
        {
            if(connection!=IntPtr.Zero) { close?.Invoke(connection); destroy?.Invoke(connection); connection=IntPtr.Zero; }
            if(module!=IntPtr.Zero) { FreeLibrary(module); module=IntPtr.Zero; }
        }
    }
}
