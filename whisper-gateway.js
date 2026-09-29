// On-demand speech-to-text gateway for OpenWhispr "self-hosted" mode. No npm dependencies.
//
// Exposes an OpenAI-compatible POST /v1/audio/transcriptions. The recognition server (and the model in
// VRAM) is only started when a request arrives and is stopped again after `idleMinutes` without
// traffic, so an idle host holds no GPU memory.
//
// Two engines, chosen by the model file in models\:
//   *.bin   whisper.cpp ggml models (Breeze-ASR-25, Whisper ...) via whisper-server
//   *.gguf  Qwen3-ASR family (Qwen3-ASR, TEA-ASR ...) via llama-server + the matching mmproj-*.gguf
//
// Nothing big is bundled: engines and ffmpeg are looked up at start time - first in engine\ next to
// this file (optional), then in an OpenWhispr install (its GPU pack / bundled CPU servers), then on
// PATH. See README "相依項目".
//
//   node whisper-gateway.js                 # normal (started by WhisprGateway.exe)
//   node whisper-gateway.js --local-only    # bind 127.0.0.1 only (testing)
const http = require("http");
const https = require("https");
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
const dataDir = resolvePath(cfg.dataDir || "data");
const tmpDir = resolvePath(cfg.tmpDir || "%TEMP%\\whispr-gateway-tmp");
if (typeof cfg.allowRemote !== "boolean") cfg.allowRemote = true;
if (typeof cfg.convertSimplified !== "boolean") cfg.convertSimplified = true;
if (!cfg.device) cfg.device = "gpu:0";

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

// ---- models ----
// A GGUF model needs its audio encoder ("mmproj") next to it. Both carry the same family name plus a
// quantization suffix, in either of the two common spellings:
//   Qwen3-ASR-1.7B-Q8_0.gguf  +  mmproj-Qwen3-ASR-1.7B-Q8_0.gguf      (ggml-org)
//   TEA-ASR-1.1.Q6_K.gguf     +  TEA-ASR-1.1.mmproj-Q8_0.gguf         (mradermacher)
const isMmprojName = (name) => /(^mmproj-|[.-]mmproj[-.])/i.test(name);
function familyOf(name) {
  let n = name.replace(/\.gguf$/i, "");
  n = n.replace(/^mmproj-/i, "").replace(/[.-]mmproj[-.][^.]*$/i, "");
  return n.replace(/[-.](i?q\d[a-z0-9_]*|bf16|f16|f32)$/i, "");
}

function describeModel(file) {
  const f = String(file || "");
  if (/\.bin$/i.test(f)) return { file: f, engine: "whisper", id: path.basename(f, ".bin").replace(/^ggml-/, ""), mmproj: null };
  if (!/\.gguf$/i.test(f) || isMmprojName(f)) return null;
  const family = familyOf(f);
  let mmproj = null;
  try {
    mmproj = fs.readdirSync(modelsDir)
      .filter((n) => /\.gguf$/i.test(n) && isMmprojName(n) && familyOf(n).toLowerCase() === family.toLowerCase())
      .sort((a, b) => (/q8_0/i.test(b) ? 1 : 0) - (/q8_0/i.test(a) ? 1 : 0))[0] || null;
  } catch {}
  return { file: f, engine: "llama", id: family, mmproj };
}
const currentModel = () => describeModel(cfg.modelFile) || { file: cfg.modelFile || "", engine: "whisper", id: "", mmproj: null };
const modelPath = () => path.join(modelsDir, cfg.modelFile || "");

// ---- external dependencies: own folder first, then what an OpenWhispr install already has ----
const env = process.env;
const openWhisprRoots = [
  path.join(env.ProgramFiles || "C:\\Program Files", "OpenWhispr"),
  path.join(env.LOCALAPPDATA || "", "Programs", "OpenWhispr"),
];
const gpuPackRoot = path.join(env.APPDATA || "", "open-whispr", "bin"); // where OpenWhispr's "啟用 GPU" installs
const firstExisting = (candidates) => candidates.find((p) => p && fs.existsSync(p)) || null;

// Resolved on every backend start, so installing a pack later needs no gateway restart.
function findEngine(engine, kind) {
  const candidates = [];
  if (engine === "whisper") {
    const exe = { cuda: "whisper-server-win32-x64-cuda.exe", vulkan: "whisper-server-win32-x64-vulkan.exe", cpu: "whisper-server-win32-x64.exe" }[kind];
    candidates.push(path.join(engineDir, kind, exe), path.join(engineDir, `whisper-${kind}`, exe));
    if (kind === "cpu") openWhisprRoots.forEach((r) => candidates.push(path.join(r, "resources", "bin", exe)));
    else candidates.push(path.join(gpuPackRoot, `whisper-${kind}`, exe));
  } else {
    candidates.push(path.join(engineDir, `llama-${kind}`, "llama-server.exe"));
    if (kind === "vulkan") candidates.push(path.join(gpuPackRoot, "llama-vulkan", "llama-server-vulkan.exe"));
    if (kind === "cpu") openWhisprRoots.forEach((r) => candidates.push(path.join(r, "resources", "bin", "llama-server-win32-x64-cpu.exe")));
  }
  return firstExisting(candidates);
}

