"""Stands in for the docker CLI in tests: records what the server asked for."""
import sys

args = sys.argv[1:]
if args[:1] == ["version"]:
    print("27.0-fake")
elif args[:2] == ["image", "inspect"] or args[:1] in (["pull"], ["kill"]):
    pass
elif args[:1] == ["run"]:
    print("ARGS:", " ".join(args), flush=True)
    print("STDIN:", sys.stdin.read(), flush=True)
    mount = args[args.index("--mount") + 1]
    source = dict(part.split("=", 1) for part in mount.split(",") if "=" in part)["source"]
    command = args[-1]
    entry = command.split("'")[1].replace("/code/", "")
    with open(source + "/" + entry, encoding="utf-8") as f:
        print(f.read(), flush=True)
    print("DONE", flush=True)
else:
    sys.exit(1)
