// On-demand speech-to-text gateway for OpenWhispr "self-hosted" mode. No npm dependencies.
//
// Exposes an OpenAI-compatible POST /v1/audio/transcriptions. The whisper.cpp server (and the model in
// VRAM) is only started when a request arrives and is stopped again after `idleMinutes` without
// traffic, so an idle host holds no GPU memory.
//
// Nothing big is bundled: the whisper.cpp engine and ffmpeg are looked up at start time - first in
// engine\ next to this file (optional), then in an OpenWhispr install (its GPU pack), then on PATH.
// See README "相依項目".
//
//   node whisper-gateway.js                 # normal (started by WhisprGateway.exe)
//   node whisper-gateway.js --local-only    # bind 127.0.0.1 only (testing)
const http = require("http");
const net = require("net");
const os = require("os");
const fs = require("fs");
const path = require("path");
const { spawn, execFile } = require("child_process");

const configPath = path.join(__dirname, "gateway.config.json");
const logFile = path.join(__dirname, "gateway.log");
// First run: the live config is created from the shipped defaults (the live file is not in git).
if (!fs.existsSync(configPath)) fs.copyFileSync(path.join(__dirname, "gateway.config.default.json"), configPath);
const cfg = JSON.parse(fs.readFileSync(configPath, "utf8"));
const localOnly = process.argv.includes("--local-only");

// Relative paths are relative to this folder, which is what keeps the app relocatable.
const resolvePath = (p) => {
  const expanded = String(p).replace(/%([^%]+)%/g, (_, k) => process.env[k] || "");
  return path.isAbsolute(expanded) ? expanded : path.join(__dirname, expanded);
};
const modelsDir = resolvePath(cfg.modelsDir || "models");
const engineDir = resolvePath(cfg.engineDir || "engine");
const tmpDir = resolvePath(cfg.tmpDir || "%TEMP%\\whispr-gateway-tmp");
// ---- external dependencies: own folder first, then what an OpenWhispr install already has ----
const env = process.env;
const openWhisprRoots = [
  path.join(env.ProgramFiles || "C:\\Program Files", "OpenWhispr"),
  path.join(env.LOCALAPPDATA || "", "Programs", "OpenWhispr"),
];
const gpuPackRoot = path.join(env.APPDATA || "", "open-whispr", "bin"); // where OpenWhispr's "啟用 GPU" installs
const firstExisting = (candidates) => candidates.find((p) => p && fs.existsSync(p)) || null;

// Resolved on every backend start, so installing a pack later needs no gateway restart.
function findEngine(kind) {
  const exe = { cuda: "whisper-server-win32-x64-cuda.exe", vulkan: "whisper-server-win32-x64-vulkan.exe", cpu: "whisper-server-win32-x64.exe" }[kind];
  const candidates = [path.join(engineDir, kind, exe)];
  if (kind === "cpu") openWhisprRoots.forEach((r) => candidates.push(path.join(r, "resources", "bin", exe)));
  else candidates.push(path.join(gpuPackRoot, `whisper-${kind}`, exe));
  return firstExisting(candidates);
}

function findFfmpegDir() {
  const candidates = [path.join(engineDir, "ffmpeg", "ffmpeg.exe")];
  String(env.PATH || "").split(";").filter(Boolean).forEach((d) => candidates.push(path.join(d.trim(), "ffmpeg.exe")));
  openWhisprRoots.forEach((r) => candidates.push(path.join(r, "resources", "app.asar.unpacked", "node_modules", "ffmpeg-static", "ffmpeg.exe")));
  const hit = firstExisting(candidates);
  return hit ? path.dirname(hit) : null;
}
if (typeof cfg.allowRemote !== "boolean") cfg.allowRemote = true;
if (!cfg.device) cfg.device = "gpu:0";

const modelPath = () => path.join(modelsDir, cfg.modelFile || "");
const modelIdOf = (file) => path.basename(file || "", ".bin").replace(/^ggml-/, "");

