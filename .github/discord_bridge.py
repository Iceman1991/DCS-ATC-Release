"""Mirror Discord forum posts to GitHub issues; announce releases on Discord."""
import json
import os
import sys
import time
import urllib.error
import urllib.request

DISCORD = "https://discord.com/api/v10"
GITHUB = "https://api.github.com"
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


def github(method, path, data=None):
    h = {
        "Authorization": "Bearer " + os.environ["GITHUB_TOKEN"],
        "Accept": "application/vnd.github+json",
        "User-Agent": "discord-bridge",
    }
    return call(method, GITHUB + path, h, data)


def truncate(text, limit=4000):
    if len(text) <= limit:
        return text
    cut = text[: limit - 3]
    nl = cut.rfind("\n")
    if nl > 0:
        cut = cut[:nl]
    return cut + "..."


def build_body(author, name, guild, thread_id, content, attachments):
    who = author.get("global_name") or author.get("username")
    link = "https://discord.com/channels/%s/%s" % (guild, thread_id)
    body = "Reported on Discord by **%s**: [%s](%s)\n\n%s\n" % (
        who, name, link, content or "_(no text)_")
    if attachments:
        names = ", ".join(a["filename"] for a in attachments)
        body += "\nAttachments (open the Discord thread to download): " + names + "\n"
    return body[:60000]


def wanted_threads(threads, forums):
    return [t for t in threads
            if t.get("parent_id") in forums and not t.get("flags", 0) & 2]


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


def mirror(env):
    guild = env["GUILD_ID"]
    forums = {env["BUG_FORUM_ID"]: "bug", env["FEATURE_FORUM_ID"]: "enhancement"}
    bot = discord("GET", "/users/@me")["id"]
    threads = discord("GET", "/guilds/%s/threads/active" % guild)["threads"]
    for t in wanted_threads(threads, forums):
        tid = t["id"]
        msgs = discord("GET", "/channels/%s/messages?after=%s&limit=50" % (tid, tid))
        if any(m["author"]["id"] == bot for m in msgs):
            continue
        try:
            start = discord("GET", "/channels/%s/messages/%s" % (tid, tid))
        except urllib.error.HTTPError as e:
            if e.code == 404:
                continue
            raise
        body = build_body(start["author"], t["name"], guild, tid,
                          start.get("content"), start.get("attachments"))
        issue = github("POST", "/repos/%s/issues" % os.environ["GITHUB_REPOSITORY"], {
            "title": t["name"], "labels": [forums[t["parent_id"]]], "body": body})
        discord("POST", "/channels/%s/messages" % tid, {
            "content": "Tracked on GitHub: %s\nWe will follow up here." % issue["html_url"],
            "allowed_mentions": {"parse": []}})
        print("#%s %s <- %s" % (issue["number"], t["name"], tid))


def main():
    env = os.environ
    try:
        if env.get("GITHUB_EVENT_NAME") == "release":
            post_release(env)
        else:
            mirror(env)
    except urllib.error.HTTPError as e:
        die(e)


def selftest():
    a = {"global_name": "Pilot", "username": "p"}
    b = build_body(a, "T", "1", "2", "hi", [])
    assert b.startswith("Reported on Discord by **Pilot**: [T](https://discord.com/channels/1/2)\n\nhi")
    assert "Attachments" not in b
    assert "_(no text)_" in build_body({"username": "p"}, "T", "1", "2", "", [])
    assert "**p**" in build_body({"username": "p"}, "T", "1", "2", "", [])
    b = build_body(a, "T", "1", "2", "hi", [{"filename": "a.log"}, {"filename": "b.png"}])
    assert b.rstrip().endswith("download): a.log, b.png")
    assert len(build_body(a, "T", "1", "2", "x" * 70000, [])) == 60000
    assert truncate("short") == "short"
    t = truncate("line\n" * 2000)
    assert len(t) <= 4000 and t.endswith("...") and "\n..." not in t[:-4] + "x"
    assert len(truncate("x" * 5000)) == 4000
    ths = [{"parent_id": "B"}, {"parent_id": "F", "flags": 0},
           {"parent_id": "B", "flags": 2}, {"parent_id": "X"}]
    assert wanted_threads(ths, {"B": 1, "F": 2}) == ths[:2]
    print("selftest ok")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        selftest()
    else:
        main()
