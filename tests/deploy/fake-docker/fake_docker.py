#!/usr/bin/env python3
"""Fake docker CLI for the deploy script tests (ADR 0163). Python standard library only.

GitHub's Windows runners only run Windows containers, so the install / backup / restore / upgrade scripts are tested against this fake:
it writes compose-style progress to standard error (the output that broke Windows PowerShell 5.1), records every call and plays the
binary parts of a backup and a restore.

Environment:
  FAKE_DOCKER_STATE  folder for the call log (calls.jsonl) and the files below (required)
  FAKE_DOCKER_FAIL   "<command prefix>=<exit code>;..." matched against the call without the compose -f / --env-file / --profile
                     options, e.g. "compose build=17;info=1"
  FAKE_DOCKER_APP_STOPPED  "1": "compose ps -q app" prints nothing (the application is not running)
  FAKE_DOCKER_IMAGES  unset: every image exists. Set (space-separated references; "none" for no image): only these exist at the start;
                     "load", "tag", "compose pull" and "compose build" add images (images.txt), "image inspect" fails for any other
  FAKE_DOCKER_LOAD_IMAGE  the image "load" reports ("Loaded image: <ref>"; default strategy-app:1.0.0); "load" fails for a missing file

Files in FAKE_DOCKER_STATE: calls.jsonl (one JSON object per call: args, cmd, env, stdinBytes), emitted-pg_dump.bin and
emitted-files.tar.gz (bytes written for pg_dump / tar), restored.dump and restored-files.tar.gz (standard input of pg_restore / the files
restore), psql.sql (SQL received by psql, on standard input or with -c), images.txt (images added during the run). Each call record also
has "files": the base names of the compose files given with -f, in order.
"""
import json
import os
import sys

COMPOSE_GLOBAL_OPTIONS = ("-f", "--file", "--env-file", "--profile", "-p", "--project-name")
TRACKED_ENV = ("APP_VERSION", "APP_IMAGE", "GIT_REVISION", "BUILD_CREATED", "BUILD_CA_FILE", "REGISTRY_PREFIX")
DEFAULT_APP_IMAGE = "ghcr.io/hashichemnad/strategy-app"

# Bytes that any text re-encoding would change: NUL, CR, LF, CR LF, 0xFF 0xFE (a UTF-16 BOM) and every other byte value.
DUMP_BYTES = b"PGDMP\x00\x01\r\n\n\r\xff\xfe" + bytes(range(256)) + b"\n-- end of fake dump\r\n"
TAR_BYTES = b"\x1f\x8b\x08\x00fake-files-archive\x00\r\n\x1a\xff" + bytes(range(255, -1, -1))
TABLE_COUNTS = "core.users\t3\nkpi.kpi\t5\n"
SETUP_LOG = ('{"@t":"2026-11-07T10:00:00.0000000Z","@mt":"FIRST-RUN SETUP: open {SetupUrl} and enter the setup token",'
             '"@l":"Warning","SetupUrl":"/setup","SetupToken":"fake-setup-token-0163"}')


def out(text):
    sys.stdout.buffer.write(text.encode("utf-8"))
    sys.stdout.buffer.flush()


def err(text):
    # Real docker writes UTF-8 (the check mark below) to standard error.
    sys.stderr.buffer.write(text.encode("utf-8"))
    sys.stderr.buffer.flush()


def progress(action, items):
    err("[+] %s %d/%d\n" % (action, len(items), len(items)))
    for item in items:
        err(" ✔ %s  %s\n" % (item, "Started" if action == "Running" else "Done"))


def read_stdin():
    return sys.stdin.buffer.read()


def append(state, name, data):
    with open(os.path.join(state, name), "ab") as f:
        f.write(data)


def write(state, name, data):
    with open(os.path.join(state, name), "wb") as f:
        f.write(data)


def normalise(argv):
    args = list(argv)
    if not args or args[0] != "compose":
        return args
    result = ["compose"]
    i = 1
    while i < len(args) and args[i] in COMPOSE_GLOBAL_OPTIONS:
        i += 2
    return result + args[i:]


def compose_files(argv):
    files = []
    args = list(argv)
    if args[:1] == ["compose"]:
        i = 1
        while i + 1 < len(args) and args[i] in COMPOSE_GLOBAL_OPTIONS:
            if args[i] in ("-f", "--file"):
                files.append(os.path.basename(args[i + 1].replace("\\", "/")))
            i += 2
    return files


def images_tracked():
    return "FAKE_DOCKER_IMAGES" in os.environ


def image_exists(state, ref):
    if not images_tracked():
        return True
    known = os.environ.get("FAKE_DOCKER_IMAGES", "").split()
    path = os.path.join(state, "images.txt")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            known += f.read().split()
    return ref in known


def add_image(state, ref):
    append(state, "images.txt", (ref + "\n").encode("utf-8"))


def compose_image():
    # What compose resolves for migrate / app: ${APP_IMAGE:-ghcr.io/...}:${APP_VERSION:-...} from the environment the scripts export.
    return "%s:%s" % (os.environ.get("APP_IMAGE") or DEFAULT_APP_IMAGE, os.environ.get("APP_VERSION") or "1.0.1")


