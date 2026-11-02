"""Optional report summary from an LLM. Off unless asked for. Only finding titles and details are sent: no frames, no code.

Configure with environment variables:
  PLAYGOBLIN_LLM_PROVIDER   openai (any OpenAI-compatible endpoint, including local ones) or anthropic
  PLAYGOBLIN_LLM_API_KEY    key (not needed for most local servers)
  PLAYGOBLIN_LLM_MODEL      model name
  PLAYGOBLIN_LLM_BASE_URL   default https://api.openai.com/v1 or https://api.anthropic.com
"""
from __future__ import annotations

import json
import os
import urllib.request

PROMPT = ("You are a QA lead. Below are findings from an automated play-through of a Unity game. Write a summary of at "
          "most 5 sentences for the developer: what is most urgent and why. Mention only what is in the findings; do not "
          "invent causes. Findings:\n\n")


def summarize(findings: list[dict], env=os.environ, timeout: float = 60.0) -> str:
    provider = env.get("PLAYGOBLIN_LLM_PROVIDER", "").lower()
    model = env.get("PLAYGOBLIN_LLM_MODEL", "")
    if provider not in ("openai", "anthropic") or not model:
        raise RuntimeError("Set PLAYGOBLIN_LLM_PROVIDER (openai|anthropic) and PLAYGOBLIN_LLM_MODEL to use --summary.")
    if not findings:
        return "No findings."
    text = PROMPT + "\n".join(f"- [{f['severity']}] {f['title']} ({f['runs_seen']}/{f['runs_total']} runs): {f.get('detail', '')[:300]}"
                              for f in findings[:30])
    key = env.get("PLAYGOBLIN_LLM_API_KEY", "")
    if provider == "openai":
        url = env.get("PLAYGOBLIN_LLM_BASE_URL", "https://api.openai.com/v1").rstrip("/") + "/chat/completions"
        headers = {"Authorization": f"Bearer {key}"} if key else {}
        body = {"model": model, "messages": [{"role": "user", "content": text}]}
    else:
        url = env.get("PLAYGOBLIN_LLM_BASE_URL", "https://api.anthropic.com").rstrip("/") + "/v1/messages"
        headers = {"x-api-key": key, "anthropic-version": "2023-06-01"}
        body = {"model": model, "max_tokens": 400, "messages": [{"role": "user", "content": text}]}
    req = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST",
                                 headers={"Content-Type": "application/json", **headers})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode())
    if provider == "openai":
        return data["choices"][0]["message"]["content"].strip()
    return "".join(b.get("text", "") for b in data["content"]).strip()
