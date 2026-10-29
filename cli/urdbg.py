#!/usr/bin/env python3
"""Unity Runtime Debugger command-line client (Python 3.8+, standard library only).

    URDBG_URL=http://127.0.0.1:18765 URDBG_TOKEN=<token> python3 urdbg.py <command>

Without a command on a terminal: interactive C# shell. Exit code 1 on failure.
"""

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

try:
    import readline  # line editing and history in the shell (Windows: pip install pyreadline3)
except ImportError:
    readline = None

HISTORY_FILE = os.path.expanduser("~/.urdbg_history")
TIMEOUT = 70  # the server waits up to 60 s for the game's main thread


class Failure(Exception):
    pass


class Client:
    def __init__(self, url, token):
        self.url = url.rstrip("/")
        self.token = token

    def request(self, method, path, body=None, query=None, raw=False):
        url = self.url + path
        if query:
            url += "?" + urllib.parse.urlencode({k: v for k, v in query.items() if v is not None})
        data = None if body is None else json.dumps(body).encode("utf-8")
        req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": "Bearer " + self.token})
        if data is not None:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=TIMEOUT) as resp:
                text = resp.read().decode("utf-8", errors="replace")
        except urllib.error.HTTPError as e:
            text = e.read().decode("utf-8", errors="replace")
            try:
                error = json.loads(text)["error"]
                message = f"{error.get('code')}: {error.get('message')}"
                if error.get("details"):
                    message += "\n" + json.dumps(error["details"], ensure_ascii=False, indent=2)
                if error.get("help"):
                    message += f"\n(see {self.url}{error['help']})"
            except (ValueError, KeyError, TypeError):
                message = f"HTTP {e.code}: {text[:400]}"
            raise Failure(message) from None
        except urllib.error.URLError as e:
            raise Failure(f"cannot reach {self.url}: {e.reason}") from None
        return text if raw else json.loads(text)

    def get(self, path, **query):
        return self.request("GET", path, query=query)

    def post(self, path, body=None):
        return self.request("POST", path, body={} if body is None else body)


# ---- formatting -------------------------------------------------------------------------------

def value(v):
    """One value on one line: `Type "text" (count)  $handle`."""
    if v is None:
        return "null"
    kind, text, type_ = v.get("kind"), v.get("text"), v.get("type")
    if kind == "null":
        out = "null"
    elif kind == "void":
        out = "(no value)"
    elif kind == "string":
        out = json.dumps(text or "", ensure_ascii=False) + ("…" if v.get("truncated") else "")
    elif kind in ("number", "bool", "pointer"):
        out = text
    elif kind == "char":
        out = repr(text)
    elif kind == "enum":
        out = f"{type_}.{text}"
    elif kind == "type":
        out = f"type {text}"
    elif kind == "destroyed":
        out = f"{type_} (destroyed)"
    elif kind == "error":
        out = f"<error: {text}>"
    else:
        out = type_ or kind
        if text:
            out += f" {text}" if kind == "struct" else f' "{text}"'
        if v.get("count") is not None:
            out += f" ({v['count']})"
    if v.get("handle"):
        out += f"  ${v['handle']}"
    return out


def print_eval(result):
    if result.get("output"):
        sys.stdout.write(result["output"])
        if not result["output"].endswith("\n"):
            print()
    error = result.get("error")
    if error:
        diagnostics = error.get("diagnostics")
        if diagnostics:
            for d in diagnostics:
                print(f"({d['line']},{d['column']}) {d['severity']} {d['id']}: {d['message']}", file=sys.stderr)
        else:
            print(error.get("message"), file=sys.stderr)
        return False
    if "value" in result:
        print(value(result["value"]))
    return True


def print_members(data):
    print(f"{value(data['value'])}  [{data['type']['fullName']}]")
    if data.get("gameObject"):
        go = data["gameObject"]
        print(f"  GameObject {go['path']}  active={go['active']}  scene={go['scene']}")
    if data.get("facets"):
        print("  facets: " + ", ".join(data["facets"]))
    if data.get("text") is not None:
        print(data["text"])
    width = max((len(m["name"]) for m in data.get("members", [])), default=0)
    for m in data.get("members", []):
        shown = m.get("error") and f"<error: {m['error']}>" or ("(lazy: GET .../members/" + m["name"] + ")" if m.get("lazy") else value(m.get("value")))
        print(f"  {m['name']:<{width}}  {m['type']:<18} {shown}")