function findFfmpeg() {
  const candidates = [path.join(engineDir, "ffmpeg", "ffmpeg.exe")];
  String(env.PATH || "").split(";").filter(Boolean).forEach((d) => candidates.push(path.join(d.trim(), "ffmpeg.exe")));
  openWhisprRoots.forEach((r) => candidates.push(path.join(r, "resources", "app.asar.unpacked", "node_modules", "ffmpeg-static", "ffmpeg.exe")));
  return firstExisting(candidates);
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
let cudaBroken = false; // CUDA engine failed to start in this session -> stay off it until restart

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

// gpu:N = CUDA build on NVIDIA GPU N; vulkan = Vulkan build (any GPU, ggml picks the device); cpu.
// Which of these exist depends on the engine the current model needs.
function deviceList() {
  const engine = currentModel().engine;
  const list = [];
  if (findEngine(engine, "cuda")) gpus.forEach((g) => list.push({ id: `gpu:${g.index}`, name: g.name, memoryMiB: g.memoryMiB }));
  if (findEngine(engine, "vulkan")) list.push({ id: "vulkan", name: "Vulkan", memoryMiB: null });
  list.push({ id: "cpu", name: "CPU", memoryMiB: null });
  return list;
}

// What the next backend start will really use (the configured GPU may be gone, e.g. a laptop in eco mode).
function effectiveDevice() {
  const engine = currentModel().engine;
  if (cfg.device === "cpu") return "cpu";
  const cudaUsable = !cudaBroken && findEngine(engine, "cuda") && gpus.length > 0;
  if (/^gpu:\d+$/.test(cfg.device) && cudaUsable) {
    const wanted = Number(cfg.device.split(":")[1]);
    return gpus.some((g) => g.index === wanted) ? `gpu:${wanted}` : `gpu:${gpus[0].index}`;
  }
  if (findEngine(engine, "vulkan")) return "vulkan";
  return "cpu";
}

// Shown in the tray menu: what is missing and where to get it.
function problems() {
  const out = [];
  const model = currentModel();
  if (!cfg.modelFile || !fs.existsSync(modelPath())) out.push(`找不到模型：models\\${cfg.modelFile || "(未設定)"}（見 README「模型」）`);
  else if (model.engine === "llama" && !model.mmproj) out.push(`缺少 ${model.id} 的音訊編碼器（mmproj-*.gguf，與模型同一個 Hugging Face 頁面）`);
  const gpuHint = model.engine === "llama" ? "把 llama.cpp 的 CUDA 版解壓到 engine\\llama-cuda\\" : "OpenWhispr →「啟用 GPU」";
  if (!findEngine(model.engine, "cuda") && !findEngine(model.engine, "vulkan") && !findEngine(model.engine, "cpu")) {
    out.push(model.engine === "llama" ? "找不到 llama.cpp 引擎：請安裝 OpenWhispr，或見 README「相依項目」" : "找不到辨識引擎：請安裝 OpenWhispr 並在其設定按「啟用 GPU」");
  } else if (cfg.device !== "cpu" && effectiveDevice() === "cpu") {
    out.push(cudaBroken ? "GPU 引擎啟動失敗，暫時改用 CPU"
      : !findEngine(model.engine, "cuda") ? `沒有 GPU 引擎（${gpuHint}），暫時改用 CPU`
      : "偵測不到 NVIDIA GPU，暫時改用 CPU");
  }
  if (!findFfmpeg()) out.push("找不到 ffmpeg：請安裝 ffmpeg 並加入 PATH（或安裝 OpenWhispr）");
  if (model.engine === "llama" && cfg.convertSimplified && scriptDictState === "missing") out.push("簡繁字典下載失敗（data\\opencc\\），簡體輸出暫不轉換");
  return out;
}

// ---- backend lifecycle ----
let child = null;
let ready = false;
let starting = null;
let activeDevice = null;
let activeEngine = null;
let inFlight = 0;
let idleTimer = null;
let idleDeadline = null;
let lastRequestAt = null;
let lastClient = null; // { ip, at } of the most recent non-loopback transcription request

// whisper-server only listens once the model is loaded; llama-server listens at once and answers
// /health with "loading model" until it is really ready.
function waitForReady(port, timeoutMs, healthCheck) {
  const deadline = Date.now() + timeoutMs;
  return new Promise((resolve, reject) => {
    const attempt = () => {
      if (!child) return reject(new Error("recognition server exited during startup"));
      if (Date.now() > deadline) return reject(new Error("recognition server startup timed out"));
      if (!healthCheck) {
        const sock = net.connect(port, "127.0.0.1");
        sock.once("connect", () => { sock.destroy(); resolve(); });
        sock.once("error", () => { sock.destroy(); setTimeout(attempt, 250); });
        return;
      }
      const req = http.get({ host: "127.0.0.1", port, path: "/health", timeout: 2000 }, (res) => {
        let body = "";
        res.on("data", (c) => { body += c; });
        res.on("end", () => (res.statusCode === 200 && /"ok"/.test(body) ? resolve() : setTimeout(attempt, 400)));
      });
      req.on("error", () => setTimeout(attempt, 400));
      req.on("timeout", () => { req.destroy(); setTimeout(attempt, 400); });
    };
    attempt();
  });
}

function startBackend(device) {
  const t0 = Date.now();
  const model = currentModel();
  const useCpu = device === "cpu";
  const kind = useCpu ? "cpu" : device === "vulkan" ? "vulkan" : "cuda";
  const exe = findEngine(model.engine, kind);
  const ffmpeg = findFfmpeg();
  if (!exe) return Promise.reject(new Error(`${model.engine} engine (${kind}) not found - see README`));
  if (!ffmpeg) return Promise.reject(new Error("ffmpeg not found - see README"));
  if (!fs.existsSync(modelPath())) return Promise.reject(new Error(`model not found: ${modelPath()}`));
  if (model.engine === "llama" && !model.mmproj) return Promise.reject(new Error(`mmproj (audio encoder) for ${model.id} not found in models folder`));
  fs.mkdirSync(tmpDir, { recursive: true });

  const threads = String(Math.min(16, Math.max(4, Math.floor(os.availableParallelism() / 2))));
  let args;
  if (model.engine === "whisper") {
    args = [
      "--model", modelPath(), "--host", "127.0.0.1", "--port", String(cfg.backendPort),
      "--inference-path", "/v1/audio/transcriptions", "--convert", "--tmp-dir", tmpDir,
      "--language", cfg.language || "auto",
    ];
    // Timestamps are what make the model emit clause-sized segments; without punctuation they are just cost.
    if (cfg.punctuation === false) args.push("--no-timestamps");
    if (useCpu) args.push("--threads", threads);
    else if (kind === "cuda") args.push("--device", device.split(":")[1]);
  } else {
    args = [
      "-m", modelPath(), "--mmproj", path.join(modelsDir, model.mmproj), "--host", "127.0.0.1", "--port", String(cfg.backendPort),
      "-c", String(cfg.llamaContext || 8192), "-np", "1", "--no-webui", "-ngl", useCpu ? "0" : "99", "-t", threads,
    ];
  }

  // PCI_BUS_ID makes CUDA's device numbering match nvidia-smi's, which is what the menu shows.
  const childEnv = { ...process.env, PATH: `${path.dirname(ffmpeg)};${process.env.PATH}`, CUDA_DEVICE_ORDER: "PCI_BUS_ID" };
  if (model.engine === "llama" && kind === "cuda") childEnv.CUDA_VISIBLE_DEVICES = device.split(":")[1];
  child = spawn(exe, args, { cwd: path.dirname(exe), env: childEnv, stdio: "ignore", windowsHide: true });
  const me = child;
  child.once("exit", (code) => {
    log(`${model.engine} server exited (code=${code})`);
    if (child === me) { child = null; ready = false; activeDevice = null; activeEngine = null; }
  });
  return waitForReady(cfg.backendPort, useCpu ? 180000 : 120000, model.engine === "llama").then(() => {
    ready = true;
    activeDevice = device;
    activeEngine = model.engine;
    chatUnreliable = false;
    log(`${model.engine} server ready on ${device} (${model.id}) in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
  });
}

function ensureBackend() {
  if (child && ready) return Promise.resolve();
  if (starting) return starting;
  const device = effectiveDevice();
  const engine = currentModel().engine;
  starting = startBackend(device)
    .catch((err) => {
      if (device === "cpu" || !findEngine(engine, "cpu") || !findFfmpeg() || !fs.existsSync(modelPath())) throw err;
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
  log(`stopping recognition server (${reason})`);
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
  const model = currentModel();
  return {
    status: "ok",
    backend: child && ready ? "loaded" : starting ? "loading" : "unloaded",
    model: model.id,
    modelFile: cfg.modelFile,
    engine: model.engine,
    mmproj: model.mmproj,
    device: cfg.device,
    activeDevice: activeDevice || effectiveDevice(),
    devices: deviceList(),
    idleMinutes: cfg.idleMinutes,
    allowRemote: cfg.allowRemote,
    punctuation: cfg.punctuation !== false,
    convertSimplified: cfg.convertSimplified,
    vocabularyTerms: vocabulary.length,
    vocabularyFile: path.join(dataDir, "vocabulary.txt"),
    lexiconFile: path.join(dataDir, "taiwan-lexicon.txt"),
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
  if (url === "/control/convert") {
    persist({ convertSimplified: param("enabled") === "true" });
    log(`simplified->traditional conversion ${cfg.convertSimplified ? "on" : "off"}`);
    if (cfg.convertSimplified) loadScriptDicts();
    return send(200, status());
  }
  if (url === "/control/model" || url === "/control/device" || url === "/control/punctuation") {
    if (inFlight > 0) return send(409, { error: "transcription in progress" });
    if (url === "/control/punctuation") {
      persist({ punctuation: param("enabled") === "true" });
      log(`punctuation ${cfg.punctuation ? "on" : "off"}`);
    } else if (url === "/control/model") {
      const file = param("file");
      if (!/^[\w.\-]+\.(bin|gguf)$/i.test(file) || !fs.existsSync(path.join(modelsDir, file)) || !describeModel(file)) {
        return send(400, { error: "model file not found in models folder (or it is an mmproj file)" });
      }
      persist({ modelFile: file });
      log(`model switched to ${file}`);
      if (describeModel(file).engine === "llama" && cfg.convertSimplified) loadScriptDicts();
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

// ---- text post-processing ----
const CJK = "\\u3400-\\u9fff\\uf900-\\ufaff";

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

// -- punctuation from whisper segments --
// Breeze-ASR-25 emits next to no punctuation, but with timestamps on its segments break exactly at
// clause boundaries (measured: 8 segments for a text with 8 punctuation marks). So the marks are added
// here: a question mark after question endings, a full stop before topic-shift openers, after a long
// pause and at the very end, a comma everywhere else. Deliberately conservative - a wrong comma reads
// better than a wrong full stop.
const QUESTION_END = /(嗎|呢|是不是|有沒有|對不對|好不好|行不行|可不可以|能不能|要不要|會不會)$/;
const SENTENCE_OPENER = /^(另外|此外|對了|首先|其次|再來|最後|總之|接下來|順便|話說)/;
const HAS_END_MARK = /[，。？！、；：…,.?!;:]$/;
const LONG_PAUSE_SECONDS = 0.7;

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

// -- Simplified -> Traditional (Taiwan character forms) for models that write Simplified --
// OpenCC's s2tw: longest-match over STPhrases + STCharacters, then TWVariants. Deliberately NOT the
// "twp" phrase table OpenWhispr applies (數據→資料, 參數→引數, 擴展→擴充套件 ...) - that rewrites
// words the user actually said. The three dictionary files (Apache-2.0, ~1 MB) are fetched into
// data\opencc\ the first time they are needed. Text that already looks Traditional is left alone.
const OPENCC_FILES = ["STPhrases.txt", "STCharacters.txt", "TWVariants.txt"];
const OPENCC_BASE = "https://raw.githubusercontent.com/BYVoid/OpenCC/master/data/dictionary/";
let scriptDictState = "unloaded"; // unloaded | loading | ready | missing
let s2t = null; // { map, maxLen, simplifiedOnly }
let twVariants = null; // { map, maxLen }

function parseDict(text) {
  const map = new Map();
  let maxLen = 1;
  for (const line of text.split(/\r?\n/)) {
    if (!line || line.startsWith("#")) continue;
    const [key, values] = line.split("\t");
    if (!key || !values) continue;
    map.set(key, values.split(" ")[0]);
    if (key.length > maxLen) maxLen = key.length;
  }
  return { map, maxLen };
}

function convertWith(dict, text) {
  let out = "";
  for (let i = 0; i < text.length; ) {
    let hit = null;
    for (let len = Math.min(dict.maxLen, text.length - i); len >= 1; len--) {
      const key = text.substr(i, len);
      if (dict.map.has(key)) { hit = { len, value: dict.map.get(key) }; break; }
    }
    if (hit) { out += hit.value; i += hit.len; } else { out += text[i]; i++; }
  }
  return out;
}

function fetchFile(url, dest) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { "User-Agent": "whispr-gateway" } }, (res) => {
      if (res.statusCode !== 200) { res.resume(); return reject(new Error(`HTTP ${res.statusCode} for ${url}`)); }
      const out = fs.createWriteStream(dest + ".part");
      res.pipe(out);
      out.on("finish", () => { fs.renameSync(dest + ".part", dest); resolve(); });
      out.on("error", reject);
    }).on("error", reject);
  });
}

function loadScriptDicts() {
  if (scriptDictState === "ready" || scriptDictState === "loading") return;
  scriptDictState = "loading";
  const dir = path.join(dataDir, "opencc");
  fs.mkdirSync(dir, { recursive: true });
  const missing = OPENCC_FILES.filter((f) => !fs.existsSync(path.join(dir, f)));
  (missing.length ? Promise.all(missing.map((f) => fetchFile(OPENCC_BASE + f, path.join(dir, f)))).then(() => log(`downloaded OpenCC dictionaries: ${missing.join(", ")}`)) : Promise.resolve())
    .then(() => {
      const phrases = parseDict(fs.readFileSync(path.join(dir, "STPhrases.txt"), "utf8"));
      const chars = parseDict(fs.readFileSync(path.join(dir, "STCharacters.txt"), "utf8"));
      const merged = new Map([...chars.map, ...phrases.map]); // phrases win over single characters
      // Characters that only exist on the Simplified side tell us a text is Simplified at all.
      const traditionalSide = new Set();
      for (const v of chars.map.values()) for (const ch of v) traditionalSide.add(ch);
      const simplifiedOnly = new Set([...chars.map.keys()].filter((k) => !traditionalSide.has(k)));
      s2t = { map: merged, maxLen: Math.max(phrases.maxLen, chars.maxLen), simplifiedOnly };
      twVariants = parseDict(fs.readFileSync(path.join(dir, "TWVariants.txt"), "utf8"));
      scriptDictState = "ready";
      log(`OpenCC dictionaries ready (${merged.size} entries)`);
    })
    .catch((err) => { scriptDictState = "missing"; log(`OpenCC dictionaries unavailable: ${err.message}`); });
}

function looksSimplified(text) {
  if (!s2t) return false;
  let hits = 0;
  for (const ch of text) if (s2t.simplifiedOnly.has(ch) && ++hits >= 1) return true;
  return false;
}

function toTraditional(text) {
  if (!cfg.convertSimplified || scriptDictState !== "ready" || !looksSimplified(text)) return text;
  return convertWith(twVariants, convertWith(s2t, text));
}

// Qwen3-ASR replies "language Chinese<asr_text>..." (fine-tunes such as TEA-ASR drop the tag and
// leave just "language Chinese") - either way the prefix is model chatter, not transcript.
const stripAsrTag = (text) => String(text || "")
  .replace(/^\s*language\s+[A-Za-z]+(\s*<asr_text>)?\s*/i, "")
  .replace(/^[\s。，、；：！？,.;:!?]+/, ""); // TEA-ASR sometimes puts a stray mark where the tag was

// -- Taiwan lexicon --
// Qwen3-ASR models (TEA-ASR included) were trained mostly on Mainland text and write 網絡/軟件/服務器
// even when a Taiwanese speaker said 網路/軟體/伺服器. data\taiwan-lexicon.txt maps such words back; it is
// created with a short list of terms a Taiwanese speaker never says, and the user can edit or empty it.
// Deliberately tiny: this is the opposite of OpenWhispr's blanket "twp" rewriting.
const LEXICON_DEFAULT = `# 台灣用詞表：每行「模型寫法<TAB>改成」。只放台灣人不會說出口的詞，避免改到本來就想說的字。
# Taiwan lexicon applied to Qwen3-ASR family output. One "from<TAB>to" per line; # starts a comment.
網絡	網路
軟件	軟體
硬件	硬體
服務器	伺服器
視頻	影片
鼠標	滑鼠
內存	記憶體
打印	列印
光標	游標
默認	預設
屏幕	螢幕
`;
let lexicon = null; // { map, maxLen } or null
let lexiconMtime = 0;
function loadLexicon() {
  const file = path.join(dataDir, "taiwan-lexicon.txt");
  try {
    if (!fs.existsSync(file)) { fs.mkdirSync(dataDir, { recursive: true }); fs.writeFileSync(file, LEXICON_DEFAULT, "utf8"); }
    const mtime = fs.statSync(file).mtimeMs;
    if (lexicon && mtime === lexiconMtime) return;
    lexicon = parseDict(fs.readFileSync(file, "utf8"));
    lexiconMtime = mtime;
    log(`taiwan lexicon loaded (${lexicon.map.size} entries)`);
  } catch (err) { lexicon = null; log(`taiwan lexicon unavailable: ${err.message}`); }
}
const applyLexicon = (text) => { loadLexicon(); return lexicon && lexicon.map.size ? convertWith(lexicon, text) : text; };

// -- vocabulary hint --
// Qwen3-ASR was trained to take context, and it uses it: with "CUDA" in the hint a Taiwanese-accented
// "酷達" comes back as "CUDA" (measured). llama-server's transcription endpoint has no prompt field,
// so the gateway talks to the model through /v1/chat/completions with the instruction + the words in
// data\vocabulary.txt as the system message. This is the replacement for OpenWhispr's custom
// dictionary, which its self-hosted mode never sends.
const VOCABULARY_DEFAULT = `# 詞彙提示：一行一個詞（英文術語、人名、專有名詞）。模型辨識時會參考，講台灣腔英文也比較能寫回英文。
# Vocabulary hint for Qwen3-ASR family models: one term per line; # starts a comment. Edit freely.
CUDA
Vulkan
GPU
llama.cpp
OpenWhispr
API
GitHub
Python
Node.js
Claude
Ollama
ZeroTier
`;
let vocabulary = []; // terms
let vocabularyMtime = 0;
function loadVocabulary() {
  const file = path.join(dataDir, "vocabulary.txt");
  try {
    if (!fs.existsSync(file)) { fs.mkdirSync(dataDir, { recursive: true }); fs.writeFileSync(file, VOCABULARY_DEFAULT, "utf8"); }
    const mtime = fs.statSync(file).mtimeMs;
    if (mtime === vocabularyMtime) return;
    vocabulary = fs.readFileSync(file, "utf8").split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith("#"));
    vocabularyMtime = mtime;
    log(`vocabulary hint loaded (${vocabulary.length} terms)`);
  } catch (err) { vocabulary = []; log(`vocabulary hint unavailable: ${err.message}`); }
}
const LLAMA_INSTRUCTION_DEFAULT = "以下是台灣人講的中文，夾雜英文術語時請保留英文原文；數字與型號請用阿拉伯數字。";
function systemPrompt() {
  loadVocabulary();
  const inst = typeof cfg.llamaInstruction === "string" ? cfg.llamaInstruction.trim() : LLAMA_INSTRUCTION_DEFAULT;
  const words = vocabulary.length ? `常見詞彙：${vocabulary.join(", ")}` : "";
  return [inst, words].filter(Boolean).join("\n") || null;
}

// ---- multipart helpers ----
// Adds a multipart text field in front of the body unless the client already sent that field.
function injectField(body, boundary, name, value) {
  if (body.includes(`name="${name}"`)) return body;
  return Buffer.concat([Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="${name}"\r\n\r\n${value}\r\n`, "utf8"), body]);
}

// Minimal parser: returns { fields: {name: string}, file: { name, type, data } | null }.
function parseMultipart(body, contentType) {
  const m = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(contentType || "");
  if (!m) return null;
  const delim = Buffer.from(`--${m[1] || m[2]}`);
  const out = { fields: {}, file: null };
  let pos = body.indexOf(delim);
  while (pos !== -1) {
    const headStart = pos + delim.length + 2; // skip CRLF after the delimiter
    if (body.slice(pos + delim.length, pos + delim.length + 2).toString() === "--") break; // closing delimiter
    const headEnd = body.indexOf("\r\n\r\n", headStart);
    if (headEnd === -1) break;
    const headers = body.slice(headStart, headEnd).toString("utf8");
    const next = body.indexOf(delim, headEnd + 4);
    if (next === -1) break;
    const data = body.slice(headEnd + 4, next - 2); // strip the CRLF before the next delimiter
    const name = (/name="([^"]*)"/i.exec(headers) || [])[1];
    const filename = (/filename="([^"]*)"/i.exec(headers) || [])[1];
    const type = (/content-type:\s*([^\r\n]+)/i.exec(headers) || [])[1] || "";
    if (filename !== undefined || name === "file") out.file = { name: filename || "audio", type, data };
    else if (name) out.fields[name] = data.toString("utf8");
    pos = next;
  }
  return out;
}

