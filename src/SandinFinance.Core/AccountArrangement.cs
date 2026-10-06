namespace SandinFinance.Core;

public static class AccountArrangement
{
    // The persisted array is the ordering source of truth, including hidden accounts.
    public static List<Account> Move(IReadOnlyList<Account> accounts, Guid movingId, Guid targetId, bool after)
    {
        var result = accounts.ToList();
        if (movingId == targetId) return result;
        var moving = result.SingleOrDefault(a => a.Id == movingId)
            ?? throw new ArgumentException("Перемещаемый счёт не найден.");
        if (!result.Any(a => a.Id == targetId)) throw new ArgumentException("Целевой счёт не найден.");
        result.Remove(moving);
        var targetIndex = result.FindIndex(a => a.Id == targetId);
        result.Insert(targetIndex + (after ? 1 : 0), moving);
        return result;
    }
}
