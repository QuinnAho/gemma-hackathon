from __future__ import annotations

import json
import os
import sys
import time
import traceback

import torch
from huggingface_hub import file_download as hf_file_download
from transformers import AutoModelForCausalLM, AutoProcessor


DEFAULT_MODEL_IDENTIFIER = "google/gemma-4-E2B-it"


class DesktopGemmaBridge:
    def __init__(self, model_identifier: str) -> None:
        self.model_identifier = model_identifier or DEFAULT_MODEL_IDENTIFIER
        self.cache_root = configure_hugging_face_cache()
        self.processor = None
        self.model = None
        self.device_name = "cpu"
        self.model_dtype = torch.float32

    def start(self) -> None:
        disable_hugging_face_symlink_probing()
        self.device_name, self.model_dtype = resolve_model_device()
        self.processor = AutoProcessor.from_pretrained(
            self.model_identifier,
            cache_dir=self.cache_root or None,
        )
        self.model = AutoModelForCausalLM.from_pretrained(
            self.model_identifier,
            torch_dtype=self.model_dtype,
            device_map=None,
            low_cpu_mem_usage=False,
            cache_dir=self.cache_root or None,
        )
        self.model.to(self.device_name)
        self.model.eval()
        configure_generation_tokens(self.processor, self.model)

    def handle_complete(self, request: dict) -> dict:
        started_at = time.time()

        try:
            messages = read_messages(request.get("messages_json"))
            tool_definitions = read_tools(request.get("tools_json"))
            options = read_options(request.get("options_json"))

            prompt_messages = build_prompt_messages(messages, tool_definitions)
            prompt_text = self.processor.apply_chat_template(
                prompt_messages,
                tokenize=False,
                add_generation_prompt=True,
                enable_thinking=False,
            )

            inputs = self.processor(text=prompt_text, return_tensors="pt")
            inputs = move_inputs_to_device(inputs, self.device_name)
            input_length = inputs["input_ids"].shape[-1]

            generation_started_at = time.time()
            generation_options = {
                "max_new_tokens": options["max_new_tokens"],
                "do_sample": options["do_sample"],
            }
            if options["do_sample"]:
                generation_options["temperature"] = options["temperature"]

            outputs = self.model.generate(
                **inputs,
                **generation_options,
            )
            generation_finished_at = time.time()

            raw_response = self.processor.decode(
                outputs[0][input_length:],
                skip_special_tokens=False,
            )

            parsed_content = extract_assistant_content(self.processor, raw_response)
            response_text, function_calls = normalize_model_output(parsed_content, tool_definitions)

            return {
                "success": True,
                "error": None,
                "cloud_handoff": False,
                "response": response_text,
                "function_calls": function_calls,
                "confidence": 1.0,
                "time_to_first_token_ms": round((generation_started_at - started_at) * 1000.0, 2),
                "total_time_ms": round((generation_finished_at - started_at) * 1000.0, 2),
                "backend": "desktop_gemma_bridge",
                "model_identifier": self.model_identifier,
                "device": self.device_name,
            }
        except Exception as error:
            return {
                "success": False,
                "error": f"{type(error).__name__}: {error}",
                "cloud_handoff": False,
                "response": "",
                "function_calls": [],
                "backend": "desktop_gemma_bridge",
                "traceback": traceback.format_exc(),
            }


def read_messages(messages_json: str) -> list:
    if not messages_json:
        return []

    parsed = json.loads(messages_json)
    return parsed if isinstance(parsed, list) else []


def read_tools(tools_json: str) -> list:
    if not tools_json:
        return []

    parsed = json.loads(tools_json)
    return parsed if isinstance(parsed, list) else []


def read_options(options_json: str) -> dict:
    options = {
        "temperature": 0.0,
        "max_new_tokens": 192,
        "do_sample": False,
    }

    if not options_json:
        return options

    try:
        parsed = json.loads(options_json)
    except Exception:
        return options

    temperature = parsed.get("temperature", 0.0)
    try:
        temperature = float(temperature)
    except Exception:
        temperature = 0.0

    max_new_tokens = (
        parsed.get("max_new_tokens")
        or parsed.get("max_tokens")
        or options["max_new_tokens"]
    )

    try:
        max_new_tokens = int(max_new_tokens)
    except Exception:
        max_new_tokens = options["max_new_tokens"]

    max_new_tokens = max(16, min(max_new_tokens, 512))

    options["temperature"] = max(0.0, temperature)
    options["max_new_tokens"] = max_new_tokens
    options["do_sample"] = options["temperature"] > 0.01
    return options