def print_call(c):
    when = time.strftime("%H:%M:%S", time.localtime(c["time"] / 1000))
    args = ", ".join(f"{a['name']}={value(a['value'])}" for a in c.get("args", []))
    line = f"{when} f{c['frame']} {c['durationUs']}µs  ({args})"
    if c.get("instance"):
        line += f"  this={value(c['instance'])}"
    if c.get("result"):
        line += f"  -> {value(c['result'])}"
    if c.get("exception"):
        line += f"  !! {c['exception']}"
    print(line)


def emit(args, data, human):
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
    else:
        human(data)


def parse_arg(text):
    """Command-line arguments: JSON when it parses (5, true, null, "x"), otherwise text ($12, (1,2,3))."""
    try:
        return json.loads(text)
    except ValueError:
        return text


# ---- commands ---------------------------------------------------------------------------------

def cmd_eval(c, args):
    if args.file:
        code = sys.stdin.read() if args.file == "-" else open(args.file, encoding="utf-8").read()
    elif args.code:
        code = " ".join(args.code)
    else:
        code = sys.stdin.read()
    result = c.post("/api/eval", {"code": code, "mode": "program" if args.program else "script"})
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return result.get("ok", False)
    return print_eval(result)


def cmd_status(c, args):
    def human(s):
        game, dbg = s.get("game"), s["debugger"]
        if game:
            print(f"{game['product']} {game['version']} · Unity {game['unity']} · {game['platform']} · {round(game['fps'])} fps · scene {game['activeScene']}")
        else:
            print("main thread not responding")
        print(f"Unity Runtime Debugger {dbg['version']} · {dbg['loader']['name']} {dbg['loader']['version']} · {dbg['runtime']} · {dbg['handles']} handles · {dbg['hooks']} hooks")
    emit(args, c.get("/api/status"), human)


def cmd_scenes(c, args):
    def human(scenes):
        for s in scenes:
            print(f"{s['name']}  ({len(s['roots'])} roots)")
            for n in s["roots"]:
                print(f"  {n['name']}  ${n['handle']}" + (f"  [{', '.join(n['components'])}]" if n.get("components") else ""))
    emit(args, c.get("/api/scenes"), human)


def cmd_find(c, args):
    def human(nodes):
        for n in nodes:
            print(f"{(n.get('path') + '/') if n.get('path') else ''}{n['name']}  ${n['handle']}" + ("" if n["activeInHierarchy"] else "  (inactive)"))
    emit(args, c.get("/api/objects/search", q=" ".join(args.query)), human)


def handle(text):
    return text.lstrip("$")


def cmd_inspect(c, args):
    emit(args, c.get(f"/api/objects/{handle(args.handle)}", statics=1 if args.statics else None), print_members)


def cmd_get(c, args):
    emit(args, c.get(f"/api/objects/{handle(args.handle)}/members/{urllib.parse.quote(args.name)}"), lambda m: print(value(m.get("value")) if not m.get("error") else m["error"]))


def cmd_set(c, args):
    m = c.request("PUT", f"/api/objects/{handle(args.handle)}/members/{urllib.parse.quote(args.name)}", body={"value": parse_arg(args.value)})
    emit(args, m, lambda m: print(value(m.get("value"))))


def cmd_call(c, args):
    result = c.post(f"/api/objects/{handle(args.handle)}/methods/{urllib.parse.quote(args.method)}", {"args": [parse_arg(a) for a in args.args]})
    emit(args, result, lambda v: print(value(v)))


def cmd_types(c, args):
    def human(types):
        for t in types:
            print(f"{t['kind']:<9} {t['fullName']}  ({t['assembly']})")
    emit(args, c.get("/api/types", q=" ".join(args.query), limit=args.limit), human)


def cmd_logs(c, args):
    page = c.get("/api/logs", limit=args.lines)
    while True:
        for e in page["entries"]:
            if args.json:
                print(json.dumps(e, ensure_ascii=False))
            else:
                print(f"{time.strftime('%H:%M:%S', time.localtime(e['time'] / 1000))} {e['level']:<7} {e['source']}: {e['text']}")
        sys.stdout.flush()
        if not args.follow:
            return
        page = c.get("/api/logs", after=page["cursor"], wait=25)


