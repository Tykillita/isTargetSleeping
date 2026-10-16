using System.IO;
using System.Text;

namespace IsTargetSleeping;

public enum TextFileError { None, TooBig, NotText, Unreadable }
public enum TextSaveResult { Saved, Conflict, Failed }

/// Un archivo de texto abierto para la edición rápida. `Text` va siempre con «\n»; al guardar se devuelve
/// la codificación y los saltos de línea que tenía el archivo, para no ensuciar un diff.
public sealed record TextDoc(string Path, string Text, Encoding Encoding, string Eol, DateTime Stamp, long Length);

/// Leer y guardar archivos de texto pequeños sin estropearlos: solo UTF-8 y UTF-16 válidos (lo demás se rechaza en vez
/// de reinterpretarlo), con tope de tamaño, y el guardado es atómico (archivo temporal + reemplazo).
public static class TextFile
{
    public const int MaxBytes = 1_000_000;

    public static (TextDoc? Doc, TextFileError Error, string? Message) Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return (null, TextFileError.Unreadable, null);
            if (info.Length > MaxBytes) return (null, TextFileError.TooBig, null);
            var bytes = File.ReadAllBytes(path);
            Encoding enc;
            int skip;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { enc = new UTF8Encoding(true, true); skip = 3; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { enc = new UnicodeEncoding(false, true, true); skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { enc = new UnicodeEncoding(true, true, true); skip = 2; }
            else
            {
                if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8192)) >= 0) return (null, TextFileError.NotText, null);
                enc = new UTF8Encoding(false, true);
                skip = 0;
            }
            string raw;
            try { raw = enc.GetString(bytes, skip, bytes.Length - skip); }
            catch (DecoderFallbackException) { return (null, TextFileError.NotText, null); }
            int crlf = Count(raw, "\r\n"), lf = Count(raw, "\n") - crlf;
            var eol = crlf == 0 && lf == 0 ? Environment.NewLine : crlf >= lf ? "\r\n" : "\n";
            return (new TextDoc(path, raw.Replace("\r\n", "\n"), enc, eol, info.LastWriteTimeUtc, info.Length), TextFileError.None, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, TextFileError.Unreadable, e.Message);
        }
    }

    /// Guarda `text` (con «\n» o «\r\n», da igual). Si el archivo cambió en el disco desde que se abrió no lo pisa
    /// (Conflict) salvo que se pida `overwrite`. Devuelve el documento actualizado.
    public static (TextDoc Doc, TextSaveResult Result, string? Message) Save(TextDoc doc, string text, bool overwrite = false)
    {
        var tmp = doc.Path + ".its-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            var current = new FileInfo(doc.Path);
            if (!overwrite && current.Exists && (current.LastWriteTimeUtc != doc.Stamp || current.Length != doc.Length))
                return (doc, TextSaveResult.Conflict, null);
            var normalized = text.Replace("\r\n", "\n");
            var body = doc.Encoding.GetBytes(doc.Eol == "\n" ? normalized : normalized.Replace("\n", doc.Eol));
            var preamble = doc.Encoding.GetPreamble();
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(preamble);
                stream.Write(body);
            }
            if (current.Exists) File.Replace(tmp, doc.Path, null);
            else File.Move(tmp, doc.Path);
            var saved = new FileInfo(doc.Path);
            return (doc with { Text = normalized, Stamp = saved.LastWriteTimeUtc, Length = saved.Length }, TextSaveResult.Saved, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return (doc, TextSaveResult.Failed, e.Message);
        }
    }

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