function buildMultipart(fields, file) {
  const boundary = "----whisprgateway" + Date.now().toString(16) + Math.random().toString(16).slice(2);
  const parts = [];
  for (const [k, v] of Object.entries(fields)) parts.push(Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="${k}"\r\n\r\n${v}\r\n`, "utf8"));
  parts.push(Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="${file.name}"\r\nContent-Type: ${file.type}\r\n\r\n`, "utf8"), file.data, Buffer.from("\r\n"));
  parts.push(Buffer.from(`--${boundary}--\r\n`));
  return { body: Buffer.concat(parts), contentType: `multipart/form-data; boundary=${boundary}` };
}

// llama-server decodes wav only (anything else needs ffprobe on its PATH, which OpenWhispr does not
// ship), so the upload is transcoded here to 16 kHz mono wav with the same ffmpeg whisper-server uses.
function toWav(input) {
  return new Promise((resolve, reject) => {
    const ffmpeg = findFfmpeg();
    if (!ffmpeg) return reject(new Error("ffmpeg not found"));
    fs.mkdirSync(tmpDir, { recursive: true });
    const base = path.join(tmpDir, `up-${process.pid}-${Date.now()}-${Math.random().toString(16).slice(2)}`);
    fs.writeFileSync(base + ".in", input);
    execFile(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", base + ".in", "-ar", "16000", "-ac", "1", "-f", "wav", base + ".wav"],
      { windowsHide: true, timeout: 120000 }, (err, _stdout, stderr) => {
        let wav = null;
        try { wav = fs.readFileSync(base + ".wav"); } catch {}
        for (const ext of [".in", ".wav"]) { try { fs.unlinkSync(base + ext); } catch {} }
        if (err || !wav) return reject(new Error(`ffmpeg failed: ${String(stderr || err?.message || "").trim().slice(0, 200)}`));
        resolve(wav);
      });
  });
}

