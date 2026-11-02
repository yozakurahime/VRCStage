using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace AvatarLab
{
    public static class StageArchive
    {
        public static void Write(string path, StageManifest manifest, byte[] bytes)
        {
            manifest.bundleSha256 = AvatarArchive.Hash(bytes);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false))) w.Write(JsonUtility.ToJson(manifest, true));
                using (var s = zip.CreateEntry("stage.bundle", System.IO.Compression.CompressionLevel.NoCompression).Open()) s.Write(bytes, 0, bytes.Length);
            }
        }
        public static StageManifest Read(string path, out byte[] bytes)
        {
            bytes = null;
            using (var zip = ZipFile.OpenRead(path))
            {
                var j = zip.GetEntry("manifest.json"); var b = zip.GetEntry("stage.bundle");
                if (zip.Entries.Count != 2 || j == null || b == null || j.Length > 256 * 1024 || b.Length < 16 || b.Length > AvatarArchive.MaxBundleBytes) throw new InvalidDataException("Pacote de cenário inválido ou maior que 1 GB.");
                StageManifest m; using (var reader = new StreamReader(j.Open())) m = JsonUtility.FromJson<StageManifest>(reader.ReadToEnd());
                if (m == null || m.format != 1 || (m.kind != "world" && m.kind != "prop") || !Guid.TryParseExact(m.id, "N", out _) || m.prefab != "assets/avatarlabworldexport/stage.prefab") throw new InvalidDataException("Formato de cenário incompatível.");
                if (m.unityVersion != Application.unityVersion) throw new InvalidDataException("Exporte com Unity " + Application.unityVersion);
                using (var input = b.Open()) using (var memory = new MemoryStream()) { input.CopyTo(memory); bytes = memory.ToArray(); }
                if (AvatarArchive.Hash(bytes) != m.bundleSha256) throw new InvalidDataException("Cenário corrompido (SHA-256).");
                return m;
            }
        }
    }
}
