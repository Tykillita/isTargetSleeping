using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsTargetSleeping.Pet;

/// Protocolo Mascota v1 (ver protocol/pet-v1.md del proyecto TargetOS): validacion estricta de lo que llega y
/// constructores de lo que sale. Nada de aqui toca la red: se prueba con bytes y cadenas.
public static class PetProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 4096;
    public const int MaxTextChars = 400;
    public const int MaxNameChars = 12;

    public static readonly string[] Expressions = ["open", "left", "right", "squint", "smile", "sleep", "happy", "listen", "think", "speak"];
    public static readonly string[] ActionKinds = ["tap", "play", "dizzy", "rest", "wake", "rename"];
    public static readonly string[] Phases = ["idle", "listening", "thinking", "speaking"];

    private static readonly Regex IdPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    public static bool IsExpression(string? s) => s is not null && Array.IndexOf(Expressions, s) >= 0;
    public static bool IsId(string? s) => s is not null && IdPattern.IsMatch(s);

    /// Id de 128 bits aleatorio (32 hex).
    public static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// Cuenta caracteres Unicode (puntos de codigo), no unidades UTF-16: un emoji cuenta 1.
    public static int CountChars(string s)
    {
        int n = 0;
        foreach (var _ in s.EnumerateRunes()) n++;
        return n;
    }

    /// Nombre valido: 1-12 caracteres, sin controles ni saltos de linea, sin espacios en los extremos.
    public static bool IsValidName(string? s) =>
        s is not null && s.Length > 0 && s == s.Trim() && CountChars(s) <= MaxNameChars && !s.Any(char.IsControl);

    // ----------------------------------------------------------------- entrada

    public static ParseResult Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxMessageBytes) return Fail("too_large", "maximo 4096 bytes");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 }); }
        catch (JsonException) { return Fail("bad_json", "JSON invalido"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("bad_json", "se esperaba un objeto");
            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int ver))
                return Fail("bad_json", "falta v");
            if (ver != Version) return Fail("bad_version", "version no soportada");
            string? id = Str(root, "id"), type = Str(root, "type");
            if (!IsId(id)) return Fail("bad_json", "id invalido");
            switch (type)
            {
                case "hello":
                    return root.TryGetProperty("since_rev", out var sr) && sr.ValueKind == JsonValueKind.Number && sr.TryGetInt64(out long rev) && rev >= 0
                        ? Ok(new HelloMsg(id!, rev, root.TryGetProperty("since_reminder", out var sr2) && sr2.ValueKind == JsonValueKind.Number && sr2.TryGetInt64(out long rem) && rem >= 0 ? rem : -1)) : Fail("bad_json", "since_rev invalido");
                case "ping":
                    return Ok(new PingMsg(id!));
                case "action":
                {
                    string? kind = Str(root, "kind");
                    if (kind is null || Array.IndexOf(ActionKinds, kind) < 0) return Fail("bad_json", "kind invalido");
                    if (!root.TryGetProperty("age_s", out var a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out int age) || age < 0 || age > 604800)
                        return Fail("bad_json", "age_s invalido");
                    string? name = Str(root, "name");
                    if (kind == "rename" && !IsValidName(name)) return Fail("bad_json", "nombre invalido");
                    return Ok(new ActionMsg(id!, kind, age, kind == "rename" ? name : null));
                }
                case "chat":
                {
                    string? text = Str(root, "text");
                    if (string.IsNullOrEmpty(text)) return Fail("bad_json", "falta text");
                    if (CountChars(text) > MaxTextChars) return Fail("text_too_long", "maximo 400 caracteres");
                    List<string>? atts = null;
                    if (root.TryGetProperty("attachments", out var av) && av.ValueKind == JsonValueKind.Array)
                    {
                        atts = av.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Take(4).ToList();
                        if (atts.Any(x => x.Length != 32 || !x.All(Uri.IsHexDigit))) return Fail("bad_json", "adjuntos invalidos");
                    }
                    return Ok(new ChatMsg(id!, text, atts is { Count: > 0 } ? atts : null));
                }
                case "decision":
                {
                    string? refId = Str(root, "ref"), verdict = Str(root, "verdict");
                    if (!IsId(refId)) return Fail("bad_json", "ref invalido");
                    if (verdict is not ("allow" or "deny")) return Fail("bad_json", "verdict invalido");
                    string? note = Str(root, "message");
                    if (note is not null && CountChars(note) > 300) return Fail("text_too_long", "maximo 300 caracteres");
                    Dictionary<string, string>? answers = null;
                    if (root.TryGetProperty("answers", out var an) && an.ValueKind == JsonValueKind.Object)
                    {
                        answers = [];
                        foreach (var kv in an.EnumerateObject())
                        {
                            if (kv.Value.ValueKind != JsonValueKind.String || answers.Count >= 4 || kv.Name.Length > 200 || (kv.Value.GetString()?.Length ?? 0) > 200) return Fail("bad_json", "answers invalido");
                            answers[kv.Name] = kv.Value.GetString()!;
                        }
                    }
                    return Ok(new DecisionMsg(id!, refId!, verdict, note, answers));
                }
                case "cancel":
                {
                    string? chat = Str(root, "chat_id");
                    return IsId(chat) ? Ok(new CancelMsg(id!, chat!)) : Fail("bad_json", "chat_id invalido");
                }
                default:
                    return Fail("bad_json", "type desconocido");
            }
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static ParseResult Ok(PetMessage m) => new(m, null);
    private static ParseResult Fail(string code, string message) => new(null, new ProtocolError(code, message));

    // ------------------------------------------------------------------ salida

    public static byte[] State(PetSnapshot s) => Build(w =>
    {
        w.WriteString("type", "state");
        w.WriteString("id", NewId());
        w.WriteNumber("rev", s.Rev);
        w.WriteStartObject("pet");
        RawString(w, "name", s.Name);
        w.WriteNumber("energy", s.Energy);
        w.WriteNumber("affection", s.Affection);
        w.WriteString("pose", s.Pose);
        w.WriteBoolean("asleep", s.Asleep);
        w.WriteEndObject();
    });

    /// Un `say` solo se emite con texto y expresion validos; si no, lanza (es un error de programacion de quien llama).
    public static byte[] Say(string chatId, string text, string expression, bool final)
    {
        if (!IsId(chatId)) throw new ArgumentException("chat_id invalido", nameof(chatId));
        if (string.IsNullOrEmpty(text) || CountChars(text) > MaxTextChars) throw new ArgumentException("texto fuera de limite", nameof(text));
        if (!IsExpression(expression)) throw new ArgumentException("expresion fuera de la lista cerrada", nameof(expression));
        return Build(w =>
        {
            w.WriteString("type", "say");
            w.WriteString("id", NewId());
            w.WriteString("chat_id", chatId);
            RawString(w, "text", text);
            w.WriteString("expression", expression);
            w.WriteBoolean("final", final);
        });
    }

    public static byte[] Status(string? chatId, string phase) => Build(w =>
    {
        w.WriteString("type", "status");
        w.WriteString("id", NewId());
        if (chatId is not null) w.WriteString("chat_id", chatId);
        w.WriteString("phase", phase);
    });

    public static byte[] Ack(string refId, long rev) => Build(w =>
    {
        w.WriteString("type", "ack");
        w.WriteString("id", NewId());
        w.WriteString("ref", refId);
        w.WriteNumber("rev", rev);
    });

    public static byte[] Error(string code, string message) => Build(w =>
    {
        w.WriteString("type", "error");
        w.WriteString("id", NewId());
        w.WriteString("code", code);
        RawString(w, "message", message.Length > 200 ? message[..200] : message);
    });

    /// Estado de los agentes de IA de la PC (idle|working|attention|alert|done), para que la mascota reaccione.
    public static byte[] Agents(string state) => Build(w =>
    {
        w.WriteString("type", "agents");
        w.WriteString("id", NewId());
        w.WriteString("state", Array.IndexOf(AgentBridge.States, state) >= 0 ? state : "idle");
    });

    /// Una accion espera confirmacion (celular, placa o PC: gana la primera respuesta).
    public static byte[] Confirm(PendingAction p) => Build(w =>
    {
        w.WriteString("type", "confirm");
        w.WriteString("id", p.Id);
        w.WriteString("tool", p.Tool);
        RawString(w, "title", p.Title.Length > 60 ? p.Title[..60] : p.Title);
        RawString(w, "summary", p.Summary.Length > 240 ? p.Summary[..240] : p.Summary);
        w.WriteString("risk", p.Risk.ToString().ToLowerInvariant());
        w.WriteNumber("expires", p.Expires.ToUnixTimeSeconds());
    });

    /// La confirmacion `id` ya no esta pendiente: `by` = canal que respondio ("phone", "board", "pc") o "expired"/"cancelled".
    public static byte[] Resolved(string id, string verdict, string by) => Build(w =>
    {
        w.WriteString("type", "resolved");
        w.WriteString("id", NewId());
        w.WriteString("ref", id);
        w.WriteString("verdict", verdict is "allow" ? "allow" : "deny");
        RawString(w, "by", by.Length > 20 ? by[..20] : by);
    });

    /// Un agente espera respuesta en la PC (permiso, pregunta o plan). `id` = identificador de la peticion.
    public static byte[] Approval(ApprovalItem a, bool canAllow = true)
    {
        var full = ApprovalJson(a, canAllow, withDiff: true);
        return full.Length <= 3900 ? full : ApprovalJson(a, canAllow, withDiff: false);   // la placa descarta lo que pasa de 4 KB: sin el diff sigue cabiendo
    }

    private static byte[] ApprovalJson(ApprovalItem a, bool canAllow, bool withDiff) => Build(w =>
    {
        w.WriteString("type", "approval");
        w.WriteString("id", a.Id);
        RawString(w, "agent", a.Agent);
        w.WriteString("kind", a.Kind);
        w.WriteBoolean("can_allow", canAllow);   // lo delicado solo se deniega desde el celular/placa (salvo permiso explicito)
        RawString(w, "tool", a.Tool);
        if (a.Cwd is not null) RawString(w, "cwd", a.Cwd);
        if (a.Detail is not null) RawString(w, "detail", a.Detail);
        if (a.Risk is not null) RawString(w, "risk", a.Risk);
        w.WriteNumber("expires", a.Expires);
        if (a.Questions.Count > 0)
        {
            w.WriteStartArray("questions");
            foreach (var q in a.Questions)
            {
                w.WriteStartObject();
                RawString(w, "question", q.Question);
                RawString(w, "header", q.Header);
                w.WriteBoolean("multi", q.Multi);
                w.WriteStartArray("options");
                foreach (var o in q.Options) w.WriteStringValue(o);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (a.Plan is not null) RawString(w, "plan", a.Plan);
        if (withDiff && a.Diff is not null) RawString(w, "diff", a.Diff);
    });

    /// La peticion `id` ya no espera: `by` = "phone", "board", "pc" o "expired".
    public static byte[] ApprovalDone(string id, string verdict, string by) => Build(w =>
    {
        w.WriteString("type", "approval_done");
        w.WriteString("id", NewId());
        w.WriteString("ref", id);
        w.WriteString("verdict", verdict == "allow" ? "allow" : "deny");
        RawString(w, "by", by.Length > 20 ? by[..20] : by);
    });

    /// Un recordatorio venció (`late` = venció con la PC apagada o sin conexión).
    public static byte[] Reminder(Reminder r, bool late, long seq = 0) => Build(w =>
    {
        w.WriteString("type", "reminder");
        w.WriteString("id", NewId());
        w.WriteNumber("ref", r.Id);
        w.WriteNumber("seq", seq);
        RawString(w, "text", r.Text.Length > 140 ? r.Text[..140] : r.Text);
        w.WriteNumber("due", r.Due);
        w.WriteBoolean("late", late);
    });

    /// Aviso proactivo (agentes, uso de IA…). `level`: info | attention.
    public static byte[] Notice(string text, string level = "info") => Build(w =>
    {
        w.WriteString("type", "notice");
        w.WriteString("id", NewId());
        RawString(w, "text", text.Length > 160 ? text[..160] : text);
        w.WriteString("level", level == "attention" ? "attention" : "info");
    });

    public static byte[] Pong() => Build(w => { w.WriteString("type", "pong"); w.WriteString("id", NewId()); });

    public static byte[] ChatAccepted(string chatId) => Build(w =>
    {
        w.WriteString("type", "chat_accepted");
        w.WriteString("id", NewId());
        w.WriteString("chat_id", chatId);
    });

    public static byte[] ChatCancelled() => Build(w => { w.WriteString("type", "chat_cancelled"); w.WriteString("id", NewId()); });

    /// Escribe una cadena JSON con UTF-8 real: solo se escapan comillas, barra invertida y controles. El escapado
    /// estandar de .NET convierte cada emoji en `\uD83D\uDE00` (12 bytes) y rompe el limite de 4 KB con 400 emojis.
    private static void RawString(Utf8JsonWriter w, string name, string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (char c in value)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        sb.Append('"');
        w.WritePropertyName(name);
        w.WriteRawValue(sb.ToString(), skipInputValidation: false);
    }

    private static byte[] Build(Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            body(w);
            w.WriteEndObject();
        }
        var bytes = ms.ToArray();
        if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException("mensaje de salida > 4 KB");
        return bytes;
    }

    public static int HttpStatus(string code) => code switch
    {
        "unauthorized" => 401,
        "not_found" => 404,
        "busy" => 409,
        "model_missing" or "model_unavailable" => 503,
        "internal" => 500,
        _ => 400,
    };
}

public abstract record PetMessage(string Id);
public sealed record HelloMsg(string Id, long SinceRev, long SinceReminder = -1) : PetMessage(Id);
public sealed record PingMsg(string Id) : PetMessage(Id);
public sealed record ActionMsg(string Id, string Kind, int AgeS, string? Name) : PetMessage(Id);
public sealed record ChatMsg(string Id, string Text, IReadOnlyList<string>? Attachments = null) : PetMessage(Id);
public sealed record CancelMsg(string Id, string ChatId) : PetMessage(Id);
/// Respuesta a una confirmacion pendiente (`ref` = id del `confirm`). Solo allow o deny.
public sealed record DecisionMsg(string Id, string Ref, string Verdict, string? Note = null, IReadOnlyDictionary<string, string>? Answers = null) : PetMessage(Id);

public sealed record ProtocolError(string Code, string Message);
public readonly record struct ParseResult(PetMessage? Message, ProtocolError? Error);

/// Lo que ven la placa y la web de la mascota (el estado autoritativo vive en PetService).
public sealed record PetSnapshot(long Rev, string Name, int Energy, int Affection, string Pose, bool Asleep);
