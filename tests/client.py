"""Minimal client for the v2 protocol, used by the tests (see docs/PROTOCOL.md)."""
import hashlib
import json
import queue
import socket
import threading
import time


def password_hash(password):
    return hashlib.sha256(password.encode("utf-8")).hexdigest()


class ServerError(Exception):
    def __init__(self, reply):
        super().__init__(f"{reply.get('code')}: {reply.get('error')}")
        self.reply = reply
        self.code = reply.get("code")


class Client:
    def __init__(self, host="127.0.0.1", port=12345, name="client"):
        self.name = name
        self.sock = socket.create_connection((host, port), timeout=10)
        self.sock.settimeout(None)
        self.rid = 0
        self.replies = {}
        self.events = queue.Queue()
        self.cond = threading.Condition()
        self.closed = False
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    # ---- framing ----
    def send_raw(self, payload: bytes):
        self.sock.sendall(b"#%08d" % len(payload) + payload)

    def _recv_exact(self, n):
        data = b""
        while len(data) < n:
            chunk = self.sock.recv(n - len(data))
            if not chunk:
                raise ConnectionError("closed")
            data += chunk
        return data

    def _read(self):
        try:
            while True:
                header = self._recv_exact(9)
                assert header[:1] == b"#", header
                payload = self._recv_exact(int(header[1:]))
                if not payload.startswith(b"{"):
                    self.events.put({"t": "raw", "payload": payload})
                    continue
                msg = json.loads(payload.decode("utf-8"))
                if "rid" in msg:
                    with self.cond:
                        self.replies[msg["rid"]] = msg
                        self.cond.notify_all()
                else:
                    self.events.put(msg)
        except Exception:
            with self.cond:
                self.closed = True
                self.cond.notify_all()

    # ---- requests ----
    def request(self, t, timeout=10, **fields):
        """Sends a request and returns the reply (raises ServerError when ok is false)."""
        reply = self.request_raw(t, timeout, **fields)
        if not reply.get("ok"):
            raise ServerError(reply)
        return reply

    def request_raw(self, t, timeout=10, **fields):
        self.rid += 1
        rid = self.rid
        self.send_raw(json.dumps({"t": t, "rid": rid, **fields}).encode("utf-8"))
        deadline = time.time() + timeout
        with self.cond:
            while rid not in self.replies:
                if self.closed:
                    raise ConnectionError(f"{self.name}: connection closed waiting for {t}")
                left = deadline - time.time()
                if left <= 0:
                    raise TimeoutError(f"{self.name}: no reply to {t}")
                self.cond.wait(left)
            return self.replies.pop(rid)

    def send(self, t, **fields):
        """Sends without waiting for a reply."""
        self.send_raw(json.dumps({"t": t, **fields}).encode("utf-8"))

    def expect_error(self, code, t, **fields):
        reply = self.request_raw(t, **fields)
        assert not reply.get("ok"), f"{t} should have failed with {code}, got {reply}"
        assert reply.get("code") == code, f"{t}: expected {code}, got {reply}"
        return reply

    # ---- events ----
    def wait_event(self, t, timeout=10, match=None):
        """Waits for an event of type t (skipping others, which are kept)."""
        deadline = time.time() + timeout
        skipped = []
        try:
            while True:
                left = deadline - time.time()
                if left <= 0:
                    raise TimeoutError(f"{self.name}: no {t} event (saw {[e['t'] for e in skipped]})")
                try:
                    ev = self.events.get(timeout=left)
                except queue.Empty:
                    continue
                if ev["t"] == t and (match is None or match(ev)):
                    return ev
                skipped.append(ev)
        finally:
            for ev in skipped:
                self.events.put(ev)

    def drain(self):
        out = []
        while True:
            try:
                out.append(self.events.get_nowait())
            except queue.Empty:
                return out

    def login(self, user, password):
        return self.request("auth.login", user=user, password=password_hash(password))

    def signup(self, name, email, password):
        return self.request("auth.signup", name=name, email=email, password=password_hash(password))

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass
