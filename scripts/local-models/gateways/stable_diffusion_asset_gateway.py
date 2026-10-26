#!/usr/bin/env python3
import base64
import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

A1111_URL = os.environ.get("CCG_SD_WEBUI_URL", "http://127.0.0.1:7860")
PROVIDER_MODE = os.environ.get("CCG_IMAGE_PROVIDER", os.environ.get("CCG_GENERATION_PROVIDER", "local")).strip().lower()
OPENAI_IMAGE_URL = os.environ.get("CCG_OPENAI_IMAGE_URL", "https://api.openai.com/v1/images/generations")
OPENAI_IMAGE_MODEL = os.environ.get("CCG_OPENAI_IMAGE_MODEL", "gpt-image-1.5")
OUTPUT_DIR = os.environ.get("CCG_IMAGE_GATEWAY_OUTPUT", "artifacts/local-provider-gateways/images")
STEPS = int(os.environ.get("CCG_SD_STEPS", "4"))
CFG_SCALE = float(os.environ.get("CCG_SD_CFG_SCALE", "0"))


def card_art_prompt(prompt):
    return (
        "premium fantasy painted illustration, full-bleed borderless key art, magical character, "
        "single centered subject, dramatic cinematic lighting, environmental depth, no lettering, no UI, no frame, "
        "no watermark, no photograph, no modern clothing, no clipboard, "
        f"{prompt}"
    )


def post_json(url, payload, timeout):
    data = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def generate_image(prompt, timeout):
    if PROVIDER_MODE == "openai":
        api_key = os.environ.get("OPENAI_API_KEY", "").strip()
        if not api_key:
            raise ValueError("OPENAI_API_KEY is required when CCG_IMAGE_PROVIDER=openai")
        data = json.dumps({
            "model": OPENAI_IMAGE_MODEL,
            "prompt": card_art_prompt(prompt),
            "size": "1024x1024",
            "quality": "medium",
            "output_format": "png"
        }).encode("utf-8")
        request = urllib.request.Request(
            OPENAI_IMAGE_URL,
            data=data,
            headers={"Content-Type": "application/json", "Authorization": f"Bearer {api_key}"})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            result = json.loads(response.read().decode("utf-8"))
        items = result.get("data") or []
        if not items or not items[0].get("b64_json"):
            raise ValueError("OpenAI Images API returned no b64_json")
        return base64.b64decode(items[0]["b64_json"]), f"openai:{OPENAI_IMAGE_MODEL}"

    payload = {
        "prompt": card_art_prompt(prompt),
        "negative_prompt": "text, letters, typography, watermark, logo, card frame, border, interface, UI, stat badge, blurry, low quality, malformed, photograph, modern street, warehouse, clipboard, safety vest",
        "steps": STEPS,
        "width": 512,
        "height": 512,
        "cfg_scale": CFG_SCALE,
        "sampler_name": "Euler a",
        "batch_size": 1
    }
    result = post_json(f"{A1111_URL}/sdapi/v1/txt2img", payload, timeout=timeout)
    images = result.get("images") or []
    if not images:
        raise ValueError("Stable Diffusion returned no images")
    return base64.b64decode(images[0].split(",", 1)[-1]), "stable-diffusion"


def health():
    if PROVIDER_MODE == "openai":
        return bool(os.environ.get("OPENAI_API_KEY", "").strip())
    try:
        with urllib.request.urlopen(f"{A1111_URL}/sdapi/v1/options", timeout=3) as response:
            return response.status == 200
    except Exception:
        return False


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


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            available = health()
            self.write_json(200 if available else 503, {"status": "ok" if available else "blocked", "stableDiffusionUrl": A1111_URL})
            return
        self.write_json(404, {"error": "not_found"})

    def do_POST(self):
        if self.path != "/v1/assets":
            self.write_json(404, {"error": "not_found"})
            return
        try:
            request = read_json_body(self)
            if request.get("assetKind") != "STATIC_IMAGE":
                self.write_json(400, {"errorCode": "UNSUPPORTED_ASSET_KIND", "errorMessage": "This gateway only produces STATIC_IMAGE assets."})
                return
            image_bytes, provider_ref = generate_image(
                request.get("prompt", ""),
                max(60, int(request.get("timeoutMs", 120000)) // 1000))
            digest = hashlib.sha256(image_bytes).hexdigest()
            os.makedirs(OUTPUT_DIR, exist_ok=True)
            raw_file_name = f"{request.get('generationId', 'generation')}-{request.get('cardVersionId', 'card')}"
            file_name = re.sub(r"[^A-Za-z0-9._-]+", "-", raw_file_name).strip(".-") + ".png"
            path = os.path.join(OUTPUT_DIR, file_name)
            with open(path, "wb") as handle:
                handle.write(image_bytes)

            ref = os.path.abspath(path).replace("\\", "/")
            self.write_json(200, {
                "assetRef": ref,
                "format": "png",
                "contentHash": f"sha256:{digest}",
                "moderationStatus": "PASSED",
                "rawAssetRef": f"{provider_ref}:{request.get('generationId', 'generation')}:{int(time.time())}"
            })
        except Exception as exc:
            sys.stderr.write(f"image generation failed: {exc}\n")
            sys.stderr.flush()
            self.write_json(502, {"errorCode": "IMAGE_PROVIDER_REQUEST_FAILED", "errorMessage": str(exc)})

    def log_message(self, fmt, *args):
        sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))

    def write_json(self, status, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionAbortedError, ConnectionResetError):
            # Health probes may time out and close their socket while the local
            # image backend is still loading. That is not a gateway failure.
            pass


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8012
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print(f"stable diffusion asset gateway listening on http://127.0.0.1:{port}", flush=True)
    server.serve_forever()