def configure_hugging_face_cache() -> str:
    configured_root = os.environ.get("GEMMA_HACKATHON_HF_HOME", "").strip()
    if not configured_root:
        return ""

    cache_root = Path(configured_root).expanduser()
    cache_root.mkdir(parents=True, exist_ok=True)

    hub_cache = cache_root / "hub"
    transformers_cache = cache_root / "transformers"
    assets_cache = cache_root / "assets"
    hub_cache.mkdir(parents=True, exist_ok=True)
    transformers_cache.mkdir(parents=True, exist_ok=True)
    assets_cache.mkdir(parents=True, exist_ok=True)

    os.environ["HF_HOME"] = str(cache_root)
    os.environ["HUGGINGFACE_HUB_CACHE"] = str(hub_cache)
    os.environ["TRANSFORMERS_CACHE"] = str(transformers_cache)
    os.environ["HF_ASSETS_CACHE"] = str(assets_cache)

    return str(cache_root)


def disable_hugging_face_symlink_probing() -> None:
    hf_file_download.are_symlinks_supported = lambda _cache_dir: False


def resolve_model_device() -> tuple[str, torch.dtype]:
    if torch.cuda.is_available():
        return "cuda", torch.float16

    if hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
        return "mps", torch.float16

    return "cpu", torch.float32


def configure_generation_tokens(processor, model) -> None:
    tokenizer = getattr(processor, "tokenizer", None)
    if tokenizer is None:
        return

    if tokenizer.pad_token_id is None and tokenizer.eos_token_id is not None:
        tokenizer.pad_token_id = tokenizer.eos_token_id

    if getattr(model.generation_config, "pad_token_id", None) is None:
        model.generation_config.pad_token_id = tokenizer.pad_token_id

    if getattr(model.generation_config, "eos_token_id", None) is None and tokenizer.eos_token_id is not None:
        model.generation_config.eos_token_id = tokenizer.eos_token_id


def move_inputs_to_device(inputs, device_name: str):
    if hasattr(inputs, "to"):
        return inputs.to(device_name)

    return {
        key: value.to(device_name) if hasattr(value, "to") else value
        for key, value in inputs.items()
    }


def build_prompt_messages(messages: list, tool_definitions: list) -> list:
    prompt_messages = []

    if messages:
        prompt_messages.extend(messages)

    prompt_messages.insert(
        0,
        {
            "role": "system",
            "content": build_response_contract(tool_definitions),
        },
    )

    return prompt_messages


def build_response_contract(tool_definitions: list) -> str:
    instruction_lines = [
        "You are the desktop Gemma validation backend for a simulation-aware assistant.",
        "Return a single JSON object and nothing else.",
        "The JSON object must have exactly these top-level fields:",
        '{"response":"assistant text","function_calls":[{"name":"tool_name","arguments":{}}]}',
        'If no tool is needed, set "function_calls" to an empty array.',
        'If one or more tools are needed before the assistant can finish, "response" may be an empty string.',
        'Each "arguments" value must be a JSON object that matches the requested tool schema.',
        'When tools are available, prefer precise tool calls over vague prose.',
    ]

    if tool_definitions:
        instruction_lines.append("Available tools:")
        instruction_lines.append(json.dumps(tool_definitions, ensure_ascii=True))
    else:
        instruction_lines.append("There are no tools available for this turn.")

    return "\n".join(instruction_lines)


def extract_assistant_content(processor, raw_response: str) -> str:
    try:
        parsed = processor.parse_response(raw_response)
    except Exception:
        parsed = None

    if isinstance(parsed, dict):
        content = parsed.get("content")
        if isinstance(content, str) and content.strip():
            return content.strip()

    if isinstance(parsed, list):
        for item in parsed:
            if isinstance(item, dict):
                content = item.get("content")
                if isinstance(content, str) and content.strip():
                    return content.strip()

    return (raw_response or "").strip()


