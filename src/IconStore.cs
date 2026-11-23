using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MascotIcons
{
    internal sealed class IconChoice
    {
        private readonly object previewLock = new object();
        private ImageSource cachedPreview;
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Subtitle { get; private set; }
        public string CustomFile { get; private set; }
        public string Group { get; private set; }
        public string SkinLabel { get; private set; }
        internal int PreviewDecodeCount { get; private set; }
        public IconChoice(string id, string name, string subtitle, string file, string group = null, string skinLabel = null)
        { Id = id; Name = name; Subtitle = subtitle; CustomFile = file; Group = group; SkinLabel = skinLabel; }
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
            if (CustomFile == null && cachedPreview != null) return cachedPreview;
            lock (previewLock)
            {
                if (CustomFile == null && cachedPreview != null) return cachedPreview;
                using (Stream stream = CustomFile != null ? (Stream)new MemoryStream(Bytes()) :
                    Assembly.GetExecutingAssembly().GetManifestResourceStream("Icons." + Id + ".png"))
                {
                    if (stream == null) throw new IOException("缺少内置预览图：" + Name);
                    BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapFrame best = decoder.Frames[0];
                    foreach (BitmapFrame frame in decoder.Frames) if (frame.PixelWidth > best.PixelWidth) best = frame;
                    best.Freeze();
                    ++PreviewDecodeCount;
                    if (CustomFile == null) cachedPreview = best;
                    return best;
                }
            }
        }
    }

    internal static class IconStore
    {
        private static Regex Pattern(string regex)
        { return new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled); }

        private const string NameSeparator = @"[\s\-_·/()（）]";
        private static Regex ChineseProduct(string name, string descriptions)
        {
            // Each pass consumes a whole suffix. Unknown text makes the entire name ineligible.
            const string common = @"桌面版?|电脑版|客户端|网页版|app(?![a-z])|快捷方式|v?\d+(?:\.\d+)*";
            return Pattern(@"^(?:" + name + @")(?>(?:" + NameSeparator + @"*(?:" + descriptions + "|" + common + @")))*" + NameSeparator + @"*$");
        }

        private static readonly Regex RegexChatGpt = Pattern(@"(?<![a-z0-9])(?:chat[\s\-_]*gpt|codex)(?![a-z])");
        private static readonly Regex RegexClaude = Pattern(@"(?<![a-z0-9])claude(?![a-z])");
        private static readonly Regex RegexGemini = Pattern(@"(?<![a-z0-9])(?:gemini|antigravity)(?![a-z])");
        private static readonly Regex RegexGrok = Pattern(@"(?<![a-z0-9])grok(?![a-z])");
        private static readonly Regex RegexDeepSeek = Pattern(@"(?<![a-z0-9])deep[\s\-_]*seek(?![a-z])");
        private static readonly Regex RegexZCode = Pattern(@"(?<![a-z0-9])z[\s\-_]*code(?![a-z])");
        private static readonly Regex RegexDoubaoAscii = Pattern(@"(?<![a-z0-9])doubao(?:[\s\-_]*work)?(?![a-z])");
        private static readonly Regex RegexDoubaoZh = ChineseProduct(@"(?:字节(?:跳动)?[\s\-_·]*)?豆包", @"工作(?:台|版|空间)?|办公|work(?![a-z])|ai(?![a-z])|智能(?:助手)?|助手|desktop(?![a-z])|client(?![a-z])|pro(?![a-z])|极速版|爱学");
        private static readonly Regex RegexDoubaoWorkFlag = Pattern(@"(?:doubao|豆包)" + NameSeparator + @"*(?:work(?![a-z])|工作|办公)");
        private static readonly Regex ProductSeparator = Pattern(@"[\s/|+、,，\-–—]+");
        private static readonly Regex ProductListSeparator = Pattern(@"\s+[-–—]\s+|[+|/、,，]");
        private static readonly Regex RegexMiniMax = Pattern(@"(?<![a-z0-9])mini[\s\-_]*max(?![a-z])");
        private static readonly Regex RegexWorkBuddy = Pattern(@"(?<![a-z0-9])work[\s\-_]*buddy(?![a-z])");
        private static readonly Regex RegexKimi = Pattern(@"(?<![a-z0-9])kimi(?![a-z])");

        private static readonly Regex AliasZCode = Pattern(@"(?<![a-z0-9])(?:chat[\s\-_]*)?glm(?:\d+(?:\.\d+)*)?(?![a-z0-9])|(?<![a-z0-9])z\.ai(?![a-z0-9])|智谱");
        private static readonly Regex AliasMiniMax = ChineseProduct("海螺", @"ai(?![a-z])|问问|视频|音乐|语音|智能(?:助手)?|助手|agent(?![a-z])");
        private static readonly Regex AliasWorkBuddy = Pattern(@"(?<![a-z0-9])(?:hunyuan|yuanbao)(?![a-z])");
        private static readonly Regex AliasWorkBuddyZh = ChineseProduct(@"(?:腾讯[\s\-_·]*)?(?:混元|元宝)", @"ai(?![a-z])|大模型|模型|智能(?:助手)?|助手|3d(?![a-z])");
        private static readonly Regex AliasKimi = Pattern(@"(?<![a-z0-9])moonshot(?![a-z])|月之暗面");

        internal static readonly IconChoice[] BuiltIn = {
            new IconChoice("ChatGPT", "ChatGPT 娘", "白色龙娘 · Codex", null),
            new IconChoice("Claude", "Claude 娘", "暖橙书娘 · Claude", null),
            new IconChoice("Gemini", "Gemini 娘", "渐变兽耳 · Antigravity", null),
            new IconChoice("Grok", "Grok 娘", "黑白哥特 · Grok", null),
            new IconChoice("DeepSeek", "DeepSeek 娘", "蓝发鲸娘 · DeepSeek", null),
            new IconChoice("ZCode", "ZCode 娘", "黑发女仆 · ZCode", null),
            new IconChoice("DoubaoClassic", "豆包经典娘", "红巾短发 · 豆包", null, "Doubao", "经典"),
            new IconChoice("DoubaoWork", "豆包工作娘", "紫蓝马尾 · 豆包工作", null, "Doubao", "工作"),
            new IconChoice("MiniMax", "MiniMax 娘", "海螺贝雷 · MiniMax", null),
            new IconChoice("WorkBuddy", "WorkBuddy 娘", "国风混元 · WorkBuddy", null),
            new IconChoice("Kimi", "Kimi 娘", "星月蕾丝 · Kimi", null)
        };
        internal static IconChoice Suggest(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string trimmed = name.Trim();
            if (trimmed.IndexOf("notebook", StringComparison.OrdinalIgnoreCase) >= 0) return null;

            bool isProduct;
            string[] parts = ProductListSeparator.Split(trimmed);
            if (parts.Length > 1)
            {
                HashSet<IconChoice> products = new HashSet<IconChoice>();
                HashSet<IconChoice> aliases = new HashSet<IconChoice>();
                foreach (string part in parts)
                {
                    IconChoice item = MatchName(part.Trim(), out isProduct);
                    // A separator alone does not establish a product list (e.g. 海螺 - 水泥报价).
                    if (item == null) return MatchName(trimmed, out isProduct);
                    (isProduct ? products : aliases).Add(item);
                }
                HashSet<IconChoice> hits = products.Count > 0 ? products : aliases;
                return hits.Count == 1 ? hits.First() : null;
            }
            return MatchName(trimmed, out isProduct);
        }

        private static IconChoice MatchName(string trimmed, out bool isProduct)
        {
            isProduct = true;
            List<IconChoice> explicitHits = new List<IconChoice>();
            if (RegexChatGpt.IsMatch(trimmed)) explicitHits.Add(BuiltIn[0]);
            if (RegexClaude.IsMatch(trimmed)) explicitHits.Add(BuiltIn[1]);
            if (RegexGemini.IsMatch(trimmed)) explicitHits.Add(BuiltIn[2]);
            if (RegexGrok.IsMatch(trimmed)) explicitHits.Add(BuiltIn[3]);
            if (RegexDeepSeek.IsMatch(trimmed)) explicitHits.Add(BuiltIn[4]);
            if (RegexZCode.IsMatch(trimmed)) explicitHits.Add(BuiltIn[5]);
            if (RegexMiniMax.IsMatch(trimmed)) explicitHits.Add(BuiltIn[8]);
            if (RegexWorkBuddy.IsMatch(trimmed)) explicitHits.Add(BuiltIn[9]);
            if (RegexKimi.IsMatch(trimmed)) explicitHits.Add(BuiltIn[10]);
            if (RegexDoubaoAscii.IsMatch(trimmed) || RegexDoubaoZh.IsMatch(trimmed))
                explicitHits.Add(RegexDoubaoWorkFlag.IsMatch(trimmed) ? BuiltIn[7] : BuiltIn[6]);
            else if (explicitHits.Count > 0 && trimmed.Contains("豆包") &&
                Array.Exists(ProductSeparator.Split(trimmed), part => RegexDoubaoZh.IsMatch(part)))
                return null;

            if (explicitHits.Count == 1) return explicitHits[0];
            if (explicitHits.Count > 1) return null;

            isProduct = false;
            List<IconChoice> aliasHits = new List<IconChoice>();
            if (AliasZCode.IsMatch(trimmed)) aliasHits.Add(BuiltIn[5]);
            if (AliasMiniMax.IsMatch(trimmed)) aliasHits.Add(BuiltIn[8]);
            if (AliasWorkBuddy.IsMatch(trimmed) || AliasWorkBuddyZh.IsMatch(trimmed)) aliasHits.Add(BuiltIn[9]);
            if (AliasKimi.IsMatch(trimmed)) aliasHits.Add(BuiltIn[10]);

            return aliasHits.Count == 1 ? aliasHits[0] : null;
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