def cmd_hook(c, args):
    if args.action == "ls":
        def human(hooks):
            for h in hooks:
                folded = len((h.get("native") or {}).get("folded") or [])
                print(f"#{h['id']} {'on ' if h['enabled'] else 'off'} {h['method']}  {h['action']}  hits={h['hits']} recorded={h['recorded']}"
                      + (f"  ICF:{folded} foreign={h['foreign']}" if folded else "") + (f"  error: {h['error']}" if h.get("error") else ""))
        emit(args, c.get("/api/hooks"), human)
    elif args.action == "add":
        body = {"method": args.target, "action": "log", "acceptFolded": args.accept_folded}
        if args.skip:
            body["action"] = "skip"
        if args.returns is not None:
            body.update(action="return", returnValue=args.returns)
        if args.source:
            body["source"] = open(args.source, encoding="utf-8").read()
        hook = c.post("/api/hooks", body)
        emit(args, hook, lambda h: print(f"#{h['id']} {h['method']}" + (f"  {h['native']['location']}" if (h.get("native") or {}).get("location") else "")))
        if args.follow:
            follow_calls(c, hook["id"], args)
    elif args.action == "rm":
        c.request("DELETE", f"/api/hooks/{args.target}")
    elif args.action == "calls":
        follow_calls(c, args.target, args, follow=args.follow)
    elif args.action == "source":
        print(c.get(f"/api/hooks/{args.target}")["source"])


def follow_calls(c, hook_id, args, follow=True):
    cursor = -1
    while True:
        page = c.get(f"/api/hooks/{hook_id}/calls", after=cursor, wait=25 if cursor >= 0 and follow else 0)
        for call in page["calls"]:
            if args.json:
                print(json.dumps(call, ensure_ascii=False))
            else:
                print_call(call)
        sys.stdout.flush()
        cursor = page["cursor"]
        if not follow:
            return


def cmd_help(c, args):
    sys.stdout.write(c.request("GET", "/help/" + (args.topic or ""), raw=True))


# ---- interactive shell ------------------------------------------------------------------------

def is_complete(code):
    """Unbalanced brackets, an open string or comment, or a trailing operator mean "keep typing"."""
    depth, quote, escaped, line, block, last = 0, None, False, False, False, ""
    i = 0
    while i < len(code):
        ch, nxt = code[i], code[i + 1] if i + 1 < len(code) else ""
        if line:
            line = ch != "\n"
        elif block:
            if ch == "*" and nxt == "/":
                block, i = False, i + 1
        elif quote:
            if escaped:
                escaped = False
            elif ch == "\\" and quote != "@":
                escaped = True
            elif quote == "@" and ch == '"':
                if nxt == '"':
                    i += 1
                else:
                    quote = None
            elif ch == quote:
                quote = None
        elif ch == "/" and nxt == "/":
            line, i = True, i + 1
        elif ch == "/" and nxt == "*":
            block, i = True, i + 1
        elif ch == "@" and nxt == '"':
            quote, last, i = "@", '"', i + 1
        elif ch in "\"'":
            quote, last = ch, ch
        else:
            if ch in "([{":
                depth += 1
            elif ch in ")]}":
                depth -= 1
            if not ch.isspace():
                last = ch
        i += 1
    if quote or block or depth > 0:
        return False
    tail = code.rstrip()
    if tail.endswith("++") or tail.endswith("--"):
        return True
    return not (last and last in ",.+-*/%=&|^!?<>(:")


def shell(c):
    if readline:
        try:
            readline.read_history_file(HISTORY_FILE)
        except OSError:
            pass
    try:
        status = c.get("/api/status")
        game = status.get("game") or {}
        print(f"Unity Runtime Debugger shell · {game.get('product', '?')} @ {c.url}")
    except Failure as e:
        print(e, file=sys.stderr)
    print("Enter runs complete code, an empty line runs what you typed · !reset clears the session · exit or Ctrl-D quits")
    while True:
        lines = []
        try:
            while True:
                line = input(">>> " if not lines else "... ")
                if not lines and line.strip() in ("exit", "quit"):
                    return
                if not lines and line.strip() == "!reset":
                    c.request("DELETE", "/api/eval/session")
                    print("session reset")
                    break
                if lines and not line.strip():
                    break
                lines.append(line)
                if is_complete("\n".join(lines)):
                    break
        except EOFError:
            print()
            return
        except KeyboardInterrupt:
            print()
            continue
        finally:
            if readline:
                try:
                    readline.write_history_file(HISTORY_FILE)
                except OSError:
                    pass
        code = "\n".join(lines)
        if code.strip():
            try:
                print_eval(c.post("/api/eval", {"code": code}))
            except Failure as e:
                print(e, file=sys.stderr)