// Qwen3-ASR takes language *names*; OpenWhispr sends ISO codes.
const LANGUAGE_NAMES = { zh: "Chinese", en: "English", ja: "Japanese", ko: "Korean", yue: "Cantonese", de: "German", fr: "French", es: "Spanish" };
const languageName = (code) => (!code || code === "auto") ? null : (LANGUAGE_NAMES[String(code).split("-")[0].toLowerCase()] || code);

// ---- HTTP front ----
const MAX_BODY_BYTES = 512 * 1024 * 1024;

function postBackend(body, contentType, urlPath = "/v1/audio/transcriptions") {
  return new Promise((resolve, reject) => {
    const req = http.request(
      { host: "127.0.0.1", port: cfg.backendPort, path: urlPath, method: "POST",
        headers: { "Content-Type": contentType, "Content-Length": String(body.length) } },
      (up) => {
        const parts = [];
        up.on("data", (c) => parts.push(c));
        up.once("end", () => resolve({ statusCode: up.statusCode, type: up.headers["content-type"] || "application/json", raw: Buffer.concat(parts) }));
      }
    );
    req.once("error", reject);
    req.end(body);
  });
}

// whisper-server: OpenWhispr's self-hosted route sends no `prompt`, and whisper-server's --prompt argv
// is mangled by the Windows ANSI code page, so the script bias is added here as a UTF-8 multipart field.
// When punctuation is on, the backend is also asked for segments - unless the client chose its own
// response_format (then the reply is passed through untouched).
async function transcribeWhisper(body, contentType) {
  const m = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(contentType || "");
  const boundary = m ? m[1] || m[2] : null;
  const wantsSegments = !!boundary && cfg.punctuation !== false && !body.includes('name="response_format"');
  if (boundary && cfg.prompt) body = injectField(body, boundary, "prompt", cfg.prompt);
  if (wantsSegments) body = injectField(body, boundary, "response_format", "verbose_json");
  const reply = await postBackend(body, contentType);
  if (!wantsSegments) return reply;
  try {
    const data = JSON.parse(reply.raw.toString("utf8"));
    if (Array.isArray(data.segments)) {
      return { statusCode: reply.statusCode, type: "application/json; charset=utf-8", raw: Buffer.from(JSON.stringify({ text: punctuate(data.segments) }), "utf8") };
    }
  } catch {} // not JSON (backend error text): hand it over as it came
  return reply;
}

