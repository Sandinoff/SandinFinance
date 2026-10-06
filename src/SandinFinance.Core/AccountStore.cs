using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandinFinance.Core;

public sealed class AccountStore(string directory)
{
    private int loadedVersion = 8;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath { get; } = Path.Combine(Path.GetFullPath(directory), "accounts.json");

    public List<Account> Load()
    {
        var document = ReadDocument();
        loadedVersion = document.Version;
        return document.Accounts!;
    }

    private StoreDocument ReadDocument()
    {
        if (!File.Exists(FilePath)) return new StoreDocument(8, [], []);
        var document = JsonSerializer.Deserialize<StoreDocument>(File.ReadAllText(FilePath), Options)
            ?? throw new InvalidDataException("Файл счетов пуст.");
        if (document.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8) || document.Accounts is null || (document.Version >= 6 && document.DeletedAccounts is null))
            throw new InvalidDataException("Неподдерживаемый формат файла счетов.");
        var accounts = document.Version < 3 ? document.Accounts.Select(Upgrade).ToList() : document.Accounts;
        var deleted = document.DeletedAccounts ?? [];
        Validate(accounts, deleted);
        return document with { Accounts = accounts, DeletedAccounts = deleted };
    }

    public void Save(IReadOnlyCollection<Account> accounts)
    {
        SaveDocument(accounts, ReadDocument().DeletedAccounts!);
    }

    public Account Delete(Guid accountId)
    {
        var document = ReadDocument();
        var selected = document.Accounts!.SingleOrDefault(a => a.Id == accountId)
            ?? throw new ArgumentException("Счёт не найден.");
        SaveDocument(document.Accounts!.Where(a => a.Id != accountId).ToList(), [.. document.DeletedAccounts!, selected]);
        return selected;
    }

    private void SaveDocument(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<Account> deleted)
    {
        Validate(accounts, deleted);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new StoreDocument(8, accounts.ToList(), deleted.ToList()), Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
            loadedVersion = 8;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public List<Account> LoadAndAccrue(DateOnly asOf)
    {
        var loaded = Load();
        var accrued = loaded.Select(account => SavingsInterest.Accrue(account, asOf)).ToList();
        if (loadedVersion < 8 || !loaded.SequenceEqual(accrued)) Save(accrued);
        return accrued;
    }

    private static Account Upgrade(Account account)
    {
        if (account is null) throw new InvalidDataException("Файл содержит пустой счёт.");
        return account with
        {
            GraceRule = account.GraceRule == GracePeriodRule.DaysThenMonthEnd ? GracePeriodRule.MonthEndPlusDays : account.GraceRule,
            PaymentParts = account.GraceRule == GracePeriodRule.DaysThenMonthEnd ? 1 : account.PaymentParts,
            Purchases = account.Purchases?.Select(p => p is not null && p.Rule == GracePeriodRule.DaysThenMonthEnd
                ? p with { Rule = GracePeriodRule.MonthEndPlusDays, PaymentParts = 1 } : p).ToList()!
        };
    }

    private static void Validate(IEnumerable<Account> accounts, IEnumerable<Account> deleted)
    {
        var active = accounts.ToList();
        var list = active.Concat(deleted).ToList();
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in list)
        {
            if (account is null) throw new InvalidDataException("Файл содержит пустой счёт.");
            account.Validate();
            if (!ids.Add(account.Id)) throw new ArgumentException("Повторяющийся идентификатор счёта.");
        }
        foreach (var account in active)
            if (!names.Add(account.Name.Trim())) throw new ArgumentException("Счёт с таким названием уже существует.");
        var transfers = list.SelectMany(a => a.Transactions.Where(t => t.IsTransfer).Select(t => (AccountId: a.Id, Entry: t))).GroupBy(item => item.Entry.TransferId);
        foreach (var transfer in transfers)
        {
            var legs = transfer.ToList();
            if (legs.Count != 2 || legs[0].AccountId == legs[1].AccountId || legs[0].Entry.Id == legs[1].Entry.Id || legs[0].Entry.Kind == legs[1].Entry.Kind ||
                legs[0].Entry.Amount != legs[1].Entry.Amount || legs[0].Entry.OccurredAt != legs[1].Entry.OccurredAt ||
                legs[0].Entry.CounterpartyAccountId != legs[1].AccountId || legs[1].Entry.CounterpartyAccountId != legs[0].AccountId)
                throw new ArgumentException("Перевод должен содержать согласованные списание и зачисление на разные счета.");
        }
    }

    private sealed record StoreDocument(int Version, List<Account>? Accounts, List<Account>? DeletedAccounts = null);
}
