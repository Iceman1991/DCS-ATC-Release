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
    public static Func<object> Charts = () => new { fields = Array.Empty<object>() };   // Program.ChartState: airfields and their procedures for the chart editor ("/chart")

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
                                 : path.StartsWith("/charts.json") ? ("application/json", JsonSerializer.SerializeToUtf8Bytes(Charts()))
                                 : path == "/chart" || path.StartsWith("/chart?") ? ("text/html; charset=utf-8", Encoding.UTF8.GetBytes(ChartPage))
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
        var (keep, keepCharts) = (State, Charts);
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
            Charts = () => new { theatre = "Nevada", fields = new[] { new { name = "Nellis" } } };
            var ed = Get("/chart"); var cj = Get("/charts.json");
            if (!ed.StartsWith("HTTP/1.1 200 OK") || !ed.Contains("/charts.json") || !ed.Contains("<canvas") || !cj.EndsWith("{\"theatre\":\"Nevada\",\"fields\":[{\"name\":\"Nellis\"}]}") || !page.Contains("href=\"/chart\""))
                throw new Exception("Lagebild /chart: " + ed[..Math.Min(80, ed.Length)] + " | " + cj);
        }
        finally { l.Stop(); (State, Charts) = (keep, keepCharts); }
        Console.WriteLine("OK   FI7 Lagebild: Seite, /state als JSON, 404, nur localhost; #30 /wheel: Seite, Rad als PNG, zu = 204; /chart: Editor-Seite, /charts.json");
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

    /// Chart editor: click reporting points and routes of an airfield on the map, the page writes the airfield's entry for charts.jsonc (as written
    /// so far plus the additions, positions as DCS [x, z]). The server stays read-only: the user copies the text into the file. Additions are kept in
    /// the browser (localStorage) per map. ponytail: an added point is entry/exit for all runways alike and a route serves every runway that has its end
    /// point as entry; per-runway differences are edited in the file.
    const string ChartPage = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>DCS-ATC chart editor</title>
