#!/usr/bin/env python3
import base64
import hashlib
import json
import math
import os
import struct
import sys
import time
import urllib.request
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO

from PIL import Image, ImageEnhance, ImageFilter

A1111_URL = os.environ.get("CCG_SD_WEBUI_URL", "http://127.0.0.1:7860")
OUTPUT_DIR = os.environ.get("CCG_ANIMATION_AUDIO_OUTPUT", "artifacts/local-provider-gateways/animation-audio")


def post_json(url, payload, timeout):
    data = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def read_json_body(handler):
    if handler.headers.get("Transfer-Encoding", "").lower() == "chunked":
        body = bytearray()
        while True:
            size_line = handler.rfile.readline().strip()
            if not size_line:
                continue
            size = int(size_line.split(b";", 1)[0], 16)
            if size == 0:
                handler.rfile.readline()
                break
            body.extend(handler.rfile.read(size))
            handler.rfile.readline()
        return json.loads(body.decode("utf-8"))

    length = int(handler.headers.get("Content-Length", "0"))
    return json.loads(handler.rfile.read(length).decode("utf-8"))


def card_art_prompt(prompt):
    return (
        "fantasy collectible card idle animation key art, magical minion character, "
        "single centered subject, ornate game card style, painted illustration, no lettering, no UI, "
        f"{prompt}"
    )


def generate_base_image(prompt, timeout):
    payload = {
        "prompt": card_art_prompt(prompt),
        "negative_prompt": "text, watermark, logo, blurry, low quality, malformed, photograph, modern street",
        "steps": 4,
        "width": 512,
        "height": 512,
        "cfg_scale": 0,
        "batch_size": 1,
    }
    result = post_json(f"{A1111_URL}/sdapi/v1/txt2img", payload, timeout)
    images = result.get("images") or []
    if not images:
        raise RuntimeError("A1111-compatible API returned no image for animation.")
    image_bytes = base64.b64decode(images[0].split(",", 1)[-1])
    return Image.open(BytesIO(image_bytes)).convert("RGBA")


def generate_idle_gif(request):
    base = generate_base_image(request.get("prompt", ""), timeout=max(60, int(request.get("timeoutMs", 120000)) // 1000))
    frames = []
    for index in range(12):
        phase = math.sin(index / 12 * math.tau)
        scale = 1.0 + phase * 0.015
        brightness = 1.0 + phase * 0.08
        blur = max(0.0, phase) * 0.35
        size = (int(base.width * scale), int(base.height * scale))
        frame = base.resize(size, Image.Resampling.BICUBIC)
        left = (frame.width - base.width) // 2
        top = (frame.height - base.height) // 2
        frame = frame.crop((left, top, left + base.width, top + base.height))
        frame = ImageEnhance.Brightness(frame).enhance(brightness)
        if blur > 0:
            glow = frame.filter(ImageFilter.GaussianBlur(blur))
            frame = Image.blend(frame, glow, 0.12)
        frames.append(frame.convert("P", palette=Image.Palette.ADAPTIVE))

    buffer = BytesIO()
    frames[0].save(buffer, format="GIF", save_all=True, append_images=frames[1:], duration=90, loop=0)
    return buffer.getvalue(), "gif"


def generate_audio(request):
    sample_rate = 44100
    duration_seconds = 1.2
    frequencies = [523.25, 659.25, 783.99, 1046.50]
    total = int(sample_rate * duration_seconds)
    buffer = BytesIO()
    with wave.open(buffer, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        for sample in range(total):
            t = sample / sample_rate
            envelope = min(1.0, t / 0.08) * max(0.0, 1.0 - (t / duration_seconds) ** 1.7)
            value = 0.0
            for offset, frequency in enumerate(frequencies):
                value += math.sin(math.tau * frequency * t) * (0.22 / (offset + 1))
            shimmer = math.sin(math.tau * 12.0 * t) * 0.05
            pcm = int(max(-1.0, min(1.0, (value + shimmer) * envelope)) * 32767)
            wav.writeframes(struct.pack("<h", pcm))
    return buffer.getvalue(), "wav"


def write_asset(request, payload, fmt):
    digest = hashlib.sha256(payload).hexdigest()
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    extension = "gif" if fmt == "gif" else "wav"
    file_name = f"{request.get('generationId', 'generation')}-{request.get('cardVersionId', 'card')}.{extension}"
    path = os.path.join(OUTPUT_DIR, file_name)
    with open(path, "wb") as handle:
        handle.write(payload)
    return {
        "assetRef": path.replace("\\", "/"),
        "format": fmt,
        "contentHash": f"sha256:{digest}",
        "moderationStatus": "PASSED",
        "rawAssetRef": f"local-animation-audio:{request.get('generationId', 'generation')}:{int(time.time())}",
    }


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            self.write_json(200, {"status": "ok", "stableDiffusionUrl": A1111_URL, "assetKinds": ["IDLE_ANIMATION", "AUDIO"]})
            return
        self.write_json(404, {"error": "not_found"})

    def do_POST(self):
        if self.path != "/v1/assets":
            self.write_json(404, {"error": "not_found"})
            return
        try:
            request = read_json_body(self)
            asset_kind = request.get("assetKind")
            if asset_kind == "IDLE_ANIMATION":
                payload, fmt = generate_idle_gif(request)
            elif asset_kind == "AUDIO":
                payload, fmt = generate_audio(request)
            else:
                self.write_json(400, {"errorCode": "UNSUPPORTED_ASSET_KIND", "errorMessage": "Supported asset kinds: IDLE_ANIMATION, AUDIO."})
                return
            self.write_json(200, write_asset(request, payload, fmt))
        except Exception as exc:
            self.write_json(502, {"errorCode": "LOCAL_ASSET_REQUEST_FAILED", "errorMessage": str(exc)})

    def log_message(self, fmt, *args):
        sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))

    def write_json(self, status, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8013
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print(f"local animation/audio gateway listening on http://127.0.0.1:{port}", flush=True)
    server.serve_forever()
