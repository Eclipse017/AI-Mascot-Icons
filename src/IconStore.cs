using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MascotIcons
{
    internal sealed class IconChoice
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Subtitle { get; private set; }
        public string CustomFile { get; private set; }
        public IconChoice(string id, string name, string subtitle, string file)
        { Id = id; Name = name; Subtitle = subtitle; CustomFile = file; }
        public override string ToString() { return Name; }
        public byte[] Bytes()
        {
            if (CustomFile != null) return File.ReadAllBytes(CustomFile);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Icons." + Id + ".ico"))
            {
                if (stream == null) throw new IOException("缺少内置图标：" + Name);
                using (MemoryStream copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
            }
        }
        public ImageSource Preview()
        {
            using (Stream stream = CustomFile != null ? (Stream)new MemoryStream(Bytes()) :
                Assembly.GetExecutingAssembly().GetManifestResourceStream("Icons." + Id + ".png"))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapFrame best = decoder.Frames[0];
                foreach (BitmapFrame frame in decoder.Frames) if (frame.PixelWidth > best.PixelWidth) best = frame;
                best.Freeze(); return best;
            }
        }
    }

    internal static class IconStore
    {
        internal static readonly IconChoice[] BuiltIn = {
            new IconChoice("ChatGPT", "ChatGPT 娘", "白色龙娘 · Codex", null),
            new IconChoice("Claude", "Claude 娘", "暖橙色 · Claude", null),
            new IconChoice("Gemini", "Gemini 娘", "渐变色 · Antigravity", null),
            new IconChoice("Grok", "Grok 娘", "黑白色 · Grok", null),
            new IconChoice("DeepSeek", "DeepSeek 娘", "蓝色 · DeepSeek", null)
        };
        internal static IconChoice Suggest(string name)
        {
            string s = name.ToLowerInvariant();
            if (s.Contains("notebook")) return null;
            if (s.Contains("chatgpt") || s.Contains("codex")) return BuiltIn[0];
            if (s.Contains("claude")) return BuiltIn[1];
            if (s.Contains("gemini") || s.Contains("antigravity")) return BuiltIn[2];
            if (s.Contains("grok")) return BuiltIn[3];
            if (s.Contains("deepseek") || s.Contains("deep seek")) return BuiltIn[4];
            return null;
        }
        internal static string Hash(byte[] data)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        internal static string FileHash(string path) { return Hash(File.ReadAllBytes(path)); }
        internal static void Validate(byte[] bytes)
        {
            if (bytes.Length < 22 || bytes.Length > 8 * 1024 * 1024 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 1 || bytes[3] != 0)
                throw new IOException("请选择有效的 ICO 图标文件（最大 8 MB）。");
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                IconBitmapDecoder decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) throw new IOException("ICO 中没有图像。");
                foreach (BitmapFrame frame in decoder.Frames)
                {
                    if (frame.PixelWidth > 1024 || frame.PixelHeight > 1024) throw new IOException("ICO 帧尺寸不能超过 1024 像素。");
                    // Force decoding, including frames not used by the preview.
                    int stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
                    frame.CopyPixels(new byte[checked(stride * frame.PixelHeight)], stride, 0);
                }
            }
        }
        internal static string Cache(string root, byte[] bytes)
        {
            Validate(bytes);
            string directory = Path.Combine(root, "icons");
            Directory.CreateDirectory(directory);
            SafeFiles.NoReparse(directory);
            string path = Path.Combine(directory, Hash(bytes) + ".ico");
            if (File.Exists(path))
            {
                SafeFiles.NoReparse(path);
                if (FileHash(path) != Hash(bytes)) throw new IOException("已保存的图标校验失败，请保留数据目录并重新下载工具。");
            }
            else SafeFiles.Create(path, bytes);
            return path;
        }
    }
}
