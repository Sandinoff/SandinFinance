using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandinFinance.Core;

public sealed class CategoryStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath { get; } = Path.Combine(Path.GetFullPath(directory), "categories.json");

    public List<Category> Load()
    {
        if (!File.Exists(FilePath)) return [];
        var document = JsonSerializer.Deserialize<CategoryDocument>(File.ReadAllText(FilePath), Options)
            ?? throw new InvalidDataException("Файл категорий пуст.");
        if (document.Version is not (1 or 2) || document.Categories is null)
            throw new InvalidDataException("Неподдерживаемый формат категорий.");
        Validate(document.Categories);
        return document.Categories;
    }

    public void Save(IReadOnlyCollection<Category> categories)
    {
        Validate(categories);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new CategoryDocument(2, categories.ToList()), Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(IEnumerable<Category> categories)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<(CategoryKind, string)>();
        var merchantIds = new HashSet<Guid>();
        foreach (var category in categories)
        {
            if (category is null) throw new InvalidDataException("Файл содержит пустую категорию.");
            category.Validate();
            if (!ids.Add(category.Id)) throw new ArgumentException("Повторяющийся идентификатор категории.");
            if (!names.Add((category.Kind, category.Name.Trim().ToUpperInvariant())))
                throw new ArgumentException("Категория с таким названием уже существует в этом разделе.");
            foreach (var merchant in category.Merchants)
                if (!merchantIds.Add(merchant.Id)) throw new ArgumentException("Повторяющийся идентификатор магазина.");
        }
    }

    private sealed record CategoryDocument(int Version, List<Category>? Categories);
}
