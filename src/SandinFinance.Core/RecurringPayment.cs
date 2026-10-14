using System.Globalization;
using System.Text.Json.Serialization;

namespace SandinFinance.Core;

public enum PaymentRepeat { None, Weekly, Monthly, Yearly }

public sealed record RecurringPayment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public decimal Amount { get; init; }
    public Guid AccountId { get; init; }
    public Guid CategoryId { get; init; }
    public string MerchantName { get; init; } = "";
    public DateTime FirstDueAt { get; init; }
    public PaymentRepeat Repeat { get; init; } = PaymentRepeat.Monthly;
    public int Interval { get; init; } = 1;
    public DateOnly? EndDate { get; init; }
    public int CompletedCount { get; init; }
    public DateTime? LastCompletedAt { get; init; }
    public bool IsEnabled { get; init; } = true;

    [JsonIgnore] public DateTime? ScheduledDueAt => DueAt(CompletedCount);
    [JsonIgnore] public DateTime? NextDueAt => IsEnabled ? ScheduledDueAt : null;
    [JsonIgnore] public string AmountText => Money.Format(Amount);
    [JsonIgnore] public string DueText => ScheduledDueAt?.ToString("dd.MM.yyyy · HH:mm", CultureInfo.GetCultureInfo("ru-RU")) ?? "Все платежи завершены";
    [JsonIgnore] public string RepeatText => Repeat switch
    {
        PaymentRepeat.None => "Без повторения",
        PaymentRepeat.Weekly => Interval == 1 ? "Каждую неделю" : $"Каждые {Interval} нед.",
        PaymentRepeat.Monthly => Interval == 1 ? "Каждый месяц" : $"Каждые {Interval} мес.",
        PaymentRepeat.Yearly => Interval == 1 ? "Каждый год" : $"Каждые {Interval} г.",
        _ => ""
    };

    public DateTime? DueAt(int occurrence)
    {
        if (occurrence < 0 || (Repeat == PaymentRepeat.None && occurrence > 0)) return null;
        try
        {
            var offset = checked(occurrence * Interval);
            var due = Repeat switch
            {
                PaymentRepeat.Weekly => FirstDueAt.AddDays(checked(offset * 7)),
                PaymentRepeat.Monthly => FirstDueAt.AddMonths(offset),
                PaymentRepeat.Yearly => FirstDueAt.AddYears(offset),
                _ => FirstDueAt
            };
            return EndDate is { } end && DateOnly.FromDateTime(due) > end ? null : due;
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (OverflowException) { return null; }
    }

    public bool NeedsReminder(DateTime now) => NextDueAt is { } due && due.Date <= now.Date.AddDays(3);

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80) throw new ArgumentException("Название платежа: от 1 до 80 символов.");
        Money.Validate(Amount, "Сумма платежа");
        if (Amount <= 0) throw new ArgumentException("Сумма платежа должна быть больше нуля.");
        if (AccountId == Guid.Empty || CategoryId == Guid.Empty) throw new ArgumentException("Выберите счёт и категорию расходов.");
        if (MerchantName is null || MerchantName.Trim().Length > 80) throw new ArgumentException("Название товара или магазина: до 80 символов.");
        if (!Enum.IsDefined(Repeat) || Interval is < 1 or > 120) throw new ArgumentException("Интервал повторения: от 1 до 120.");
        if (FirstDueAt.Year < 1900 || FirstDueAt.Year > 9998 || FirstDueAt.Kind == DateTimeKind.Utc) throw new ArgumentException("Укажите дату и местное время платежа.");
        if (EndDate is { } end && end < DateOnly.FromDateTime(FirstDueAt)) throw new ArgumentException("Последний срок не может быть раньше первого платежа.");
        if (CompletedCount is < 0 or > 100000 || (Repeat == PaymentRepeat.None && CompletedCount > 1)) throw new ArgumentException("Некорректная история отметок оплаты.");
    }
}