# ---- entry ------------------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(prog="urdbg.py", description=__doc__.split("\n")[0], epilog="Environment: URDBG_URL, URDBG_TOKEN.",
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--url", default=os.environ.get("URDBG_URL", "http://127.0.0.1:18765"), help="debugger address (default $URDBG_URL or http://127.0.0.1:18765)")
    parser.add_argument("--token", default=os.environ.get("URDBG_TOKEN"), help="token (default $URDBG_TOKEN)")
    parser.add_argument("--json", action="store_true", help="print raw JSON")
    sub = parser.add_subparsers(dest="command")

    p = sub.add_parser("eval", help="run C# (script session, or --program)")
    p.add_argument("code", nargs="*", help="code; read from stdin when omitted")
    p.add_argument("-f", "--file", help="read code from a file (- for stdin)")
    p.add_argument("--program", action="store_true", help="program mode: a full compilation unit with Execute()")
    p.set_defaults(run=cmd_eval)

    sub.add_parser("status", help="game and debugger status").set_defaults(run=cmd_status)
    sub.add_parser("scenes", help="scenes and root GameObjects").set_defaults(run=cmd_scenes)

    p = sub.add_parser("find", help="GameObjects by name")
    p.add_argument("query", nargs="+")
    p.set_defaults(run=cmd_find)

    p = sub.add_parser("inspect", help="an object's members")
    p.add_argument("handle")
    p.add_argument("--statics", action="store_true", help="also read static properties (type handles)")
    p.set_defaults(run=cmd_inspect)

    p = sub.add_parser("get", help="read one member")
    p.add_argument("handle")
    p.add_argument("name")
    p.set_defaults(run=cmd_get)

    p = sub.add_parser("set", help="write one member")
    p.add_argument("handle")
    p.add_argument("name")
    p.add_argument("value", help="JSON (5, true, \"x\") or text ((1,2,3), #ff8800, $12)")
    p.set_defaults(run=cmd_set)

    p = sub.add_parser("call", help="call a method")
    p.add_argument("handle")
    p.add_argument("method", help="name or id")
    p.add_argument("args", nargs="*")
    p.set_defaults(run=cmd_call)

    p = sub.add_parser("types", help="search types")
    p.add_argument("query", nargs="+")
    p.add_argument("--limit", type=int, default=30)
    p.set_defaults(run=cmd_types)

    p = sub.add_parser("logs", help="recent log entries")
    p.add_argument("-n", "--lines", type=int, default=50)
    p.add_argument("-f", "--follow", action="store_true")
    p.set_defaults(run=cmd_logs)

    p = sub.add_parser("hook", help="hooks: add, ls, rm, calls, source")
    p.add_argument("action", choices=["add", "ls", "rm", "calls", "source"])
    p.add_argument("target", nargs="?", help="Type::Method for add, a hook id otherwise")
    p.add_argument("--skip", action="store_true", help="add: skip the original method")
    p.add_argument("--return", dest="returns", metavar="EXPR", help="add: skip the original and return this C# expression")
    p.add_argument("--source", metavar="FILE", help="add: your own patch class")
    p.add_argument("--accept-folded", action="store_true", help="add: hook even if ICF folded other methods into it")
    p.add_argument("-f", "--follow", action="store_true", help="print calls as they happen")
    p.set_defaults(run=cmd_hook)

    p = sub.add_parser("help", help="server documentation (/help)")
    p.add_argument("topic", nargs="?")
    p.set_defaults(run=cmd_help)

    args = parser.parse_args()
    if not args.token and args.command != "help":
        parser.error("no token: pass --token or set URDBG_TOKEN (it is printed in the game's log at startup)")
    client = Client(args.url, args.token or "")

    try:
        if args.command is None:
            if sys.stdin.isatty():
                shell(client)
                return 0
            args.code, args.file, args.program = [], None, False
            return 0 if cmd_eval(client, args) else 1
        if args.command == "hook" and args.action != "ls" and not args.target:
            parser.error("hook " + args.action + " needs a target")
        return 0 if args.run(client, args) is not False else 1
    except Failure as e:
        print(e, file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