function log(msg) {
  const line = `${new Date().toISOString()} ${msg}`;
  console.log(line);
  try {
    if (fs.existsSync(logFile) && fs.statSync(logFile).size > 1024 * 1024) fs.renameSync(logFile, logFile + ".1");
    fs.appendFileSync(logFile, line + "\n");
  } catch {}
}

// The gateway is the only writer of the config while it runs; the tray app goes through /control/*.
function persist(patch) {
  Object.assign(cfg, patch);
  try {
    const onDisk = JSON.parse(fs.readFileSync(configPath, "utf8"));
    fs.writeFileSync(configPath, JSON.stringify(Object.assign(onDisk, patch), null, 2) + "\n", "utf8");
  } catch (err) {
    log(`could not persist ${Object.keys(patch).join(",")}: ${err.message}`);
  }
}

// ---- network: where to listen, who may connect ----
// Remote access is simply "this PC's address + port". With allowRemote on, the gateway binds every
// private IPv4 address of this machine (LAN, ZeroTier, Tailscale, ...) and admits clients from those
// same subnets; with it off, only 127.0.0.1 is bound. `remoteInterfaces` (a regex on the adapter
// name) narrows that, and explicit `listen` / `allowCidrs` arrays override the automatic choice.
const listening = new Map(); // address -> http.Server
let candidates = []; // [{ address, name, cidr }] addresses remote clients could use
let autoCidrs = [];

const isPrivateV4 = (ip) => {
  const [a, b] = ip.split(".").map(Number);
  return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 100 && b >= 64 && b <= 127);
};

function scanInterfaces() {
  const filter = cfg.remoteInterfaces ? new RegExp(cfg.remoteInterfaces, "i") : null;
  const found = [];
  for (const [name, entries] of Object.entries(os.networkInterfaces())) {
    if (filter && !filter.test(name)) continue;
    for (const e of entries || []) {
      if (e.family === "IPv4" && !e.internal && isPrivateV4(e.address)) found.push({ address: e.address, name, cidr: e.cidr });
    }
  }
  return found;
}

function refreshListeners() {
  candidates = Array.isArray(cfg.listen)
    ? cfg.listen.filter((a) => a !== "127.0.0.1").map((address) => ({ address, name: "", cidr: null }))
    : scanInterfaces();
  autoCidrs = candidates.map((c) => c.cidr).filter(Boolean);

  const wanted = new Set(["127.0.0.1"]);
  if (!localOnly && cfg.allowRemote) candidates.forEach((c) => wanted.add(c.address));

  for (const [host, server] of listening) {
    if (wanted.has(host)) continue; // remote access switched off, or the address went away
    listening.delete(host);
    server.close();
    server.closeAllConnections();
    log(`stopped listening on ${host}`);
  }
  for (const host of wanted) {
    if (listening.has(host)) continue;
    const server = http.createServer(handle);
    server.requestTimeout = 0; // long uploads + cold start must not be cut off
    listening.set(host, server);
    server.once("error", (err) => {
      listening.delete(host); // retried on the next scan (e.g. a VPN adapter not up yet at logon)
      log(`listen ${host}:${cfg.port} failed (${err.code})`);
    });
    server.listen(cfg.port, host, () => log(`listening on http://${host}:${cfg.port}/v1`));
  }
}

const ipToInt = (ip) => ip.split(".").reduce((acc, o) => (acc << 8) + Number(o), 0) >>> 0;
function isAllowed(remote) {
  const ip = String(remote || "").replace(/^::ffff:/, "");
  if (ip === "::1" || ip.startsWith("127.")) return true;
  if (!cfg.allowRemote || !net.isIPv4(ip)) return false;
  const cidrs = Array.isArray(cfg.allowCidrs) ? cfg.allowCidrs : autoCidrs;
  return cidrs.some((cidr) => {
    const [base, bits] = cidr.split("/");
    const mask = bits === "0" ? 0 : (~0 << (32 - Number(bits))) >>> 0;
    return (ipToInt(ip) & mask) === (ipToInt(base) & mask);
  });
}