// llama-server (Qwen3-ASR family): wav in, model chatter and Simplified script out.
// Two ways to ask the model: the chat endpoint (takes the vocabulary hint; preferred) and the plain
// transcription endpoint (no hint). Fine-tunes such as TEA-ASR answer the chat endpoint with nothing
// but the language tag, so an empty chat reply switches this backend session to the plain endpoint.
let chatUnreliable = false;

async function llamaChat(wav) {
  const messages = [];
  const system = systemPrompt();
  if (system) messages.push({ role: "system", content: system });
  messages.push({ role: "user", content: [{ type: "input_audio", input_audio: { data: wav.toString("base64"), format: "wav" } }] });
  const body = Buffer.from(JSON.stringify({ messages, temperature: 0, max_tokens: cfg.llamaMaxTokens || 2048 }), "utf8");
  const reply = await postBackend(body, "application/json", "/v1/chat/completions");
  if (reply.statusCode !== 200) throw new Error(`chat endpoint HTTP ${reply.statusCode}`);
  const data = JSON.parse(reply.raw.toString("utf8"));
  const content = data?.choices?.[0]?.message?.content;
  if (typeof content !== "string") throw new Error("chat endpoint reply without content");
  return content;
}

async function llamaTranscriptions(wav, language) {
  const fields = { response_format: "json" };
  if (language) fields.language = language;
  const req = buildMultipart(fields, { name: "audio.wav", type: "audio/wav", data: wav });
  const reply = await postBackend(req.body, req.contentType);
  if (reply.statusCode !== 200) throw new Error(`transcription endpoint HTTP ${reply.statusCode}: ${reply.raw.toString("utf8").slice(0, 120)}`);
  const data = JSON.parse(reply.raw.toString("utf8"));
  if (typeof data.text !== "string") throw new Error("transcription endpoint reply without text");
  return data.text;
}

