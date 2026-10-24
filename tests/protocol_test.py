"""End-to-end tests of the v2 server over its real protocol.

    python tests/protocol_test.py <path to server.exe> [legacy syncDBTemp.sqlite to import]

Starts the server in a fresh temporary folder on a free port, runs every test,
then stops it. The legacy database (if given) is copied, never modified.
"""
import base64
import json
import os
import random
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import time
import zlib

sys.path.insert(0, os.path.dirname(__file__))
from client import Client, ServerError, password_hash  # noqa: E402

PASSED = []


def test(fn):
    def run(*args):
        print(f"--- {fn.__name__}", flush=True)
        fn(*args)
        PASSED.append(fn.__name__)
    run.__name__ = fn.__name__
    return run


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


class Server:
    def __init__(self, exe, legacy=None, extra_config=None):
        self.dir = tempfile.mkdtemp(prefix="ghd-test-")
        self.port = free_port()
        shutil.copy(exe, os.path.join(self.dir, "server.exe"))
        if legacy:
            shutil.copy(legacy, os.path.join(self.dir, "syncDBTemp.sqlite"))
        config = {
            "port": self.port,
            "saveIntervalSeconds": 1,
            "admins": ["alice"],
            "logLevel": "debug",
            "rateLimits": {"loginMaxFailures": 5, "loginWindowMinutes": 15, "signupsPerHourPerIp": 1000},
            "run": {"docker": sys.executable + " " + os.path.join(os.path.dirname(os.path.abspath(__file__)), "fake_docker.py")},
        }
        config.update(extra_config or {})
        with open(os.path.join(self.dir, "server.json"), "w") as f:
            json.dump(config, f)
        self.log = open(os.path.join(self.dir, "console.txt"), "w")
        self.proc = subprocess.Popen([os.path.join(self.dir, "server.exe")], cwd=self.dir,
                                     stdin=subprocess.PIPE, stdout=self.log, stderr=subprocess.STDOUT)
        for _ in range(100):
            try:
                socket.create_connection(("127.0.0.1", self.port), timeout=1).close()
                return
            except OSError:
                time.sleep(0.1)
        raise RuntimeError("server didn't start: " + self.console())

    def client(self, name="client"):
        return Client(port=self.port, name=name)

    def command(self, line):
        self.proc.stdin.write((line + "\n").encode())
        self.proc.stdin.flush()

    def console(self):
        with open(os.path.join(self.dir, "console.txt"), encoding="utf-8", errors="replace") as f:
            return f.read()

    def stop(self):
        try:
            self.command("exit")
            self.proc.wait(15)
        except Exception:
            self.proc.kill()
        self.log.close()


def tiny_png(size=64, color=(200, 80, 40)):
    raw = b"".join(b"\x00" + bytes(color) * size for _ in range(size))
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


# ---------------------------------------------------------------------------

@test
def legacy_import(srv):
    a = srv.client("alice")
    me = a.login("alice", "alicepw")
    assert me["user"]["name"] == "alice" and me["isAdmin"], me
    projects = a.request("project.list")["projects"]
    assert len(projects) == 1, projects
    p = projects[0]
    opened = a.request("project.open", id=p["id"])
    names = sorted(m["user"]["name"] for m in opened["project"]["members"])
    assert names == ["alice", "carol"], names
    chat = a.request("chat.history", projectId=p["id"])["messages"]
    assert chat and all(m["user"]["name"] in ("alice", "carol") for m in chat), chat
    tasks = a.request("task.list", projectId=p["id"])["tasks"]
    assert len(tasks) == 1 and len(tasks[0]["due"]) == 10, tasks
    friends = a.request("friends.list")["friends"]
    assert [f["name"] for f in friends] == ["carol"], friends
    print(f"    imported project {p['name']!r}: {len(chat)} chat messages, task {tasks[0]['text']!r}")
    a.request("project.close", id=p["id"])
    a.close()