// ---- compute devices ----
let gpus = []; // [{ index, name, memoryMiB }] as reported by nvidia-smi (PCI bus order)
let cudaBroken = false; // CUDA engine failed to start in this session -> stay on CPU until restart

function refreshGpus() {
  const candidates = ["nvidia-smi", path.join(process.env.SystemRoot || "C:\\Windows", "System32", "nvidia-smi.exe")];
  const tryNext = (i) => {
    if (i >= candidates.length) { gpus = []; return; }
    execFile(candidates[i], ["--query-gpu=index,name,memory.total", "--format=csv,noheader,nounits"],
      { timeout: 8000, windowsHide: true }, (err, stdout) => {
        if (err) return tryNext(i + 1);
        gpus = String(stdout).split(/\r?\n/).map((l) => l.split(",").map((s) => s.trim())).filter((p) => p.length >= 3)
          .map((p) => ({ index: Number(p[0]), name: p[1], memoryMiB: Number(p[2]) }));
      });
  };
  tryNext(0);
}

// gpu:N = CUDA engine on NVIDIA GPU N; vulkan = Vulkan engine (any GPU, ggml picks the device); cpu.
function deviceList() {
  const list = [];
  if (findEngine("cuda")) gpus.forEach((g) => list.push({ id: `gpu:${g.index}`, name: g.name, memoryMiB: g.memoryMiB }));
  if (findEngine("vulkan")) list.push({ id: "vulkan", name: "Vulkan", memoryMiB: null });
  list.push({ id: "cpu", name: "CPU", memoryMiB: null });
  return list;
}

// What the next backend start will really use (the configured GPU may be gone, e.g. a laptop in eco mode).
function effectiveDevice() {
  if (cfg.device === "cpu") return "cpu";
  const cudaUsable = !cudaBroken && findEngine("cuda") && gpus.length > 0;
  if (/^gpu:\d+$/.test(cfg.device) && cudaUsable) {
    const wanted = Number(cfg.device.split(":")[1]);
    return gpus.some((g) => g.index === wanted) ? `gpu:${wanted}` : `gpu:${gpus[0].index}`;
  }
  if (findEngine("vulkan")) return "vulkan";
  return "cpu";
}

// Shown in the tray menu: what is missing and where to get it.
function problems() {
  const out = [];
  if (!cfg.modelFile || !fs.existsSync(modelPath())) out.push(`找不到模型：models\\${cfg.modelFile || "(未設定)"}（見 README「模型」）`);
  if (!findEngine("cuda") && !findEngine("vulkan") && !findEngine("cpu")) {
    out.push("找不到辨識引擎：請安裝 OpenWhispr 並在其設定按「啟用 GPU」");
  } else if (cfg.device !== "cpu" && effectiveDevice() === "cpu") {
    out.push(cudaBroken ? "GPU 引擎啟動失敗，暫時改用 CPU"
      : !findEngine("cuda") ? "沒有 GPU 引擎（OpenWhispr →「啟用 GPU」），暫時改用 CPU"
      : "偵測不到 NVIDIA GPU，暫時改用 CPU");
  }
  if (!findFfmpegDir()) out.push("找不到 ffmpeg：請安裝 ffmpeg 並加入 PATH（或安裝 OpenWhispr）");
  return out;
}

// ---- backend lifecycle ----
let child = null;
let ready = false;
let starting = null;
let activeDevice = null;
let inFlight = 0;
let idleTimer = null;
let idleDeadline = null;
let lastRequestAt = null;
let lastClient = null; // { ip, at } of the most recent non-loopback transcription request

