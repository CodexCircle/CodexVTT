#!/usr/bin/env python3
"""Fissa revisione, dimensioni e SHA-256 del catalogo modelli (ADR-0003, ADR-0009).

Legge i metadati dall'API pubblica di Hugging Face e aggiorna
src/CodexVtt.Core/Models/models-catalog.json. I modelli Whisper (file singolo) usano la sorgente del catalogo;
i modelli a più file (Parakeet, Canary, Cohere) hanno una sorgente propria e si fissano uno alla volta, perché
sono export di terzi: cambiare la loro revisione è una scelta, non un aggiornamento automatico.
I file piccoli senza metadati LFS (tokens.txt) vengono scaricati e calcolati; i pesi non vengono mai scaricati.

Uso:
  python3 tools/pin_catalog.py                          # fissa i modelli Whisper all'ultima revisione
  python3 tools/pin_catalog.py --revision <sha>         # idem, a una revisione precisa
  python3 tools/pin_catalog.py --model <id> [--revision <sha>]   # fissa un modello a più file
  python3 tools/pin_catalog.py --check                  # verifica tutto senza modificare (codice 1 se non coincide)
"""
import argparse
import hashlib
import json
import pathlib
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
CATALOG = ROOT / "src" / "CodexVtt.Core" / "Models" / "models-catalog.json"
API = "https://huggingface.co/api/models/{repo}"
SMALL_FILE_BYTES = 5_000_000


def get_json(url: str):
    request = urllib.request.Request(url, headers={"User-Agent": "codexvtt-catalog-pin"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def sha256_of(url: str) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": "codexvtt-catalog-pin"})
    digest = hashlib.sha256()
    with urllib.request.urlopen(request, timeout=120) as response:
        for block in iter(lambda: response.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def tree_files(repo: str, revision: str) -> dict:
    tree = get_json(API.format(repo=repo) + f"/tree/{revision}")
    return {entry["path"]: entry for entry in tree if entry.get("type") == "file"}


def file_facts(repo: str, revision: str, entry: dict, path: str):
    """(sha256, size) di un file: dai metadati LFS, oppure calcolato se è piccolo."""
    if "lfs" in entry:
        return entry["lfs"]["oid"], entry["lfs"]["size"]
    if entry["size"] > SMALL_FILE_BYTES:
        raise SystemExit(f"{repo}/{path}: {entry['size']} byte senza metadati LFS, troppo grande per calcolare l'hash")
    return sha256_of(f"https://huggingface.co/{repo}/resolve/{revision}/{path}"), entry["size"]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--revision", help="commit del repository Hugging Face (default: main attuale)")
    parser.add_argument("--model", help="id di un modello a più file da fissare")
    parser.add_argument("--check", action="store_true", help="verifica senza modificare")
    args = parser.parse_args()

    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    problems = []

    # --- modelli Whisper: file singolo, sorgente del catalogo
    repo = catalog["source"]["repository"]
    pinned = catalog["source"].get("revision")
    pin_whisper = not args.check and not args.model
    if pin_whisper or args.check:
        # In verifica si controlla la revisione già fissata: main su Hugging Face può avanzare.
        revision = args.revision or (pinned if args.check and pinned else get_json(API.format(repo=repo))["sha"])
        files = tree_files(repo, revision)
        for model in catalog["models"]:
            if model.get("files"):
                continue
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
            problems.append(f"revisione catalogo {pinned} diversa da {revision}")
        if pin_whisper:
            catalog["source"]["revision"] = revision

    # --- modelli a più file: sorgente propria
    for model in catalog["models"]:
        if not model.get("files"):
            continue
        if args.model and model["id"] != args.model:
            continue
        if not args.check and not args.model:
            continue  # non si toccano senza --model
        source = model["source"]
        if args.check:
            revision = source["revision"]
        elif args.revision:
            revision = args.revision
        else:
            revision = get_json(API.format(repo=source["repository"]))["sha"]
        files = tree_files(source["repository"], revision)
        total = 0
        for item in model["files"]:
            entry = files.get(item["path"])
            if entry is None:
                problems.append(f"{model['id']}: {item['path']} non trovato in {source['repository']}@{revision[:10]}")
                continue
            sha256, size = file_facts(source["repository"], revision, entry, item["path"])
            if args.check:
                if item["sha256"] != sha256 or item["sizeBytes"] != size:
                    problems.append(f"{model['id']}/{item['path']}: atteso sha256={sha256} size={size}, trovato sha256={item['sha256']} size={item['sizeBytes']}")
            else:
                item["sha256"], item["sizeBytes"] = sha256, size
            total += size
        if args.check:
            if model["sizeBytes"] != sum(i["sizeBytes"] for i in model["files"]):
                problems.append(f"{model['id']}: sizeBytes non è la somma dei file")
        else:
            source["revision"] = revision
            model["sizeBytes"] = total

    if not args.check:
        CATALOG.write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(json.dumps({"revision": catalog["source"].get("revision"), "models": [
        {"id": m["id"], "engine": m.get("engine", "whisper"), "sizeBytes": m.get("sizeBytes"),
         "revision": (m.get("source") or catalog["source"]).get("revision")} for m in catalog["models"]
    ]}, indent=2))
    for problem in problems:
        print("PROBLEMA:", problem, file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