@test
def accounts_and_lockout(srv):
    c = srv.client()
    c.expect_error("not_logged_in", "project.list")
    c.expect_error("bad_request", "auth.signup", name="x", email="x@y.z", password=password_hash("pw"))
    c.expect_error("bad_request", "auth.signup", name="bobby", email="nope", password=password_hash("pw"))
    c.expect_error("bad_request", "auth.signup", name="bobby", email="b@y.z", password="plaintext")
    r = c.signup("bob", "bob@example.com", "bobpw")
    assert r["user"]["name"] == "bob" and not r["isAdmin"]
    c2 = srv.client()
    c2.expect_error("conflict", "auth.signup", name="BOB", email="other@example.com", password=password_hash("x"))
    c2.expect_error("conflict", "auth.signup", name="bobby", email="BOB@example.com", password=password_hash("x"))
    c2.login("bob@example.com", "bobpw")          # by email
    c2.request("auth.logout")
    srv.client().signup("mallory", "mallory@example.com", "mallorypw")
    for _ in range(5):
        c2.expect_error("bad_request", "auth.login", user="mallory", password=password_hash("wrong"))
    r = c2.expect_error("rate_limited", "auth.login", user="mallory", password=password_hash("mallorypw"))
    assert r["retryAfter"] > 0, r
    # other accounts from the same address still work
    c2.login("alice", "alicepw")
    c2.request("auth.changePassword", user="carol", oldPassword=password_hash("carolpw"), newPassword=password_hash("carolpw2"))
    c3 = srv.client()
    c3.expect_error("bad_request", "auth.login", user="carol", password=password_hash("carolpw"))
    c3.login("carol", "carolpw2")
    for x in (c, c2, c3):
        x.close()


@test
def friends_and_presence(srv):
    d = srv.client("dana")
    d.signup("dana", "dana@example.com", "danapw")
    e = srv.client("erez")
    e.signup("erez", "erez@example.com", "erezpw")
    found = d.request("user.search", query="ere")["users"]
    assert [u["name"] for u in found] == ["erez"] and found[0]["relation"] == "none", found
    assert d.request("user.search", query="erez@example.com")["users"][0]["name"] == "erez"
    assert d.request("user.search", query="example.com")["users"] == []  # emails only match exactly
    assert d.request("friends.request", name="erez")["state"] == "sent"
    ev = e.wait_event("ev.inbox")
    assert ev["item"]["kind"] == "friendRequest" and ev["item"]["from"]["name"] == "dana", ev
    e.wait_event("ev.friends")
    assert e.request("friends.list")["incoming"][0]["name"] == "dana"
    dana_id = ev["item"]["from"]["id"]
    e.request("friends.respond", userId=dana_id, accept=True)
    d.wait_event("ev.friends")
    friends = d.request("friends.list")["friends"]
    assert friends[0]["name"] == "erez" and friends[0]["presence"]["status"] == "online", friends
    # the answered request disappears from erez's inbox
    assert all(i["kind"] != "friendRequest" for i in e.request("inbox.list")["items"])
    d.drain()
    e.request("presence.set", status="away")
    ev = d.wait_event("ev.presence", match=lambda x: x["presence"]["status"] == "away")
    e.close()
    ev = d.wait_event("ev.presence", match=lambda x: x["presence"]["status"] == "offline")
    prof = d.request("user.profile", name="erez")
    assert prof["relation"] == "friend" and "email" not in prof, prof
    assert d.request("user.profile", name="dana")["email"] == "dana@example.com"
    d.close()


