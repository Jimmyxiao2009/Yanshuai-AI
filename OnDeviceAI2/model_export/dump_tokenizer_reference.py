"""P3 对齐基准：HF tokenizer 对多类文本的 encode 结果 → JSON，供 C# BpeTokenizer 对齐。"""
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(HERE, "qwen3_5_0_8b")
OUT = os.path.join(HERE, "reference_tokenizer.json")

SENTENCES = [
    "中国的首都是北京，法国的首都是",
    "Hello, world! How are you today?",
    "混合中英文 mixed text 123 测试。",
    "1234567890",
    "  leading spaces and\ttabs\nnewlines\r\n\r\nhere",
    "emoji 😀🔥 和特殊符号 ©®™ £€¥",
    "def fib(n):\n    return n if n < 2 else fib(n-1) + fib(n-2)",
    "I'm sure it's Bob's — isn't it? We'll see...",
    "你好！请用一句话介绍一下你自己。",
    "<think>\n\n</think>\n\n好的，我来回答。",
]

# 聊天模板样例（特殊 token 用 id 直插，中间文本段落分别 encode）
CHAT_SEGMENTS = [
    "system\n你是一个有帮助的助手。",
    "user\n你好，介绍一下你自己。",
    "assistant\n<think>\n\n</think>\n\n",
]


def main():
    from transformers import AutoTokenizer
    tok = AutoTokenizer.from_pretrained(MODEL_DIR)

    out = {"sentences": [], "chat_segments": []}
    for s in SENTENCES:
        ids = tok.encode(s, add_special_tokens=False)
        out["sentences"].append({"text": s, "ids": ids})
        print(f"{len(ids):4d}  {s[:40]!r}")
    for s in CHAT_SEGMENTS:
        ids = tok.encode(s, add_special_tokens=False)
        out["chat_segments"].append({"text": s, "ids": ids})

    json.dump(out, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("写出", OUT)


if __name__ == "__main__":
    main()
