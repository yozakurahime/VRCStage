using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace AvatarLab.Editor
{
    public static class GoGoPoseExporter
    {
        [MenuItem("Tools/AvatarLab/Exportar poses locais do GoGo Loco")]
        public static void Open()
        {
            var folder=EditorUtility.OpenFolderPanel("Selecione a pasta Clips dentro de Poses do GoGo Loco", "Packages/gogoloco/Runtime/GoGo/GoLoco/Animations/Clips/Poses/Clips", "");
            if(string.IsNullOrEmpty(folder)) return;
            var output=EditorUtility.SaveFilePanel("Exportar poses para uso local", "", "GoGo-Loco-local", "alposes");
            if(!string.IsNullOrEmpty(output)) Export(folder,output);
        }
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int source=Array.IndexOf(args,"--pose-source"),output=Array.IndexOf(args,"--pose-output");
            if(source<0 || output<0) throw new ArgumentException("Informe --pose-source e --pose-output");
            Export(args[source+1],args[output+1]);EditorApplication.Exit(0);
        }
        public static void Export(string source,string output)
        {
            const string temp="Assets/AvatarLabPoseExport";
            if(Directory.Exists(temp)) throw new IOException("A pasta temporária já existe: "+temp);
            if(Application.unityVersion!="2022.3.22f1") throw new InvalidOperationException("Use Unity 2022.3.22f1.");
            string build=Path.Combine(Path.GetTempPath(),"AvatarLabPoses-"+Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);Directory.CreateDirectory(build);
                // Import only the body poses, without controllers, scripts or avatar assets.
                foreach(var file in Directory.GetFiles(source,"*.anim").OrderBy(p=>p,StringComparer.Ordinal))
                    if(Path.GetFileName(file)!="go_hands_fist_close.anim") File.Copy(file,Path.Combine(temp,Path.GetFileName(file)));
                AssetDatabase.Refresh();
                var pack=ScriptableObject.CreateInstance<PosePack>();pack.author="Franada / colaboradores GoGo Loco";pack.source="https://gogoloco.net/";
                pack.entries=Directory.GetFiles(temp,"*.anim").OrderBy(p=>p,StringComparer.Ordinal).Select(p=>
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(p.Replace('\\','/'));
                    if(clip==null || !clip.isHumanMotion) throw new InvalidDataException("Pose não Humanoid: "+p);
                    string id=Path.GetFileNameWithoutExtension(p);
                    var events=AnimationUtility.GetAnimationEvents(clip);if(events.Length>0) AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
                    return new PosePack.Entry{id=id,label=id.Substring(3).Replace('_',' '),clip=clip,animated=id.Contains("motion") || id.Contains("push_ups")};
                }).ToArray();
                if(pack.entries.Length==0) throw new InvalidDataException("Nenhuma pose encontrada.");
                AssetDatabase.CreateAsset(pack,temp+"/GoGo.asset");AssetDatabase.SaveAssets();
                var map=new AssetBundleBuild{assetBundleName="poses.bundle",assetNames=new[]{temp+"/GoGo.asset"}};
                if(BuildPipeline.BuildAssetBundles(build,new[]{map},BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64)==null) throw new IOException("Falha ao gerar o pacote de poses.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.Copy(Path.Combine(build,"poses.bundle"),output,true);
                Debug.Log("AVATARLAB_POSES_EXPORTED "+pack.entries.Length+" / "+output);
            }
            finally { AssetDatabase.DeleteAsset(temp);if(Directory.Exists(build)) Directory.Delete(build,true); }
        }
    }
}