def normalize_model_output(content: str, tool_definitions: list) -> tuple[str, list]:
    parsed_object = try_parse_embedded_json_object(content)
    if isinstance(parsed_object, dict):
        response_text = parsed_object.get("response", "")
        function_calls = normalize_function_calls(parsed_object.get("function_calls"))
        return safe_string(response_text), function_calls

    if tool_definitions:
        return safe_string(content), []

    return safe_string(content), []


def try_parse_embedded_json_object(content: str):
    if not content:
        return None

    direct = try_parse_json(content)
    if isinstance(direct, dict):
        return direct

    start_index = content.find("{")
    while start_index >= 0:
        extracted = extract_balanced_json_object(content, start_index)
        if extracted:
            parsed = try_parse_json(extracted)
            if isinstance(parsed, dict):
                return parsed

        start_index = content.find("{", start_index + 1)

    return None


def try_parse_json(value: str):
    try:
        return json.loads(value)
    except Exception:
        return None


def extract_balanced_json_object(content: str, start_index: int) -> str | None:
    brace_depth = 0
    in_string = False
    escape_next = False

    for index in range(start_index, len(content)):
        character = content[index]

        if in_string:
            if escape_next:
                escape_next = False
                continue

            if character == "\\":
                escape_next = True
                continue

            if character == '"':
                in_string = False
                continue

            continue

        if character == '"':
            in_string = True
            continue

        if character == "{":
            brace_depth += 1
            continue

        if character == "}":
            brace_depth -= 1
            if brace_depth == 0:
                return content[start_index : index + 1]

    return None


def normalize_function_calls(raw_function_calls) -> list:
    if not isinstance(raw_function_calls, list):
        return []

    normalized_calls = []
    for raw_call in raw_function_calls:
        if not isinstance(raw_call, dict):
            continue

        name = safe_string(raw_call.get("name"))
        arguments = raw_call.get("arguments")
        if not isinstance(arguments, dict):
            arguments = {}

        if not name:
            continue

        normalized_calls.append(
            {
                "name": name,
                "arguments": arguments,
            }
        )

    return normalized_calls


def safe_string(value) -> str:
    return value if isinstance(value, str) else ""


def emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=True) + "\n")
    sys.stdout.flush()


def main() -> int:
    model_identifier = DEFAULT_MODEL_IDENTIFIER
    if len(sys.argv) > 1 and sys.argv[1].strip():
        model_identifier = sys.argv[1].strip()

    bridge = DesktopGemmaBridge(model_identifier)

    try:
        bridge.start()
        emit(
            {
                "ready": True,
                "error": None,
                "backend": "desktop_gemma_bridge",
                "model_identifier": bridge.model_identifier,
                "device": bridge.device_name,
            }
        )
    except Exception as error:
        emit(
            {
                "ready": False,
                "error": f"{type(error).__name__}: {error}",
                "backend": "desktop_gemma_bridge",
                "traceback": traceback.format_exc(),
            }
        )
        return 1

    for raw_line in sys.stdin:
        request_line = (raw_line or "").strip()
        if not request_line:
            continue

        try:
            request = json.loads(request_line)
        except Exception as error:
            emit(
                {
                    "success": False,
                    "error": f"Invalid request JSON: {error}",
                    "cloud_handoff": False,
                    "response": "",
                    "function_calls": [],
                    "backend": "desktop_gemma_bridge",
                }
            )
            continue

        command = request.get("command", "")

        if command == "complete":
            emit(bridge.handle_complete(request))
            continue

        if command == "reset":
            emit({"success": True, "error": None})
            continue

        if command == "shutdown":
            emit({"success": True, "error": None})
            break

        emit(
            {
                "success": False,
                "error": f"Unsupported command: {command}",
                "cloud_handoff": False,
                "response": "",
                "function_calls": [],
                "backend": "desktop_gemma_bridge",
            }
        )

    return 0


if __name__ == "__main__":
    sys.exit(main())
