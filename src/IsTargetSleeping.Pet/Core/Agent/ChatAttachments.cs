using System.Text;

namespace IsTargetSleeping.Pet;

/// Los archivos que el celular manda con un mensaje de chat. Texto y código se pegan en el mensaje (recortados); las imágenes viajan al modelo
/// local (si el modelo no ve imágenes, las ignora); los PDF no se leen aquí: para eso está el modo Equipo. Lo leído se borra.
public static class ChatAttachments
{
    public const int MaxFiles = 3, MaxTextChars = 8000, MaxImageBytes = 10 * 1024 * 1024;
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static (string Text, IReadOnlyList<string> Images) Read(string uploads, IReadOnlyList<string> ids)
    {
        var text = new StringBuilder();
        var images = new List<string>();
        foreach (var id in ids.Take(MaxFiles))
        {
            if (id.Length != 32 || !id.All(Uri.IsHexDigit)) continue;
            string dir = Path.Combine(uploads, id);
            try
            {
                string? file = Directory.Exists(dir) ? Directory.GetFiles(dir).FirstOrDefault() : null;
                if (file is null) continue;
                string name = Path.GetFileName(file), ext = Path.GetExtension(file);
                var bytes = File.ReadAllBytes(file);
                if (Images.Contains(ext)) { if (bytes.Length <= MaxImageBytes && images.Count < 2) images.Add(Convert.ToBase64String(bytes)); text.Append($"\n\n[Imagen adjunta: {name}]"); }
                else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) text.Append($"\n\n[PDF adjunto: {name}. Aquí no puedo leer PDF; mándalo al modo Equipo]");
                else if (Array.IndexOf(bytes, (byte)0) >= 0) text.Append($"\n\n[Archivo adjunto: {name} (binario, no se puede leer)]");
                else
                {
                    string body = Encoding.UTF8.GetString(bytes);
                    if (body.Length > MaxTextChars) body = body[..MaxTextChars] + "\n[…recortado]";
                    text.Append($"\n\n[Archivo adjunto: {name}]\n{body}");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finally { try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
        return (text.ToString(), images);
    }
}
