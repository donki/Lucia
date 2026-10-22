# sOC Lucia: entrenamiento LoRA de una IA con los ejemplos del usuario (MIT).
#
# Lo lanza la aplicacion con el Python de su entorno aislado:
#     python lucia_train.py <config.json>
# y lee la salida linea a linea: cada linea que empieza por «@lucia » es un JSON con el estado
# (etapa, progreso, error, fin). Lo demas es registro y va a logs\finetune.log.
#
# Pasos: cargar la IA base (safetensors de Hugging Face), en 4 bits si hay grafica NVIDIA; poner
# encima un LoRA (PEFT); entrenar solo las respuestas del asistente; guardar el adaptador y
# convertirlo a GGUF con el convert_lora_to_gguf.py de llama.cpp, para cargarlo con --lora.

import json
import math
import os
import subprocess
import sys
import time
import traceback


def emit(event, **data):
    data["event"] = event
    print("@lucia " + json.dumps(data, ensure_ascii=False), flush=True)


def load_examples(path):
    examples = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                examples.append(json.loads(line)["messages"])
    return examples


def tokenize(tokenizer, messages, max_len):
    """Tokens de la conversacion y etiquetas: solo cuenta la ultima respuesta del asistente."""
    prompt = tokenizer.apply_chat_template(messages[:-1], tokenize=False, add_generation_prompt=True)
    full = tokenizer.apply_chat_template(messages, tokenize=False)
    if not full.startswith(prompt):
        # Plantillas que escriben distinto el turno abierto y el cerrado: se pega la respuesta al prompt.
        full = prompt + messages[-1]["content"] + (tokenizer.eos_token or "")
    prompt_ids = tokenizer(prompt, add_special_tokens=False)["input_ids"]
    full_ids = tokenizer(full, add_special_tokens=False)["input_ids"]
    if len(full_ids) > max_len:
        # Se recorta por delante (la historia vieja), nunca la respuesta.
        cut = len(full_ids) - max_len
        full_ids = full_ids[cut:]
        prompt_len = max(0, len(prompt_ids) - cut)
    else:
        prompt_len = len(prompt_ids)
    labels = [-100] * prompt_len + full_ids[prompt_len:]
    return {"input_ids": full_ids, "labels": labels}


def lora_targets(model):
    """Las proyecciones habituales de atencion y MLP que existan en este modelo (fuera la vision)."""
    wanted = {"q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"}
    found = set()
    for name, _ in model.named_modules():
        if "vision" in name or "visual" in name:
            continue
        last = name.rsplit(".", 1)[-1]
        if last in wanted:
            found.add(last)
    return sorted(found) or "all-linear"


