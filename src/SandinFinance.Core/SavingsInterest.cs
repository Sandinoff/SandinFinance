namespace SandinFinance.Core;

public sealed record SavingsAccrual(DateOnly Date, decimal Amount, DateTime? OccurredAt = null);

public static class SavingsInterest
{
    public static IReadOnlyList<SavingsAccrual> History(Account account, DateOnly asOf, DateTime? now = null)
    {
        asOf = EffectiveDate(account, asOf, now);
        account = EnsureHistory(account);
        var result = new List<SavingsAccrual>();
        AccrueCore(account with { CreditedInterest = account.InterestBaseCredited, PendingInterest = account.InterestBasePending, InterestAsOf = account.InterestHistoryStart, InitialInterestPosted = false }, asOf, result);
        return result;
    }

    public static void Correct(AccountStore store, Guid accountId, DateOnly day, decimal actualAmount)
    {
        Money.Validate(actualAmount, "Начисленные проценты");
        var accounts = store.Load();
        var account = accounts.SingleOrDefault(a => a.Id == accountId && a.IsSavings) ?? throw new ArgumentException("Накопительный счёт не найден.");
        account = EnsureHistory(account);
        if (!History(account, DateOnly.FromDateTime(DateTime.Today)).Any(a => a.Date == day)) throw new ArgumentException("Начисление за эту дату не найдено.");
        var timestamp = account.InterestCorrections.FirstOrDefault(c => c.Date == day)?.OccurredAt;
        var corrected = Recalculate(account with { InterestCorrections = [.. account.InterestCorrections.Where(c => c.Date != day), new(day, actualAmount, timestamp)] }, DateOnly.FromDateTime(DateTime.Today));
        store.Save(accounts.Select(a => a.Id == accountId ? corrected : a).ToList());
    }
    public static Account EnsureHistory(Account account) => !account.IsSavings || account.InterestHistoryStart is not null ? account : account with
    {
        InterestHistoryStart = account.InterestAsOf ?? account.OpeningDate,
        InterestBaseCredited = account.CreditedInterest,
        InterestBasePending = account.PendingInterest,
        InterestBasePayout = account.InterestPayout,
        InterestRates = [new(account.InterestAsOf ?? account.OpeningDate, account.AnnualInterestRate, account.InterestPayout)]
    };

    public static Account Recalculate(Account account, DateOnly asOf)
    {
        asOf = EffectiveDate(account, asOf);
        account = EnsureHistory(account);
        account.Validate();
        var basis = account with
        {
            CreditedInterest = account.InterestBaseCredited, PendingInterest = account.InterestBasePending,
            InterestAsOf = account.InterestHistoryStart, InitialInterestPosted = false
        };
        var result = AccrueCore(basis, asOf);
        result.Validate();
        return result;
    }

    public static Account Accrue(Account account, DateOnly asOf, DateTime? now = null)
    {
        asOf = EffectiveDate(account, asOf, now);
        account.Validate();
        if (!account.IsSavings) return account;
        var cursor = account.InterestAsOf ?? account.OpeningDate;
        if (asOf <= cursor) return account;
        account = EnsureHistory(account);
        return AccrueCore(account, asOf);
    }

