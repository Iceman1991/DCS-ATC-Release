using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DcsAtc;

/// FI7: situation map in the browser (tablet, second monitor) at http://localhost:MapPort/: airfields, own aircraft, traffic, controller with phase and
/// runway, frequencies, the last radio calls, the last call to you. A plain TcpListener (HttpListener needs a URL reservation as admin), read-only:
/// "/" the page, "/state" the JSON the page fetches every second. MapLan = all network interfaces (Windows asks about the firewall once).
static class Lagebild
{
    public static Func<object> State = () => new { };   // Program: snapshot under TowerLock

    public static TcpListener Start(int port, bool lan)
    {
        var l = new TcpListener(lan ? IPAddress.Any : IPAddress.Loopback, port);
        l.Start();
        new Thread(() =>
        {
            try { while (true) { var c = l.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => Serve(c)); } }
            catch (SocketException) { }   // Stop()
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "Lagebild" }.Start();
        return l;
    }

    static void Serve(TcpClient c)
    {
        try
        {
            using (c)
            {
                c.ReceiveTimeout = c.SendTimeout = 3000;
                var s = c.GetStream();
                var r = new StreamReader(s, Encoding.ASCII);
                var path = (r.ReadLine() ?? "").Split(' ').ElementAtOrDefault(1) ?? "/";   // "GET /state HTTP/1.1"
                while (!string.IsNullOrEmpty(r.ReadLine())) { }   // headers unread would reset the connection on close
                var (type, body) = path.StartsWith("/state") ? ("application/json", JsonSerializer.SerializeToUtf8Bytes(State()))
                                 : path.StartsWith("/wheel.png") ? ("image/png", Wheel.Web ?? Array.Empty<byte>())   // #30: empty = wheel closed (204)
                                 : path == "/wheel" || path.StartsWith("/wheel?") ? ("text/html; charset=utf-8", Encoding.UTF8.GetBytes(WheelPage))
                                 : path == "/" || path.StartsWith("/?") ? ("text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page)) : ("text/plain", "not found"u8.ToArray());
                var status = type == "text/plain" ? "404 Not Found" : body.Length == 0 ? "204 No Content" : "200 OK";
                s.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"));
                s.Write(body);
            }
        }
        catch (Exception e) when (e is IOException or SocketException) { }   // browser gone
    }

    /// Self-test: page, state as JSON, 404, on a free port.
    internal static void SelfTest()
    {
        var keep = State;
        State = () => new { me = new { cs = "Enfield 1-1", x = 1.5 } };
        var l = Start(0, false);
        try
        {
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            string Get(string p)
            {
                using var c = new TcpClient("127.0.0.1", port);
                var s = c.GetStream();
                s.Write(Encoding.ASCII.GetBytes($"GET {p} HTTP/1.1\r\nHost: localhost\r\nUser-Agent: test\r\n\r\n"));
                return new StreamReader(s, Encoding.UTF8).ReadToEnd();
            }
            var page = Get("/"); var st = Get("/state"); var nf = Get("/x");
            if (!page.StartsWith("HTTP/1.1 200 OK") || !page.Contains("<canvas") || !st.Contains("application/json") || !st.EndsWith("{\"me\":{\"cs\":\"Enfield 1-1\",\"x\":1.5}}") || !nf.StartsWith("HTTP/1.1 404"))
                throw new Exception("Lagebild: " + page[..Math.Min(80, page.Length)] + " | " + st + " | " + nf);
            Wheel.Web = null; var closed = Get("/wheel.png");
            Wheel.Web = "PNGx"u8.ToArray(); var open = Get("/wheel.png"); var wp = Get("/wheel");
            Wheel.Web = null;
            if (!closed.StartsWith("HTTP/1.1 204") || !open.StartsWith("HTTP/1.1 200 OK") || !open.Contains("image/png") || !open.EndsWith("PNGx") || !wp.StartsWith("HTTP/1.1 200 OK") || !wp.Contains("/wheel.png"))
                throw new Exception("Lagebild /wheel: " + closed + " | " + open + " | " + wp[..Math.Min(80, wp.Length)]);
        }
        finally { l.Stop(); State = keep; }
        Console.WriteLine("OK   FI7 Lagebild: Seite, /state als JSON, 404, nur localhost; #30 /wheel: Seite, Rad als PNG, zu = 204");
    }

    /// #30: the radio wheel for an OpenKneeboard "Web Dashboard" tab (VR) or a tablet: transparent page, shows the wheel picture while the wheel is open.
    /// ponytail: the page loads the whole PNG five times a second while the wheel is open (about 100 KB each); a version number in the request if that ever hurts over a network.
    const string WheelPage = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>DCS-ATC radio wheel</title>
<style>
html, body { margin:0; height:100%; background:transparent; overflow:hidden; }
img { display:block; width:100vmin; height:100vmin; margin:0 auto; }
</style></head><body>
<img id="w" alt="" hidden>
<script>
const w = document.getElementById("w");
let old;
w.onload = () => { if (old) URL.revokeObjectURL(old); old = w.src; };
try { window.OpenKneeboard?.SetPreferredPixelSize?.(840, 840)?.catch?.(() => {}); } catch (e) {}   // square page in OpenKneeboard
async function poll() {
  try {
    const r = await fetch("/wheel.png", { cache: "no-store" });
    if (r.status == 200) { w.src = URL.createObjectURL(await r.blob()); w.hidden = false; }
    else w.hidden = true;
  } catch (e) { w.hidden = true; }
  setTimeout(poll, 200);
}
poll();
</script></body></html>
""";

    const string Page = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>DCS-ATC map</title>
<style>
:root { --bg:#10141a; --panel:#181e27; --line:#2a3340; --text:#d8dee9; --dim:#7d8896; --me:#7fd4ff; --blue:#5aa0ff; --red:#ff6b6b; --grey:#b8b8b8; --rwy:#e8e8e8; --mine:#ffd166; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:14px system-ui, sans-serif; display:flex; flex-wrap:wrap; height:100vh; }
#map { flex:1 1 480px; min-height:60vh; position:relative; }
canvas { width:100%; height:100%; display:block; }
#zoom { position:absolute; top:8px; left:8px; display:flex; gap:4px; }
#zoom button { background:var(--panel); color:var(--text); border:1px solid var(--line); border-radius:4px; padding:6px 10px; font-size:14px; }
#side { flex:0 1 380px; background:var(--panel); padding:12px 16px; overflow:auto; max-height:100vh; }
h2 { font-size:12px; text-transform:uppercase; letter-spacing:.08em; color:var(--dim); margin:14px 0 6px; }
.row { margin:2px 0; } .dim { color:var(--dim); } .mine { color:var(--mine); }
#last { font-size:16px; color:var(--mine); }
#radio div { padding:3px 0; border-bottom:1px solid var(--line); }
</style></head><body>
<div id="map"><canvas id="c"></canvas><div id="zoom"><button data-r="10">10</button><button data-r="25">25</button><button data-r="50">50</button><button data-r="100">100 NM</button></div></div>
<div id="side">
  <div id="me" class="row dim">No data yet. Start a mission.</div>
  <h2>Controller</h2><div id="atc" class="dim">-</div>
  <h2>Last call to you</h2><div id="last" class="dim">-</div>
  <h2>Radio</h2><div id="radio"></div>
</div>
<script>
const NM = 1852, FT = 0.3048;
let range = 25, st = null;
try { range = +localStorage.getItem("range") || 25; } catch (e) {}
document.querySelectorAll("#zoom button").forEach(b => b.onclick = () => { range = +b.dataset.r; try { localStorage.setItem("range", range); } catch (e) {} draw(); });
const cv = document.getElementById("c"), cx = cv.getContext("2d");
const css = n => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
const esc = s => String(s).replace(/[&<>]/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[ch]));
function draw() {
  const w = cv.clientWidth, h = cv.clientHeight, dpr = window.devicePixelRatio || 1;
  cv.width = w * dpr; cv.height = h * dpr; cx.setTransform(dpr, 0, 0, dpr, 0, 0);
  cx.fillStyle = css("--bg"); cx.fillRect(0, 0, w, h);
  if (!st) return;
  const c0 = st.me || st.center; if (!c0) return;
  const k = Math.min(w, h) / 2 / (range * NM), ox = w / 2, oy = h / 2;
  const P = (x, z) => [ox + (z - c0.z) * k, oy - (x - c0.x) * k];
  cx.strokeStyle = css("--line"); cx.fillStyle = css("--dim"); cx.font = "11px system-ui";
  for (const f of [0.5, 1]) { cx.beginPath(); cx.arc(ox, oy, range * NM * k * f, 0, 7); cx.stroke(); cx.fillText((range * f) + " NM", ox + 4, oy - range * NM * k * f + 12); }
  cx.lineWidth = 3; cx.strokeStyle = css("--rwy");
  for (const f of st.fields) {
    for (const r of f.rw) { const [a, b] = [P(r[0], r[1]), P(r[2], r[3])]; cx.beginPath(); cx.moveTo(a[0], a[1]); cx.lineTo(b[0], b[1]); cx.stroke(); }
    const p = P(f.x, f.z); cx.fillStyle = css("--dim"); cx.fillText(f.name, p[0] + 8, p[1] + 14);
  }
  cx.lineWidth = 1.5;
  const plane = (x, z, hdg, col, label, size) => {
    const [px, py] = P(x, z), s = Math.sin(hdg), c = Math.cos(hdg);
    cx.strokeStyle = cx.fillStyle = col; cx.beginPath();
    cx.moveTo(px + s * size, py - c * size); cx.lineTo(px - s * size * .6 + c * size * .5, py + c * size * .6 + s * size * .5);
    cx.lineTo(px - s * size * .6 - c * size * .5, py + c * size * .6 - s * size * .5); cx.closePath(); cx.stroke();
    cx.beginPath(); cx.moveTo(px + s * size, py - c * size); cx.lineTo(px + s * size * 3, py - c * size * 3); cx.stroke();
    cx.font = "12px system-ui"; label.split("\n").forEach((l, i) => cx.fillText(l, px + 10, py - 4 + i * 13));
  };
  for (const t of st.traffic) {
    const col = !st.me || !t.side ? css("--grey") : t.side == st.me.side ? css("--blue") : css("--red");
    plane(t.x, t.z, t.hdg, col, t.cs + "\n" + (t.air ? Math.round(t.ft / 100) * 100 + " ft " + Math.round(t.kt) + " kt" : "ground"), 7);
  }
  if (st.me) plane(st.me.x, st.me.z, st.me.hdg, css("--me"), "", 9);
}
async function poll() {
  try {
    st = await (await fetch("/state", { cache: "no-store" })).json();
    const m = st.me;
    document.getElementById("me").innerHTML = m ? `<b>${esc(m.cs)}</b> ${esc(m.type)} &nbsp; ${Math.round(m.ft)} ft &nbsp; ${Math.round(m.kt)} kt &nbsp; heading ${String(Math.round(m.hdg * 180 / Math.PI + 360) % 360).padStart(3, "0")}` : "No own aircraft.";
    const a = st.atc;
    document.getElementById("atc").innerHTML = a ? `<div class="row"><b>${esc(a.field)}</b> &nbsp; ${esc(a.phase)} &nbsp; runway ${esc(a.rw)}${a.atis ? " &nbsp; ATIS " + esc(a.atis) : ""}</div>` +
      a.freqs.map(f => `<div class="row dim">${esc(f[0])} ${esc(f[1])}</div>`).join("") : "No airfield.";
    document.getElementById("last").textContent = st.last || "-";
    document.getElementById("radio").innerHTML = st.radio.slice().reverse().map(r => `<div class="${r.mine ? "mine" : ""}"><span class="dim">${esc(r.t)} ${esc(r.who)}</span><br>${esc(r.text)}</div>`).join("");
  } catch (e) { st = null; document.getElementById("me").textContent = "DCS-ATC not reachable."; }
  draw();
}
window.onresize = draw;
poll(); setInterval(poll, 1000);
</script></body></html>
""";
}