async function transcribeLlama(body, contentType) {
  const parsed = parseMultipart(body, contentType);
  if (!parsed || !parsed.file) throw new Error("multipart upload without a file field");
  const wav = /wav/i.test(parsed.file.type) || /\.wav$/i.test(parsed.file.name) ? parsed.file.data : await toWav(parsed.file.data);
  const language = languageName(parsed.fields.language || cfg.language);
  const substantial = wav.length > 32000; // more than about a second of audio should never transcribe to nothing
  let text = null;
  let via = "chat";
  if (cfg.llamaMode !== "transcriptions" && !chatUnreliable) {
    try { text = await llamaChat(wav); } catch (err) { log(`chat endpoint failed (${err.message}); using the transcription endpoint`); }
    if (text !== null && substantial && !stripAsrTag(text).trim()) {
      chatUnreliable = true;
      log("chat endpoint returned no transcript for this model; using the transcription endpoint until the next load");
      text = null;
    }
  }
  if (text === null) {
    via = "transcriptions";
    text = await llamaTranscriptions(wav, language);
    // TEA-ASR occasionally answers a fresh process's first request with nothing but the language tag.
    if (substantial && !stripAsrTag(text).trim()) { log("empty transcript; retrying once"); text = await llamaTranscriptions(wav, language); }
  }
  const out = tidy(applyLexicon(toTraditional(stripAsrTag(text))));
  return { statusCode: 200, type: "application/json; charset=utf-8", raw: Buffer.from(JSON.stringify({ text: out }), "utf8"), via };
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
    res.end(JSON.stringify({ object: "list", data: [{ id: currentModel().id, object: "model", owned_by: "local" }] }));
    return;
  }
  if (req.method === "GET") {
    res.writeHead(200, { "Content-Type": "application/json" });
    const s = status();
    res.end(JSON.stringify({ status: s.status, backend: s.backend, model: s.model, engine: s.engine }));
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
      const body = Buffer.concat(chunks);
      const contentType = req.headers["content-type"] || "";
      return activeEngine === "llama" ? transcribeLlama(body, contentType) : transcribeWhisper(body, contentType);
    })
    .then((reply) => {
      res.writeHead(reply.statusCode, { "Content-Type": reply.type, "Content-Length": reply.raw.length });
      res.end(reply.raw);
      done(`-> ${reply.statusCode}${reply.via ? ` (${reply.via})` : ""}`);
    })
    .catch((err) => {
      if (!res.headersSent) res.writeHead(503, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: { message: err.message } }));
      done(`failed (${err.message})`);
    });
}

