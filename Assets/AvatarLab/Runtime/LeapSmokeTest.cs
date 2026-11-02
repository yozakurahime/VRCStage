using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace AvatarLab
{
    public sealed class LeapSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--leap-test")<0) yield break;
            var input=new OpticalInput(); input.StartLeap(false); int frames=0,hands=0; float end=Time.realtimeSinceStartup+8;
            while(Time.realtimeSinceStartup<end) { var frame=input.Read(); if(frame!=null) { frames++; hands=Math.Max(hands,frame.hands.Length); } yield return null; }
            string status=input.Status; input.Dispose();
            bool passed=frames>0;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"leap-test-result.txt"),(passed?"PASS":"FAIL")+"\nReal service frames consumed: "+frames+"\nMaximum hands: "+hands+"\n"+status);
            Application.Quit(passed?0:1);
        }
    }
}
