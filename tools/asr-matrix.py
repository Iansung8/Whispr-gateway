"""Compare how a llama-server ASR backend handles clips under different prompting.

Usage:  python asr-matrix.py http://127.0.0.1:8791 clip1.wav [clip2.wav ...]
Runs each clip through /v1/audio/transcriptions and /v1/chat/completions with a few
system-prompt variants and prints the raw model text (tags included) with timings.
"""
import base64, json, sys, time, urllib.request

VOCAB = "CUDA, Vulkan, llama.cpp, GPU, RTX 5080, OpenWhispr, API, localhost, Python, GitHub"
VARIANTS = {
    "chat/no hint": None,
    "chat/inst": "以下是台灣人講的中文，夾雜英文術語時請保留英文原文；數字與型號請用阿拉伯數字。",
    "chat/inst+vocab": "以下是台灣人講的中文，夾雜英文術語時請保留英文原文；數字與型號請用阿拉伯數字。\n常見詞彙：" + VOCAB,
    "chat/vocab only": "常見詞彙：" + VOCAB,
    "chat/en inst+vocab": "Transcribe Taiwanese Mandarin. Keep English technical terms in English (Latin letters); write numbers and model names with digits. Vocabulary: " + VOCAB,
}


def transcriptions(base, clip):
    boundary = "----matrix"
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"language\"\r\n\r\nChinese\r\n"
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"response_format\"\r\n\r\njson\r\n"
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.wav\"\r\nContent-Type: audio/wav\r\n\r\n").encode() \
        + open(clip, "rb").read() + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(base + "/v1/audio/transcriptions", data=body, headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    return json.load(urllib.request.urlopen(req, timeout=300))["text"]


def chat(base, clip, system):
    wav = base64.b64encode(open(clip, "rb").read()).decode()
    msgs = ([{"role": "system", "content": system}] if system else []) + \
        [{"role": "user", "content": [{"type": "input_audio", "input_audio": {"data": wav, "format": "wav"}}]}]
    req = urllib.request.Request(base + "/v1/chat/completions", data=json.dumps({"messages": msgs, "temperature": 0, "max_tokens": 512}).encode(),
                                 headers={"Content-Type": "application/json"})
    return json.load(urllib.request.urlopen(req, timeout=300))["choices"][0]["message"]["content"]


base = sys.argv[1].rstrip("/")
for clip in sys.argv[2:]:
    print(f"== {clip}")
    t = time.time(); print(f"  {'transcriptions':<20} {time.time() - t:5.2f}s  {transcriptions(base, clip)}")
    for name, system in VARIANTS.items():
        t = time.time(); out = chat(base, clip, system); print(f"  {name:<20} {time.time() - t:5.2f}s  {out}")
