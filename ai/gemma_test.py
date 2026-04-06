from transformers import AutoProcessor, AutoModelForCausalLM
import torch
import sys

MODEL_ID = "google/gemma-4-E2B-it"

def load_model():
    print("Loading processor...")
    processor = AutoProcessor.from_pretrained(MODEL_ID)

    print("Loading model...")
    try:
        model = AutoModelForCausalLM.from_pretrained(
            MODEL_ID,
            dtype="auto",
            device_map="auto"
        )
        print("Model loaded with device_map='auto'")
        return processor, model
    except Exception as e:
        print(f"Auto device load failed: {e}")
        print("Falling back to CPU float32 load...")
        model = AutoModelForCausalLM.from_pretrained(
            MODEL_ID,
            dtype=torch.float32,
            device_map="cpu"
        )
        print("Model loaded on CPU")
        return processor, model

def main():
    try:
        processor, model = load_model()

        messages = [
            {"role": "system", "content": "You are a helpful assistant."},
            {"role": "user", "content": "Respond with exactly: GEMMA_WORKS"},
        ]

        print("Building prompt...")
        text = processor.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
            enable_thinking=False
        )

        print("Tokenizing input...")
        inputs = processor(text=text, return_tensors="pt").to(model.device)
        input_len = inputs["input_ids"].shape[-1]

        print("Generating response...")
        outputs = model.generate(
            **inputs,
            max_new_tokens=32,
            do_sample=False
        )

        response = processor.decode(
            outputs[0][input_len:],
            skip_special_tokens=False
        )

        print("\nRAW RESPONSE:")
        print(response)

        print("\nPARSED RESPONSE:")
        parsed = processor.parse_response(response)
        print(parsed)

        print("\nSUCCESS: Gemma local test completed.")
        return 0

    except Exception as e:
        print(f"\nERROR: {e}")
        print(
            "\nCommon causes:\n"
            "- Hugging Face access not approved for Gemma\n"
            "- huggingface-cli login not completed\n"
            "- insufficient RAM/VRAM\n"
            "- missing Python dependencies\n"
        )
        return 1

if __name__ == "__main__":
    sys.exit(main())
