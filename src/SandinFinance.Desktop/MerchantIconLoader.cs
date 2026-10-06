using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public static class MerchantIconLoader
{
    public static string? SelectPng(DependencyObject owner)
    {
        var dialog = new OpenFileDialog { Title = "PNG-иконка магазина", Filter = "PNG-изображения (*.png)|*.png", CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(Window.GetWindow(owner)) == true ? LoadPng(dialog.FileName) : null;
    }

    public static string LoadPng(string path)
    {
        if (new FileInfo(path).Length > PngIcon.MaximumBytes) throw new ArgumentException("Выберите PNG-иконку размером до 1 МБ.");
        var bytes = File.ReadAllBytes(path);
        PngIcon.Validate(bytes);
        using var stream = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new ArgumentException("PNG-файл не содержит изображения.");
        return Convert.ToBase64String(bytes);
    }
}

public sealed class PngIconConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.Ordinal);
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } data) return null;
        if (Cache.TryGetValue(data, out var cached)) return cached;
        try
        {
            var bytes = System.Convert.FromBase64String(data);
            PngIcon.Validate(bytes);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (width >= height) image.DecodePixelWidth = (int)Math.Min(64, width);
            else image.DecodePixelHeight = (int)Math.Min(64, height);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            if (Cache.Count >= 128) Cache.Clear();
            Cache[data] = image;
            return image;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
