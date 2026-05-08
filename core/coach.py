"""Claude coach -- elite crew chief over the radio.

Sends rich race context (sector deltas, recent inputs, theoretical best gain)
and gets back a tight, spoken-style call. Two modes:
- coach_call(): for auto-fired events
- coach_question(): for push-to-talk driver questions

Routing:
- If a Companion install token is present (ChiefCompanion.exe), proxy through
  chiefracing.com so the user never needs their own API key.
- Otherwise, fall back to direct Anthropic calls using config.ANTHROPIC_API_KEY
  (developer / advanced user mode).
"""
import logging
import requests

import config

log = logging.getLogger("chief.coach")

# Lazy-import cloud_coach so a missing companion package (in dev) doesn't break us.
try:
    from core import cloud_coach as _cloud
except Exception:
    _cloud = None

SYSTEM_PROMPT = """You are CHIEF -- a NASCAR-grade crew chief speaking to a driver mid-session over the radio.

VOICE STYLE:
- Tight radio call. 1-2 short sentences. Maximum 25 words.
- Calm, confident, decisive. Never hedge, never apologize.
- Talk like radio: "Easy on entry at three" -- never "I would suggest you..."
- Plain spoken numbers: "two tenths off", "five tenths", "one second" -- never "0.2s" -- TTS reads spoken better.
- Reference sectors as "sector four" not "sector 4".
- Do NOT greet, introduce yourself, use the driver's name, or say "yes/no".
- Do NOT explain your reasoning unless explicitly asked.

WHAT TO SAY:
- For sector_loss: name the sector + the gain available + ONE specific actionable change ("trail brake deeper", "wait one beat then throttle", "tighten your line").
- For great_lap / great_sector: 5-8 words of reinforcement ("good lap, keep it"). Don't explain.
- For critical events (fuel, incident): lead with the urgent word ("Fuel low. Save what you can.").
- For coast: blunt and short ("you're coasting -- pick brake or throttle").
- For first_lap: state the time, set expectation ("Twenty-eight five. Build from here.").

EVENT CONTEXT will be provided. Respond with ONLY the radio call. No quotes, no preamble."""


QUESTION_SYSTEM_PROMPT = """You are CHIEF -- a NASCAR-grade crew chief on radio with the driver mid-session.

The driver just asked you a question via push-to-talk. They want a fast, useful answer.

VOICE STYLE:
- 1-3 short sentences max. Conversational radio tone.
- Plain spoken numbers ("two tenths", "fifty percent fuel").
- Reference corners by sector number ("sector four"), tracks/cars by name.
- If you don't know the exact answer, give your best fast read and say "trying" or "I'll check".
- Decisive, never wishy-washy.

WHAT YOU CAN ANSWER:
- "How's my fuel?" -- read fuel %, estimate laps left from fuel_per_lap if available
- "Where am I losing time?" -- name the worst sector + what to try
- "What should I change?" -- setup advice for handling described
- "How am I doing?" -- summarize position, lap delta, key stats
- "What lap is this?" -- read lap number, time
- General race strategy / setup theory

Race context will be provided. Respond ONLY with the spoken radio answer."""


def coach_call(event_summary, race_context):
    """Auto-event coaching call.  Cloud-proxied if Companion token is present."""
    # Prefer cloud proxy
    if _cloud and _cloud.has_token():
        out = _cloud.coach_call(event_summary, race_context)
        if out:
            return out
        # Cloud failed — only fall through to direct Claude if user supplied own key
        if not config.ANTHROPIC_API_KEY:
            return None
        log.info("cloud coach failed — falling back to direct Claude.")

    if not config.ANTHROPIC_API_KEY:
        log.error("No cloud token and no ANTHROPIC_API_KEY -- cannot call Claude.")
        return None

    user_msg = _build_user_message(event_summary, race_context)
    return _send(SYSTEM_PROMPT, user_msg, max_tokens=120)


def coach_question(transcript, race_context):
    """Driver pressed PTT and asked a question. Return spoken answer."""
    if _cloud and _cloud.has_token():
        out = _cloud.coach_question(transcript, race_context)
        if out:
            return out
        if not config.ANTHROPIC_API_KEY:
            return None
        log.info("cloud ptt failed — falling back to direct Claude.")

    if not config.ANTHROPIC_API_KEY:
        return None
    user_msg = _build_question_message(transcript, race_context)
    return _send(QUESTION_SYSTEM_PROMPT, user_msg, max_tokens=180)


