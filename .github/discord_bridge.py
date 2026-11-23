"""Announce published GitHub releases in a Discord channel."""
import json
import os
import sys
import time
import urllib.error
import urllib.request

DISCORD = "https://discord.com/api/v10"
UA = "DiscordBot (https://github.com/Iceman1991/DCS-ATC-Release, 1.0)"


def call(method, url, headers, data=None, retry=True):
    body = json.dumps(data).encode() if data is not None else None
    req = urllib.request.Request(url, data=body, method=method, headers=headers)
    if body:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        text = e.read().decode(errors="replace")
        if e.code == 429 and retry and "discord.com" in url:
            time.sleep(float(json.loads(text).get("retry_after", 1)) + 0.1)
            return call(method, url, headers, data, retry=False)
        e.text = text
        raise


def die(e):
    print("HTTP %s: %s" % (e.code, getattr(e, "text", "")), file=sys.stderr)
    sys.exit(1)


def discord(method, path, data=None):
    h = {"Authorization": "Bot " + os.environ["DISCORD_TOKEN"], "User-Agent": UA}
    return call(method, DISCORD + path, h, data)


def truncate(text, limit=4000):
    if len(text) <= limit:
        return text
    cut = text[: limit - 3]
    nl = cut.rfind("\n")
    if nl > 0:
        cut = cut[:nl]
    return cut + "..."


def post_release(env):
    with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as f:
        rel = json.load(f)["release"]
    name = rel.get("name") or rel["tag_name"]
    discord("POST", "/channels/%s/messages" % env["RELEASE_CHANNEL_ID"], {
        "content": "New release: " + name,
        "embeds": [{"title": name, "url": rel["html_url"],
                    "description": truncate(rel.get("body") or "")}],
    })
    print("announced " + name)


def main():
    env = os.environ
    if env.get("GITHUB_EVENT_NAME") != "release":
        print("not a release event, nothing to do")
        return
    try:
        post_release(env)
    except urllib.error.HTTPError as e:
        die(e)


def selftest():
    assert truncate("short") == "short"
    t = truncate("line\n" * 2000)
    assert len(t) <= 4000 and t.endswith("...") and "\n..." not in t[:-4] + "x"
    assert len(truncate("x" * 5000)) == 4000
    print("selftest ok")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        selftest()
    else:
        main()
