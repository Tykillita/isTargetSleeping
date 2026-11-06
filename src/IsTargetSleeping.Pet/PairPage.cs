using System.Globalization;
using System.Net;
using System.Text;

namespace IsTargetSleeping.Pet;

/// La página que abre `pair`: un QR con la marca de la app (módulos líquidos, ojos propios y la cara de Nimbo al centro), los pasos para conectar el
/// celular y un aviso en vivo cuando el teléfono se conecta. Todo va dentro del archivo, sin cargar nada de internet.
public static class PairPage
{
    const string Orange = "#f0a23c", EyeAccent = "#a85a05";

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// La cara de Nimbo (píldora blanca con dos ojos) dentro de la caja (x, y, ancho).
    static string Face(double x, double y, double w)
    {
        double h = w * 0.56;
        return $"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(w)}\" height=\"{F(h)}\" rx=\"{F(h / 2)}\" fill=\"#f7f8fb\"/>" +
               $"<g class=\"look\"><ellipse class=\"eye\" cx=\"{F(x + w * 0.33)}\" cy=\"{F(y + h / 2)}\" rx=\"{F(w * 0.06)}\" ry=\"{F(w * 0.085)}\" fill=\"#0c0c14\"/>" +
               $"<ellipse class=\"eye\" cx=\"{F(x + w * 0.67)}\" cy=\"{F(y + h / 2)}\" rx=\"{F(w * 0.06)}\" ry=\"{F(w * 0.085)}\" fill=\"#0c0c14\"/></g>";
    }

