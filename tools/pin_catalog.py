#!/usr/bin/env python3
"""Fissa revisione, dimensioni e SHA-256 del catalogo modelli (ADR-0003, issue #4).

Legge i metadati LFS dall'API pubblica di Hugging Face (non scarica i modelli) e aggiorna
src/CodexVtt.Core/Models/models-catalog.json. Con --check verifica che hash e dimensioni
del catalogo coincidano con quelli della revisione fissata (codice 1 se no).

Uso:
  python3 tools/pin_catalog.py               # fissa all'ultima revisione di main
  python3 tools/pin_catalog.py --revision <sha>
  python3 tools/pin_catalog.py --check
"""
import argparse
import json
import pathlib
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
CATALOG = ROOT / "src" / "CodexVtt.Core" / "Models" / "models-catalog.json"
API = "https://huggingface.co/api/models/{repo}"


def get_json(url: str):
    request = urllib.request.Request(url, headers={"User-Agent": "codexvtt-catalog-pin"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--revision", help="commit del repository Hugging Face (default: main attuale)")
    parser.add_argument("--check", action="store_true", help="verifica senza modificare")
    args = parser.parse_args()

    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    repo = catalog["source"]["repository"]
    pinned = catalog["source"].get("revision")
    # In verifica si controlla la revisione già fissata: main su Hugging Face può avanzare.
    revision = args.revision or (pinned if args.check and pinned else get_json(API.format(repo=repo))["sha"])
    tree = get_json(API.format(repo=repo) + f"/tree/{revision}")
    files = {entry["path"]: entry for entry in tree if entry.get("type") == "file"}

    problems = []
    for model in catalog["models"]:
        entry = files.get(model["file"])
        if entry is None or "lfs" not in entry:
            problems.append(f"{model['id']}: {model['file']} non trovato nella revisione {revision}")
            continue
        sha256, size = entry["lfs"]["oid"], entry["lfs"]["size"]
        if args.check:
            if model.get("sha256") != sha256 or model.get("sizeBytes") != size:
                problems.append(f"{model['id']}: atteso sha256={sha256} size={size}, trovato sha256={model.get('sha256')} size={model.get('sizeBytes')}")
        else:
            model["sha256"], model["sizeBytes"] = sha256, size

    if args.check and pinned != revision:
        problems.append(f"revisione catalogo {catalog['source'].get('revision')} diversa da {revision}")

    if not args.check:
        catalog["source"]["revision"] = revision
        CATALOG.write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(json.dumps({"revision": revision, "models": [
        {"id": m["id"], "file": m["file"], "sizeBytes": m.get("sizeBytes"), "sha256": m.get("sha256")} for m in catalog["models"]
    ]}, indent=2))
    for problem in problems:
        print("PROBLEMA:", problem, file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
