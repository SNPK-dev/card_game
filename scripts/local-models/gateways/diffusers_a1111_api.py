#!/usr/bin/env python3
import base64
import hashlib
import io
import os
import sys
import threading
from typing import Any

import torch
import uvicorn
from diffusers import AutoPipelineForText2Image
from fastapi import FastAPI
from pydantic import BaseModel

MODEL_ID = os.environ.get("CCG_DIFFUSERS_MODEL", "stabilityai/sd-turbo")
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
DTYPE = torch.float16 if DEVICE == "cuda" else torch.float32

app = FastAPI(title="Local Diffusers A1111-compatible API")
pipe = None
pipeline_lock = threading.Lock()


class Txt2ImgRequest(BaseModel):
    prompt: str
    negative_prompt: str | None = None
    steps: int | None = 4
    width: int | None = 512
    height: int | None = 512
    cfg_scale: float | None = 0.0
    sampler_name: str | None = None
    batch_size: int | None = 1


def get_pipe():
    global pipe
    if pipe is None:
        loaded = AutoPipelineForText2Image.from_pretrained(
            MODEL_ID,
            torch_dtype=DTYPE,
            variant="fp16" if DEVICE == "cuda" else None,
        )
        pipe = loaded.to(DEVICE)
        if DEVICE == "cuda":
            pipe.enable_attention_slicing()
    return pipe


@app.get("/sdapi/v1/options")
def options() -> dict[str, Any]:
    return {
        "sd_model_checkpoint": MODEL_ID,
        "device": DEVICE,
        "api": "diffusers-a1111-compatible",
    }


@app.post("/sdapi/v1/txt2img")
def txt2img(request: Txt2ImgRequest) -> dict[str, Any]:
    steps = max(1, min(int(request.steps or 4), 20))
    width = max(256, min(int(request.width or 512), 768))
    height = max(256, min(int(request.height or 512), 768))
    batch_size = max(1, min(int(request.batch_size or 1), 1))

    seed = int(hashlib.sha256(request.prompt.encode("utf-8")).hexdigest()[:8], 16)
    generator = torch.Generator(device=DEVICE).manual_seed(seed)
    # Diffusers pipelines mutate scheduler state during inference. FastAPI runs
    # sync handlers in a thread pool, so concurrent requests must not share the
    # pipeline while that state is changing.
    with pipeline_lock:
        pipeline = get_pipe()
        with torch.inference_mode():
            result = pipeline(
                prompt=request.prompt,
                negative_prompt=request.negative_prompt,
                num_inference_steps=steps,
                guidance_scale=float(request.cfg_scale if request.cfg_scale is not None else 0.0),
                width=width,
                height=height,
                num_images_per_prompt=batch_size,
                generator=generator,
            )

    images = []
    for image in result.images:
        buffer = io.BytesIO()
        image.save(buffer, format="PNG")
        images.append(base64.b64encode(buffer.getvalue()).decode("ascii"))

    return {
        "images": images,
        "parameters": request.model_dump(),
        "info": f"model={MODEL_ID};device={DEVICE};steps={steps}",
    }


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 7860
    get_pipe()
    uvicorn.run(app, host="127.0.0.1", port=port, log_level="info")