    /// QR con corrección alta (30 %) para poder tapar el centro con la cara de Nimbo sin perder lectura.
    public static string QrSvg(string link)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var qr = gen.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.H);
        var m = qr.ModuleMatrix;
        int q = 4, n = m.Count - 2 * q;   // QRCoder añade 4 módulos de margen a cada lado
        bool Dark(int x, int y) => x >= 0 && y >= 0 && x < n && y < n && m[y + q][x + q];
        bool InEye(int x, int y) => (x < 8 && y < 8) || (x >= n - 8 && y < 8) || (x < 8 && y >= n - 8);
        int clear = (int)Math.Round(n * 0.27) | 1;
        int c0 = (n - clear) / 2, c1 = c0 + clear;
        bool InLogo(int x, int y) => x >= c0 - 1 && x < c1 + 1 && y >= c0 - 1 && y < c1 + 1;
        bool On(int x, int y) => Dark(x, y) && !InEye(x, y) && !InLogo(x, y);

        double pad = 2.4, size = n + pad * 2;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(size)} {F(size)}\" role=\"img\" aria-label=\"Código QR de emparejamiento\">");
        sb.Append("<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#0d0d10\"/><stop offset=\"1\" stop-color=\"#0d0d10\"/></linearGradient>");
        sb.Append("<linearGradient id=\"o\"><stop offset=\"0\" stop-color=\"#f0a23c\"/><stop offset=\"1\" stop-color=\"#f0a23c\"/></linearGradient></defs>");
        sb.Append($"<g transform=\"translate({F(pad)} {F(pad)})\">");
        // módulos líquidos: cada punto redondea solo las esquinas que no tocan a un vecino, y así los vecinos se funden en formas continuas
        sb.Append("<path fill=\"url(#g)\" d=\"");
        const double r = 0.5;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                if (!On(x, y)) continue;
                bool u = On(x, y - 1), d = On(x, y + 1), l = On(x - 1, y), rt = On(x + 1, y);
                double tl = !u && !l ? r : 0, tr = !u && !rt ? r : 0, br = !d && !rt ? r : 0, bl = !d && !l ? r : 0;
                sb.Append($"M{F(x + tl)} {F(y)}H{F(x + 1 - tr)}");
                if (tr > 0) sb.Append($"A{F(tr)} {F(tr)} 0 0 1 {F(x + 1)} {F(y + tr)}");
                sb.Append($"V{F(y + 1 - br)}");
                if (br > 0) sb.Append($"A{F(br)} {F(br)} 0 0 1 {F(x + 1 - br)} {F(y + 1)}");
                sb.Append($"H{F(x + bl)}");
                if (bl > 0) sb.Append($"A{F(bl)} {F(bl)} 0 0 1 {F(x)} {F(y + 1 - bl)}");
                sb.Append($"V{F(y + tl)}");
                if (tl > 0) sb.Append($"A{F(tl)} {F(tl)} 0 0 1 {F(x + tl)} {F(y)}");
                sb.Append('Z');
            }
        sb.Append("\"/>");
        // ojos propios: aro redondeado y, dentro, una píldora naranja (como los ojos de Nimbo)
        foreach (var (ex, ey) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            sb.Append($"<rect x=\"{ex + 0.5}\" y=\"{ey + 0.5}\" width=\"6\" height=\"6\" rx=\"2.3\" fill=\"none\" stroke=\"url(#g)\" stroke-width=\"1\"/>");
            sb.Append($"<rect x=\"{ex + 2}\" y=\"{ey + 2}\" width=\"3\" height=\"3\" rx=\"1.5\" fill=\"{EyeAccent}\"/>");
        }
        // la cara de Nimbo al centro, sobre una ficha naranja
        double cs = clear + 0.6, cx = c0 - 0.3, cy = c0 - 0.3;
        sb.Append($"<rect x=\"{F(cx)}\" y=\"{F(cy)}\" width=\"{F(cs)}\" height=\"{F(cs)}\" rx=\"{F(cs * 0.3)}\" fill=\"url(#o)\"/>");
        sb.Append(Face(cx + cs * 0.12, cy + cs * 0.5 - cs * 0.76 * 0.56 / 2, cs * 0.76));
        sb.Append("</g></svg>");
        return sb.ToString();
    }

    const string Css = """
:root{--bg:#000;--card:#161618;--tx:#f5f5f7;--mut:#8e8e96;--ac:#f0a23c;--line:#262628}
*{box-sizing:border-box}html,body{margin:0;min-height:100%}
body{font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif;color:var(--tx);background:var(--bg);display:grid;place-items:center;padding:34px 20px;overflow-x:hidden}
.aurora{position:fixed;inset:0;z-index:-1;overflow:hidden}
.aurora i{position:absolute;border-radius:50%;filter:blur(110px);background:#f0a23c;opacity:.10;animation:drift 18s ease-in-out infinite alternate}
.aurora i:nth-child(1){width:520px;height:520px;left:-160px;top:-220px}
.aurora i:nth-child(2){width:420px;height:420px;right:-160px;bottom:-200px;animation-delay:-8s;opacity:.07}
.aurora i:nth-child(3){display:none}
@keyframes drift{to{transform:translate(70px,50px) scale(1.15)}}
main{width:100%;max-width:960px}
header{text-align:center;margin-bottom:34px}
.mascot{width:96px;margin:0 auto 14px;display:block;animation:bob 4s ease-in-out infinite}
@keyframes bob{50%{transform:translateY(-5px) rotate(-1.5deg)}}
.eye,.look{transform-box:fill-box;transform-origin:center}
.eye{animation:blink 5s infinite}
.look{animation:glance 9s ease-in-out infinite}
@keyframes blink{0%,92%,100%{transform:scaleY(1)}95%{transform:scaleY(.1)}}
.look{--dx:4}.qr svg .look{--dx:.5}
@keyframes glance{0%,12%{transform:translate(0,0)}20%,38%{transform:translate(calc(var(--dx)*1px),calc(var(--dx)*-.25px))}46%,62%{transform:translate(calc(var(--dx)*-1px),calc(var(--dx)*.25px))}72%,100%{transform:translate(0,0)}}
.hap{display:none}
.ok .mascot .eye{display:none}.ok .mascot .hap{display:inline;animation:hop .6s ease-out}.ok .mascot{animation:cheer .7s ease-out 3}
@keyframes hop{from{transform:scale(.4)}}@keyframes cheer{40%{transform:translateY(-14px) scale(1.06)}}
.dead .mascot .eye{animation:none;transform:scaleY(.12)}.dead .mascot .look{animation:none}.dead .mascot{animation:sleep 4s ease-in-out infinite}
@keyframes sleep{50%{transform:translateY(2px) scale(.98)}}
.qr svg .eye{animation-delay:-2s}
h1{font-size:clamp(30px,5vw,44px);margin:0;letter-spacing:-.025em;line-height:1.1;color:#fff}
.sub{color:var(--mut);margin:8px 0 0;font-size:17px}
.grid{display:grid;grid-template-columns:minmax(280px,400px) 1fr;gap:48px;align-items:center}
@media (max-width:800px){.grid{grid-template-columns:1fr;gap:30px}}
.stage{position:relative;padding:14px;border-radius:38px;animation:float 6s ease-in-out infinite}
@keyframes float{50%{transform:translateY(-8px)}}
.stage::before{content:"";position:absolute;inset:-2px;border-radius:40px;background:conic-gradient(from var(--a,0deg),transparent 0 62%,#f0a23c 100%);animation:spin 6s linear infinite;z-index:0}
@property --a{syntax:"<angle>";inherits:false;initial-value:0deg}
@keyframes spin{to{--a:360deg}}
.stage::after{content:"";position:absolute;inset:-2px;border-radius:40px;background:#26262a;z-index:-1}
.qr{position:relative;z-index:1;background:#fff;border-radius:28px;padding:10px;overflow:hidden}
.qr svg{display:block;width:100%;height:auto}
.scan{position:absolute;left:0;right:0;height:70px;top:-70px;background:linear-gradient(to bottom,transparent,#f0a23c26);border-bottom:2px solid #f0a23c;animation:sweep 3.2s ease-in-out infinite;pointer-events:none}
@keyframes sweep{0%{top:-70px}100%{top:100%}}
.qr.dead svg{filter:blur(8px) grayscale(1);opacity:.45}.qr.dead .scan{display:none}
.qr.ok svg{filter:blur(10px);opacity:.25}.qr.ok .scan{display:none}
.over{position:absolute;inset:0;display:none;place-items:center;text-align:center;padding:24px;color:#14141a;font-weight:700;font-size:19px}
.qr.dead .over.d,.qr.ok .over.k{display:grid}
.check{width:84px;height:84px;margin:0 auto 10px;display:block}
.check circle{fill:#f0a23c;transform-origin:center;animation:pop .5s cubic-bezier(.2,1.4,.4,1)}
.check path{fill:none;stroke:#111;stroke-width:6;stroke-linecap:round;stroke-linejoin:round;stroke-dasharray:60;stroke-dashoffset:60;animation:draw .5s .3s forwards}
@keyframes pop{from{transform:scale(0)}}@keyframes draw{to{stroke-dashoffset:0}}
.corner{position:absolute;width:26px;height:26px;border:3px solid #fff;z-index:2;opacity:.9}
.c1{left:4px;top:4px;border-right:0;border-bottom:0;border-radius:18px 0 0 0}.c2{right:4px;top:4px;border-left:0;border-bottom:0;border-radius:0 18px 0 0}
.c3{left:4px;bottom:4px;border-right:0;border-top:0;border-radius:0 0 0 18px}.c4{right:4px;bottom:4px;border-left:0;border-top:0;border-radius:0 0 18px 0}
.steps{list-style:none;margin:0 0 24px;padding:0;position:relative}
.steps::before{content:"";position:absolute;left:21px;top:22px;bottom:22px;width:2px;background:var(--line)}
.steps li{display:flex;gap:16px;padding:12px 0;position:relative}
.ic{flex:none;width:44px;height:44px;border-radius:14px;display:grid;place-items:center;background:var(--card);border:1px solid var(--line);color:var(--ac);z-index:1}
.ic svg{width:22px;height:22px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}
.steps b{display:block;font-size:17px}.steps span{color:var(--mut);font-size:14.5px}
.code{background:var(--card);border:1px solid var(--line);border-radius:22px;padding:22px 26px 20px}
.ch{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:18px}
.ch small{color:var(--mut);font-size:12px;text-transform:uppercase;letter-spacing:.12em}
.chars{display:flex;gap:18px;align-items:center;min-width:0}
.grp{display:flex;gap:7px}
.chars i{font:700 28px ui-monospace,Menlo,Consolas,monospace;width:44px;height:58px;border-radius:13px;background:#0c0c0e;border:1px solid var(--line);display:grid;place-items:center;font-style:normal}
.chars em{width:10px;height:2px;border-radius:2px;background:var(--mut);opacity:.6;font-size:0}
.copy{flex:none;width:36px;height:36px;margin:-6px -8px -6px 0;border:0;border-radius:10px;display:grid;place-items:center;background:transparent;color:var(--ac);cursor:pointer;transition:background .15s,transform .15s}
.copy svg{width:21px;height:21px;fill:none;stroke:currentColor;stroke-width:1.9;stroke-linecap:round;stroke-linejoin:round;grid-area:1/1}
.copy .i-ok{display:none}.copy.done .i-ok{display:block}.copy.done .i-clip{display:none}
.copy:hover{background:#ffffff12}.copy:active{transform:scale(.92)}
.url{display:flex;align-items:center;gap:8px;color:var(--mut);font-size:13px;margin-top:20px;padding-top:16px;border-top:1px solid var(--line);word-break:break-all}
.url svg{width:15px;height:15px;flex:none;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;opacity:.8}
.status{display:flex;align-items:center;gap:14px;margin-top:20px}
.ring{position:relative;width:54px;height:54px;flex:none}
.ring svg{width:54px;height:54px;transform:rotate(-90deg)}
.ring circle{fill:none;stroke-width:5}.ring .bg{stroke:var(--line)}.ring .fg{stroke:var(--ac);stroke-linecap:round;transition:stroke-dashoffset 1s linear}
.ring b{position:absolute;inset:0;display:grid;place-items:center;font:700 12px ui-monospace,monospace}
.pill{display:inline-flex;align-items:center;gap:8px;padding:8px 14px;border-radius:99px;background:var(--card);border:1px solid var(--line);font-size:14px;color:var(--mut)}
.pill::before{content:"";width:9px;height:9px;border-radius:50%;background:var(--ac);animation:pulse 1.4s infinite}
@keyframes pulse{50%{opacity:.25;transform:scale(.7)}}
.pill.ok{color:#fff;border-color:#3a3a3e}.pill.ok::before{background:#f0a23c;animation:none}
.pill.bad{color:var(--mut)}.pill.bad::before{background:#6a6a72;animation:none}
.foot{margin-top:30px;text-align:center;color:var(--mut);font-size:13px}
.err{background:var(--card);border:1px solid var(--line);border-radius:20px;padding:20px 22px;color:#fff;max-width:560px;margin:0 auto}
@media (prefers-reduced-motion:reduce){*{animation:none!important}}
""";

    static string MascotHeader() =>
        "<svg class=\"mascot\" viewBox=\"0 0 100 60\" aria-hidden=\"true\"><rect x=\"2\" y=\"2\" width=\"96\" height=\"56\" rx=\"28\" fill=\"#f7f8fb\"/>" +
        "<g class=\"look\"><ellipse class=\"eye\" cx=\"33\" cy=\"30\" rx=\"6\" ry=\"8.5\" fill=\"#0c0c14\"/><ellipse class=\"eye\" cx=\"67\" cy=\"30\" rx=\"6\" ry=\"8.5\" fill=\"#0c0c14\"/>" +
        "<path class=\"hap\" d=\"M26 34q7-12 14 0M60 34q7-12 14 0\" fill=\"none\" stroke=\"#0c0c14\" stroke-width=\"4.5\" stroke-linecap=\"round\"/></g></svg>";

    static string Page(string title, string body, string script = "") =>
        "<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"color-scheme\" content=\"dark\">" +
        $"<title>{title}</title><style>{Css}</style><body><div class=\"aurora\"><i></i><i></i><i></i></div><main>{body}</main>{script}</body></html>";

    static string Icon(string path) => $"<div class=\"ic\"><svg viewBox=\"0 0 24 24\">{path}</svg></div>";

    /// `statusUrl` = dirección (en este mismo servidor) que dice si el teléfono ya se conectó; sin ella la página no avisa.
    public static string Html(string url, string code, string link, DateTimeOffset expires, string? statusUrl)
    {
        string h(string s) => WebUtility.HtmlEncode(s);
        var chars = new StringBuilder();
        foreach (var grp in code.Split('-')) { if (chars.Length > 0) chars.Append("<em>-</em>"); chars.Append("<span class=\"grp\">"); foreach (char c in grp) chars.Append($"<i>{h(c.ToString())}</i>"); chars.Append("</span>"); }
        string body = $$"""
<header>{{MascotHeader()}}<h1>Conecta tu celular con Nimbo</h1><p class="sub">Un código, un toque y tu mascota va en tu bolsillo.</p></header>
<div class="grid">
  <div class="stage"><div class="qr" id="qr">{{QrSvg(link)}}<div class="scan"></div>
    <div class="over d">El código caducó.<br>Pídelo de nuevo desde la app.</div>
    <div class="over k"><div><svg class="check" viewBox="0 0 84 84"><circle cx="42" cy="42" r="40"/><path d="M26 43l11 11 21-24"/></svg>¡Celular conectado!</div></div></div>
    <i class="corner c1"></i><i class="corner c2"></i><i class="corner c3"></i><i class="corner c4"></i></div>
  <div>
    <ol class="steps">
      <li>{{Icon("<path d=\"M12 3l8 4.5v9L12 21l-8-4.5v-9z\"/><path d=\"M12 12l8-4.5M12 12v9M12 12L4 7.5\"/>")}}<div><b>Abre Tailscale en el teléfono</b><span>Con la misma cuenta que esta PC: así tu conexión es privada.</span></div></li>
      <li>{{Icon("<path d=\"M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>")}}<div><b>Escanea el código</b><span>Con la cámara del teléfono, o con «Escanear código QR» en la web de Nimbo.</span></div></li>
      <li>{{Icon("<rect x=\"7\" y=\"2.5\" width=\"10\" height=\"19\" rx=\"2.5\"/><path d=\"M11 18.5h2\"/>")}}<div><b>Añádela a tu pantalla de inicio</b><span>Así Nimbo te avisa aunque el teléfono esté bloqueado.</span></div></li>
    </ol>
    <div class="code"><div class="ch"><small>¿Sin cámara? Escribe este código</small><button class="copy" id="cp" type="button" aria-label="Copiar código" title="Copiar código"><svg class="i-clip" viewBox="0 0 24 24"><rect x="9" y="9" width="11" height="11" rx="2.5"/><path d="M5 15V7.5A2.5 2.5 0 0 1 7.5 5H15"/></svg><svg class="i-ok" viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg></button></div>
      <div class="chars" id="c" data-code="{{h(code)}}">{{chars}}</div>
      <div class="url"><svg viewBox="0 0 24 24"><path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3A4 4 0 0 0 11 18.7l1-1"/></svg><span>Ábrela en el navegador del teléfono: {{h(url)}}</span></div></div>
    <div class="status"><div class="ring"><svg viewBox="0 0 54 54"><circle class="bg" cx="27" cy="27" r="23"/><circle class="fg" id="fg" cx="27" cy="27" r="23" stroke-dasharray="144.5" stroke-dashoffset="0"/></svg><b id="t">5:00</b></div>
      <div><div class="pill" id="st">Esperando al teléfono…</div><div class="url" style="margin-top:6px" id="hint">Vale 5 minutos y un solo uso.</div></div></div>
  </div>
</div>
<p class="foot">Puedes cerrar esta página cuando termines.</p>
""";
        string script = $$"""
<script>
const end={{expires.ToUnixTimeMilliseconds()}},total=5*60*1000,$=i=>document.getElementById(i);let done=false;
function fin(kind){if(done)return;done=true;$('qr').classList.add(kind==='ok'?'ok':'dead');document.body.className=kind==='ok'?'ok':'dead';const s=$('st');s.className='pill '+(kind==='ok'?'ok':'bad');
 s.textContent=kind==='ok'?'Conectado':'Caducado';$('hint').textContent=kind==='ok'?'Ya puedes cerrar esta página.':'Vuelve a pedirlo desde la app.';$('fg').style.strokeDashoffset=144.5}
function tick(){if(done)return;const l=end-Date.now();if(l<=0){$('t').textContent='0:00';fin('dead');return}
 const s=Math.ceil(l/1000);$('t').textContent=Math.floor(s/60)+':'+String(s%60).padStart(2,'0');$('fg').style.strokeDashoffset=144.5*(1-l/total);setTimeout(tick,1000)}
tick();
{{(statusUrl is null ? "" : "async function poll(){if(done)return;try{const r=await fetch('" + statusUrl + "',{cache:'no-store'});const d=await r.json();if(d.state==='used'){fin('ok');return}if(d.state==='expired'){fin('dead');return}}catch{}setTimeout(poll,1500)}poll();")}}
$('cp').onclick=async()=>{const b=$('cp');try{await navigator.clipboard.writeText($('c').dataset.code);b.classList.add('done');b.title='¡Copiado!'}catch{b.title='Selecciona el código y cópialo'}setTimeout(()=>{b.classList.remove('done');b.title='Copiar código'},1600)};
</script>
""";
        return Page("Conectar celular", body, script);
    }

    public static string Error(string message) =>
        Page("No se pudo conectar", $"<header>{MascotHeader()}<h1>No se pudo conectar el celular</h1></header><div class=\"err\">{WebUtility.HtmlEncode(message)}</div><p class=\"foot\">Corrige eso y vuelve a pedir el código desde la app.</p>");
}
