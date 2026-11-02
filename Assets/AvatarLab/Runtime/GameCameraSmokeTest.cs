using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace AvatarLab
{
    public sealed class GameCameraSmokeTest : MonoBehaviour
    {
        static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
        IEnumerator Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--game-camera-test")<0) yield break;
            var app=FindObjectOfType<LabApp>(); float end=Time.realtimeSinceStartup+60;
            while(app.currentAvatar==null && Time.realtimeSinceStartup<end) yield return null;
            yield return new WaitForSeconds(.5f);
            try
            {
                Check(app.motor.Root!=null,"No avatar"); app.motor.enabled=false;
                app.motor.Teleport(new Vector3(0,5,0),Quaternion.identity);
                var room=app.room; var focus=room.focus; float yaw=room.yaw,pitch=room.pitch,distance=room.distance;
                app.photo.mainPreview=true;
                var broadcast=app.cinema.Camera.transform; var broadcastPos=broadcast.position; var broadcastRot=broadcast.rotation;
                app.motor.SetControl(true); Check(!app.photo.mainPreview,"Photo preview hides game camera");
                app.motor.StepCamera(Vector2.zero,0,.1f); var first=room.orbit.position;
                app.motor.StepCamera(new Vector2(25,8),0,.1f);
                Check(Vector3.Distance(first,room.orbit.position)>.5f,"Mouse did not orbit avatar");
                app.motor.StepCamera(Vector2.zero,3,.1f);Check(app.motor.cameraDistance<2,"Scroll did not zoom");
                app.motor.Teleport(new Vector3(5,5,3),Quaternion.identity);
                for(int i=0;i<60;i++) app.motor.StepCamera(Vector2.zero,0,1f/60);
                Check(Vector3.ProjectOnPlane(room.focus-app.motor.Root.position,Vector3.up).magnitude<.01f,"Camera did not follow teleported character");
                var direction=(room.orbit.position-room.focus).normalized;
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetPositionAndRotation(room.focus+direction*.8f,Quaternion.LookRotation(direction));wall.transform.localScale=new Vector3(3,3,.1f);Physics.SyncTransforms();
                app.motor.StepCamera(Vector2.zero,0,.1f);
                Check(Vector3.Distance(room.orbit.position,room.focus)<.8f,"Camera clipped through wall"); DestroyImmediate(wall);
                app.motor.StepCamera(Vector2.zero,10000,.1f);Check(app.motor.cameraDistance>=.45f,"Minimum zoom invalid");
                app.motor.StepCamera(Vector2.zero,-10000,.1f);Check(app.motor.cameraDistance<=10,"Maximum zoom invalid");
                Check(Vector3.Distance(broadcast.position,broadcastPos)<.001f && Quaternion.Angle(broadcast.rotation,broadcastRot)<.001f,"Game camera moved broadcast camera");
                app.motor.SetControl(false);
                Check(app.photo.mainPreview && room.focus==focus && room.yaw==yaw && room.pitch==pitch && room.distance==distance,"Exit did not restore editing/photo camera");
                Finish(true,"Third person entry from photo preview, mouse orbit, zoom limits, actor follow, wall collision, separate broadcast camera and exit restoration passed.");
            }
            catch(Exception ex) {Finish(false,ex.ToString());}
        }
        static void Finish(bool pass,string detail)
        {
            string folder=Path.Combine(Application.persistentDataPath,"Validation");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"game-camera-result.txt"),(pass?"PASS\n":"FAIL\n")+detail);Debug.Log("AVATARLAB_GAME_CAMERA "+detail);Application.Quit(pass?0:1);
        }
    }
}
