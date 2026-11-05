#!/usr/bin/env python3
"""Fail if tracked CMS files contain an OpenAI API key or Azure Blob SAS.

Prints repository-relative paths and rule names only. Never prints setting values.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

SECRET_JSON_KEYS = ("ApiKey", "SasToken")
SAS_SIGNATURE = re.compile(r"(?i)(?:\?|&|\")sig=[A-Za-z0-9%+/=]{8,}")
PS_SAS_LITERAL = re.compile(r"""(?m)^\s*\$sas\s*=\s*(['"])(?:(?!\1).)+\1""")
WEB_ENV_TAG = re.compile(r"<environmentVariable\b[^>]*>", re.IGNORECASE)
WEB_ENV_ATTR = re.compile(r"""\b(name|value)\s*=\s*(['"])(.*?)\2""", re.IGNORECASE)
SECRET_ENV_NAMES = {"OpenAI__ApiKey", "AzureBlob__SasToken"}
LAUNCH_ENV_VAR = re.compile(
    r'"(OpenAI__ApiKey|AzureBlob__SasToken)"\s*:\s*"([^"]*)"'
)
SKIP_DIR_PARTS = {"node_modules", "bin", "obj", ".git"}
SKIP_SUFFIXES = {
    ".png",
    ".jpg",
    ".jpeg",
    ".gif",
    ".webp",
    ".ico",
    ".dll",
    ".exe",
    ".pdb",
    ".zip",
    ".nupkg",
    ".woff",
    ".woff2",
    ".ttf",
    ".eot",
    ".map",
}


def tracked_files() -> list[tuple[str, Path]]:
    out = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT)
    files: list[tuple[str, Path]] = []
    for raw in out.split(b"\0"):
        if not raw:
            continue
        rel = raw.decode()
        parts = rel.replace("\\", "/").split("/")
        if any(part in SKIP_DIR_PARTS for part in parts):
            continue
        path = ROOT / rel
        if not path.is_file():
            continue
        if path.suffix.lower() in SKIP_SUFFIXES:
            continue
        files.append((rel.replace("\\", "/"), path))
    return files


def check_json_secrets(rel: str, text: str, failures: list[tuple[str, str]]) -> None:
    name = Path(rel).name
    if not (name.startswith("appsettings") and name.endswith(".json")):
        return
    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        failures.append((rel, "invalid-json"))
        return

    def walk(obj: object, prefix: str) -> None:
        if isinstance(obj, dict):
            for key, value in obj.items():
                path = f"{prefix}:{key}" if prefix else key
                if key in SECRET_JSON_KEYS and isinstance(value, str) and value.strip():
                    failures.append((rel, f"{path}-not-empty"))
                elif isinstance(value, (dict, list)):
                    walk(value, path)
        elif isinstance(obj, list):
            for item in obj:
                walk(item, prefix)

    walk(data, "")


def check_text(rel: str, text: str, failures: list[tuple[str, str]]) -> None:
    if "\0" in text:
        return
    for index, line in enumerate(text.splitlines(), 1):
        if SAS_SIGNATURE.search(line):
            failures.append((rel, f"line-{index}-sas-signature"))
        if rel.lower().endswith(".ps1") and PS_SAS_LITERAL.search(line):
            failures.append((rel, f"line-{index}-literal-sas-assignment"))

    for tag in WEB_ENV_TAG.finditer(text):
        attrs = {match.group(1).lower(): match.group(3) for match in WEB_ENV_ATTR.finditer(tag.group(0))}
        name = attrs.get("name", "")
        value = attrs.get("value", "")
        if name in SECRET_ENV_NAMES and value.strip():
            failures.append((rel, f"{name}-not-empty"))

    if Path(rel).name == "launchSettings.json":
        for match in LAUNCH_ENV_VAR.finditer(text):
            if match.group(2).strip():
                failures.append((rel, f"{match.group(1)}-not-empty"))


def scan_files(files: list[tuple[str, str]]) -> list[tuple[str, str]]:
    failures: list[tuple[str, str]] = []
    for rel, text in files:
        check_json_secrets(rel, text, failures)
        check_text(rel, text, failures)
    return failures


def scan_repository() -> tuple[list[tuple[str, str]], int]:
    failures: list[tuple[str, str]] = []
    files = tracked_files()
    for rel, path in files:
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            failures.append((rel, "unreadable"))
            continue
        check_json_secrets(rel, text, failures)
        check_text(rel, text, failures)
    return failures, len(files)


def print_failures(failures: list[tuple[str, str]]) -> None:
    for rel, rule in failures:
        print(f"FAIL {rel} {rule}")


def self_test() -> int:
    fake_key = "0123456789abcdef0123456789abcdef"
    fake_sig = "FAKESIGNATUREVALUE12"
    fake_sas = f"sv=2020-02-10&spr=https&sr=c&sp=r&sig={fake_sig}"
    files = [
        (
            "Application/appsettings.json",
            json.dumps({"OpenAI": {"ApiKey": fake_key}, "AzureBlob": {"SasToken": fake_sas}}),
        ),
        (
            "Application/SqlScripts/example.ps1",
            f"$sas = '{fake_sas}'\n",
        ),
        (
            "Application/web.config",
            f'<environmentVariable name="OpenAI__ApiKey" value="{fake_key}" />',
        ),
        (
            "Application/appsettings.Secrets.example.json",
            json.dumps({"OpenAI": {"ApiKey": ""}, "AzureBlob": {"SasToken": ""}}),
        ),
    ]
    failures = scan_files(files)
    rendered = "\n".join(f"FAIL {rel} {rule}" for rel, rule in failures)
    leaked = [token for token in (fake_key, fake_sig, fake_sas) if token in rendered]
    rules = {rule for _, rule in failures}
    expected = {
        "OpenAI:ApiKey-not-empty",
        "AzureBlob:SasToken-not-empty",
        "line-1-sas-signature",
        "line-1-literal-sas-assignment",
        "OpenAI__ApiKey-not-empty",
    }
    clean = scan_files(
        [
            (
                "Application/appsettings.json",
                json.dumps(
                    {
                        "OpenAI": {"ApiKey": "", "Model": "gpt-4o"},
                        "AzureBlob": {"SasToken": "", "MaxUploadBytes": 1},
                    }
                ),
            )
        ]
    )
    ok = not leaked and expected.issubset(rules) and not clean
    if not ok:
        print("self-test failed")
        print(f"rules={sorted(rules)}")
        print(f"leaked={bool(leaked)} clean_findings={len(clean)}")
        return 1
    print("self-test passed")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", help="run fixture checks that never print sample values")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()

    failures, count = scan_repository()
    if failures:
        print_failures(failures)
        print(f"tracked secret check failed: {len(failures)} finding(s) in {count} file(s)")
        return 1
    print(f"OK tracked secret check: {count} file(s), 0 findings")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
