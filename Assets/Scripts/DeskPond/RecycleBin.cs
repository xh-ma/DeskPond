using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Opt-in source of "ghost file" names: the file names (never contents, never paths) of
    /// what is in this user's Recycle Bin. Off by default; Settings turns it on. Nothing read here
    /// is stored or sent anywhere, it only appears in the catch bubble.
    ///
    /// Windows keeps a small "$I" metadata file per deleted item in C:\$Recycle.Bin\&lt;SID&gt;\,
    /// holding the original path after a short header. Folders belonging to other users are not
    /// readable and are skipped.
    /// </summary>
    public static class RecycleBin
    {
        public static List<string> ReadNames(int max)
        {
            var names = new List<string>();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                var root = Path.Combine(drive ?? "C:\\", "$Recycle.Bin");
                if (!Directory.Exists(root))
                    return names;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string[] files;
                    try
                    {
                        files = Directory.GetFiles(dir, "$I*");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var f in files)
                    {
                        var name = ReadOriginalName(f);
                        if (!string.IsNullOrEmpty(name))
                            names.Add(name);
                        if (names.Count >= max)
                            return names;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("DeskPond: recycle bin not readable. " + ex.Message);
            }
#endif
            return names;
        }

        static string ReadOriginalName(string metaFile)
        {
            try
            {
                var bytes = File.ReadAllBytes(metaFile);
                if (bytes.Length < 28)
                    return null;
                var version = BitConverter.ToInt64(bytes, 0);
                // v1 (Vista-8): fixed 520-byte path at offset 24. v2 (10+): length at 24, path at 28.
                string path;
                if (version >= 2)
                {
                    var len = BitConverter.ToInt32(bytes, 24);
                    var count = Math.Min(len * 2, bytes.Length - 28);
                    path = Encoding.Unicode.GetString(bytes, 28, Math.Max(0, count));
                }
                else
                {
                    path = Encoding.Unicode.GetString(bytes, 24, Math.Min(520, bytes.Length - 24));
                }

                path = path.TrimEnd('\0');
                return Path.GetFileName(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