@test
def projects_roles_invites(srv):
    d = srv.client("dana")
    d.login("dana", "danapw")
    e = srv.client("erez")
    e.login("erez", "erezpw")
    b = srv.client("bob")
    b.login("bob", "bobpw")
    # direct invites are for friends only
    d.expect_error("forbidden", "project.create", name="Blocked", language="py", invites=[{"name": "bob", "role": "member"}])
    p = d.request("project.create", name="Studio שלום", language="cpp", description="Hebrew ✓", visibility="private",
                  invites=[{"name": "erez", "role": "admin"}])["project"]
    assert p["myRole"] == "owner" and p["invites"][0]["user"]["name"] == "erez", p
    d.expect_error("conflict", "project.create", name="STUDIO שלום", language="cpp")
    ev = e.wait_event("ev.inbox", match=lambda x: x["item"]["kind"] == "projectInvite")
    e.expect_error("forbidden", "project.open", id=p["id"])
    e.request("project.respondInvite", id=p["id"], accept=True)
    assert e.request("project.get", id=p["id"])["project"]["myRole"] == "admin"
    # invite code with approval
    code = e.request("project.inviteCode", id=p["id"], action="create", autoApprove=False)["inviteCode"]
    assert len(code) == 8
    b.expect_error("forbidden", "project.open", id=p["id"])
    r = b.request("project.join", code="githubdemo://join/" + code.lower())
    assert r["status"] == "requested", r
    ev = d.wait_event("ev.inbox", match=lambda x: x["item"]["kind"] == "joinRequest")
    bob_id = ev["item"]["from"]["id"]
    b.expect_error("forbidden", "project.respondJoin", projectId=p["id"], userId=bob_id, accept=True)
    d.request("project.respondJoin", projectId=p["id"], userId=bob_id, accept=True)
    assert b.request("project.get", id=p["id"])["project"]["myRole"] == "member"
    # roles: admin can't touch the owner; members can't manage
    b.expect_error("forbidden", "project.update", id=p["id"], name="Hijacked")
    e.expect_error("forbidden", "project.update", id=p["id"], remove=[d.request("project.get", id=p["id"])["project"]["owner"]["id"]])
    e.request("project.update", id=p["id"], description="Updated by erez")
    # owner hands over, then leaves; the new owner keeps the project
    erez_id = next(m["user"]["id"] for m in d.request("project.get", id=p["id"])["project"]["members"] if m["user"]["name"] == "erez")
    d.request("project.update", id=p["id"], roles=[{"userId": erez_id, "role": "owner"}])
    assert d.request("project.get", id=p["id"])["project"]["myRole"] == "admin"
    d.request("project.leave", id=p["id"])
    e.request("project.update", id=p["id"], remove=[bob_id])
    b.expect_error("forbidden", "project.open", id=p["id"])
    proj = e.request("project.get", id=p["id"])["project"]
    assert [m["user"]["name"] for m in proj["members"]] == ["erez"], proj
    # public projects can be watched read-only
    e.request("project.update", id=p["id"], visibility="public")
    assert any(x["id"] == p["id"] for x in b.request("project.public", query="שלום")["projects"])
    opened = b.request("project.open", id=p["id"])
    assert opened["readOnly"] and opened["role"] == "viewer"
    e.request("project.open", id=p["id"])
    f = e.request("file.create", projectId=p["id"], path="main.cpp")["file"]
    b.wait_event("ev.tree")
    b.request("file.open", fileId=f["id"])
    b.expect_error("forbidden", "file.op", fileId=f["id"], rev=0, op=["x"])
    b.expect_error("forbidden", "chat.send", projectId=p["id"], text="hi")
    b.expect_error("forbidden", "file.create", projectId=p["id"], path="evil.cpp")
    b.request("chat.history", projectId=p["id"])
    for x in (d, e, b):
        x.close()
    globals()["PUBLIC_PROJECT"] = p["id"]