<style>
:root { --bg:#10141a; --panel:#181e27; --line:#2a3340; --text:#d8dee9; --dim:#7d8896; --rwy:#e8e8e8; --old:#5aa0ff; --arr:#4fc36b; --dep:#ff9f43; --new:#ffd166; --bad:#ff6b6b; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:14px system-ui, sans-serif; display:flex; flex-wrap:wrap; height:100vh; }
#map { flex:1 1 520px; min-height:60vh; position:relative; }
canvas { width:100%; height:100%; display:block; cursor:crosshair; }
#pos { position:absolute; left:8px; bottom:8px; color:var(--dim); background:var(--panel); padding:2px 6px; border-radius:4px; }
#side { flex:0 1 430px; background:var(--panel); padding:12px 16px; overflow:auto; max-height:100vh; }
h2 { font-size:12px; text-transform:uppercase; letter-spacing:.08em; color:var(--dim); margin:14px 0 6px; }
select, input, button, textarea { background:var(--bg); color:var(--text); border:1px solid var(--line); border-radius:4px; padding:4px 6px; font:inherit; }
button.on { border-color:var(--new); color:var(--new); }
input.alt { width:80px; }
.row { margin:4px 0; display:flex; gap:6px; align-items:center; flex-wrap:wrap; }
.dim { color:var(--dim); } .bad { color:var(--bad); } .name { color:var(--new); font-weight:600; }
textarea { width:100%; height:260px; font:12px Consolas, monospace; white-space:pre; }
a { color:var(--old); }
</style></head><body>
<div id="map"><canvas id="c"></canvas><div id="pos"></div></div>
<div id="side">
  <div class="row"><select id="field"></select> <a href="/">map</a></div>
  <h2>Add by clicking on the map</h2>
  <div class="row" id="modes"><button data-m="crp">Reporting point</button><button data-m="arr">Arrival route</button><button data-m="dep">Departure route</button></div>
  <div id="hint" class="dim"></div>
  <h2>Added</h2><div id="list" class="dim"></div>
  <h2>For charts.jsonc</h2>
  <div id="warn" class="bad"></div>
  <textarea id="out" readonly spellcheck="false"></textarea>
  <div class="row"><button id="copy">Copy</button></div>
  <div class="dim">Paste it inside the outer { } of charts.jsonc in the DCS-ATC folder, in place of the airfield's entry if it has one. It applies from the next mission start. Drag moves the map, the mouse wheel zooms.</div>
</div>
<script>
const NM = 1852, $ = id => document.getElementById(id);
const esc = s => String(s).replace(/[&<>"]/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
const css = n => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
const label = n => (/[a-z]/.test(n) || !/[A-Za-z]/.test(n) ? "CRP " : "") + n.replace(/_/g, " ");
const HINT = {
  crp: "Click where the point is. A lower-case name (north) is called \"CRP north\", a name in CAPITALS (CRAIG) is called by its name.",
  arr: "Click the fixes in order, then the reporting point where the route ends. A click on an existing point uses that point.",
  dep: "Click the fixes in order, then \"Finish route\". A click on an existing point uses that point." };
const cv = $("c"), cx = cv.getContext("2d");
let data = { fields: [] }, F = null, adds = {}, mode = "crp", range = 15, vx = 0, vz = 0, view = null, drag = null;
const A = () => adds[F.name] ??= { crp: [], routes: [], deps: [] };

// every named point of the airfield, as loaded and as added: reporting points (crp) and route fixes
function known() {
  const a = A(), l = [];
  for (const [n, p] of Object.entries(F.crp)) l.push({ n, x: p[0], z: p[1], crp: true });
  for (const c of a.crp) l.push({ n: c.name, x: c.x, z: c.z, crp: true });
  for (const r of [...F.routes, ...F.deps, ...a.routes, ...a.deps]) for (const p of r.pts) l.push({ n: p.fix, x: p.x, z: p.z });
  return l;
}

// the airfield's entry as written so far plus the additions
function entry() {
  const a = A(), o = JSON.parse(JSON.stringify(F.raw || {})), pos = p => [Math.round(p.x), Math.round(p.z)];
  if (a.crp.length) { o.crp ??= {}; o.rwy ??= {}; }
  for (const c of a.crp) {
    o.crp[c.name] = pos(c);
    for (const e of F.ends) {
      const r = o.rwy[e.n] ??= {};
      if (c.via) r.entry = { [c.name]: { via: c.via }, ...r.entry };   // in front: the last entry stays the default one
      if (c.exit) r.exit = { ...r.exit, [c.name]: {} };
    }
  }
  const entries = rw => Object.keys(o.rwy?.[rw]?.entry || {});
  for (const r of a.routes) if (r.to && r.pts.length)
    (o.routes ??= {})[r.name] = { rwy: F.ends.map(e => e.n).filter(rw => entries(rw).includes(r.to)), to: r.to, pts: r.pts.map(fix) };
  for (const r of a.deps) if (r.pts.length) (o.departures ??= {})[r.name] = { rwy: F.ends.map(e => e.n), pts: r.pts.map(fix) };
  return o;
  function fix(p) { return { fix: p.fix, pos: pos(p), ...(p.alt > 0 ? { [p.cross]: +p.alt } : {}) }; }
}

function render() {
  const a = A(), o = entry(), warn = [];
  const sel = (v, opts, on) => `<select onchange="${on}">${opts.map(([k, t]) => `<option value="${k}"${k == v ? " selected" : ""}>${t}</option>`).join("")}</select>`;
  let h = a.crp.map((c, i) => `<div class="row"><span class="name">${esc(label(c.name))}</span> entry
    ${sel(c.via, [["", "no"], ["initial", "via initial"], ["overhead", "via overhead"]], `A().crp[${i}].via=this.value;changed()`)}
    <label><input type="checkbox" ${c.exit ? "checked" : ""} onchange="A().crp[${i}].exit=this.checked;changed()"> exit</label>
    <button onclick="A().crp.splice(${i},1);changed()">&times;</button></div>`).join("");
  for (const [key, word] of [["routes", "recovery"], ["deps", "departure"]])
    a[key].forEach((r, i) => {
      h += `<div class="row"><span class="name">${esc(r.name)} ${word}</span>
        ${key == "routes" ? (r.to ? "to " + esc(label(r.to)) : '<span class="bad">click the reporting point where it ends</span>') : r.open ? `<button onclick="A().deps[${i}].open=false;changed()">Finish route</button>` : ""}
        <button onclick="A().${key}.splice(${i},1);changed()">&times;</button></div>` +
        r.pts.map((p, j) => `<div class="row">&nbsp; ${esc(label(p.fix))}
          ${sel(p.cross, [["above", "at or above"], ["at", "at"], ["below", "at or below"]], `A().${key}[${i}].pts[${j}].cross=this.value;changed()`)}
          <input class="alt" type="number" min="0" step="100" placeholder="ft" value="${esc(p.alt)}" onchange="A().${key}[${i}].pts[${j}].alt=this.value;changed()">
          <button onclick="A().${key}[${i}].pts.splice(${j},1);changed()">&times;</button></div>`).join("");
    });
  $("list").innerHTML = h || "Nothing yet.";
  if (o.crp) for (const e of F.ends) for (const k of ["entry", "exit"])
    if (!Object.keys(o.rwy?.[e.n]?.[k] || {}).length) warn.push(`Runway ${e.n} has no ${k} point: every runway needs at least one of each.`);
  for (const r of a.routes) if (r.to && !o.routes[r.name].rwy.length) warn.push(`${r.name}: ${label(r.to)} is not an entry point of any runway.`);
  $("warn").innerHTML = warn.map(esc).join("<br>");
  $("out").value = a.crp.length + a.routes.length + a.deps.length == 0 ? "" : JSON.stringify({ [F.key]: o }, null, 2).split("\n").slice(1, -1).map(l => l.slice(2)).join("\n")
    .replace(/\[\s+(-?[\d.]+),\s+(-?[\d.]+)\s+\]/g, "[$1, $2]").replace(/\[\s+("[^"\n]*"(?:,\s+"[^"\n]*")*)\s+\]/g, (m, s) => "[" + s.replace(/\s+/g, " ") + "]");
}

function draw() {
  const w = cv.clientWidth, h = cv.clientHeight, dpr = window.devicePixelRatio || 1;
  cv.width = w * dpr; cv.height = h * dpr; cx.setTransform(dpr, 0, 0, dpr, 0, 0);
  cx.fillStyle = css("--bg"); cx.fillRect(0, 0, w, h);
  if (!F) return;
  const k = Math.min(w, h) / 2 / (range * NM), P = (x, z) => [w / 2 + (z - vz) * k, h / 2 - (x - vx) * k];
  view = { w, h, k };
  cx.font = "12px system-ui"; cx.lineWidth = 1; cx.strokeStyle = css("--line"); cx.fillStyle = css("--dim");
  const [fx, fy] = P(F.x, F.z), step = [1, 2, 5, 10, 20, 50, 100].find(s => s >= range / 3) || 100;
  for (let r = step; r <= range * 2; r += step) { cx.beginPath(); cx.arc(fx, fy, r * NM * k, 0, 7); cx.stroke(); cx.fillText(r + " NM", fx + 4, fy - r * NM * k + 12); }
  for (const f of data.fields) {
    cx.lineWidth = 3; cx.strokeStyle = cx.fillStyle = css(f == F ? "--rwy" : "--dim");
    for (const e of f.ends) { const p = P(e.x, e.z), q = P(e.x2, e.z2); cx.beginPath(); cx.moveTo(p[0], p[1]); cx.lineTo(q[0], q[1]); cx.stroke(); if (f == F) cx.fillText(e.n, p[0] + 6, p[1] - 6); }
    if (f != F) { const p = P(f.x, f.z); cx.fillText(f.name, p[0] + 8, p[1] + 14); }
  }
  cx.lineWidth = 1.5;
  const dot = (x, z, n, col) => { const [px, py] = P(x, z); cx.strokeStyle = cx.fillStyle = css(col); cx.beginPath(); cx.arc(px, py, 5, 0, 7); cx.stroke(); cx.fillText(label(n), px + 8, py + 4); };
  const path = (pts, col, dash) => { cx.strokeStyle = css(col); cx.setLineDash(dash ? [6, 4] : []); cx.beginPath(); pts.forEach((p, i) => { const q = P(p.x, p.z); i ? cx.lineTo(q[0], q[1]) : cx.moveTo(q[0], q[1]); }); cx.stroke(); cx.setLineDash([]); };
  const a = A(), end = n => known().filter(p => p.crp && p.n == n).slice(0, 1);
  for (const [n, p] of Object.entries(F.crp)) dot(p[0], p[1], n, "--old");
  for (const [list, col, dash] of [[F.routes, "--arr", 0], [F.deps, "--dep", 1], [a.routes, "--new", 0], [a.deps, "--new", 1]])
    for (const r of list) { path([...r.pts, ...(r.to ? end(r.to) : [])], col, dash); for (const p of r.pts) dot(p.x, p.z, p.fix, col); }
  for (const c of a.crp) dot(c.x, c.z, c.name, "--new");
}

function changed() { try { localStorage.setItem("chart-" + data.theatre, JSON.stringify(adds)); } catch (e) {} render(); draw(); }
function setMode(m) { mode = m; document.querySelectorAll("#modes button").forEach(b => b.classList.toggle("on", b.dataset.m == m)); $("hint").textContent = HINT[m]; }
function pick(name) {
  F = data.fields.find(f => f.name == name) || data.fields[0];
  if (!F) { $("list").textContent = "No airfields yet. Start a mission once."; return; }
  $("field").value = F.name; vx = F.x; vz = F.z;
  try { localStorage.setItem("chart-field", F.name); } catch (e) {}
  changed();
}
const at = e => [vx - (e.offsetY - view.h / 2) / view.k, vz + (e.offsetX - view.w / 2) / view.k];
function click(e) {
  const [x, z] = at(e), a = A(), hit = known().find(p => Math.hypot(p.x - x, p.z - z) * view.k < 12);
  const ask = (q, caps) => { const n = (prompt(q) || "").trim().replace(/\s+/g, "_").replace(/[^\w-]/g, ""); return caps ? n.toUpperCase() : n; };
  if (mode == "crp") {
    const name = hit ? "" : ask("Name of the reporting point (north, CRAIG)");
    if (!name) return;
    if (known().some(p => p.n == name)) { alert(label(name) + " already exists at this airfield."); return; }
    a.crp.push({ name, x, z, via: "initial", exit: true });
  } else {
    const list = mode == "arr" ? a.routes : a.deps;
    let cur = list.find(r => mode == "arr" ? !r.to : r.open);
    if (!cur) {
      const name = ask("Name of the " + (mode == "arr" ? "arrival" : "departure") + " route (MINTT)", true);
      if (!name) return;
      list.push(cur = { name, to: "", open: true, pts: [] });
    }
    if (mode == "arr" && hit && hit.crp && cur.pts.length) { cur.to = hit.n; cur.open = false; }
    else {
      const fix = hit ? hit.n : ask("Name of the fix (GASS_PEAK)", true);
      if (fix) cur.pts.push({ fix, x: hit ? hit.x : x, z: hit ? hit.z : z, alt: "", cross: "above" });
      else if (!cur.pts.length) list.splice(list.indexOf(cur), 1);
    }
  }
  changed();
}
cv.onmousedown = e => drag = { x: e.offsetX, y: e.offsetY, vx, vz, moved: false };
cv.onmouseleave = () => drag = null;
cv.onmouseup = e => { const d = drag; drag = null; if (d && !d.moved && F) click(e); };
cv.onmousemove = e => {
  if (!F || !view) return;
  if (drag) {
    const dx = e.offsetX - drag.x, dy = e.offsetY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 4) drag.moved = true;
    if (drag.moved) { vz = drag.vz - dx / view.k; vx = drag.vx + dy / view.k; draw(); }
  }
  const [x, z] = at(e), b = (Math.atan2(z - F.z, x - F.x) * 180 / Math.PI - F.mv + 720) % 360;
  $("pos").textContent = `${String(Math.round(b) % 360).padStart(3, "0")}° magnetic, ${(Math.hypot(x - F.x, z - F.z) / NM).toFixed(1)} NM from ${F.name}`;
};
cv.addEventListener("wheel", e => { e.preventDefault(); range = Math.min(200, Math.max(1, range * (e.deltaY > 0 ? 1.25 : 0.8))); draw(); }, { passive: false });
document.querySelectorAll("#modes button").forEach(b => b.onclick = () => setMode(b.dataset.m));
$("field").onchange = e => pick(e.target.value);
$("copy").onclick = () => { $("out").select(); navigator.clipboard?.writeText($("out").value).catch(() => document.execCommand("copy")); };
window.onresize = draw;
(async () => {
  setMode("crp");
  try { data = await (await fetch("/charts.json", { cache: "no-store" })).json(); } catch (e) { $("list").textContent = "DCS-ATC not reachable."; return; }
  let want;
  try { adds = JSON.parse(localStorage.getItem("chart-" + data.theatre)) || {}; want = localStorage.getItem("chart-field"); } catch (e) {}
  if (!data.fields.some(f => f.name == want)) try { want = (await (await fetch("/state", { cache: "no-store" })).json()).atc?.field; } catch (e) {}
  $("field").innerHTML = data.fields.map(f => `<option>${esc(f.name)}</option>`).join("");
  pick(want);
})();
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
  <h2>Tools</h2><div class="row"><a href="/chart" style="color:var(--blue)">Chart editor</a> <span class="dim">reporting points and routes for charts.jsonc</span></div>
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