    private static Account AccrueCore(Account account, DateOnly asOf, List<SavingsAccrual>? history = null)
    {
        var cursor = account.InterestAsOf ?? account.OpeningDate;
        var credited = account.CreditedInterest;
        var initialPosted = account.InitialInterestPosted;
        if (!initialPosted && account.DailyInterestPostingTime is not null && cursor == account.InterestHistoryStart)
        {
            var first = account.InterestCorrections.FirstOrDefault(c => c.Date == cursor);
            if (first is not null && (first.OccurredAt ?? cursor.ToDateTime(account.DailyInterestPostingTime.Value)) <= DateTime.Now)
            {
                credited += first.Amount;
                initialPosted = true;
                history?.Add(first);
            }
        }
        var pending = account.PendingInterest;
        var delta = account.Transactions.Where(t => DateOnly.FromDateTime(t.OccurredAt) < cursor).Sum(t => t.SignedAmount);
        var movements = account.Transactions.GroupBy(t => DateOnly.FromDateTime(t.OccurredAt)).ToDictionary(g => g.Key, g => g.OrderBy(t => t.OccurredAt).ToList());
        InterestPayout PayoutAt(DateOnly date) => account.InterestRates.Where(r => r.From <= date).OrderBy(r => r.From).Last().Payout;
        var previousPayout = cursor > account.InterestHistoryStart ? PayoutAt(cursor.AddDays(-1)) : account.InterestBasePayout;
        for (var day = cursor; day < asOf; day = day.AddDays(1))
        {
            var rate = account.InterestRates.Where(r => r.From <= day).OrderBy(r => r.From).Last();
            if ((rate.Payout != previousPayout || account.InterestSettlementDates.Contains(day)) && pending != 0)
            {
                credited += decimal.Round(pending, 2, MidpointRounding.AwayFromZero);
                pending = 0;
            }
            previousPayout = rate.Payout;
            ApplyDayMovements(day);
            var yearDays = DateTime.IsLeapYear(day.Year) ? 366 : 365;
            var interest = (account.OpeningBalance + credited + delta) * rate.AnnualRate / 100m / yearDays;
            if (rate.Payout == InterestPayout.Daily)
            {
                var postingDay = account.DailyInterestPostingTime is null ? day : day.AddDays(1);
                var correction = account.InterestCorrections.FirstOrDefault(c => c.Date == postingDay);
                var amount = correction?.Amount ?? decimal.Round(interest, 2, MidpointRounding.AwayFromZero);
                credited += amount;
                history?.Add(new(postingDay, amount, correction?.OccurredAt ?? (account.DailyInterestPostingTime is { } time ? postingDay.ToDateTime(time) : null)));
            }
            else
            {
                pending += interest;
                if (day.Day == DateTime.DaysInMonth(day.Year, day.Month))
                {
                    var amount = account.InterestCorrections.FirstOrDefault(c => c.Date == day)?.Amount ?? decimal.Round(pending, 2, MidpointRounding.AwayFromZero);
                    credited += amount;
                    history?.Add(new(day, amount, account.InterestCorrections.FirstOrDefault(c => c.Date == day)?.OccurredAt));
                    pending = 0;
                }
            }
            if (account.OpeningBalance + credited + delta > 1_000_000_000_000m)
                throw new ArgumentException($"Остаток счёта «{account.Name}» с процентами превышает поддерживаемую сумму.");
        }
        if ((PayoutAt(asOf) != previousPayout || account.InterestSettlementDates.Contains(asOf)) && pending != 0)
        {
            credited += decimal.Round(pending, 2, MidpointRounding.AwayFromZero);
            pending = 0;
        }
        ApplyDayMovements(asOf);
        return account with { CreditedInterest = credited, PendingInterest = pending, InterestAsOf = asOf, InitialInterestPosted = initialPosted };

        void ApplyDayMovements(DateOnly date)
        {
            if (!movements.TryGetValue(date, out var entries)) return;
            foreach (var entry in entries)
            {
                delta += entry.SignedAmount;
                if (account.OpeningBalance + credited + delta < 0) throw new ArgumentException("На дату операции на накопительном счёте недостаточно денег.");
            }
        }
    }

    private static DateOnly EffectiveDate(Account account, DateOnly asOf, DateTime? now = null)
    {
        var timestamp = now ?? DateTime.Now;
        return account.DailyInterestPostingTime is { } time && asOf == DateOnly.FromDateTime(timestamp) && TimeOnly.FromDateTime(timestamp) < time && asOf > account.InterestHistoryStart
            ? asOf.AddDays(-1) : asOf;
    }

    // Old days keep their old rate; settings start with the current unfinished day.
    public static Account ChangeSettings(Account account, decimal annualRate, InterestPayout payout, DateOnly effectiveDate)
    {
        var settled = EnsureHistory(Accrue(account, effectiveDate));
        if (settled.InterestAsOf > effectiveDate)
            throw new ArgumentException("Дата компьютера раньше последнего расчёта. Проверьте дату перед изменением ставки.");
        if (payout != settled.InterestPayout && settled.PendingInterest != 0)
            settled = settled with
            {
                CreditedInterest = settled.CreditedInterest + decimal.Round(settled.PendingInterest, 2, MidpointRounding.AwayFromZero),
                PendingInterest = 0,
                InterestSettlementDates = [.. settled.InterestSettlementDates.Where(d => d != effectiveDate), effectiveDate]
            };
        var changed = settled with
        {
            AnnualInterestRate = annualRate, InterestPayout = payout,
            InterestRates = [.. settled.InterestRates.Where(r => r.From != effectiveDate), new(effectiveDate, annualRate, payout)]
        };
        changed.Validate();
        return changed;
    }
}