for (const sig of ["SIGINT", "SIGTERM", "SIGHUP"]) process.on(sig, () => { stopBackend(sig); process.exit(0); });
process.on("exit", () => { try { child && child.kill(); } catch {} });

log(`gateway starting (model=${cfg.modelFile} engine=${currentModel().engine} device=${cfg.device}, idle unload after ${cfg.idleMinutes} min, localOnly=${localOnly})`);
refreshGpus();
refreshListeners();
if (currentModel().engine === "llama" && cfg.convertSimplified) loadScriptDicts();
loadVocabulary();
loadLexicon(); // both files exist from the first start on, so the tray's "edit" items always have something to open
setInterval(refreshListeners, 20000); // picks up ZeroTier coming up after logon
setInterval(refreshGpus, 120000); // a dGPU can appear / disappear (laptop eco mode)
setTimeout(() => { for (const p of problems()) log(`problem: ${p}`); }, 10000); // after the first GPU scan has answered
// "Never unload" (idleMinutes 0) means the user wants the model resident: load it shortly after start so
// the first dictation after a reboot does not pay the cold load (measured 23 s right after a power cut).
if (!(cfg.idleMinutes > 0)) {
  setTimeout(() => {
    if (child || starting) return;
    log("preloading model (automatic unload is off)");
    ensureBackend().then(armIdleTimer).catch((err) => log(`preload failed: ${err.message}`));
  }, 30000);
}
