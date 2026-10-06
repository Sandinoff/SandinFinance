namespace SandinFinance.Core;

public static class CategoryArrangement
{
    public static List<Category> Move(IReadOnlyList<Category> categories, Guid movingId, Guid targetId, bool after)
    {
        var result = categories.ToList();
        var moving = result.SingleOrDefault(c => c.Id == movingId)
            ?? throw new ArgumentException("Перемещаемая категория не найдена.");
        var target = result.SingleOrDefault(c => c.Id == targetId)
            ?? throw new ArgumentException("Целевая категория не найдена.");
        if (moving.Kind != target.Kind) throw new ArgumentException("Категории можно перемещать только внутри своего раздела.");
        if (movingId == targetId) return result;
        // Reorder only this kind's slots; keep the other tab's order intact.
        var section = result.Where(c => c.Kind == moving.Kind).ToList();
        section.Remove(moving);
        section.Insert(section.FindIndex(c => c.Id == targetId) + (after ? 1 : 0), moving);
        var index = 0;
        for (var i = 0; i < result.Count; i++)
            if (result[i].Kind == moving.Kind) result[i] = section[index++];
        return result;
    }
}
