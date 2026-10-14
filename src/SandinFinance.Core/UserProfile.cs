using System.Security.Cryptography;
using System.Text.Json;

namespace SandinFinance.Core;

public sealed record UserProfile
{
    public int Version { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Sandin";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? PinSalt { get; init; }
    public string? PinHash { get; init; }
    public int PinIterations { get; init; }
    public int FailedAttempts { get; init; }
    public decimal MonthlyBudget { get; init; }
    public Guid? BudgetAccountId { get; init; }
    public Guid? BudgetDisplayAccountId { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasPin => PinHash is not null;

    public void Validate()
    {
        Money.Validate(MonthlyBudget, "Месячный лимит");
        if (BudgetAccountId == Guid.Empty) throw new ArgumentException("Некорректный счёт месячного лимита.");
        if (BudgetDisplayAccountId == Guid.Empty) throw new ArgumentException("Некорректная карта отображения бюджета.");
        if (Version != 1 || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || CreatedAt == default)
            throw new InvalidDataException("Повреждены данные профиля.");
        if (FailedAttempts is < 0 or > 5) throw new InvalidDataException("Повреждены параметры входа.");
        if ((PinHash is null) != (PinSalt is null) || (HasPin && PinIterations is < 100_000 or > 2_000_000))
            throw new InvalidDataException("Повреждены параметры PIN.");
        if (HasPin)
        {
            try
            {
                if (Convert.FromBase64String(PinHash!).Length != 32 || Convert.FromBase64String(PinSalt!).Length != 16)
                    throw new InvalidDataException("Повреждены параметры PIN.");
            }
            catch (FormatException) { throw new InvalidDataException("Повреждены параметры PIN."); }
        }
    }
}

public sealed class ProfileStore(string directory)
{
    private const int Iterations = 600_000;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(DirectoryPath, "profile.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultDirectory(string documentsDirectory) => Path.Combine(Path.GetFullPath(documentsDirectory), "SandinFinance", "Sandin");

    public static string DirectoryForName(string documentsDirectory, string name)
    {
        name = ValidateProfileName(name);
        return Path.Combine(Path.GetFullPath(documentsDirectory), "SandinFinance", name);
    }

    public static string ValidateProfileName(string name)
    {
        name = (name ?? "").Trim();
        var device = name.Split('.')[0].ToUpperInvariant();
        if (name.Length is < 1 or > 80 || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) || name.EndsWith('.') ||
            device is "CON" or "PRN" or "AUX" or "NUL" ||
            (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] is >= '1' and <= '9'))
            throw new ArgumentException("Введите имя до 80 символов без знаков <> : \" / \\ | ? * и точки в конце.");
        return name;
    }

    public static string? FindExistingDirectory(string documentsDirectory)
    {
        var root = Path.Combine(Path.GetFullPath(documentsDirectory), "SandinFinance");
        var previous = DefaultDirectory(documentsDirectory);
        if (File.Exists(Path.Combine(previous, "profile.json"))) return previous;
        if (!Directory.Exists(root)) return null;
        var profiles = Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, "profile.json"))).Take(2).ToList();
        if (profiles.Count > 1) throw new IOException("Найдено несколько профилей. Откройте нужный с параметром --data-dir <папка профиля>.");
        return profiles.SingleOrDefault();
    }

    public static ProfileStore Initialize(string directory, string? legacyDirectory = null, string name = "Sandin")
    {
        var store = new ProfileStore(directory);
        if (File.Exists(store.FilePath)) { store.Load(); ProfileLayout.Migrate(store.DirectoryPath); return store; }
        name = ValidateProfileName(name);
        Directory.CreateDirectory(store.DirectoryPath);
        ProfileLayout.Migrate(store.DirectoryPath);
        if (legacyDirectory is not null && !string.Equals(Path.GetFullPath(legacyDirectory), store.DirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            // Validate original files without changing them. All financial history and PNG icons live in these documents.
            new AccountStore(legacyDirectory).Load();
            new CategoryStore(legacyDirectory).Load();
            foreach (var fileName in new[] { "accounts.json", "accounts.json.bak", "categories.json", "categories.json.bak" })
            {
                var source = fileName.EndsWith(".bak") ? ProfileLayout.AutomaticBackup(legacyDirectory, fileName[..^4]) : ProfileLayout.ReadFile(legacyDirectory, fileName);
                if (!File.Exists(source)) source = Path.Combine(legacyDirectory, fileName);
                var destination = fileName.EndsWith(".bak") ? ProfileLayout.AutomaticBackup(store.DirectoryPath, fileName[..^4]) : ProfileLayout.DataFile(store.DirectoryPath, fileName);
                if (!File.Exists(source) || File.Exists(destination)) continue;
                var temporary = destination + ".migration.tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, temporary, overwrite: true);
                File.Move(temporary, destination);
            }
        }
        store.Save(new UserProfile { Name = name });
        ProfileLayout.Migrate(store.DirectoryPath);
        return store;
    }

    public UserProfile Load()
    {
        var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(FilePath), Options)
            ?? throw new InvalidDataException("Профиль пуст.");
        profile.Validate();
        return profile;
    }

    private void Save(UserProfile profile)
    {
        profile.Validate();
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, Options);
                stream.Flush(flushToDisk: true);
            }
            var backup = ProfileLayout.AutomaticBackup(DirectoryPath, "profile.json");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, backup);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void SetInitialPin(string pin)
    {
        ValidatePin(pin);
        var profile = Load();
        if (profile.HasPin) throw new InvalidOperationException("PIN уже установлен. Измените его через настройки профиля.");
        Save(WithPin(profile, pin));
    }

    public bool VerifyPin(string pin, DateTimeOffset? timestamp = null)
    {
        ValidatePin(pin);
        var now = timestamp ?? DateTimeOffset.UtcNow;
        var profile = Load();
        if (!profile.HasPin) return false;
        var needsReset = profile.FailedAttempts != 0 || profile.LockedUntil is not null;
        if (profile.LockedUntil is { } locked && locked > now)
            throw new InvalidOperationException($"Повторите ввод через {Math.Ceiling((locked - now).TotalSeconds)} сек.");
        if (profile.LockedUntil is not null) profile = profile with { FailedAttempts = 0, LockedUntil = null };
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(profile.PinSalt!), profile.PinIterations, HashAlgorithmName.SHA256, 32);
        var valid = CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(profile.PinHash!));
        CryptographicOperations.ZeroMemory(actual);
        if (valid)
        {
            if (needsReset) Save(profile with { FailedAttempts = 0, LockedUntil = null });
            return true;
        }
        var attempts = profile.FailedAttempts + 1;
        Save(profile with { FailedAttempts = attempts, LockedUntil = attempts >= 5 ? now.AddSeconds(30) : null });
        return false;
    }

    public void ChangePin(string currentPin, string newPin)
    {
        ValidatePin(newPin);
        if (!VerifyPin(currentPin)) throw new ArgumentException("Текущий PIN неверный.");
        Save(WithPin(Load(), newPin));
    }
    public void SetBudget(decimal amount, Guid? accountId)
    {
        Save(Load() with { MonthlyBudget = amount, BudgetAccountId = accountId });
    }
    public void SetBudgetDisplay(Guid? accountId) => Save(Load() with { BudgetDisplayAccountId = accountId });

    private static UserProfile WithPin(UserProfile profile, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var updated = profile with
        {
            PinSalt = Convert.ToBase64String(salt), PinHash = Convert.ToBase64String(hash), PinIterations = Iterations,
            FailedAttempts = 0, LockedUntil = null
        };
        CryptographicOperations.ZeroMemory(hash);
        return updated;
    }

    private static void ValidatePin(string pin)
    {
        if (pin is null || pin.Length != 4 || pin.Any(ch => ch < '0' || ch > '9'))
            throw new ArgumentException("PIN должен состоять ровно из четырёх цифр.");
    }
}
