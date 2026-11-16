using System.IO;
using System.Text;

namespace SrtpAvatar
{
    internal static class AtomicFile
    {
        internal static void WriteUtf8(string target, string content, string backup = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
            var temporary = target + ".tmp";
            var ownsTemporary = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    var bytes = Encoding.UTF8.GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(target)) File.Replace(temporary, target, backup);
                else File.Move(temporary, target);
            }
            finally
            {
                if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