def train(cfg):
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, Trainer, TrainingArguments, TrainerCallback

    emit("stage", stage="loading")
    cuda = torch.cuda.is_available()
    use_4bit = cfg.get("use_4bit", True) and cuda
    dtype = torch.bfloat16 if cuda and torch.cuda.is_bf16_supported() else (torch.float16 if cuda else torch.float32)
    tokenizer = AutoTokenizer.from_pretrained(cfg["base_dir"])
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    kwargs = {"dtype": dtype}
    if use_4bit:
        from transformers import BitsAndBytesConfig
        kwargs["quantization_config"] = BitsAndBytesConfig(
            load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_compute_dtype=dtype, bnb_4bit_use_double_quant=True)
    if cuda:
        kwargs["device_map"] = {"": 0}
    model = AutoModelForCausalLM.from_pretrained(cfg["base_dir"], **kwargs)
    model.config.use_cache = False

    from peft import LoraConfig, get_peft_model, prepare_model_for_kbit_training
    if use_4bit:
        model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True)
    else:
        model.gradient_checkpointing_enable()
        model.enable_input_require_grads()
    lora = LoraConfig(r=cfg["rank"], lora_alpha=cfg["alpha"], lora_dropout=0.05, bias="none",
                      task_type="CAUSAL_LM", target_modules=lora_targets(model))
    model = get_peft_model(model, lora)

    emit("stage", stage="preparing")
    examples = [tokenize(tokenizer, m, cfg["max_len"]) for m in load_examples(cfg["dataset"])]
    examples = [e for e in examples if any(l != -100 for l in e["labels"])]
    if not examples:
        raise ValueError("no_examples")

    def collate(batch):
        width = max(len(e["input_ids"]) for e in batch)
        pad = tokenizer.pad_token_id
        ids = [e["input_ids"] + [pad] * (width - len(e["input_ids"])) for e in batch]
        labels = [e["labels"] + [-100] * (width - len(e["labels"])) for e in batch]
        mask = [[1] * len(e["input_ids"]) + [0] * (width - len(e["input_ids"])) for e in batch]
        return {"input_ids": torch.tensor(ids), "labels": torch.tensor(labels), "attention_mask": torch.tensor(mask)}

    started = time.time()

    class Progress(TrainerCallback):
        def on_log(self, args, state, control, logs=None, **kw):
            if logs and "loss" in logs:
                emit("progress", step=state.global_step, total=state.max_steps, loss=round(float(logs["loss"]), 4),
                     epoch=round(float(state.epoch or 0), 2), elapsed=round(time.time() - started))

        def on_step_end(self, args, state, control, **kw):
            if os.path.exists(cfg["stop_file"]):
                control.should_training_stop = True

    batch = cfg.get("batch", 1)
    accum = cfg.get("grad_accum", 4)
    steps_per_epoch = max(1, math.ceil(len(examples) / (batch * accum)))
    args = TrainingArguments(
        output_dir=os.path.join(cfg["output_dir"], "checkpoints"),
        num_train_epochs=cfg["epochs"],
        per_device_train_batch_size=batch,
        gradient_accumulation_steps=accum,
        learning_rate=cfg["learning_rate"],
        lr_scheduler_type="cosine",
        warmup_steps=min(10, steps_per_epoch),
        logging_steps=1,
        save_strategy="no",
        report_to=[],
        bf16=dtype == torch.bfloat16,
        fp16=dtype == torch.float16,
        optim="paged_adamw_8bit" if use_4bit else "adamw_torch",
        gradient_checkpointing=True,
        seed=cfg.get("seed", 42),
        dataloader_num_workers=0,
        remove_unused_columns=False,
    )
    emit("stage", stage="training", examples=len(examples), total=steps_per_epoch * cfg["epochs"])
    trainer = Trainer(model=model, args=args, train_dataset=examples, data_collator=collate, callbacks=[Progress()])
    trainer.train()
    if os.path.exists(cfg["stop_file"]):
        emit("stopped")
        return

    peak_mb = torch.cuda.max_memory_allocated() // (1024 * 1024) if cuda else 0
    emit("stage", stage="saving")
    adapter_dir = os.path.join(cfg["output_dir"], "adapter")
    model.save_pretrained(adapter_dir)
    del trainer, model
    if cuda:
        torch.cuda.empty_cache()

    emit("stage", stage="converting")
    gguf_path = os.path.join(cfg["output_dir"], "adapter.gguf")
    convert = os.path.join(cfg["llamacpp_dir"], "convert_lora_to_gguf.py")
    result = subprocess.run([sys.executable, convert, "--base", cfg["base_dir"], "--outfile", gguf_path, "--outtype", "f16", adapter_dir],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(result.stdout)
    print(result.stderr)
    if result.returncode != 0 or not os.path.exists(gguf_path):
        raise RuntimeError("convert_failed")
    emit("done", adapter=gguf_path, examples=len(examples), seconds=round(time.time() - started), peak_vram_mb=peak_mb)


def convert_model(cfg):
    """Un modelo de Hugging Face (safetensors) a GGUF F16; la aplicacion lo cuantiza despues con llama-quantize."""
    emit("stage", stage="converting")
    convert = os.path.join(cfg["llamacpp_dir"], "convert_hf_to_gguf.py")
    result = subprocess.run([sys.executable, convert, cfg["base_dir"], "--outfile", cfg["outfile"], "--outtype", cfg.get("outtype", "f16")],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(result.stdout)
    print(result.stderr)
    if result.returncode != 0 or not os.path.exists(cfg["outfile"]):
        tail = (result.stderr or "").strip().splitlines()[-1:] or [""]
        raise RuntimeError("convert_failed: " + tail[0])
    emit("done", outfile=cfg["outfile"])


def check():
    """Que hay en este entorno: la aplicacion lo enseña antes de entrenar."""
    import torch
    info = {"torch": torch.__version__, "cuda": torch.cuda.is_available()}
    if info["cuda"]:
        props = torch.cuda.get_device_properties(0)
        info["gpu"] = props.name
        info["vram_mb"] = props.total_memory // (1024 * 1024)
        info["bf16"] = torch.cuda.is_bf16_supported()
    emit("check", **info)


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--check":
        check()
        return
    with open(sys.argv[1], encoding="utf-8") as f:
        cfg = json.load(f)
    try:
        if cfg.get("mode") == "convert":
            convert_model(cfg)
        else:
            train(cfg)
    except Exception as ex:  # noqa: BLE001 - cualquier fallo se le cuenta a la aplicacion
        traceback.print_exc()
        text = str(ex)
        kind = "oom" if "out of memory" in text.lower() or type(ex).__name__ == "OutOfMemoryError" else "error"
        emit("error", kind=kind, message=text[-600:])
        sys.exit(1)


if __name__ == "__main__":
    main()
