English | [繁體中文](README.zh-TW.md)

# WhisprGateway

**An on-demand speech-to-text gateway for OpenWhispr.** It lets [OpenWhispr](https://github.com/OpenWhispr/openwhispr) dictate with a local model of your choice, either the Qwen3-ASR family (GGUF) or a whisper.cpp model. The model is loaded when you dictate and unloaded after 15 idle minutes (adjustable), so it does not sit in VRAM all day. From the tray you pick the model and the graphics card (or the CPU); optionally the gateway steps aside while a game needs the VRAM; other computers on your LAN or VPN can use it too. The interface is in English and Traditional Chinese.

Unofficial project, not affiliated with OpenWhispr, Alibaba Qwen or MediaTek. Developed and tested on Windows 11, OpenWhispr 1.9.1 and Node 26.7, with an NVIDIA RTX 5080 and with a laptop that has an RTX 5070 Ti Laptop GPU next to Intel integrated graphics. AMD and Intel Arc cards should work through Vulkan but have not been tested.

## Why

In OpenWhispr 1.9.1 the local mode offers only its six built-in Whisper models. A model stays resident on the GPU from the moment the app starts, and other computers cannot use it. This gateway fills those gaps without changing OpenWhispr: switch its transcription mode to "Self-Hosted" and point it here.

```
OpenWhispr (mode = Self-Hosted; this PC or another one)
   └─ POST http://<this PC>:8790/v1/audio/transcriptions
        └─ whisper-gateway.js (Node, always running, next to no resources)
             └─ starts the recognition server (127.0.0.1:8791) + model on demand, stops it when idle
                  *.gguf → llama.cpp llama-server (Qwen3-ASR / TEA-ASR)
                  *.bin  → whisper.cpp whisper-server (Breeze-ASR-25 / Whisper)
WhisprGateway.exe = tray icon: supervises the gateway, manual control
```

## Quick start

The shortest path, with the recommended model on any GPU through Vulkan:

1. Install **Node.js 20 LTS or later** from <https://nodejs.org>.
2. Download `WhisprGateway-vX.Y.Z.zip` from **Releases** and unzip it into a folder you can write to, for example `%LOCALAPPDATA%\WhisprGateway`. Not `Program Files`: the app keeps its config and log next to the exe.
3. Download llama.cpp's Vulkan build, `llama-bNNNN-bin-win-vulkan-x64.zip` (b11178 or later), from <https://github.com/ggml-org/llama.cpp/releases> and unzip it so that `engine\llama-vulkan\llama-server.exe` exists.
4. Open PowerShell in the `models` folder and run the first two `curl.exe` lines from [models/README.md](models/README.md) (the model and its audio encoder, 2.5 GB together).
5. ffmpeg comes with OpenWhispr; without OpenWhispr on this PC, run `winget install Gyan.FFmpeg`.
6. Run `WhisprGateway.exe`. The exe is not signed, so SmartScreen may ask first: "More info" → "Run anyway" (or build it yourself, see "Build from source"). A round icon with the character 語 appears in the tray; on Windows 11 it may be hidden behind the ^ arrow.
7. Click the icon → "OpenWhispr settings…" and enter in OpenWhispr:
   - Settings → Speech-to-Text → **Self-Hosted** → Endpoint URL `http://127.0.0.1:8790/v1` (leave API key and model name empty).
   - Preferences → Transcription language → **Auto**. If you dictate Chinese, also set Chinese script (dictation) → **Keep as transcribed**; "Chinese (Traditional)" there makes OpenWhispr rewrite your vocabulary with OpenCC's Taiwan phrases table (details in [docs/openwhispr-notes.md](docs/openwhispr-notes.md)).
8. Dictate. The first request loads the model (a few seconds); right after installing, the very first one can take 20 seconds or more while the graphics driver compiles its shaders.

To start with Windows, tick "Start at sign-in" in the menu. The language follows the Windows display language; "語言 / Language" in the menu switches it.

After these steps the folder looks like this:

```
WhisprGateway\
  WhisprGateway.exe, whisper-gateway.js, gateway.config.default.json, README.md …
  gateway.config.json, gateway.log            created on the first run
  engine\llama-vulkan\llama-server.exe        + the other files of that zip
  models\Qwen3-ASR-1.7B-Q8_0.gguf
  models\mmproj-Qwen3-ASR-1.7B-Q8_0.gguf
  data\                                       created on the first run (vocabulary, dictionaries)
```

## Models

| Model | Engine | Size | Notes |
|---|---|---|---|
| **Qwen3-ASR-1.7B** (recommended) | llama.cpp | Q8 2.17 GB or Q4_K_M 1.28 GB + mmproj 0.36 GB | 30 languages, detects the language by itself, punctuates; handles Taiwanese accents and mixed Chinese-English speech without fine-tuning. Chinese comes out in Simplified characters, which the gateway converts to Taiwan Traditional (characters only, no vocabulary changes) |
| Qwen3-ASR-0.6B | llama.cpp | 0.80 GB + mmproj 0.21 GB | Fastest on the CPU, about twice the errors of the 1.7B |
| TEA-ASR-1.1 | llama.cpp | Q6_K 1.42 GB + mmproj 0.36 GB | Qwen3-ASR-1.7B fine-tuned for Taiwanese Mandarin; writes Traditional characters and Taiwanese wording |
| Breeze-ASR-25 | whisper.cpp | q8_0 1.66 GB | MediaTek's Taiwanese Mandarin fine-tune of Whisper; the gateway adds punctuation from pauses; needs only OpenWhispr's engine |

GGUF (`*.gguf`) is llama.cpp's model format and ggml (`*.bin`) is whisper.cpp's. A Qwen3-ASR model needs its **mmproj** file, the audio encoder, next to it. Q8, Q6_K and Q4_K_M are quantization levels: the higher the number, the larger the file and the closer to the original accuracy. Download commands and sha256 sums are in [models/README.md](models/README.md); a measured comparison of these and other models is in [docs/asr-model-comparison.md](docs/asr-model-comparison.md) (Traditional Chinese, with an English summary). To change models, put the files into `models\` and pick the model from the tray's "Model" menu.

## Dependencies in detail

Nothing large is bundled: the app itself is about 140 KB. At start it looks for each dependency in the order below; paths are relative to the folder of `WhisprGateway.exe`.

| Dependency | Needs | Looked up in | Where to get it |
|---|---|---|---|
| **Node.js** | 20 or later | `runtime\node.exe` → PATH | <https://nodejs.org> |
| **llama.cpp** (GGUF models) | `llama-server`, b11178 or later | `engine\llama-cuda\` (if present) → `engine\llama-vulkan\` → OpenWhispr's `%APPDATA%\open-whispr\bin\llama-vulkan\` → `engine\llama-cpu\` → the CPU build bundled with OpenWhispr | The Vulkan build `llama-bNNNN-bin-win-vulkan-x64.zip` runs on any GPU vendor. For CUDA on NVIDIA, unzip `llama-bNNNN-bin-win-cuda-12.4-x64.zip` and `cudart-llama-bin-win-cuda-12.4-x64.zip` into `engine\llama-cuda\`. With only OpenWhispr installed it still works, on the CPU and slowly |
| **whisper.cpp** (ggml models) | `whisper-server-win32-x64-cuda.exe` / `-vulkan.exe` / `whisper-server-win32-x64.exe` | GPU builds: `engine\whisper-cuda\` or `engine\whisper-vulkan\` → OpenWhispr's `%APPDATA%\open-whispr\bin\whisper-cuda\` or `whisper-vulkan\`. CPU build: `engine\whisper-cpu\` → OpenWhispr's install folder | OpenWhispr → Settings → Speech-to-Text → Local → "Enable GPU" installs the CUDA build on NVIDIA and the Vulkan build elsewhere; the gateway borrows it. Or get it from <https://github.com/OpenWhispr/whisper.cpp/releases> |
| **ffmpeg** | 4.0 or later with opus decoding | `engine\ffmpeg\ffmpeg.exe` → PATH → the ffmpeg-static bundled with OpenWhispr | Comes with OpenWhispr; or `winget install Gyan.FFmpeg` |
| **Models** | see above | `models\` | [models/README.md](models/README.md) |
| OpenCC dictionaries (Simplified → Traditional) | 3 text files, about 1 MB | `data\opencc\` | Downloaded from OpenCC's GitHub the first time they are needed |

> When the gateway borrows OpenWhispr's engines, removing the GPU engine in OpenWhispr or uninstalling OpenWhispr makes the gateway fall back to the CPU or stop recognizing.

### Serving other computers

With "Allow other computers (LAN / VPN)" ticked, the gateway binds every private address of this PC and admits only clients from those same subnets; without it, it listens on `127.0.0.1` only. On the other computer, enter `http://<address>:8790/v1` in OpenWhispr.

- The first time, Windows Firewall asks about **Node.js**; allow **private networks** only.
- **There is no API key.** Anyone on the same subnet can use the gateway: everyone on the same café Wi-Fi, or everyone on your Tailscale or ZeroTier network. Leave this off on networks you do not trust, never forward the port to the internet, and use `remoteInterfaces` to limit it to particular adapters.

## Tray menu

| Item | What it does |
|---|---|
| Load / Unload model now | Manual control over whether the model holds resources |
| Unload when idle | 5 / 15 (default) / 30 / 60 minutes / never; with "never" the model is loaded about 30 s after the gateway starts |
| Model | The `*.bin` and `*.gguf` files in `models\` (mmproj files are paired automatically) |
| Device | Each NVIDIA GPU (CUDA), Vulkan choosing automatically, Vulkan on one particular card, CPU only. See "Choosing the graphics card" |
| Yield the GPU to other programs | On/off, and the "VRAM usage, whitelist and thresholds" window. See "Yielding the GPU"; off by default |
| Add punctuation from pauses (whisper models) | See "Punctuation"; on by default |
| Convert Simplified output to Taiwan Traditional (Qwen3-ASR family) | OpenCC s2tw: character conversion only, no vocabulary changes; on by default |
| Vocabulary (Qwen3-ASR family) | Opens `data\vocabulary.txt` (vocabulary hints) or `data\taiwan-lexicon.txt` (replacement table); saved changes apply at once |
| Allow other computers | See above; off by default |
| OpenWhispr settings… | Shows the endpoint URLs with copy buttons |
| Start at sign-in | Creates the Task Scheduler task `WhisprGateway` (starts 20 s after sign-in, checks every 5 minutes that it is still running) |
| 語言 / Language | Automatic (Windows display language), 繁體中文, English |
| Open log / Restart gateway / Quit | `gateway.log` in Notepad / restart the Node gateway / stop everything and free the GPU |

Icon colors: green = model on the GPU, blue = dictation runs on the CPU, orange = loading, grey = not loaded, red = gateway not responding.

## Troubleshooting

When something is missing, the tray menu shows it in red under the status line, one problem at a time; clicking it opens this README. Fix it and the next one, if any, appears.

| Message or symptom | What to do |
|---|---|
| Model not found: models\… | Put that file into `models\`, or pick a model you have under "Model" |
| The audio encoder for … is missing | Download the model's `mmproj-*.gguf` from the same Hugging Face page into `models\` |
| llama.cpp not found / No recognition engine found | Quick start step 3 (or install OpenWhispr and enable its GPU engine for whisper models) |
| No GPU engine (…); using the CPU for now | Dictation works but slowly; install the Vulkan build as in step 3 |
| The GPU engine failed to start; using the CPU for now | Update the graphics driver, check `gateway.log` ("Open log"), then choose the device again or "Restart gateway" |
| The chosen card (…) is not there; choosing automatically for now | The card picked under "Device" is gone (an external GPU unplugged, say); pick another one |
| ffmpeg not found | Quick start step 5 |
| The Simplified-to-Traditional dictionaries could not be downloaded | Needs internet once; or turn off "Convert Simplified output…" |
| Cannot read VRAM use per program (typeperf) | Yielding then only reacts to VRAM running out. The GPU counters need Windows 10 version 1709 or later; if they are broken, `lodctr /r` in an administrator prompt rebuilds Windows' performance counters |
| Red icon, "the gateway does not respond" | Node.js missing (a notification says so), or port 8790 taken by another program; see "Open log" |
| A "Cannot create the config file" message at start | The folder is not writable; move the app out of `Program Files` |

## Performance

Measured on an RTX 5080. "Load" is the time from the first request to a ready model; "Per sentence" is with the model already loaded.

| Model | Load | Per sentence | VRAM |
|---|---|---|---|
| Qwen3-ASR-1.7B Q8_0 | about 3 s | 0.3–0.4 s | about 3 GB |
| Qwen3-ASR-0.6B Q8_0 | about 2 s | 0.25 s | about 1.9 GB |
| TEA-ASR-1.1 Q6_K | about 3 s | 0.3–0.8 s | about 2.8 GB |
| Breeze-ASR-25 q8_0 | about 2 s | 0.75 s | about 2.3 GB |

On the CPU (i5-13600K, 10 threads), Qwen3-ASR-1.7B Q4_K_M takes about 2.8 s for a 17-second sentence. The gateway itself (tray + Node) uses about 60 MB of RAM.

## Features

### Yielding the GPU

When a game, LM Studio or anything else needs the VRAM, the gateway can unload the model and dictate on the CPU until that program has exited, then return to the GPU. It is off by default; turn it on under "Yield the GPU to other programs".

Every 10 seconds the gateway reads Windows' GPU memory counters, which exist for every GPU vendor. It only looks at the card the model uses, so a game on another card does not count. It yields when either condition holds:

- One program uses more VRAM than the threshold in two samples in a row, about 10–20 seconds. Default 2 GB.
- While the model is on the GPU, free VRAM stays below the threshold for 10 seconds. Default 1 GB; 0 turns this condition off.

Both thresholds can be set in GB or as a share of the card (for example 15%), which is easier to get right across cards of different sizes. While the GPU is yielded, dictation runs on the CPU with a smaller file (`vramGuardCpuModel`): by default `Qwen3-ASR-1.7B.Q4_K_M.gguf` if present, else `Qwen3-ASR-0.6B-Q8_0.gguf`, else the configured model. After the program exits, the gateway waits 60 seconds (adjustable) before it uses the GPU again.

Tray → "Yield the GPU to other programs" → "VRAM usage, whitelist and thresholds…" opens a window that lists, every 3 seconds, each program on that card with its VRAM use and share, marking which programs are above the threshold and which one caused the yield. The thresholds and the waiting time are set there as well.

**Whitelist**: tick a program in the list and it may use any amount of VRAM without the gateway giving way, which suits programs that are always open, such as OBS or a browser. Programs that are not running can be added by name. The whitelist starts empty; what goes on it is up to you. Windows' own desktop processes (dwm, csrss) never count, because their counters grow with the number and resolution of your monitors.

A model on an integrated GPU never yields: it lives in system RAM and does not compete with games for VRAM. The gateway treats a card as integrated when Windows reports less than 1 GB of dedicated memory for it, which fits Intel integrated graphics; AMD APUs that reserve more than that are treated like discrete cards.

### Choosing the graphics card

"Device" lists every card the Vulkan engine sees, integrated graphics included. With two discrete cards llama.cpp splits the model across both by default; choosing one keeps it on that card. You can also put the model on the integrated GPU and leave the discrete card entirely to games.

Vulkan numbers the cards according to their current state: on the test laptop the Intel and NVIDIA cards swapped numbers within minutes. The config therefore stores the card's name (`vulkan:<name>`), and the gateway looks up its current number right before each start. Two cards with the same name cannot be told apart; the first one is used. This applies to the Qwen3-ASR family (llama.cpp); for whisper models, whisper.cpp picks the Vulkan card itself.

### Dictating in English or other languages

The Qwen3-ASR family detects the language of each recording; in a test on 2026-10-08, Qwen3-ASR-1.7B transcribed an English recording correctly with the default settings. For English-only use, set these in `gateway.config.json` (choose "Quit" first): `"language": "auto"` (the default `zh` is used whenever OpenWhispr sends no language, which matters for whisper models and TEA-ASR), `"llamaInstruction": ""` (the default instruction tells Qwen3-ASR to expect Taiwanese Chinese) and `"prompt": ""` (a Chinese hint for whisper models). If you want Simplified Chinese, turn off "Convert Simplified output…". The rest of the clean-up only touches Chinese text, except that letters spelled out one by one are joined into one word ("G P U" → "GPU").

### Vocabulary hints (Qwen3-ASR family)

The gateway sends a vocabulary list to the model as its system prompt, so that technical terms come back in their usual spelling (CUDA rather than a phonetic rendering in Chinese).

- 35 terms are built in: AI tools, GPU and developer words such as VRAM, NVIDIA, repo, GitHub, Claude (the list is `BUILTIN_VOCABULARY` in `whisper-gateway.js`; `builtinVocabulary: false` turns it off).
- Put the words you say often and the model gets wrong into `data\vocabulary.txt`, one per line.
- Leave out short words the model already gets right (GPU, AI, API): listed words attract similar-sounding ones. At most 100 terms in total.

### Acronyms and numbers (Qwen3-ASR family, Chinese)

- Acronyms said letter by letter are joined: `G P U` → `GPU`, `N V I D I A` → `NVIDIA`.
- Chinese numerals become digits when they are clearly numbers, for example `RTX 五零八零` → `RTX 5080`, `十六 GB` → `16 GB`, `百分之二十` → `20%`; counting words such as `一個` stay as they are.

Single words can be fixed with the replacement table `data\taiwan-lexicon.txt` (one "model output, Tab, replacement" per line); by default it maps a few mainland terms to their Taiwanese equivalents.

### Punctuation (whisper models)

Breeze-ASR-25 hardly outputs punctuation, but its segments end at clause boundaries, so the gateway adds it: a question particle at the end gets "？"; a pause of 0.7 s or more, a next segment that opens with a connective such as "另外" (also) or "最後" (finally), or the last segment gets "。"; everything else gets "，". The Qwen3-ASR family punctuates by itself.

## Config file `gateway.config.json`

| Key | Meaning |
|---|---|
| `port` / `backendPort` | 8790 for clients / 8791 internal |
| `listen` | `"auto"` = 127.0.0.1 plus (with remote access on) every private IPv4 address of this PC; or an array of addresses |
| `remoteInterfaces` | Regular expression on adapter names, empty = all (e.g. `zerotier|tailscale`) |
| `allowCidrs` | `"auto"` = the subnets of those adapters; or an array of CIDRs |
| `device` | `gpu:N` (CUDA, NVIDIA card N), `vulkan` (automatic), `vulkan:<card name>`, `cpu`. The default `gpu:0` uses the CUDA build on the first NVIDIA card when it is installed, otherwise Vulkan, otherwise the CPU |
| `allowRemote`, `idleMinutes`, `modelFile`, `punctuation`, `convertSimplified`, `uiLanguage`, `vramGuard` | Written by the tray menu |
| `vramGuardProcess`, `vramGuardMinFree` | The two yield thresholds: a number = MiB, a string such as `"15%"` = share of the card (defaults 2048, 1024) |
| `vramGuardResumeSeconds` | Seconds to wait after the program exits before using the GPU again (default 60) |
| `vramGuardWhitelist` | Program names (without .exe) that never cause a yield, written by the VRAM window; empty by default |
| `vramGuardIgnore` | Advanced: a regular expression of programs to ignore, in addition to the whitelist |
| `vramGuardCpuModel` | Model file for the CPU while the GPU is yielded; `""` = keep the configured one |
| `builtinVocabulary` | `false` = do not send the built-in terms |
| `language` | Recognition language when OpenWhispr sends none (`zh`, `en`, `auto`…) |
| `prompt` | Prompt for whisper models only |
| `llamaInstruction` | Instruction for the Qwen3-ASR family (first line of the system prompt) |
| `llamaMode` | `chat` (default, sends the vocabulary hints) or `transcriptions` |
| `llamaContext`, `llamaMaxTokens` | llama-server context length (default 8192) and output limit per request (default 2048) |
| `modelsDir`, `engineDir`, `dataDir`, `tmpDir` | Relative paths are relative to this folder |

While it runs, only the gateway writes this file. To edit it by hand, choose "Quit" first; with "Start at sign-in" on, Task Scheduler starts the app again within 5 minutes, so finish the edit before then or turn that option off. `/control/*` (the tray's local control API) accepts calls from this PC only. `gateway.log` records times, client IPs and durations, never what was said.

## Build from source

- `powershell -ExecutionPolicy Bypass -File build.ps1` builds `WhisprGateway.exe` from `src\GatewayTray.cs` with the C# compiler that ships with Windows (.NET Framework 4, no SDK); `whisper-gateway.js` runs as is.
- If the app runs under "Start at sign-in", run `schtasks /Change /TN WhisprGateway /DISABLE` and quit it before rebuilding, then `/ENABLE` and `/Run` afterwards.
- `WhisprGateway.exe --render-guard file.png --sample --lang en` draws the VRAM window into an image for layout checks.
- `tools\` (in the source only, not in the release zip) holds test and monitoring scripts; `bash tools/watch-guard.sh` shows the yield decisions live and needs Git Bash, Python and curl.

## Other notes

- Self-hosted mode has no live transcription preview, and OpenWhispr's custom dictionary is not sent (use the vocabulary hints instead).
- Windows only; "start with Windows" means at sign-in.

## Thanks

[OpenWhispr](https://github.com/OpenWhispr/openwhispr), [llama.cpp](https://github.com/ggml-org/llama.cpp), [whisper.cpp](https://github.com/ggml-org/whisper.cpp), [Qwen3-ASR](https://github.com/QwenLM/Qwen3-ASR), [TEA-ASR by JacobLinCool](https://huggingface.co/JacobLinCool/TEA-ASR-1.1), [GGUF quantizations by mradermacher](https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF), [MediaTek Research Breeze-ASR-25](https://huggingface.co/MediaTek-Research/Breeze-ASR-25), [ggml conversion by shdennlin](https://huggingface.co/shdennlin/breeze-asr-25-ggml), [OpenCC](https://github.com/BYVoid/OpenCC).