@test
def files_and_folders(srv):
    e = srv.client("erez")
    e.login("erez", "erezpw")
    pid = globals()["PUBLIC_PROJECT"]
    e.request("project.open", id=pid)
    for bad in ["../x", "a/../b", "con:x", "", "a" * 120]:
        e.expect_error("bad_request", "file.create", projectId=pid, path=bad)
    f = e.request("file.create", projectId=pid, path="src\\util\\helper.cpp")["file"]
    assert f["path"] == "src/util/helper.cpp", f
    tree = e.wait_event("ev.tree", match=lambda x: any(ff["path"] == "src/util/helper.cpp" for ff in x["files"]))
    assert "src" in tree["folders"] and "src/util" in tree["folders"], tree
    e.expect_error("conflict", "file.create", projectId=pid, path="src/util/helper.cpp")
    e.expect_error("conflict", "file.create", projectId=pid, path="src")
    e.request("folder.create", projectId=pid, path="docs/empty")
    e.request("folder.rename", projectId=pid, path="src", newPath="lib")
    e.expect_error("bad_request", "folder.rename", projectId=pid, path="lib", newPath="lib/inner")
    tree = e.wait_event("ev.tree", match=lambda x: any(ff["path"] == "lib/util/helper.cpp" for ff in x["files"]))
    assert "src" not in tree["folders"] and "lib/util" in tree["folders"], tree
    e.request("file.rename", fileId=f["id"], path="lib/helper2.cpp")
    e.request("folder.delete", projectId=pid, path="lib")
    tree = e.wait_event("ev.tree", match=lambda x: not any(ff["path"].startswith("lib") for ff in x["files"]))
    assert "lib" not in tree["folders"], tree
    e.close()