function waitForPort(port, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  return new Promise((resolve, reject) => {
    const attempt = () => {
      if (!child) return reject(new Error("whisper-server exited during startup"));
      const sock = net.connect(port, "127.0.0.1");
      sock.once("connect", () => { sock.destroy(); resolve(); });
      sock.once("error", () => {
        sock.destroy();
        if (Date.now() > deadline) return reject(new Error("whisper-server startup timed out"));
        setTimeout(attempt, 250);
      });
    };
    attempt();
  });
}

function startBackend(device) {
  const t0 = Date.now();
  const useCpu = device === "cpu";
  const kind = useCpu ? "cpu" : device === "vulkan" ? "vulkan" : "cuda";
  const exe = findEngine(kind);
  const ffmpegDir = findFfmpegDir();
  if (!exe) return Promise.reject(new Error(`whisper-server (${kind}) not found - see README`));
  if (!ffmpegDir) return Promise.reject(new Error("ffmpeg not found - see README"));
  if (!fs.existsSync(modelPath())) return Promise.reject(new Error(`model not found: ${modelPath()}`));
  fs.mkdirSync(tmpDir, { recursive: true });

  const args = [
    "--model", modelPath(), "--host", "127.0.0.1", "--port", String(cfg.backendPort),
    "--inference-path", "/v1/audio/transcriptions", "--convert", "--tmp-dir", tmpDir,
    "--language", cfg.language || "auto",
  ];
  // Timestamps are what make the model emit clause-sized segments; without punctuation they are just cost.
  if (cfg.punctuation === false) args.push("--no-timestamps");
  if (useCpu) args.push("--threads", String(Math.min(16, Math.max(4, Math.floor(os.availableParallelism() / 2)))));
  else if (kind === "cuda") args.push("--device", device.split(":")[1]);

  child = spawn(exe, args, {
    cwd: path.dirname(exe),
    // PCI_BUS_ID makes CUDA's device numbering match nvidia-smi's, which is what the menu shows.
    env: { ...process.env, PATH: `${ffmpegDir};${process.env.PATH}`, CUDA_DEVICE_ORDER: "PCI_BUS_ID" },
    stdio: "ignore",
    windowsHide: true,
  });
  const me = child;
  child.once("exit", (code) => {
    log(`whisper-server exited (code=${code})`);
    if (child === me) { child = null; ready = false; activeDevice = null; }
  });
  return waitForPort(cfg.backendPort, useCpu ? 120000 : 90000).then(() => {
    ready = true;
    activeDevice = device;
    log(`whisper-server ready on ${device} in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
  });
}

function ensureBackend() {
  if (child && ready) return Promise.resolve();
  if (starting) return starting;
  const device = effectiveDevice();
  starting = startBackend(device)
    .catch((err) => {
      if (device === "cpu" || !findEngine("cpu") || !findFfmpegDir() || !fs.existsSync(modelPath())) throw err;
      log(`${device} start failed (${err.message}); falling back to CPU for this session`);
      if (device !== "vulkan") cudaBroken = true;
      stopBackend("gpu start failed");
      return startBackend("cpu");
    })
    .finally(() => { starting = null; });
  return starting;
}

function stopBackend(reason) {
  if (!child) return;
  log(`stopping whisper-server (${reason})`);
  ready = false;
  try { child.kill(); } catch {}
}

// idleMinutes = 0 means "never unload automatically" (manual control only).
function armIdleTimer() {
  clearTimeout(idleTimer);
  idleDeadline = null;
  if (!(cfg.idleMinutes > 0)) return;
  idleDeadline = Date.now() + cfg.idleMinutes * 60000;
  idleTimer = setTimeout(() => { if (inFlight === 0) stopBackend(`idle ${cfg.idleMinutes} min`); }, cfg.idleMinutes * 60000);
}

function status() {
  return {
    status: "ok",
    backend: child && ready ? "loaded" : starting ? "loading" : "unloaded",
    model: modelIdOf(cfg.modelFile),
    modelFile: cfg.modelFile,
    device: cfg.device,
    activeDevice: activeDevice || effectiveDevice(),
    devices: deviceList(),
    idleMinutes: cfg.idleMinutes,
    allowRemote: cfg.allowRemote,
    punctuation: cfg.punctuation !== false,
    listening: [...listening.keys()],
    addresses: candidates.map((c) => ({ address: c.address, name: c.name })),
    port: cfg.port,
    problems: problems(),
    inFlight,
    lastRequestAt,
    lastClient,
    unloadAt: child && ready && inFlight === 0 ? idleDeadline : null,
    pid: process.pid,
  };
}

// Manual control (tray icon). Loopback callers only - remote clients can transcribe, not manage.
function handleControl(req, res, url) {
  const send = (code, obj) => { res.writeHead(code, { "Content-Type": "application/json" }); res.end(JSON.stringify(obj)); };
  const param = (name) => new URL(req.url, "http://x").searchParams.get(name) || "";
  if (req.method === "GET" && url === "/control/status") return send(200, status());
  if (req.method !== "POST") return send(405, { error: "method not allowed" });

  if (url === "/control/load") {
    log("manual load requested");
    ensureBackend().then(armIdleTimer).catch((err) => log(`manual load failed: ${err.message}`));
    return send(202, status());
  }
  if (url === "/control/unload") {
    if (inFlight > 0) return send(409, { error: "transcription in progress" });
    clearTimeout(idleTimer);
    idleDeadline = null;
    stopBackend("manual unload");
    return send(200, status());
  }
  if (url === "/control/idle-minutes") {
    const value = Number(param("value"));
    if (!Number.isFinite(value) || value < 0 || value > 1440) return send(400, { error: "value must be 0-1440" });
    persist({ idleMinutes: value });
    log(`idleMinutes set to ${value}`);
    if (child && ready && inFlight === 0) armIdleTimer();
    return send(200, status());
  }
  if (url === "/control/remote") {
    persist({ allowRemote: param("enabled") === "true" });
    log(`remote access ${cfg.allowRemote ? "enabled" : "disabled"}`);
    refreshListeners();
    return send(200, status());
  }
  if (url === "/control/model" || url === "/control/device" || url === "/control/punctuation") {
    if (inFlight > 0) return send(409, { error: "transcription in progress" });
    if (url === "/control/punctuation") {
      persist({ punctuation: param("enabled") === "true" });
      log(`punctuation ${cfg.punctuation ? "on" : "off"}`);
    } else if (url === "/control/model") {
      const file = param("file");
      if (!/^[\w.\-]+\.bin$/.test(file) || !fs.existsSync(path.join(modelsDir, file))) {
        return send(400, { error: "model file not found in models folder" });
      }
      persist({ modelFile: file });
      log(`model switched to ${file}`);
    } else {
      const value = param("value");
      if (value !== "cpu" && value !== "vulkan" && !/^gpu:\d+$/.test(value)) return send(400, { error: "value must be cpu, vulkan or gpu:N" });
      persist({ device: value });
      cudaBroken = false; // an explicit choice deserves a fresh GPU attempt
      log(`device set to ${value}`);
    }
    clearTimeout(idleTimer);
    idleDeadline = null;
    stopBackend("settings changed"); // the next request (or a manual load) starts it with the new settings
    return send(200, status());
  }
  return send(404, { error: "unknown control path" });
}

// ---- punctuation from segments ----
// Breeze-ASR-25 emits next to no punctuation, but with timestamps on its segments break exactly at
// clause boundaries (measured: 8 segments for a text with 8 punctuation marks). So the marks are added
// here: a question mark after question endings, a full stop before topic-shift openers, after a long
// pause and at the very end, a comma everywhere else. Deliberately conservative - a wrong comma reads
// better than a wrong full stop.
const CJK = "\\u3400-\\u9fff\\uf900-\\ufaff";
const QUESTION_END = /(嗎|呢|是不是|有沒有|對不對|好不好|行不行|可不可以|能不能|要不要|會不會)$/;
const SENTENCE_OPENER = /^(另外|此外|對了|首先|其次|再來|最後|總之|接下來|順便|話說)/;
const HAS_END_MARK = /[，。？！、；：…,.?!;:]$/;
const LONG_PAUSE_SECONDS = 0.7;

function tidy(text) {
  return String(text || "")
    .replace(/\s+/g, " ")
    .replace(new RegExp(`([${CJK}]) (?=[${CJK}0-9])`, "g"), "$1") // "下午 3 點" -> "下午3點"
    .replace(new RegExp(`([0-9]) (?=[${CJK}])`, "g"), "$1")
    .replace(new RegExp(`(?<=[${CJK}]),`, "g"), "，")
    .replace(new RegExp(`(?<=[${CJK}])\\?`, "g"), "？")
    .replace(new RegExp(`(?<=[${CJK}])!`, "g"), "！")
    .trim();
}

function punctuate(segments) {
  const parts = segments.map((s) => ({ text: tidy(s.text), start: Number(s.start), end: Number(s.end) })).filter((p) => p.text);
  const hasCjk = new RegExp(`[${CJK}]`);
  let out = "";
  parts.forEach((p, i) => {
    const next = parts[i + 1];
    out += p.text;
    if (HAS_END_MARK.test(p.text)) return;
    if (!hasCjk.test(p.text)) { out += next ? " " : ""; return; } // leave non-Chinese segments alone
    if (QUESTION_END.test(p.text)) out += "？";
    else if (!next || SENTENCE_OPENER.test(next.text) || next.start - p.end >= LONG_PAUSE_SECONDS) out += "。";
    else out += "，";
  });
  return out;
}

// Adds a multipart text field in front of the body unless the client already sent that field.
function injectField(body, boundary, name, value) {
  if (body.includes(`name="${name}"`)) return body;
  return Buffer.concat([Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="${name}"\r\n\r\n${value}\r\n`, "utf8"), body]);
}