def failure_code(cmd):
    for entry in os.environ.get("FAKE_DOCKER_FAIL", "").split(";"):
        if "=" not in entry:
            continue
        prefix, code = entry.rsplit("=", 1)
        prefix = prefix.strip()
        if prefix and (cmd == prefix or cmd.startswith(prefix + " ")):
            return int(code)
    return 0


def after(args, option):
    return args[args.index(option) + 1] if option in args and args.index(option) + 1 < len(args) else ""


def main(argv):
    state = os.environ.get("FAKE_DOCKER_STATE")
    if not state:
        err("fake docker: FAKE_DOCKER_STATE is not set\n")
        return 99
    os.makedirs(state, exist_ok=True)
    args = normalise(argv)
    cmd = " ".join(args)
    sub = args[1] if len(args) > 1 and args[0] == "compose" else ""

    # Standard input is read only where the scripts send data, so a call never waits for input it does not get.
    stdin = b""
    is_psql = sub == "exec" and "psql" in args
    entrypoint = after(args, "--entrypoint")
    if is_psql and "-c" not in args:
        stdin = read_stdin()
    elif sub == "exec" and "pg_restore" in args:
        stdin = read_stdin()
    elif sub == "run" and entrypoint == "sh":
        stdin = read_stdin()

    record = {"args": args, "cmd": cmd, "files": compose_files(argv), "env": {k: os.environ.get(k, "") for k in TRACKED_ENV}, "stdinBytes": len(stdin)}
    append(state, "calls.jsonl", (json.dumps(record, ensure_ascii=True) + "\n").encode("ascii"))

    code = failure_code(cmd)
    if code:
        err("Error response from daemon: fake failure of '%s'\n" % cmd)
        return code

    if args[:1] == ["info"]:
        out("Server Version: 99.0.0-fake\n")
        err("WARNING: No swap limit support\n")
    elif args[:2] == ["compose", "version"]:
        out("Docker Compose version v2.99.0-fake\n")
    elif args[:2] == ["image", "inspect"]:
        if not image_exists(state, args[-1]):
            err("Error response from daemon: No such image: %s\n" % args[-1])
            return 1
        if "-f" in args:
            out('{"org.opencontainers.image.title":"Integrated Strategy System","org.opencontainers.image.version":"1.0.0"}\n')
        else:
            out('[{"Id":"sha256:fake"}]\n')
    elif args[:1] == ["load"]:
        archive = after(args, "-i")
        if archive and not os.path.exists(archive):
            err("open %s: no such file or directory\n" % archive)
            return 1
        loaded = os.environ.get("FAKE_DOCKER_LOAD_IMAGE") or "strategy-app:1.0.0"
        add_image(state, loaded)
        err("Loading layer  1.024kB/1.024kB\n")
        out("Loaded image: %s\n" % loaded)
    elif args[:1] == ["tag"]:
        if len(args) != 3 or not image_exists(state, args[1]):
            err("Error response from daemon: No such image: %s\n" % (args[1] if len(args) > 1 else ""))
            return 1
        add_image(state, args[2])
    elif sub == "pull":
        add_image(state, compose_image())
        progress("Pulling", [a for a in args[2:] if not a.startswith("-")] or ["migrate", "app"])
    elif args[:1] == ["inspect"]:
        out("healthy\n")
    elif sub == "build":
        add_image(state, compose_image())
        err("#1 [internal] load build definition from Dockerfile\n#1 DONE 0.0s\n")
        progress("Building", ["strategy-app"])
    elif sub == "up":
        progress("Running", [a for a in args[2:] if not a.startswith("-")] or ["postgres", "gotenberg", "migrate", "app"])
    elif sub == "stop":
        progress("Stopping", args[2:] or ["app"])
    elif sub == "ps":
        if "-q" in args:
            if os.environ.get("FAKE_DOCKER_APP_STOPPED") != "1":
                out("fakeappcontainer0163\n")
        # --format {{.Publishers}}: no published ports, so the port check never sees its own installation.
    elif sub == "logs":
        out('{"@t":"2026-11-07T09:59:59.0000000Z","@mt":"Now listening on: {address}","address":"http://[::]:8080"}\n')
        out(SETUP_LOG + "\n")
    elif sub == "exec":
        if "pg_isready" in args:
            out("/var/run/postgresql:5432 - accepting connections\n")
        elif "pg_dump" in args:
            write(state, "emitted-pg_dump.bin", DUMP_BYTES)
            sys.stdout.buffer.write(DUMP_BYTES)
            sys.stdout.buffer.flush()
        elif "pg_restore" in args:
            write(state, "restored.dump", stdin)
        elif is_psql:
            if "-At" in args:
                append(state, "psql.sql", stdin)
                out(TABLE_COUNTS)
            else:
                sql = stdin if stdin else "\n".join(args[i + 1] for i, a in enumerate(args) if a == "-c").encode("utf-8") + b"\n"
                append(state, "psql.sql", sql)
    elif sub == "run":
        if entrypoint == "tar":
            write(state, "emitted-files.tar.gz", TAR_BYTES)
            sys.stdout.buffer.write(TAR_BYTES)
            sys.stdout.buffer.flush()
        elif entrypoint == "sh":
            write(state, "restored-files.tar.gz", stdin)
        elif "--verify-audit-chain" in args:
            out("Audit hash chain intact: 42 entries verified (fake)\n")
        else:
            progress("Creating", [a for a in args[2:] if not a.startswith("-")][:1] or ["migrate"])
            out("Migrations applied (fake)\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
