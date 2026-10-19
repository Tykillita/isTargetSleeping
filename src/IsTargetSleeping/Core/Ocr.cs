using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace IsTargetSleeping;

/// OCR del sistema, siempre local; solo el texto reconocido pasa al portapapeles.
public static class Ocr
{
    public static async Task<string> ReadImage(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No hay un idioma OCR instalado en Windows.");
        var result = await engine.RecognizeAsync(bitmap);
        return result.Text;
    }
}