def _send(system_prompt, user_msg, max_tokens):
    body = {
        "model": config.ANTHROPIC_MODEL,
        "max_tokens": max_tokens,
        "system": system_prompt,
        "messages": [{"role": "user", "content": user_msg}],
    }
    headers = {
        "x-api-key": config.ANTHROPIC_API_KEY,
        "anthropic-version": "2023-06-01",
        "content-type": "application/json",
    }
    try:
        log.info(f"Claude call -> {user_msg[:80]}")
        r = requests.post(config.ANTHROPIC_URL, json=body, headers=headers, timeout=12)
        if r.status_code != 200:
            log.warning(f"Claude {r.status_code}: {r.text[:200]}")
            return None
        data = r.json()
        parts = data.get("content", [])
        text = "".join(p.get("text", "") for p in parts if p.get("type") == "text").strip()
        if text.startswith('"') and text.endswith('"'):
            text = text[1:-1].strip()
        log.info(f"Claude reply <- {text}")
        return text or None
    except Exception as e:
        log.warning(f"Claude call failed: {e}")
        return None


def _build_user_message(event_summary, ctx):
    lines = [f"EVENT: {event_summary}", ""]
    lines.extend(_race_state_lines(ctx))
    lines.append("")
    lines.append("Respond with ONLY the short radio call.")
    return "\n".join(lines)


def _build_question_message(transcript, ctx):
    lines = [f"DRIVER ASKED: \"{transcript}\"", ""]
    lines.extend(_race_state_lines(ctx))
    lines.append("")
    lines.append("Respond ONLY with the spoken radio answer to the driver.")
    return "\n".join(lines)


def _race_state_lines(ctx):
    lines = ["RACE STATE:"]
    if ctx.get("car"):           lines.append(f"- Car: {ctx['car']}")
    if ctx.get("track"):         lines.append(f"- Track: {ctx['track']}")
    if ctx.get("session_type"):  lines.append(f"- Session: {ctx['session_type']}")
    if ctx.get("lap"):           lines.append(f"- Lap: {ctx['lap']}")
    if ctx.get("position"):      lines.append(f"- Position: P{ctx['position']}")
    if ctx.get("best_lap"):      lines.append(f"- Best lap: {ctx['best_lap']:.3f}s")
    if ctx.get("last_lap"):      lines.append(f"- Last lap: {ctx['last_lap']:.3f}s")
    if ctx.get("delta") is not None:
        lines.append(f"- Lap delta vs best: {ctx['delta']:+.3f}s")
    if ctx.get("theoretical_best"):
        lines.append(f"- Theoretical best (sum of best sectors): {ctx['theoretical_best']:.3f}s")
    if ctx.get("biggest_loss_sector"):
        bls = ctx["biggest_loss_sector"]
        lines.append(f"- Biggest sector loss: sector {bls['sector']+1}, {bls['delta']:+.3f}s")
    if ctx.get("recent_sector"):
        rs = ctx["recent_sector"]
        lines.append(f"- Just completed sector {rs['sector']+1}: {rs['time']:.3f}s ({rs['delta']:+.3f}s vs best)")
    if ctx.get("fuel_pct") is not None:
        lines.append(f"- Fuel: {ctx['fuel_pct']*100:.0f}%")
    if ctx.get("fuel_per_lap"):
        lines.append(f"- Fuel per lap: {ctx['fuel_per_lap']:.3f}")
    if ctx.get("incidents") is not None:
        lines.append(f"- Incidents: {ctx['incidents']}x")
    if ctx.get("tire_temps"):
        t = ctx["tire_temps"]
        lines.append(f"- Tires (°C): LF {t.get('LF',0):.0f}, RF {t.get('RF',0):.0f}, LR {t.get('LR',0):.0f}, RR {t.get('RR',0):.0f}")
    if ctx.get("saved_setups_for_combo"):
        n = ctx["saved_setups_for_combo"]
        lines.append(f"- Saved setups for this car/track: {n}")
    return lines
