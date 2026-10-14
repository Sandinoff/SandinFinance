using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandinFinance.Core;

public sealed class RecurringPaymentStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string FilePath { get; } = ProfileLayout.DataFile(directory, "recurring-payments.json");
    public string BackupPath { get; } = ProfileLayout.AutomaticBackup(directory, "recurring-payments.json");

    public List<RecurringPayment> Load()
    {
        var source = ProfileLayout.ReadFile(directory, "recurring-payments.json");
        if (!File.Exists(source)) return [];
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(source), Options) ?? throw new InvalidDataException("Файл регулярных платежей пуст.");
        if (document.Version != 1 || document.Payments is null) throw new InvalidDataException("Неподдерживаемый формат регулярных платежей.");
        Validate(document.Payments);
        return document.Payments;
    }

    public void Save(IReadOnlyCollection<RecurringPayment> payments)
    {
        Validate(payments);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Document(1, payments.ToList()), Options);
                stream.Flush(flushToDisk: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, BackupPath);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public RecurringPayment Complete(Guid id, DateTime expectedDueAt, DateTime? now = null)
    {
        var payments = Load();
        var payment = payments.SingleOrDefault(p => p.Id == id) ?? throw new ArgumentException("Платёж не найден.");
        if (payment.NextDueAt != expectedDueAt) throw new ArgumentException("Срок уже изменился или оплата отмечена. Обновите список.");
        var updated = payment with { CompletedCount = checked(payment.CompletedCount + 1), LastCompletedAt = now ?? DateTime.Now };
        Save(payments.Select(p => p.Id == id ? updated : p).ToList());
        return updated;
    }

    public List<RecurringPayment> Reminders(DateTime now) => Load().Where(p => p.NeedsReminder(now)).OrderBy(p => p.NextDueAt).ThenBy(p => p.Name).ToList();

    private static void Validate(IEnumerable<RecurringPayment> payments)
    {
        var ids = new HashSet<Guid>();
        foreach (var payment in payments)
        {
            if (payment is null) throw new InvalidDataException("Пустой регулярный платёж.");
            payment.Validate();
            if (!ids.Add(payment.Id)) throw new ArgumentException("Повторяющийся идентификатор регулярного платежа.");
        }
    }
    private sealed record Document(int Version, List<RecurringPayment>? Payments);
}
