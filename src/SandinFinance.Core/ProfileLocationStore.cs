using System.Text.Json;

namespace SandinFinance.Core;

// This preference belongs to the signed-in Windows user, not to the application folder.
public sealed class ProfileLocationStore(string localApplicationData)
{
    public string FilePath { get; } = Path.Combine(Path.GetFullPath(localApplicationData), "SandinFinance", "profile-location.json");
    public string? LoadDirectory()
    {
        if (!File.Exists(FilePath)) return null;
        var value = JsonSerializer.Deserialize<Location>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Не удалось прочитать выбранную папку профиля.");
        if (value.Version != 1 || !Path.IsPathFullyQualified(value.Directory)) throw new InvalidDataException("Некорректная папка профиля.");
        var directory = Path.GetFullPath(value.Directory);
        // Never fall back to an empty budget when a selected drive is unavailable.
        if (!File.Exists(Path.Combine(directory, "profile.json"))) throw new IOException("Выбранная папка профиля недоступна. Проверьте подключение диска: " + directory);
        new ProfileStore(directory).Load();
        return directory;
    }
    public void SaveDirectory(string directory)
    {
        directory = Path.GetFullPath(directory);
        new ProfileStore(directory).Load();
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Location(1, directory));
                stream.Flush(true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record Location(int Version, string Directory);
}

public static class ProfileRelocation
{
    public static void Copy(string source, string destination)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Этот профиль уже находится в выбранной папке.");
        if (destination.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите отдельную папку вне текущего профиля.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Выберите пустую папку. Существующие файлы не перезаписываются.");
        if (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("Выберите обычную папку, а не ссылку.");
        new ProfileStore(source).Load();
        new AccountStore(source).Load();
        new CategoryStore(source).Load();
        new RecurringPaymentStore(source).Load();
        var files = Files(source).ToList();
        Directory.CreateDirectory(destination);
        foreach (var file in files)
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            input.CopyTo(output);
            output.Flush(true);
            input.Position = output.Position = 0;
            if (!System.Security.Cryptography.SHA256.HashData(input).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(output))) throw new IOException("Не удалось проверить скопированный файл.");
        }
        new ProfileStore(destination).Load();
        new AccountStore(destination).Load();
        new CategoryStore(destination).Load();
        new RecurringPaymentStore(destination).Load();
        // Keep the original directory intact as a recovery copy.
    }
    private static IEnumerable<string> Files(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Перенос папок-ссылок не поддерживается.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Профиль содержит ссылку. Перенос отменён.");
            if ((attributes & FileAttributes.Directory) != 0) { foreach (var file in Files(entry)) yield return file; }
            else yield return entry;
        }
    }
}