// ---- HTTP front ----
const MAX_BODY_BYTES = 512 * 1024 * 1024;

// OpenWhispr's self-hosted route sends no `prompt`, and whisper-server's --prompt argv is mangled by
// the Windows ANSI code page, so the script bias is added here as a UTF-8 multipart field. When
// punctuation is on, the backend is also asked for segments - unless the client chose its own
// response_format (then the reply is passed through untouched).
function prepareBody(body, contentType) {
  const m = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(contentType || "");
  if (!m) return { body, punctuate: false };
  const boundary = m[1] || m[2];
  const wantsSegments = cfg.punctuation !== false && !body.includes('name="response_format"');
  if (cfg.prompt) body = injectField(body, boundary, "prompt", cfg.prompt);
  if (wantsSegments) body = injectField(body, boundary, "response_format", "verbose_json");
  return { body, punctuate: wantsSegments };
}

function handle(req, res) {
  const remote = req.socket.remoteAddress;
  if (!isAllowed(remote)) {
    log(`denied ${remote} ${req.method} ${req.url}`);
    res.writeHead(403).end();
    return;
  }
  const url = req.url.split("?")[0];

  if (url.startsWith("/control/")) {
    const ip = String(remote || "").replace(/^::ffff:/, "");
    if (ip !== "127.0.0.1" && ip !== "::1") { res.writeHead(403).end(); return; }
    return handleControl(req, res, url);
  }

  if (req.method === "GET" && /\/models$/.test(url)) {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ object: "list", data: [{ id: modelIdOf(cfg.modelFile), object: "model", owned_by: "local" }] }));
    return;
  }
  if (req.method === "GET") {
    res.writeHead(200, { "Content-Type": "application/json" });
    const s = status();
    res.end(JSON.stringify({ status: s.status, backend: s.backend, model: s.model }));
    return;
  }
  if (req.method !== "POST" || !/\/audio\/transcriptions$/.test(url)) {
    res.writeHead(404).end();
    return;
  }

  inFlight++;
  clearTimeout(idleTimer);
  lastRequestAt = Date.now();
  const clientIp = String(remote || "").replace(/^::ffff:/, "");
  if (clientIp !== "::1" && !clientIp.startsWith("127.")) lastClient = { ip: clientIp, at: lastRequestAt };
  const t0 = Date.now();
  let finished = false;
  const done = (note) => {
    if (finished) return;
    finished = true;
    inFlight--;
    log(`${clientIp} transcription ${note} in ${((Date.now() - t0) / 1000).toFixed(2)}s`);
    armIdleTimer();
  };

  // Buffer the upload (dictation clips are small) while the backend warms up in parallel.
  const chunks = [];
  let size = 0;
  const bodyReady = new Promise((resolve, reject) => {
    req.on("data", (c) => {
      size += c.length;
      if (size > MAX_BODY_BYTES) return reject(new Error("upload too large"));
      chunks.push(c);
    });
    req.once("end", () => resolve());
    req.once("error", reject);
  });

  Promise.all([ensureBackend(), bodyReady])
    .then(() => {
      const prepared = prepareBody(Buffer.concat(chunks), req.headers["content-type"]);
      const body = prepared.body;
      const headers = { ...req.headers, host: `127.0.0.1:${cfg.backendPort}`, "content-length": String(body.length) };
      delete headers["transfer-encoding"];
      const upstream = http.request(
        { host: "127.0.0.1", port: cfg.backendPort, path: "/v1/audio/transcriptions", method: "POST", headers },
        (up) => {
          if (!prepared.punctuate) {
            res.writeHead(up.statusCode, up.headers);
            up.pipe(res);
            up.once("end", () => done(`-> ${up.statusCode}`));
            return;
          }
          // Collapse the backend's segment list into the {"text": ...} shape OpenWhispr expects.
          const parts = [];
          up.on("data", (c) => parts.push(c));
          up.once("end", () => {
            const raw = Buffer.concat(parts);
            let reply = raw;
            let type = up.headers["content-type"] || "application/json";
            try {
              const data = JSON.parse(raw.toString("utf8"));
              if (Array.isArray(data.segments)) {
                reply = Buffer.from(JSON.stringify({ text: punctuate(data.segments) }), "utf8");
                type = "application/json; charset=utf-8";
              }
            } catch {} // not JSON (backend error text): hand it over as it came
            res.writeHead(up.statusCode, { "Content-Type": type, "Content-Length": reply.length });
            res.end(reply);
            done(`-> ${up.statusCode}`);
          });
        }
      );
      upstream.once("error", (err) => {
        if (!res.headersSent) res.writeHead(502, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ error: { message: `backend error: ${err.message}` } }));
        done(`failed (${err.message})`);
      });
      upstream.end(body);
    })
    .catch((err) => {
      res.writeHead(503, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: { message: err.message } }));
      done(`failed (${err.message})`);
    });
}

for (const sig of ["SIGINT", "SIGTERM", "SIGHUP"]) process.on(sig, () => { stopBackend(sig); process.exit(0); });
process.on("exit", () => { try { child && child.kill(); } catch {} });

log(`gateway starting (device=${cfg.device}, idle unload after ${cfg.idleMinutes} min, localOnly=${localOnly})`);
refreshGpus();
refreshListeners();
setInterval(refreshListeners, 20000); // picks up ZeroTier coming up after logon
setInterval(refreshGpus, 120000); // a dGPU can appear / disappear (laptop eco mode)
setTimeout(() => { for (const p of problems()) log(`problem: ${p}`); }, 10000); // after the first GPU scan has answered
