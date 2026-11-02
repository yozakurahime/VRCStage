using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
namespace AvatarLab
{
    // Keep the Unity company/product identifiers and wire formats stable: existing
    // avatars, tracking profiles, studios and OBS setups must survive the rename.
    public static class StageBrand
    {
        public const string Name="VRCStage", Version="0.10.0";
        public const string Repository="https://github.com/yozakurahime/VRCStage";
        public static string Executable=>Process.GetCurrentProcess().MainModule.FileName;
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetWindowText(IntPtr window,string text);
        public static IEnumerator SetTitle()
        {
            while(!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            if(Application.platform==RuntimePlatform.WindowsPlayer && SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
                try {SetWindowText(Process.GetCurrentProcess().MainWindowHandle,Name);} catch(Exception) { }
        }
    }
}
