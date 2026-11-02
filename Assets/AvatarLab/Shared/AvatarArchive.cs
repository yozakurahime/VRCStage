using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AvatarLab
{
    public static class AvatarArchive
    {
        public const long MaxBundleBytes = 1024L * 1024 * 1024;
        public static void CommitFile(string temporary, string destination)
        {
            // Same-directory rename with rollback. Avoids platform-dependent File.Replace metadata failures.
            for (int attempt = 0; ; attempt++)
            {
                string backup = null;
                try
                {
                    if (File.Exists(destination))
                    {
                        backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
                        File.Move(destination, backup);
                    }
                    try { File.Move(temporary, destination); }
                    catch
                    {
                        if (backup != null && !File.Exists(destination)) File.Move(backup, destination);
                        throw;
                    }
                    if (backup != null)
                        try { File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 4 || (backup != null && File.Exists(backup))) throw;
                    System.Threading.Thread.Sleep(50);
                }
            }
        }
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static void Write(string path, AvatarManifest manifest, byte[] bundle)
        {
            manifest.bundleSha256 = Hash(bundle);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(manifest, true));
                using (var output = zip.CreateEntry("avatar.bundle", System.IO.Compression.CompressionLevel.NoCompression).Open())
                    output.Write(bundle, 0, bundle.Length);
            }
        }
        public static AvatarManifest Read(string path, out byte[] bundle)
        {
            bundle = null;
            using (var zip = ZipFile.OpenRead(path))
            {
                var json = zip.GetEntry("manifest.json");
                var asset = zip.GetEntry("avatar.bundle");
                if (zip.Entries.Count != 2 || json == null || asset == null || json.Length > 256 * 1024 || asset.Length < 16 || asset.Length > MaxBundleBytes)
                    throw new InvalidDataException("Pacote inválido ou maior que 1 GB.");
                AvatarManifest m;
                using (var reader = new StreamReader(json.Open())) m = JsonUtility.FromJson<AvatarManifest>(reader.ReadToEnd());
                if (m == null || m.format != 1 || !Guid.TryParseExact(m.id, "N", out _) || string.IsNullOrWhiteSpace(m.name)
                    || m.prefab != "assets/avatarlabexport/avatar.prefab") throw new InvalidDataException("Manifesto incompatível.");
                if (m.unityVersion != Application.unityVersion)
                    throw new InvalidDataException("Exporte com Unity " + Application.unityVersion + ". Pacote: " + m.unityVersion);
                using (var input = asset.Open())
                using (var memory = new MemoryStream()) { input.CopyTo(memory); bundle = memory.ToArray(); }
                if (Hash(bundle) != m.bundleSha256) throw new InvalidDataException("O pacote está incompleto ou foi alterado (SHA-256).");
                return m;
            }
        }
    }
}
