using System.IO;
using System.Windows;

namespace SandinFinance.Desktop;

public sealed record MerchantIconChoice(string Name, string PngIconBase64);

public static class MerchantIconCatalog
{
    private static readonly (string File, string Name, string[] Aliases)[] Definitions =
    [
        ("5ka", "Пятёрочка", ["Пятерочка", "5ка"]), ("fixprice", "Fix Price", ["FixPrice", "Фикс Прайс"]),
        ("kb", "Красное & Белое", ["Красное&Белое", "Красное и Белое", "КБ"]), ("lenta", "Лента", []),
        ("perekrestok", "Перекрёсток", ["Перекресток"]), ("samokat", "Самокат", []), ("spar", "SPAR", ["Спар"]),
        ("stolichki", "Столички", []), ("vkusvill", "ВкусВилл", ["Вкус Вилл"]), ("xorek", "Хорёк", ["Хорек"])
    ];
    private static readonly Lazy<(List<MerchantIconChoice> Choices, Dictionary<string, string> Lookup)> Catalog = new(() =>
    {
        var choices = new List<MerchantIconChoice>();
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in Definitions)
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/SandinFinance.Desktop;component/Assets/MerchantIcons/{item.File}.png"));
            if (resource is null) continue;
            using var input = resource.Stream;
            using var buffer = new MemoryStream(); input.CopyTo(buffer);
            var png = Convert.ToBase64String(buffer.ToArray());
            choices.Add(new(item.Name, png));
            foreach (var name in item.Aliases.Prepend(item.Name)) lookup[Normalize(name)] = png;
        }
        return (choices, lookup);
    });
    private static string Normalize(string name) => new(name.ToUpperInvariant().Replace('Ё', 'Е').Where(char.IsLetterOrDigit).ToArray());
    public static IReadOnlyList<MerchantIconChoice> Choices => Catalog.Value.Choices;
    public static string? ForName(string name) => Catalog.Value.Lookup.GetValueOrDefault(Normalize(name));
}
