namespace SandinFinance.Core;

public static class ProfileLayout
{
    public static string DataFile(string root, string name) => Path.Combine(Path.GetFullPath(root), "data", name);
    public static string AutomaticBackup(string root, string name) => Path.Combine(Path.GetFullPath(root), "backups", "automatic", name + ".bak");
    public static string ManualBackups(string root) => Path.Combine(Path.GetFullPath(root), "backups", "manual");
    public static string ReadFile(string root, string name) => File.Exists(DataFile(root, name)) ? DataFile(root, name) : Path.Combine(Path.GetFullPath(root), name);

    public static void Migrate(string directory)
    {
        var root = Path.GetFullPath(directory);
        // Check financial files before reorganizing anything. Never replace a different copy.
        new AccountStore(root).Load();
        new CategoryStore(root).Load();
        new RecurringPaymentStore(root).Load();
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "backups", "automatic"));
        Directory.CreateDirectory(ManualBackups(root));
        foreach (var name in new[] { "accounts.json", "categories.json", "recurring-payments.json" })
            MoveFile(Path.Combine(root, name), DataFile(root, name));
        foreach (var name in new[] { "accounts.json", "categories.json", "recurring-payments.json", "profile.json" })
        {
            MoveFile(Path.Combine(root, name + ".bak"), AutomaticBackup(root, name));
            if (name != "profile.json") MoveFile(DataFile(root, name) + ".bak", AutomaticBackup(root, name));
        }
        var backupRoot = Path.Combine(root, "backups");
        foreach (var source in Directory.EnumerateDirectories(backupRoot).ToList())
        {
            var name = Path.GetFileName(source);
            if (name is "automatic" or "manual" || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) continue;
            var target = Path.Combine(ManualBackups(root), name);
            if (!Directory.Exists(target)) Directory.Move(source, target);
        }
    }

    private static void MoveFile(string source, string target)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target)) { File.Move(source, target); return; }
        if (File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target))) File.Delete(source);
        else throw new IOException($"Найдены разные копии файла. Обе сохранены: {source} и {target}");
    }
}