def apply(text, op):
    """Applies an operation; positions are UTF-16 code units, like the protocol."""
    units = text.encode("utf-16-le")
    out, pos = [], 0
    for c in op:
        if isinstance(c, str):
            out.append(c.encode("utf-16-le"))
        elif c > 0:
            out.append(units[2 * pos:2 * (pos + c)]); pos += c
        else:
            pos -= c
    assert 2 * pos == len(units), (pos, len(units) // 2, op)
    return b"".join(out).decode("utf-16-le")


def utf16_len(s):
    return len(s.encode("utf-16-le")) // 2


@test
def live_editing_converges(srv):
    """Three users type into the same file at random; every copy ends up identical."""
    clients = []
    e = srv.client("erez")
    e.login("erez", "erezpw")
    pid = globals()["PUBLIC_PROJECT"]
    names = ["dana", "bob"]
    # make them members again
    code = e.request("project.inviteCode", id=pid, action="create", autoApprove=True)["inviteCode"]
    clients.append(e)
    for n in names:
        c = srv.client(n)
        c.login(n, n + "pw")
        assert c.request("project.join", code=code)["status"] == "joined"
        clients.append(c)
    fid = e.request("file.create", projectId=pid, path="live.txt")["file"]["id"]
    state = {}
    for c in clients:
        c.request("project.open", id=pid)
        r = c.request("file.open", fileId=fid)
        state[c.name] = {"text": r["text"], "rev": r["rev"]}
    rnd = random.Random(7)
    alphabet = "abc שלום 😀\n"
    # Each client sends edits based on its last known revision without waiting,
    # then catches up from events; the server transforms them.
    for round_ in range(30):
        for c in clients:
            st = state[c.name]
            # apply everything received so far
            for ev in c.drain():
                if ev["t"] == "ev.file.op" and ev["fileId"] == fid and ev["rev"] == st["rev"] + 1:
                    st["text"] = apply(st["text"], ev["op"]); st["rev"] = ev["rev"]
            text = st["text"]
            n = utf16_len(text)
            units = text.encode("utf-16-le")
            pos = rnd.randint(0, n)
            # keep surrogate pairs whole
            while 0 < pos < n and 0xDC00 <= struct.unpack("<H", units[2 * pos:2 * pos + 2])[0] <= 0xDFFF:
                pos -= 1
            if rnd.random() < 0.7 or n == 0:
                ins = rnd.choice(alphabet)
                op = ([pos] if pos else []) + [ins] + ([n - pos] if n - pos else [])
            else:
                cut = 1
                if pos == n:
                    pos -= 1
                if 0xD800 <= struct.unpack("<H", units[2 * pos:2 * pos + 2])[0] <= 0xDBFF:
                    cut = 2
                op = ([pos] if pos else []) + [-cut] + ([n - pos - cut] if n - pos - cut else [])
            r = c.request("file.op", fileId=fid, rev=st["rev"], op=op)
            assert "resync" not in r, r
            # our own op, transformed past everything we hadn't seen, is now at r["rev"];
            # catch up by re-reading: simplest correct client is to wait for the events up to rev
            st["text"] = apply(st["text"], op) if r["rev"] == st["rev"] + 1 else None
            st["rev"] = r["rev"]
            if st["text"] is None:
                st["text"] = c.request("file.open", fileId=fid)["text"]  # resnapshot (test shortcut)
                c.drain()
    time.sleep(0.5)
    finals = set()
    for c in clients:
        finals.add(c.request("file.open", fileId=fid)["text"])
    assert len(finals) == 1, finals
    print(f"    {len(next(iter(finals)))} characters, all copies identical")
    globals()["LIVE_FILE"] = fid
    for c in clients:
        c.close()


@test
def cursors_versions_autosave(srv):
    e = srv.client("erez")
    e.login("erez", "erezpw")
    d = srv.client("dana")
    d.login("dana", "danapw")
    pid = globals()["PUBLIC_PROJECT"]
    fid = e.request("file.create", projectId=pid, path="v.py")["file"]["id"]
    for c in (e, d):
        c.request("project.open", id=pid)
    r = e.request("file.open", fileId=fid)
    e.request("file.op", fileId=fid, rev=0, op=["print('hello')\n"])
    d.request("file.open", fileId=fid)
    users = e.wait_event("ev.file.users", match=lambda x: len(x["users"]) == 2)
    # dana's cursor at the end of rev 1; erez inserts at the start -> cursor moves right
    d.send("file.cursor", fileId=fid, rev=1, anchor=5, head=5)
    ev = e.wait_event("ev.file.cursor")
    assert ev["anchor"] == 5, ev
    e.request("file.op", fileId=fid, rev=1, op=["# hi\n", 15])
    d.send("file.cursor", fileId=fid, rev=1, anchor=5, head=5)  # stale revision: server moves it
    ev = e.wait_event("ev.file.cursor")
    assert ev["anchor"] == 10 and ev["rev"] == 2, ev
    e.request("file.checkpoint", fileId=fid, label="first")
    e.request("file.op", fileId=fid, rev=2, op=[20, "x = 1\n"])
    versions = e.request("file.versions", fileId=fid)["versions"]
    assert versions[0]["label"] == "first", versions
    first = versions[0]["id"]
    assert e.request("file.version", versionId=first)["text"] == "# hi\nprint('hello')\n"
    d.drain()
    e.request("file.restore", versionId=first)
    ev = d.wait_event("ev.file.op")
    assert ev["rev"] == 4, ev
    assert d.request("file.open", fileId=fid)["text"] == "# hi\nprint('hello')\n"
    labels = [v["label"] for v in e.request("file.versions", fileId=fid)["versions"]]
    assert "Before restore" in labels, labels
    # close everywhere; autosave writes it and unloads it; reopening reads the database
    e.request("project.close", id=pid)
    d.request("project.close", id=pid)
    time.sleep(2.5)
    e.request("project.open", id=pid)
    assert e.request("file.open", fileId=fid)["rev"] == 0  # a fresh load from the database
    assert e.request("file.open", fileId=fid)["text"] == "# hi\nprint('hello')\n"
    # an automatic version was taken when the last editor left
    assert any(v["label"] == "" for v in e.request("file.versions", fileId=fid)["versions"])
    e.close()
    d.close()


@test
def chat(srv):
    e = srv.client("erez")
    e.login("erez", "erezpw")
    d = srv.client("dana")
    d.login("dana", "danapw")
    pid = globals()["PUBLIC_PROJECT"]
    e.request("project.open", id=pid)
    d.request("project.open", id=pid)
    d.drain()
    e.send("chat.typing", projectId=pid)
    assert d.wait_event("ev.chat.typing")["user"]["name"] == "erez"
    m = e.request("chat.send", projectId=pid, text="hey @dana, look:\n```cpp\nint x;\n```")["message"]
    ev = d.wait_event("ev.chat")
    assert ev["message"]["id"] == m["id"]
    ev = d.wait_event("ev.inbox", match=lambda x: x["item"]["kind"] == "mention")
    assert "mentioned you" in ev["item"]["text"], ev
    d.expect_error("forbidden", "chat.edit", id=m["id"], text="mine now")
    e.request("chat.edit", id=m["id"], text="edited ✓")
    ev = d.wait_event("ev.chat", match=lambda x: x["message"].get("editedAt"))
    assert ev["message"]["text"] == "edited ✓"
    d.expect_error("forbidden", "chat.delete", id=m["id"])  # dana is a member, not admin
    e.request("chat.delete", id=m["id"])
    ev = d.wait_event("ev.chat", match=lambda x: x["message"]["deleted"])
    assert ev["message"]["text"] == ""
    for i in range(5):
        e.request("chat.send", projectId=pid, text=f"msg {i}")
    page = e.request("chat.history", projectId=pid, limit=3)
    assert [x["text"] for x in page["messages"]] == ["msg 2", "msg 3", "msg 4"] and page["more"]
    older = e.request("chat.history", projectId=pid, limit=3, before=page["messages"][0]["id"])
    assert older["messages"][-1]["text"] == "msg 1"
    e.expect_error("bad_request", "chat.send", projectId=pid, text="   ")
    # rate limit: 10 per 10 s
    errors = 0
    for i in range(12):
        r = e.request_raw("chat.send", projectId=pid, text="spam")
        errors += not r["ok"]
    assert errors >= 2, errors
    e.close()
    d.close()


@test
def tasks_and_activity(srv):
    e = srv.client("erez")
    e.login("erez", "erezpw")
    d = srv.client("dana")
    d.login("dana", "danapw")
    pid = globals()["PUBLIC_PROJECT"]
    e.request("project.open", id=pid)
    d.request("project.open", id=pid)
    d.drain()
    dana_id = next(m["user"]["id"] for m in e.request("project.get", id=pid)["project"]["members"] if m["user"]["name"] == "dana")
    e.expect_error("bad_request", "task.create", projectId=pid, text="x", due="tomorrow")
    e.expect_error("bad_request", "task.create", projectId=pid, text="x", due="2030-01-01", assignees=[99999])
    t = e.request("task.create", projectId=pid, text="Write README ✓", due="2030-01-05", assignees=[dana_id])["task"]
    assert t["assignees"][0]["name"] == "dana" and not t["done"]
    d.wait_event("ev.task")
    d.wait_event("ev.inbox", match=lambda x: x["item"]["kind"] == "task")
    d.request("task.setDone", id=t["id"], done=True)
    assert e.wait_event("ev.task")["task"]["done"]
    e.request("task.update", id=t["id"], text="Write README", due="2030-02-01", assignees=[])
    tasks = d.request("task.list", projectId=pid)["tasks"]
    assert tasks[0]["assignees"] == [] and tasks[0]["due"] == "2030-02-01"
    feed = d.request("activity.feed")["items"]
    kinds = {i["kind"] for i in feed}
    assert {"task.created", "task.done", "file.created"} <= kinds, kinds
    e.request("task.delete", id=t["id"])
    assert d.wait_event("ev.task.deleted")["id"] == t["id"]
    e.close()
    d.close()


@test
def avatars(srv):
    d = srv.client("dana")
    d.login("dana", "danapw")
    e = srv.client("erez")
    e.login("erez", "erezpw")
    e.drain()
    assert d.request("user.setAvatar", preset="fox")["avatar"] == "p:fox"
    d.expect_error("bad_request", "user.setAvatar", preset="../../etc")
    assert e.wait_event("ev.user")["user"]["avatar"] == "p:fox"
    png = tiny_png()
    r = d.request("user.setAvatar", png=base64.b64encode(png).decode())
    assert r["avatar"].startswith("u:")
    got = e.request("user.avatar", hash=r["avatar"][2:])["png"]
    assert base64.b64decode(got) == png
    d.expect_error("bad_request", "user.setAvatar", png=base64.b64encode(b"GIF89a....").decode())
    d.expect_error("bad_request", "user.setAvatar", png=base64.b64encode(tiny_png(16)).decode())
    d.request("user.setAvatar")  # back to initials
    e.expect_error("not_found", "user.avatar", hash=r["avatar"][2:])  # unused pictures are removed
    d.close()
    e.close()


@test
def import_export(srv):
    e = srv.client("erez")
    e.login("erez", "erezpw")
    pid = globals()["PUBLIC_PROJECT"]
    files = [{"path": "imp/a.py", "text": "print(1)\n"}, {"path": "imp/b/c.txt", "text": "שלום"}, {"path": "v.py", "text": "dup"}]
    r = e.request("project.import", id=pid, files=files, folders=["imp/empty"])
    assert r == {**r, "created": 2, "skipped": 1}, r
    exp = e.request("project.export", id=pid)
    paths = {f["path"]: f["text"] for f in exp["files"]}
    assert paths["imp/b/c.txt"] == "שלום" and "imp/empty" in exp["folders"], exp["folders"]
    e.close()


@test
def run_code(srv):
    e = srv.client("erez")
    e.login("erez", "erezpw")
    pid = globals()["PUBLIC_PROJECT"]
    e.request("project.open", id=pid)
    fid = e.request("file.create", projectId=pid, path="run_me.py")["file"]["id"]
    e.request("file.open", fileId=fid)
    e.request("file.op", fileId=fid, rev=0, op=["print('hi')\n"])
    run = e.request("run.start", fileId=fid, stdin="input-line")["runId"]
    exit_ev = e.wait_event("ev.run.exit", timeout=20)
    out = [ev["text"] for ev in e.drain() if ev["t"] == "ev.run.output" and ev["runId"] == run]
    text = "".join(out)
    # the fake docker echoes its command line and stdin, then the file it was given
    assert "--network none" in text and "input-line" in text and "print('hi')" in text, text
    assert exit_ev["exitCode"] == 0 and not exit_ev["timedOut"], exit_ev
    txt = e.request("file.create", projectId=pid, path="notes.md")["file"]["id"]
    e.expect_error("bad_request", "run.start", fileId=txt)
    e.close()


@test
def admin(srv):
    a = srv.client("alice")
    a.login("alice", "alicepw")
    b = srv.client("bob")
    b.login("bob", "bobpw")
    b.expect_error("forbidden", "admin.stats")
    stats = a.request("admin.stats")
    assert stats["users"] >= 5 and stats["onlineUsers"] >= 2, stats
    users = a.request("admin.users", query="bo")["users"]
    bob = next(u for u in users if u["name"] == "bob")
    assert bob["online"] and bob["email"] == "bob@example.com"
    a.request("admin.ban", userId=bob["id"], banned=True, reason="spam")
    ev = b.wait_event("ev.kicked")
    assert "spam" in ev["reason"], ev
    b2 = srv.client("bob2")
    r = b2.expect_error("banned", "auth.login", user="bob", password=password_hash("bobpw"))
    a.request("admin.ban", userId=bob["id"], banned=False)
    b2.login("bob", "bobpw")
    projects = a.request("admin.projects", query="")["projects"]
    assert projects
    # the console
    srv.command("status")
    srv.command("kick bob")
    b2.wait_event("ev.kicked")
    a.close()


@test
def concurrency_stress(srv):
    """24 users in 6 projects edit and chat at the same time on separate threads."""
    import threading
    owners = []
    projects = []
    for k in range(6):
        o = srv.client(f"owner{k}")
        o.signup(f"owner{k}", f"owner{k}@example.com", "pw")
        pr = o.request("project.create", name=f"Stress {k}", language="py")["project"]
        code = o.request("project.inviteCode", id=pr["id"], action="create", autoApprove=True)["inviteCode"]
        o.request("project.open", id=pr["id"])
        fid = o.request("file.create", projectId=pr["id"], path="shared.txt")["file"]["id"]
        owners.append(o)
        projects.append((pr["id"], code, fid))
    errors = []

    def worker(n):
        try:
            pid, code, fid = projects[n % 6]
            c = srv.client(f"w{n}")
            c.signup(f"worker{n}", f"worker{n}@example.com", "pw")
            c.request("project.join", code=code)
            c.request("project.open", id=pid)
            r = c.request("file.open", fileId=fid)
            rev, text = r["rev"], r["text"]
            for i in range(40):
                # always insert at the start, based on whatever revision we know
                reply = c.request("file.op", fileId=fid, rev=rev, op=[f"{n}:{i};"] + ([utf16_len(text)] if text else []))
                if reply.get("resync"):
                    rev, text = reply["rev"], reply["text"]
                    continue
                # re-read after each edit to keep the test client trivial
                snap = c.request("file.open", fileId=fid)
                rev, text = snap["rev"], snap["text"]
                if i % 10 == 0:
                    c.request("chat.send", projectId=pid, text=f"worker {n} at {i}")
            c.close()
        except Exception as e:  # noqa: BLE001
            errors.append(f"worker {n}: {e!r}")

    threads = [threading.Thread(target=worker, args=(n,)) for n in range(24)]
    start = time.time()
    for t in threads:
        t.start()
    for t in threads:
        t.join(120)
    assert not errors, errors
    for k, (pid, code, fid) in enumerate(projects):
        text = owners[k].request("file.open", fileId=fid)["text"]
        pieces = [x for x in text.split(";") if x]
        assert len(pieces) == 4 * 40, (k, len(pieces))   # every edit from the 4 workers of this project
    print(f"    {24 * 40} edits in 6 projects in {time.time() - start:.1f} s, nothing lost")
    for o in owners:
        o.close()


@test
def malformed_and_limits(srv):
    c = srv.client()
    c.send_raw(b"{not json")
    assert c.wait_event("error")["code"] == "bad_request"
    r = c.request_raw("no.such.thing")
    assert r["code"] == "bad_request"
    r = c.request_raw("auth.login", user=5, password=None)
    assert r["code"] == "bad_request", r
    c.login("dana", "danapw")
    limited = 0
    for i in range(600):
        c.send("friends.list", rid=100000 + i)
    deadline = time.time() + 10
    got = 0
    while got < 600 and time.time() < deadline:
        with c.cond:
            ids = [k for k in c.replies if k >= 100000]
            for k in ids:
                limited += c.replies.pop(k).get("code") == "rate_limited"
                got += 1
        time.sleep(0.05)
    assert got == 600, got
    assert limited > 0, "the request limit never kicked in"
    print(f"    {limited} of 600 rapid requests were rate limited")
    # legacy unframed version check still answered
    s = socket.create_connection(("127.0.0.1", srv.port))
    s.sendall(b"501")
    data = s.recv(100)
    assert data.startswith(b"502"), data
    s.close()
    # a frame that claims to be huge is refused
    s = socket.create_connection(("127.0.0.1", srv.port))
    s.sendall(b"#99999999{")
    s.settimeout(5)
    assert s.recv(10) == b""
    s.close()
    c.close()


@test
def signup_limit(exe):
    srv = Server(exe, extra_config={"rateLimits": {}})  # defaults: 5 sign-ups per hour per address
    try:
        for k in range(5):
            srv.client().signup(f"user{k}", f"user{k}@example.com", "pw")
        srv.client().expect_error("rate_limited", "auth.signup", name="user9", email="user9@example.com", password=password_hash("pw"))
    finally:
        srv.stop()


def main():
    exe = sys.argv[1]
    legacy = sys.argv[2] if len(sys.argv) > 2 else None
    srv = Server(exe, legacy)
    print("server in", srv.dir, "port", srv.port)
    try:
        if legacy:
            legacy_import(srv)
        accounts_and_lockout(srv)
        friends_and_presence(srv)
        projects_roles_invites(srv)
        files_and_folders(srv)
        live_editing_converges(srv)
        cursors_versions_autosave(srv)
        chat(srv)
        tasks_and_activity(srv)
        avatars(srv)
        import_export(srv)
        run_code(srv)
        admin(srv)
        concurrency_stress(srv)
        malformed_and_limits(srv)
    except Exception:
        print("\n==== server console ====")
        print(srv.console()[-6000:])
        raise
    finally:
        srv.stop()
    signup_limit(exe)
    print(f"\nALL {len(PASSED)} TESTS PASSED")


if __name__ == "__main__":
    main()
