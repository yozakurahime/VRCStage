using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace AvatarLab
{
    public sealed class ReleaseSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--release-test")<0) yield break;
            var app=FindObjectOfType<LabApp>();float deadline=Time.realtimeSinceStartup+60;
            while(app.driver==null && Time.realtimeSinceStartup<deadline) yield return null;
            string failure=null;
            try
            {
                if(app.driver==null) throw new Exception("No public example loaded");
                if(Path.GetFileName(StageBrand.Executable)!="VRCStage.exe") throw new Exception("Test the renamed public package");
                if(!Application.persistentDataPath.Replace('\\','/').EndsWith("/AvatarLab/AvatarLab Offline")) throw new Exception("Legacy data path changed");
                app.photo.Frame(1);app.photo.mainPreview=true;app.video.width=1280;app.video.height=720;app.video.frameRate=30;
                app.video.StartOutput(true);
                if(!app.video.Running || !app.video.OutputWindowRunning) throw new Exception("Renamed player could not launch its clean video viewer: "+app.video.Status);
            }
            catch(Exception ex) {failure=ex.ToString();}
            deadline=Time.realtimeSinceStartup+15;
            while(failure==null && app.video.Frames<5 && Time.realtimeSinceStartup<deadline) yield return null;
            if(failure==null && (app.video.Frames<5 || !app.video.OutputWindowRunning)) failure="Output frames/viewer failed";
            int frames=app.video.Frames;app.video.StopOutput();
            string folder=Path.Combine(Application.persistentDataPath,"Validation");Directory.CreateDirectory(folder);
            string result=failure==null?"PASS\nRenamed VRCStage.exe loaded the generated public example, preserved legacy data path and launched its own clean output viewer; video frames="+frames:"FAIL\n"+failure;
            File.WriteAllText(Path.Combine(folder,"public-release-result.txt"),result);Debug.Log(result);Application.Quit(failure==null?0:1);
        }
    }
}
