using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MascotIcons
{
    internal static class SafeFiles
    {
        internal static void NoReparse(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("为避免修改重定向文件，请使用普通本地文件：" + Path.GetFileName(path));
        }
        internal static void Create(string path, byte[] bytes)
        {
            using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        }
        internal static void WriteAtomic(string path, byte[] bytes)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Create(temp, bytes);
                if (File.Exists(path)) { NoReparse(path); File.Replace(temp, path, null); }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    [DataContract]
    internal sealed class BackupRecord
    {
        [DataMember] public int Version = 1;
        [DataMember] public string ShortcutPath;
        [DataMember] public string OriginalHash;
        [DataMember] public string AppliedHash;
        [DataMember] public string PendingHash;
        [DataMember] public string IconPath;
        [DataMember] public string ChangedUtc;
    }

    internal sealed class ShortcutService
    {
        internal string Root { get; private set; }
        // Test-only fault boundary, before committing the shortcut. Never set by the UI.
        internal Action AfterJournalSaved { get; set; }
        internal ShortcutService(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root); SafeFiles.NoReparse(Root);
            foreach (string name in new[] { "backups", "records" })
            { string path = Path.Combine(Root, name); Directory.CreateDirectory(path); SafeFiles.NoReparse(path); }
        }
        internal static string Normalize(string file)
        {
            string path = Path.GetFullPath(file);
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || !string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new IOException("首版支持本地 .lnk 快捷方式；请拖入快捷方式，不是 EXE 或网址文件。");
            if (!File.Exists(path)) throw new FileNotFoundException("找不到快捷方式，可能已被移动或删除。", path);
            SafeFiles.NoReparse(path);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("快捷方式文件异常：超过 4 MB。");
            return path;
        }
        internal string Key(string file) { return IconStore.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())); }
        internal string RecordPath(string file) { return Path.Combine(Root, "records", Key(file) + ".json"); }
        internal string BackupPath(string file) { return Path.Combine(Root, "backups", Key(file) + ".lnk"); }
        internal BackupRecord Load(string file)
        {
            string path = RecordPath(file);
            if (!File.Exists(path)) return null;
            SafeFiles.NoReparse(path);
            if (new FileInfo(path).Length > 65536) throw new IOException("备份记录异常，请保留数据目录。");
            using (FileStream stream = File.OpenRead(path))
            {
                BackupRecord record = (BackupRecord)new DataContractJsonSerializer(typeof(BackupRecord)).ReadObject(stream);
                if (record.Version != 1 || !string.Equals(record.ShortcutPath, file, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("备份记录与快捷方式不匹配。");
                return record;
            }
        }
        internal void Save(string file, BackupRecord record)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(BackupRecord)).WriteObject(stream, record);
                SafeFiles.WriteAtomic(RecordPath(file), stream.ToArray());
            }
        }
        private void CheckBackup(string file, BackupRecord record)
        {
            string path = BackupPath(file);
            if (!File.Exists(path)) throw new IOException("找不到原始备份；已停止，避免失去恢复能力。");
            SafeFiles.NoReparse(path);
            if (IconStore.FileHash(path) != record.OriginalHash) throw new IOException("原始备份校验失败；已停止，请保留数据目录。");
        }
        private static void CheckCurrent(string hash, BackupRecord record)
        {
            if (hash != record.OriginalHash && hash != record.AppliedHash && hash != record.PendingHash)
                throw new IOException("此快捷方式后来被其他程序修改过。为保留新的启动设置，已跳过；请使用一个新的快捷方式副本。");
        }
        internal string State(string file)
        {
            try
            {
                BackupRecord record = Load(file);
                if (record == null) return "未修改";
                CheckBackup(file, record);
                string hash = IconStore.FileHash(file);
                if (hash == record.OriginalHash) return "已恢复";
                CheckCurrent(hash, record);
                return "可恢复";
            }
            catch { return "需要检查"; }
        }
        internal void Apply(string file, byte[] iconBytes)
        {
            file = Normalize(file);
            byte[] original = File.ReadAllBytes(file);
            string currentHash = IconStore.Hash(original);
            LinkInfo before = NativeLink.Read(file);
            string icon = IconStore.Cache(Root, iconBytes);
            BackupRecord record = Load(file);
            if (record != null) { CheckBackup(file, record); CheckCurrent(currentHash, record); }
            else
            {
                string backup = BackupPath(file);
                if (File.Exists(backup))
                {
                    SafeFiles.NoReparse(backup);
                    if (IconStore.FileHash(backup) != currentHash) throw new IOException("存在未完成的旧备份；已停止，请保留数据目录。");
                }
                else SafeFiles.Create(backup, original);
                record = new BackupRecord { ShortcutPath = file, OriginalHash = currentHash };
                CheckBackup(file, record);
            }
            string stage = Path.Combine(Path.GetDirectoryName(file), ".mascot-" + Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                SafeFiles.Create(stage, original);
                NativeLink.SetIcon(stage, icon, 0);
                LinkInfo after = NativeLink.Read(stage);
                if (!before.SameLaunch(after) || after.IconPath != icon || after.IconIndex != 0)
                    throw new IOException("新图标的验证未通过，原快捷方式没有更改。");
                string stagedHash = IconStore.FileHash(stage);
                record.PendingHash = stagedHash; record.IconPath = icon;
                record.ChangedUtc = DateTime.UtcNow.ToString("o");
                Save(file, record); // Durable recovery journal BEFORE the destination changes.
                if (AfterJournalSaved != null) AfterJournalSaved();
                SafeFiles.NoReparse(file);
                if (IconStore.FileHash(file) != currentHash) throw new IOException("快捷方式在操作中被修改，已停止。请重新扫描。");
                File.Replace(stage, file, null);
                // AppliedHash and PendingHash deliberately remain equal. A crash during/after
                // replacement is recoverable without requiring a second journal write.
                record.AppliedHash = stagedHash;
                try { Save(file, record); } catch (IOException) { /* PendingHash is already durable. */ }
                NativeLink.Notify(file);
            }
            finally { if (File.Exists(stage)) File.Delete(stage); }
        }
        internal void Restore(string file)
        {
            file = Normalize(file);
            BackupRecord record = Load(file);
            if (record == null) throw new IOException("此快捷方式没有本工具的备份。");
            CheckBackup(file, record);
            string hash = IconStore.FileHash(file);
            CheckCurrent(hash, record);
            if (hash == record.OriginalHash) return;
            byte[] original = File.ReadAllBytes(BackupPath(file));
            string stage = Path.Combine(Path.GetDirectoryName(file), ".mascot-restore-" + Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                SafeFiles.Create(stage, original);
                SafeFiles.NoReparse(file);
                if (IconStore.FileHash(file) != hash) throw new IOException("快捷方式在恢复过程中被修改，已停止。");
                File.Replace(stage, file, null);
                NativeLink.Notify(file);
            }
            finally { if (File.Exists(stage)) File.Delete(stage); }
        }
    }
}
