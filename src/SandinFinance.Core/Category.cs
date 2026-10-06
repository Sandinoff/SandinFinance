using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SandinFinance.Core;

public enum CategoryKind { Expense, Income }
public sealed record CategoryIcon(string Key, string Glyph, string Label);
public sealed record CategoryColor(string Hex, string Label);

public static class CategoryAppearance
{
    public static IReadOnlyList<CategoryIcon> Icons { get; } = new CategoryIcon[]
    {
        new("cart", "🛒", "Покупки"), new("food", "🍽", "Еда"), new("home", "🏠", "Дом"),
        new("car", "🚗", "Транспорт"), new("health", "💊", "Здоровье"), new("clothes", "👕", "Одежда"),
        new("entertainment", "🎬", "Развлечения"), new("education", "📚", "Образование"),
        new("travel", "✈", "Путешествия"), new("work", "💼", "Работа"), new("money", "💰", "Деньги"),
        new("gift", "🎁", "Подарки"), new("growth", "📈", "Инвестиции"), new("tools", "🔧", "Услуги"),
        new("phone", "📱", "Связь"), new("star", "★", "Другое"),
        new("delivery", "🚚", "Доставка"), new("parcel", "📦", "Посылки"),
        new("takeout", "🥡", "Доставка еды"), new("coffee", "☕", "Кофе"),
        new("groceries", "🥦", "Продукты"), new("restaurant", "🍴", "Рестораны и кафе"),
        new("taxi", "🚕", "Такси"), new("bus", "🚌", "Общественный транспорт"),
        new("train", "🚆", "Поезда"), new("fuel", "⛽", "Бензин"),
        new("parking", "🅿", "Парковка"), new("bicycle", "🚲", "Велосипед"),
        new("utilities", "💡", "Коммунальные услуги"), new("internet", "🌐", "Интернет"),
        new("rent", "🔑", "Аренда"), new("electronics", "💻", "Техника"),
        new("pets", "🐾", "Питомцы"), new("children", "🧸", "Дети"),
        new("beauty", "💄", "Красота и уход"), new("sport", "🏋", "Спорт"),
        new("books", "📖", "Книги"), new("subscription", "🔄", "Подписки"),
        new("insurance", "🛡", "Страхование"), new("taxes", "🧾", "Налоги"),
        new("charity", "🤝", "Благотворительность"), new("salary", "💵", "Зарплата"),
        new("freelance", "🖥", "Подработка"), new("refund", "↩", "Возврат денег"),
        new("bank", "🏦", "Банк"), new("interest", "🪙", "Проценты и кешбэк"),
        new("cleaning", "🧹", "Уборка"), new("hotel", "🏨", "Отели"),
        new("tobacco", "🚬", "Табак")
    };
    public static IReadOnlyList<CategoryColor> Colors { get; } = new CategoryColor[]
    {
        new("#12796B", "Зелёный"), new("#2478A8", "Синий"), new("#6B55AE", "Фиолетовый"),
        new("#BD507C", "Розовый"), new("#BC4545", "Красный"), new("#B76A22", "Оранжевый"),
        new("#9A8018", "Золотой"), new("#51842D", "Салатовый"), new("#187F8E", "Бирюзовый"),
        new("#555EAA", "Индиго"), new("#826244", "Коричневый"), new("#607780", "Серый")
    };
}

public sealed record Merchant
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public string? PngIconBase64 { get; init; }
    [JsonIgnore] public bool HasPngIcon => !string.IsNullOrEmpty(PngIconBase64);
    [JsonIgnore] public string IconLabel => string.IsNullOrEmpty(Name) ? "•" : System.Globalization.StringInfo.GetNextTextElement(Name.Trim()).ToUpperInvariant();

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80)
            throw new ArgumentException("Название магазина или источника: от 1 до 80 символов.");
        if (PngIconBase64 is not null)
        {
            if (PngIconBase64.Length > 1_400_000) throw new ArgumentException("PNG-иконка слишком большая.");
            try { PngIcon.Validate(Convert.FromBase64String(PngIconBase64)); }
            catch (FormatException) { throw new ArgumentException("Повреждены данные PNG-иконки магазина."); }
        }
    }
}

public sealed record Category
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public CategoryKind Kind { get; init; }
    public string ColorHex { get; init; } = "#12796B";
    public string IconKey { get; init; } = "cart";
    public List<Merchant> Merchants { get; init; } = [];

    [JsonIgnore] public string IconGlyph => CategoryAppearance.Icons.Single(icon => icon.Key == IconKey).Glyph;
    [JsonIgnore] public bool HasMerchants => Merchants.Count > 0;
    [JsonIgnore] public bool IsTransport => Kind == CategoryKind.Expense && (IconKey == "car" || Name.Trim().Equals("Транспорт", StringComparison.OrdinalIgnoreCase));
    [JsonIgnore] public string MerchantGroupLabel => Kind == CategoryKind.Income ? "Источники" : IsTransport ? "Товары и услуги" : "Магазины";
    [JsonIgnore] public string MerchantsHeading => $"{MerchantGroupLabel} · {Merchants.Count}";
    [JsonIgnore] public string MerchantPrompt => Kind == CategoryKind.Income ? "Название источника дохода" : IsTransport ? "Название товара, услуги или места" : "Название магазина";

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80)
            throw new ArgumentException("Название категории: от 1 до 80 символов.");
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("Неизвестный тип категории.");
        if (ColorHex is null || !Regex.IsMatch(ColorHex, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Выберите цвет категории.");
        if (!CategoryAppearance.Icons.Any(icon => icon.Key == IconKey))
            throw new ArgumentException("Выберите иконку категории.");
        if (Merchants is null) throw new ArgumentException("Не удалось прочитать магазины категории.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        foreach (var merchant in Merchants)
        {
            if (merchant is null) throw new ArgumentException("Категория содержит пустой магазин.");
            merchant.Validate();
            if (!names.Add(merchant.Name.Trim())) throw new ArgumentException("Такое название уже есть в этой категории.");
            if (!ids.Add(merchant.Id)) throw new ArgumentException("Повторяющийся идентификатор магазина.");
        }
    }
}
