using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class LocalPosePacks : IDisposable
    {
        public static string DirectoryPath=>Path.Combine(Application.persistentDataPath,"PosePacks");
        readonly List<AssetBundle> bundles=new List<AssetBundle>();
        public readonly List<PosePack.Entry> entries=new List<PosePack.Entry>();
        public LocalPosePacks()
        {
            Directory.CreateDirectory(DirectoryPath);
            foreach(var path in Directory.GetFiles(DirectoryPath,"*.alposes").OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
            {
                AssetBundle bundle=null;
                try
                {
                    if(new FileInfo(path).Length>128*1024*1024) throw new InvalidDataException("Pacote excede 128 MB.");
                    bundle=AssetBundle.LoadFromFile(path);if(bundle==null) throw new InvalidDataException("Pacote incompatível.");
                    var packs=bundle.LoadAllAssets<PosePack>();
                    if(packs.Length!=1 || packs[0].format!=1) throw new InvalidDataException("Formato de poses inválido.");
                    var valid=packs[0].entries.Where(e=>e!=null && e.clip!=null && e.clip.isHumanMotion && !string.IsNullOrEmpty(e.id)).ToArray();
                    if(valid.Length==0 || valid.Length>256) throw new InvalidDataException("Catálogo de poses inválido.");
                    foreach(var entry in valid) if(!entries.Any(e=>e.id==entry.id)) entries.Add(entry);
                    bundles.Add(bundle);bundle=null;
                }
                catch(Exception ex) { Debug.LogWarning("Pacote de poses ignorado: "+Path.GetFileName(path)+": "+ex.Message); }
                finally { if(bundle!=null) bundle.Unload(true); }
            }
        }
        public void Dispose() {foreach(var bundle in bundles) if(bundle!=null) bundle.Unload(true);bundles.Clear();entries.Clear();}
    }
}
