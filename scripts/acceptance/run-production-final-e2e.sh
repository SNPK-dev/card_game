#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

PYTHON_BIN="${PYTHON:-}"
if [[ -z "$PYTHON_BIN" ]]; then
  if command -v cmd.exe >/dev/null 2>&1 && cmd.exe /c py -3 --version >/dev/null 2>&1; then
    PYTHON_BIN="cmd.exe /c py -3"
  elif command -v python3 >/dev/null 2>&1; then
    PYTHON_BIN="python3"
  elif command -v python >/dev/null 2>&1; then
    PYTHON_BIN="python"
  elif command -v py >/dev/null 2>&1; then
    PYTHON_BIN="py -3"
  else
    echo "python was not found on PATH. Set PYTHON=/path/to/python and rerun." >&2
    exit 127
  fi
fi

# shellcheck disable=SC2086
$PYTHON_BIN - "$@" <<'PY'
import argparse
import datetime as dt
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

READY = "production-e2e-full-scale-acceptance-ready"
FAILED = "production-e2e-full-scale-acceptance-failed"
PRODUCT_BLOCKED = "production-e2e-full-scale-acceptance-product-blocked"

REQUIRED_FILES = [
    "summary.json",
    "human-report.md",
    "run-config.json",
    "environment.json",
    "generation/cards.generated.jsonl",
    "generation/card-pack-manifest.json",
    "generation/validation-report.json",
    "generation/catalog-publish-report.json",
    "generation/catalog.json",
    "generation/ruleset.json",
    "decks/deck-player-one.json",
    "decks/deck-player-two.json",
    "decks/deck-validation-report.json",
    "decks/deck-composition.md",
    "match/sessions.json",
    "match/match-start.json",
    "match/command-log.jsonl",
    "match/event-log.jsonl",
    "match/state-snapshots.jsonl",
    "match/reconnect-report.json",
    "match/hidden-info-report.json",
    "match/bot-decision-log.jsonl",
    "replay/replay-verification-report.json",
    "final/final-verdict.json",
    "environment-remediation.md",
    "generator-remediation.md",
    "previous-run-audit.md",
    "attempts/attempt-log.jsonl",
]

def now_utc():
    return dt.datetime.now(dt.timezone.utc)

def iso(ts):
    return ts.isoformat().replace("+00:00", "Z")

def json_default(value):
    if isinstance(value, pathlib.Path):
        return str(value)
    return str(value)

def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True, default=json_default) + "\n", encoding="utf-8")

def append_jsonl(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(value, sort_keys=True, default=json_default) + "\n")

def append_attempt(out, kind, status, detail):
    append_jsonl(out / "attempts/attempt-log.jsonl", {
        "at": iso(now_utc()),
        "kind": kind,
        "status": status,
        "detail": detail,
    })

def http_json(method, base_url, path, body=None, token=None, timeout=10):
    url = urllib.parse.urljoin(base_url.rstrip("/") + "/", path.lstrip("/"))
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Accept", "application/json")
    if body is not None:
        request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("X-Session-Token", token)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        payload = response.read()
        if not payload:
            return {"statusCode": response.status}
        return json.loads(payload.decode("utf-8"))

def http_json_idempotent_post(base_url, path, body, timeout=10):
    try:
        return http_json("POST", base_url, path, body, timeout=timeout)
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        if error.code == 409:
            return {"statusCode": 409, "alreadyExists": True, "detail": detail}
        raise

def submit_command_with_rate_limit(base_url, match_id, submission, out, timeout=20):
    path = f"/matches/{match_id}/commands"
    last_error = ""
    for attempt in range(1, 4):
        if attempt == 1:
            time.sleep(1.1)
        try:
            return http_json("POST", base_url, path, submission, timeout=timeout)
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            last_error = f"HTTP {error.code} from {path}: {detail}"
            append_attempt(out, "command-submit", "http-error", {
                "attempt": attempt,
                "statusCode": error.code,
                "detail": detail,
            })
            if error.code != 429 or attempt == 3:
                raise RuntimeError(last_error) from error
            retry_after = error.headers.get("Retry-After")
            try:
                sleep_seconds = max(1.0, min(75.0, float(retry_after))) if retry_after else 61.0
            except ValueError:
                sleep_seconds = 61.0
            time.sleep(sleep_seconds)
    raise RuntimeError(last_error or "Command submission failed after rate-limit retries.")

def try_health(base_url):
    try:
        return http_json("GET", base_url, "/health", timeout=3).get("status") == "ok"
    except Exception:
        return False

def command_available(command):
    try:
        subprocess.run([command, "--version"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False, timeout=10)
        return True
    except Exception:
        return False

def diagnose_port(port):
    if os.name == "nt":
        command = ["powershell", "-NoProfile", "-Command", f"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 5 LocalAddress,LocalPort,OwningProcess | ConvertTo-Json -Compress"]
        try:
            result = subprocess.run(command, text=True, capture_output=True, timeout=10)
            data = result.stdout.strip()
            return data if data else "no listening process detected"
        except Exception as error:
            return f"port diagnosis failed: {error}"
    for command in (["lsof", "-i", f":{port}"], ["ss", "-ltnp"], ["netstat", "-ano"]):
        try:
            result = subprocess.run(command, text=True, capture_output=True, timeout=10)
            output = result.stdout.strip()
            if output:
                return output[:2000]
        except Exception:
            continue
    return "no listening process detected"

def wsl_host_ip():
    if os.name == "nt":
        return ""
    try:
        release = pathlib.Path("/proc/sys/kernel/osrelease").read_text(encoding="utf-8", errors="ignore").lower()
        if "microsoft" not in release and "wsl" not in release:
            return ""
        result = subprocess.run(["sh", "-lc", "ip route | awk '/^default/ {print $3; exit}'"], text=True, capture_output=True, timeout=5)
        return result.stdout.strip()
    except Exception:
        return ""

def backend_probe_urls(base_url, port):
    parsed = urllib.parse.urlparse(base_url)
    scheme = parsed.scheme or "http"
    host = parsed.hostname or "localhost"
    urls = [f"{scheme}://{host}:{port}"]
    gateway = wsl_host_ip()
    if gateway and host in ("localhost", "127.0.0.1", "::1"):
        urls.append(f"{scheme}://{gateway}:{port}")
    return list(dict.fromkeys(urls))

def backend_health_payload(base_url):
    try:
        return http_json("GET", base_url, "/health", timeout=3)
    except Exception as error:
        return {"error": str(error)}

def backend_capabilities_payload(base_url):
    try:
        return http_json("GET", base_url, "/engine/capabilities", timeout=10)
    except Exception as error:
        return {"error": str(error)}

def start_backend_if_needed(base_url, keep_running, out, args):
    parsed = urllib.parse.urlparse(base_url)
    requested_port = parsed.port or 80
    initial_diagnosis = diagnose_port(requested_port)
    append_attempt(out, "backend", "diagnose", {
        "backendUrl": base_url,
        "port": requested_port,
        "portDiagnosis": initial_diagnosis,
    })
    healthy_existing_url = next((url for url in backend_probe_urls(base_url, requested_port) if try_health(url)), "")
    if healthy_existing_url:
        if healthy_existing_url != base_url:
            args.backend_url = healthy_existing_url
        capabilities = backend_capabilities_payload(base_url)
        if healthy_existing_url != base_url:
            capabilities = backend_capabilities_payload(healthy_existing_url)
        (out / "environment-remediation.md").write_text(
            "# Environment remediation\n\n"
            f"- Initial backend URL: `{base_url}`\n"
            f"- Occupied port: `{requested_port}`\n"
            f"- Process detected: `{initial_diagnosis}`\n"
            "- Action taken: existing listener was the expected healthy backend; no process was stopped.\n"
            f"- Backend command used: existing listener\n"
            f"- Final backend URL: `{healthy_existing_url}`\n"
            f"- Health result: `{json.dumps(backend_health_payload(healthy_existing_url), sort_keys=True)}`\n"
            f"- Capabilities result: `{json.dumps(capabilities, sort_keys=True)[:2000]}`\n",
            encoding="utf-8")
        append_attempt(out, "backend", "healthy-existing", {"backendUrl": healthy_existing_url})
        return None, True, healthy_existing_url, capabilities
    dotnet = os.environ.get("DOTNET")
    if not dotnet:
        for candidate in ("dotnet", "dotnet.exe"):
            try:
                subprocess.run([candidate, "--version"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
                dotnet = candidate
                break
            except Exception:
                continue
    if not dotnet:
        raise RuntimeError("dotnet was not found on PATH and backend health is not already reachable.")

    candidate_ports = []
    for port in [requested_port, 5001, 5010, 5050, 5080]:
        if port not in candidate_ports:
            candidate_ports.append(port)
    started_processes = []
    log = out / "backend.log"
    for attempt, port in enumerate(candidate_ports, start=1):
        candidate_url = f"{parsed.scheme or 'http'}://{parsed.hostname or 'localhost'}:{port}"
        probe_urls = backend_probe_urls(base_url, port)
        diagnosis = diagnose_port(port)
        append_attempt(out, "backend", "start-attempt", {
            "attempt": attempt,
            "backendUrl": candidate_url,
            "probeUrls": probe_urls,
            "port": port,
            "portDiagnosis": diagnosis,
        })
        healthy_probe_url = next((url for url in probe_urls if try_health(url)), "")
        if healthy_probe_url:
            capabilities = backend_capabilities_payload(healthy_probe_url)
            args.backend_url = healthy_probe_url
            (out / "environment-remediation.md").write_text(
                "# Environment remediation\n\n"
                f"- Initial backend URL: `{base_url}`\n"
                f"- Occupied port: `{requested_port}`\n"
                f"- Process detected: `{initial_diagnosis}`\n"
                "- Action taken: selected an already healthy backend listener during fallback probing.\n"
                "- Backend command used: existing listener\n"
                f"- Final backend URL: `{healthy_probe_url}`\n"
                f"- Health result: `{json.dumps(backend_health_payload(healthy_probe_url), sort_keys=True)}`\n"
                f"- Capabilities result: `{json.dumps(capabilities, sort_keys=True)[:2000]}`\n",
                encoding="utf-8")
            return None, True, healthy_probe_url, capabilities
        if "no listening process detected" not in diagnosis and port == requested_port:
            continue
        env = os.environ.copy()
        start_url = f"{parsed.scheme or 'http'}://0.0.0.0:{port}" if wsl_host_ip() else candidate_url
        health_url = probe_urls[-1] if wsl_host_ip() and len(probe_urls) > 1 else candidate_url
        env["ASPNETCORE_URLS"] = start_url
        handle = log.open("ab")
        process = subprocess.Popen(
            [dotnet, "run", "--project", "src/Game.Api", "--configuration", "Release", "--no-launch-profile", "--urls", start_url],
            stdout=handle,
            stderr=subprocess.STDOUT,
            env=env,
        )
        started_processes.append(process)
        for _ in range(90):
            if try_health(health_url):
                capabilities = backend_capabilities_payload(health_url)
                args.backend_url = health_url
                (out / "environment-remediation.md").write_text(
                    "# Environment remediation\n\n"
                    f"- Initial backend URL: `{base_url}`\n"
                    f"- Occupied port: `{requested_port}`\n"
                    f"- Process detected: `{initial_diagnosis}`\n"
                    f"- Action taken: started backend on candidate port `{port}`.\n"
                    f"- Backend command used: `{dotnet} run --project src/Game.Api --configuration Release --no-launch-profile --urls {start_url}`\n"
                    f"- Final backend URL: `{health_url}`\n"
                    f"- Health result: `{json.dumps(backend_health_payload(health_url), sort_keys=True)}`\n"
                    f"- Capabilities result: `{json.dumps(capabilities, sort_keys=True)[:2000]}`\n",
                    encoding="utf-8")
                return process, True, health_url, capabilities
            if process.poll() is not None:
                break
            time.sleep(1)
    (out / "environment-remediation.md").write_text(
        "# Environment remediation\n\n"
        f"- Initial backend URL: `{base_url}`\n"
        f"- Occupied port: `{requested_port}`\n"
        f"- Process detected: `{initial_diagnosis}`\n"
        f"- Action taken: tried backend ports `{candidate_ports}` using the repo-supported Game.Api startup path.\n"
        "- Final backend URL: none healthy\n"
        f"- Health result: `{json.dumps(backend_health_payload(base_url), sort_keys=True)}`\n"
        "- Capabilities result: unavailable\n",
        encoding="utf-8")
    return (started_processes[-1] if started_processes else None), False, base_url, {}

def ensure_generator(out):
    generator_command = os.environ.get("CCG_FINAL_E2E_GENERATOR_COMMAND")
    discovered = False
    if not generator_command and pathlib.Path("scripts/acceptance/run_final_e2e_generator_adapter.py").exists():
        generator_command = subprocess.list2cmdline([sys.executable, "scripts/acceptance/run_final_e2e_generator_adapter.py"])
        os.environ["CCG_FINAL_E2E_GENERATOR_COMMAND"] = generator_command
        discovered = True
    elif not generator_command and pathlib.Path("scripts/acceptance/run-final-e2e-generator-adapter.sh").exists():
        generator_command = "bash scripts/acceptance/run-final-e2e-generator-adapter.sh"
        os.environ["CCG_FINAL_E2E_GENERATOR_COMMAND"] = generator_command
        discovered = True
    gateway = wsl_host_ip()
    default_structured_host = gateway if gateway else "127.0.0.1"
    os.environ.setdefault("CCG_LOCAL_STRUCTURED_ENDPOINT", f"http://{default_structured_host}:8011/v1/structured-card")
    os.environ.setdefault("CCG_LOCAL_STRUCTURED_MODEL", "qwen3.5:latest")
    provider_health = {}
    provider_base = os.environ["CCG_LOCAL_STRUCTURED_ENDPOINT"].split("/v1/", 1)[0]
    try:
        provider_health = http_json("GET", provider_base, "/health", timeout=3)
    except Exception as error:
        provider_health = {"error": str(error)}
    status = "configured" if generator_command and "error" not in provider_health else "unavailable"
    append_attempt(out, "generator", status, {
        "generatorCommand": generator_command or "",
        "discovered": discovered,
        "structuredEndpoint": os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT"),
        "structuredModel": os.environ.get("CCG_LOCAL_STRUCTURED_MODEL"),
        "providerHealth": provider_health,
    })
    (out / "generator-remediation.md").write_text(
        "# Generator remediation\n\n"
        f"- Generator command discovered/configured: `{generator_command or ''}`\n"
        f"- Generator command source: `scripts/acceptance/run_final_e2e_generator_adapter.py` wrapping `src/Game.UnityCardPackExporter` production mode\n"
        f"- Structured provider endpoint: `{os.environ.get('CCG_LOCAL_STRUCTURED_ENDPOINT')}`\n"
        f"- Structured provider model: `{os.environ.get('CCG_LOCAL_STRUCTURED_MODEL')}`\n"
        f"- Provider health: `{json.dumps(provider_health, sort_keys=True)}`\n"
        "- Asset providers: not required for this final E2E because the documented production card-pack export supports `--skip-assets` for LLM-side card generation.\n",
        encoding="utf-8")
    return generator_command, provider_health

def command_type_counts(commands):
    counts = {}
    for command in commands:
        command_type = command.get("command", command).get("commandType", "UNKNOWN")
        counts[command_type] = counts.get(command_type, 0) + 1
    return dict(sorted(counts.items()))

def choose_action(actions):
    priority = {
        "RESOLVE_CHOICE": 0,
        "PLAY_CARD": 1,
        "ATTACK": 2,
        "ACTIVATE_LOCATION": 3,
        "USE_HERO_POWER": 4,
        "END_TURN": 5,
        "CONCEDE": 99,
    }
    return sorted(actions, key=lambda a: (priority.get(a.get("commandType"), 50), json.dumps(a, sort_keys=True)))[0]

def hidden_zone_cards(state, player_id, zone_name):
    player = state.get("players", {}).get(player_id, {})
    zones = player.get("zones", {})
    zone = zones.get(zone_name) or zones.get(zone_name.lower()) or {}
    return zone.get("cards") or []

def hidden_info_safe(p1_state, p2_state, spectator_state, p1, p2):
    checks = {
        "playerOneCannotSeePlayerTwoHandIdentities": len(hidden_zone_cards(p1_state, p2, "hand")) == 0,
        "playerTwoCannotSeePlayerOneHandIdentities": len(hidden_zone_cards(p2_state, p1, "hand")) == 0,
        "spectatorCannotSeePlayerOneHandIdentities": len(hidden_zone_cards(spectator_state, p1, "hand")) == 0,
        "spectatorCannotSeePlayerTwoHandIdentities": len(hidden_zone_cards(spectator_state, p2, "hand")) == 0,
        "playerOneDeckOrderHidden": len(hidden_zone_cards(p1_state, p1, "deck")) == 0,
        "playerTwoDeckOrderHidden": len(hidden_zone_cards(p2_state, p2, "deck")) == 0,
        "spectatorDeckOrderHidden": len(hidden_zone_cards(spectator_state, p1, "deck")) == 0 and len(hidden_zone_cards(spectator_state, p2, "deck")) == 0,
    }
    return checks, all(checks.values())

def audit_previous_run(out):
    summary_path = pathlib.Path("artifacts/final-acceptance-e2e/20260708-153708/summary.json")
    deck_path = pathlib.Path("artifacts/final-acceptance-e2e/20260708-153708/decks/deck-validation-report.json")
    validation_path = pathlib.Path("artifacts/final-acceptance-e2e/20260708-153708/generation/validation-report.json")
    summary = json.loads(summary_path.read_text(encoding="utf-8")) if summary_path.exists() else {}
    deck_report = json.loads(deck_path.read_text(encoding="utf-8")) if deck_path.exists() else {}
    validation = json.loads(validation_path.read_text(encoding="utf-8")) if validation_path.exists() else {}
    root_cause = (
        "The prior run used an explicit local scale override of two generated cards, and the runner/deck validator "
        "accepted one-card decks because no production deck-size or copy-limit constraint was enforced."
    )
    audit = {
        "previousGeneratedRequested": int(summary.get("generator", {}).get("requested", validation.get("requestedCards", 0)) or 0),
        "previousAccepted": int(summary.get("generator", {}).get("accepted", validation.get("acceptedCards", 0)) or 0),
        "previousPlayerOneDeckSize": int(deck_report.get("playerOneSize", 0) or 0),
        "previousPlayerTwoDeckSize": int(deck_report.get("playerTwoSize", 0) or 0),
        "rootCause": root_cause,
        "fixed": False,
    }
    (out / "previous-run-audit.md").write_text(
        "# Previous run audit\n\n"
        f"- Previous artifact: `artifacts/final-acceptance-e2e/20260708-153708/`\n"
        f"- Previous generated cards requested: {audit['previousGeneratedRequested']}\n"
        f"- Previous accepted cards: {audit['previousAccepted']}\n"
        f"- Previous PlayerOne deck size: {audit['previousPlayerOneDeckSize']}\n"
        f"- Previous PlayerTwo deck size: {audit['previousPlayerTwoDeckSize']}\n"
        f"- Root cause: {root_cause}\n"
        "- Full-scale fix status: runner now rejects tiny generated pools and product-blocks when production deck constraints are absent.\n",
        encoding="utf-8")
    return audit

def profile_has_tiny_marker(profile):
    text = json.dumps(profile, sort_keys=True).lower()
    return any(marker in text for marker in ("test", "tiny", "minimal", "demo", "fixture", "smoke", "dry-run", "dry_run"))

def fetch_public_production_ruleset(base_url, capabilities):
    ruleset_id = capabilities.get("defaultProductionRulesetId") or capabilities.get("DefaultProductionRulesetId") or ""
    version = capabilities.get("defaultProductionRulesetVersion") or capabilities.get("DefaultProductionRulesetVersion") or ""
    if ruleset_id and version:
        return http_json("GET", base_url, f"/rulesets/{urllib.parse.quote(ruleset_id)}/versions/{urllib.parse.quote(version)}", timeout=10)
    if ruleset_id:
        return http_json("GET", base_url, f"/rulesets/{urllib.parse.quote(ruleset_id)}", timeout=10)
    rulesets = http_json("GET", base_url, "/rulesets", timeout=10)
    if isinstance(rulesets, list):
        for ruleset in rulesets:
            if ruleset.get("productionScale") or ruleset.get("ProductionScale"):
                return ruleset
    raise RuntimeError("No default production ruleset was discoverable from public capabilities or /rulesets.")

def discover_ruleset_constraints(out, args, capabilities, profile=None, readiness_reports=None):
    profile = profile or {}
    readiness_reports = readiness_reports or {}
    limits = profile.get("limits") or profile.get("Limits") or {}
    deck_rules = profile.get("deckRules") or profile.get("DeckRules") or {}
    card_pool_policy = profile.get("cardPoolPolicy") or profile.get("CardPoolPolicy") or {}
    metadata_sources = []
    deck_size = (
        deck_rules.get("defaultDeckSize")
        or deck_rules.get("DefaultDeckSize")
        or deck_rules.get("maximumDeckSize")
        or deck_rules.get("MaximumDeckSize")
        or limits.get("deckSize")
        or limits.get("DeckSize")
        or limits.get("minDeckSize")
        or limits.get("MinDeckSize")
        or profile.get("deckSize")
        or profile.get("DeckSize")
    )
    copy_limit = (
        deck_rules.get("maximumCopiesPerCard")
        or deck_rules.get("MaximumCopiesPerCard")
        or limits.get("copyLimit")
        or limits.get("CopyLimit")
        or limits.get("maxCopiesPerCard")
        or limits.get("MaxCopiesPerCard")
        or profile.get("copyLimit")
        or profile.get("CopyLimit")
    )
    if deck_size is not None:
        metadata_sources.append("public backend ruleset contract" if deck_rules else "resolved ruleset profile")
    for report_name, report in readiness_reports.items():
        if deck_size is None:
            deck_size = report.get("deckSize") or report.get("requiredDeckSize") or report.get("minDeckSize")
        if copy_limit is None:
            copy_limit = report.get("copyLimit") or report.get("maxCopiesPerCard")
        if deck_size is not None or copy_limit is not None:
            metadata_sources.append(f"deck readiness response: {report_name}")
    try:
        deck_size = int(deck_size) if deck_size is not None else 0
    except Exception:
        deck_size = 0
    try:
        copy_limit = int(copy_limit) if copy_limit is not None else 0
    except Exception:
        copy_limit = 0
    ruleset_id = profile.get("rulesetId") or profile.get("RulesetId") or profile.get("sourceProfileId") or profile.get("SourceProfileId") or profile.get("id") or profile.get("Id") or ""
    ruleset_version = profile.get("rulesetVersion") or profile.get("RulesetVersion") or profile.get("sourceProfileVersion") or profile.get("SourceProfileVersion") or ""
    environment = profile.get("environment") or profile.get("Environment") or ""
    declared_production_scale = bool(profile.get("productionScale") or profile.get("ProductionScale"))
    test_or_tiny = (
        profile_has_tiny_marker(profile)
        or ruleset_id.lower() in ("", "test", "tiny", "demo")
        or str(environment).lower() in ("test", "development", "dev")
    )
    production_scale = (
        deck_size >= args.min_deck_size
        and copy_limit > 0
        and not test_or_tiny
        and bool(ruleset_id)
        and declared_production_scale
        and str(environment).lower() == "production"
        and bool(card_pool_policy.get("catalogBound", card_pool_policy.get("CatalogBound", False)))
        and bool(card_pool_policy.get("requirePublishedCardVersion", card_pool_policy.get("RequirePublishedCardVersion", False)))
        and not bool(card_pool_policy.get("allowUnknownCards", card_pool_policy.get("AllowUnknownCards", True)))
    )
    report = {
        "rulesetId": ruleset_id,
        "rulesetVersion": ruleset_version,
        "deckSize": deck_size,
        "copyLimit": copy_limit,
        "allowedCardPool": "catalog-bound-published-card-versions",
        "generatedPackId": "",
        "productionScale": production_scale,
        "declaredProductionScale": declared_production_scale,
        "environment": environment,
        "cardPoolPolicy": card_pool_policy,
        "testOrTinyRulesetDetected": test_or_tiny,
        "sources": metadata_sources or ["backend capabilities/ruleset/deck-readiness sources did not expose deck-size or copy-limit constraints"],
        "capabilityRulesetVersions": capabilities.get("supportedRulesetProfileVersions", []),
        "verdict": READY if production_scale else PRODUCT_BLOCKED,
    }
    write_json(out / "ruleset-discovery-report.json", report)
    return report

def final_reports(out, args, started, verdict, reason, stage_data):
    completed = now_utc()
    duration_ms = int((completed - started).total_seconds() * 1000)
    commit = subprocess.run(["git", "rev-parse", "HEAD"], text=True, capture_output=True).stdout.strip()
    branch = subprocess.run(["git", "rev-parse", "--abbrev-ref", "HEAD"], text=True, capture_output=True).stdout.strip()
    summary = {
        "runId": args.run_id,
        "startedAt": iso(started),
        "completedAt": iso(completed),
        "durationMs": duration_ms,
        "commit": commit,
        "branch": branch,
        "backendUrl": args.backend_url,
        "fullScale": True,
        "previousRunAudit": stage_data.get("previousRunAudit", {
            "previousGeneratedRequested": 0,
            "previousAccepted": 0,
            "rootCause": "",
            "fixed": False,
        }),
        "generation": stage_data.get("generation", {
            "requested": 0,
            "accepted": 0,
            "rejected": 0,
            "minRequestedRequired": args.generated_card_request_count,
            "minAcceptedRequired": args.min_accepted_generated_cards,
            "provider": os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT", ""),
            "model": os.environ.get("CCG_LOCAL_STRUCTURED_MODEL", ""),
            "verdict": verdict,
        }),
        "ruleset": stage_data.get("ruleset", {
            "rulesetId": "",
            "rulesetVersion": "",
            "deckSize": 0,
            "copyLimit": 0,
            "productionScale": False,
            "testOrTinyRulesetDetected": False,
            "verdict": verdict,
        }),
        "generator": stage_data.get("generator", {"requested": 0, "accepted": 0, "rejected": 0, "cardPackId": "", "verdict": verdict}),
        "catalog": stage_data.get("catalog", {"published": False, "catalogId": "", "version": "", "verdict": verdict}),
        "decks": stage_data.get("decks", {
            "playerOneDeckId": "",
            "playerTwoDeckId": "",
            "playerOneLegal": False,
            "playerTwoLegal": False,
            "playerOneDeckSize": 0,
            "playerTwoDeckSize": 0,
            "minDeckSizeRequired": args.min_deck_size,
            "allCardsFromGeneratedPack": False,
            "fixtureCardsUsed": False,
            "starterCardsUsed": False,
            "bothLegal": False,
            "verdict": verdict,
        }),
        "match": stage_data.get("match", {"matchId": "", "terminal": False, "winner": "", "turnCount": 0, "commandCount": 0, "eventCount": 0, "finalStateHash": "", "verdict": verdict}),
        "clientBehavior": stage_data.get("clientBehavior", {"publicApiOnly": True, "eventPolling": False, "reconnectTested": False, "hiddenInfoPassed": False, "verdict": verdict}),
        "replay": stage_data.get("replay", {"verified": False, "finalStateHash": "", "finalStateHashMatched": False, "verdict": verdict}),
        "remediation": stage_data.get("remediation", {}),
        "finalVerdict": verdict,
        "reason": reason,
    }
    write_json(out / "summary.json", summary)
    final_verdict = {
        "finalVerdict": verdict,
        "reason": reason,
        "ready": verdict == READY,
        "productBlocked": verdict == PRODUCT_BLOCKED,
        "failed": verdict == FAILED,
        "completedAt": iso(completed),
    }
    write_json(out / "final/final-verdict.json", final_verdict)
    statement = {
        READY: "The production full-scale E2E acceptance passed.",
        FAILED: "The production full-scale E2E acceptance failed.",
        PRODUCT_BLOCKED: "The production full-scale E2E acceptance is product-blocked.",
    }[verdict]
    report = f"""# Production Full-Scale E2E Acceptance Report

## Executive summary

- Final verdict: `{verdict}`
- Run id: `{args.run_id}`
- Date/time: `{iso(completed)}`
- Duration: `{duration_ms}` ms
- Backend URL: `{args.backend_url}`
- Commit: `{commit}`
- Branch: `{branch}`
- Provider/model: `{os.environ.get('CCG_LOCAL_STRUCTURED_ENDPOINT', '')}` / `{os.environ.get('CCG_LOCAL_STRUCTURED_MODEL', '')}`
- Ruleset: `{summary['ruleset']['rulesetId']}` version `{summary['ruleset']['rulesetVersion']}`
- Catalog: `{summary['catalog']['catalogId']}` version `{summary['catalog']['version']}`
- Reason: {reason}

## Previous run audit

- Previous generated cards requested: {summary['previousRunAudit'].get('previousGeneratedRequested', 0)}
- Previous accepted cards: {summary['previousRunAudit'].get('previousAccepted', 0)}
- Root cause: {summary['previousRunAudit'].get('rootCause', '')}
- Fixed in runner controls: {summary['previousRunAudit'].get('fixed', False)}

## Generation summary

- Requested cards: {summary['generation']['requested']}
- Accepted cards: {summary['generation']['accepted']}
- Rejected cards: {summary['generation']['rejected']}
- Required requested minimum: {summary['generation']['minRequestedRequired']}
- Required accepted minimum: {summary['generation']['minAcceptedRequired']}
- Card pack id: `{summary['generator']['cardPackId']}`

## Deck summary

- PlayerOne deck id: `{summary['decks']['playerOneDeckId']}`
- PlayerTwo deck id: `{summary['decks']['playerTwoDeckId']}`
- PlayerOne deck size: {summary['decks'].get('playerOneDeckSize', 0)}
- PlayerTwo deck size: {summary['decks'].get('playerTwoDeckSize', 0)}
- Minimum deck size required: {summary['decks'].get('minDeckSizeRequired', args.min_deck_size)}
- All cards from generated pack: {summary['decks'].get('allCardsFromGeneratedPack', False)}
- Fixture cards used: {summary['decks'].get('fixtureCardsUsed', False)}
- Starter cards used: {summary['decks'].get('starterCardsUsed', False)}
- Deck validation result: `{summary['decks']['verdict']}`

## Match summary

- Match id: `{summary['match']['matchId']}`
- Seed: `{args.seed}`
- Terminal: `{summary['match']['terminal']}`
- Winner/draw: `{summary['match']['winner']}`
- Turn count: {summary['match']['turnCount']}
- Command count: {summary['match']['commandCount']}
- Event count: {summary['match']['eventCount']}
- Final state hash: `{summary['match']['finalStateHash']}`
- Reconnect tested: {summary['clientBehavior']['reconnectTested']}

## Replay result

- Replay verified: {summary['replay']['verified']}
- Final state hash: `{summary['replay']['finalStateHash']}`

## Hidden-info result

- Hidden-info passed: {summary['clientBehavior']['hiddenInfoPassed']}

## Ruleset result

- Ruleset id: `{summary['ruleset']['rulesetId']}`
- Ruleset version: `{summary['ruleset']['rulesetVersion']}`
- Deck size: {summary['ruleset']['deckSize']}
- Copy limit: {summary['ruleset']['copyLimit']}
- Production scale: {summary['ruleset']['productionScale']}
- Test/tiny marker detected: {summary['ruleset']['testOrTinyRulesetDetected']}
- Ruleset verdict: `{summary['ruleset']['verdict']}`

## Environment remediation performed

- Backend remediation: see `environment-remediation.md`
- Generator remediation: see `generator-remediation.md`
- Attempt log: see `attempts/attempt-log.jsonl`
- Final backend URL: `{args.backend_url}`
- Generator command: `{os.environ.get('CCG_FINAL_E2E_GENERATOR_COMMAND', '')}`
- Provider endpoint: `{os.environ.get('CCG_LOCAL_STRUCTURED_ENDPOINT', '')}`
- Provider model: `{os.environ.get('CCG_LOCAL_STRUCTURED_MODEL', '')}`
- Final verdict reason: {reason}

## Evidence files

""" + "\n".join(f"- `{path}`" for path in REQUIRED_FILES) + f"""

## Final acceptance statement

{statement}
"""
    (out / "human-report.md").write_text(report, encoding="utf-8")
    return summary

def product_block(out, args, started, reason, stage_data=None):
    stage_data = stage_data or {}
    final_reports(out, args, started, PRODUCT_BLOCKED, reason, stage_data)
    print(f"Final acceptance verdict: {PRODUCT_BLOCKED}")
    print(f"Reason: {reason}")
    print(f"Artifacts: {out}")
    return 2

def fail(out, args, started, reason, stage_data=None):
    stage_data = stage_data or {}
    final_reports(out, args, started, FAILED, reason, stage_data)
    print(f"Final acceptance verdict: {FAILED}")
    print(f"Reason: {reason}")
    print(f"Artifacts: {out}")
    return 1

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-id", default="")
    parser.add_argument("--seed", default="production-final-e2e-seed")
    parser.add_argument("--output-dir", default="")
    parser.add_argument("--backend-url", default=os.environ.get("CARDGAME_BACKEND_URL", "http://localhost:5000"))
    parser.add_argument("--strict", action="store_true", default=True)
    parser.add_argument("--keep-backend-running", action="store_true")
    parser.add_argument("--provider-timeout-seconds", type=int, default=300)
    parser.add_argument("--match-timeout-seconds", type=int, default=900)
    parser.add_argument("--generated-card-request-count", type=int, default=100)
    parser.add_argument("--min-accepted-generated-cards", type=int, default=60)
    parser.add_argument("--min-deck-size", type=int, default=30)
    parser.add_argument("--require-production-ruleset", action="store_true", default=True)
    parser.add_argument("--forbid-fixture-cards", action="store_true", default=True)
    parser.add_argument("--forbid-starter-pool", action="store_true", default=True)
    parser.add_argument("--forbid-reduced-ruleset", action="store_true", default=True)
    parser.add_argument("--full-scale", action="store_true", default=True)
    args = parser.parse_args()

    started = now_utc()
    timestamp = started.strftime("%Y%m%d-%H%M%S")
    if not args.run_id:
        args.run_id = f"production-final-e2e-full-scale-{timestamp}"
    out = pathlib.Path(args.output_dir) if args.output_dir else pathlib.Path("artifacts/final-acceptance-e2e-full-scale") / timestamp
    out.mkdir(parents=True, exist_ok=True)
    for rel in REQUIRED_FILES:
        path = out / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.suffix == ".jsonl":
            path.write_text("", encoding="utf-8")
        elif not path.exists():
            write_json(path, {})

    commit = subprocess.run(["git", "rev-parse", "HEAD"], text=True, capture_output=True).stdout.strip()
    branch = subprocess.run(["git", "rev-parse", "--abbrev-ref", "HEAD"], text=True, capture_output=True).stdout.strip()
    write_json(out / "run-config.json", {
        "runId": args.run_id,
        "seed": args.seed,
        "outputDir": str(out),
        "backendUrl": args.backend_url,
        "strict": args.strict,
        "keepBackendRunning": args.keep_backend_running,
        "providerTimeoutSeconds": args.provider_timeout_seconds,
        "matchTimeoutSeconds": args.match_timeout_seconds,
        "fullScale": args.full_scale,
        "generatedCardRequestCount": args.generated_card_request_count,
        "minAcceptedGeneratedCards": args.min_accepted_generated_cards,
        "minDeckSize": args.min_deck_size,
        "requireProductionRuleset": args.require_production_ruleset,
        "forbidFixtureCards": args.forbid_fixture_cards,
        "forbidStarterPool": args.forbid_starter_pool,
        "forbidReducedRuleset": args.forbid_reduced_ruleset,
        "generatorCommandConfigured": bool(os.environ.get("CCG_FINAL_E2E_GENERATOR_COMMAND")),
    })
    write_json(out / "environment.json", {
        "createdAt": iso(started),
        "commit": commit,
        "branch": branch,
        "python": sys.version,
        "platform": sys.platform,
        "providerStatus": {
            "CCG_FINAL_E2E_GENERATOR_COMMAND": "configured" if os.environ.get("CCG_FINAL_E2E_GENERATOR_COMMAND") else "missing",
            "CCG_LOCAL_STRUCTURED_ENDPOINT": "configured" if os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT") else "missing",
            "CCG_LOCAL_IMAGE_ENDPOINT": "configured" if os.environ.get("CCG_LOCAL_IMAGE_ENDPOINT") else "missing",
            "CCG_LOCAL_ANIMATION_ENDPOINT": "configured" if os.environ.get("CCG_LOCAL_ANIMATION_ENDPOINT") else "missing",
            "CCG_LOCAL_AUDIO_ENDPOINT": "configured" if os.environ.get("CCG_LOCAL_AUDIO_ENDPOINT") else "missing",
        },
    })

    backend_process = None
    try:
        backend_process, healthy, args.backend_url, capabilities = start_backend_if_needed(args.backend_url, args.keep_backend_running, out, args)
        if not healthy:
            return product_block(out, args, started, f"Backend health endpoint did not become reachable at {args.backend_url} after default and fallback port remediation attempts.")

        write_json(out / "engine-capabilities.json", capabilities)
        previous_audit = audit_previous_run(out)
        previous_audit["fixed"] = True
        generator_command, provider_health = ensure_generator(out)

        public_ruleset = {}
        try:
            public_ruleset = fetch_public_production_ruleset(args.backend_url, capabilities)
        except Exception as error:
            public_ruleset = {"discoveryError": str(error)}
        initial_ruleset_report = discover_ruleset_constraints(out, args, capabilities, public_ruleset)
        write_json(out / "generation/ruleset.json", public_ruleset)
        if args.full_scale and args.require_production_ruleset and initial_ruleset_report["verdict"] != READY:
            reason = (
                "Full-scale production ruleset/deck constraints are not discoverable from public backend ruleset/capability sources. "
                "The current product validates tiny decks but exposes no production deck-size or copy-limit policy, so a full-scale pass would be a false positive."
            )
            write_json(out / "generation/validation-report.json", {
                "requestedCards": 0,
                "generatedCards": 0,
                "acceptedCards": 0,
                "rejectedCards": 0,
                "repairCount": 0,
                "providerFailures": 0,
                "schemaErrors": [],
                "semanticErrors": [],
                "unsupportedMechanics": [],
                "duplicateSimilarityChecks": "not-run",
                "gameplayValidationStatus": "not-run",
                "finalGenerationVerdict": PRODUCT_BLOCKED,
                "diagnostics": [reason],
            })
            write_json(out / "generation/catalog-publish-report.json", {
                "published": False,
                "finalPublishVerdict": PRODUCT_BLOCKED,
                "diagnostics": [reason],
            })
            write_json(out / "decks/deck-validation-report.json", {
                "playerOneDeckSize": 0,
                "playerTwoDeckSize": 0,
                "bothLegal": False,
                "allCardsFromGeneratedPack": False,
                "fixtureCardsUsed": False,
                "starterCardsUsed": False,
                "verdict": PRODUCT_BLOCKED,
                "diagnostics": [reason],
            })
            (out / "decks/deck-composition.md").write_text(
                "# Deck composition\n\n"
                "No decks were constructed because production deck-size and copy-limit constraints are not available.\n",
                encoding="utf-8")
            stage_data = {
                "previousRunAudit": previous_audit,
                "generation": {
                    "requested": 0,
                    "accepted": 0,
                    "rejected": 0,
                    "minRequestedRequired": args.generated_card_request_count,
                    "minAcceptedRequired": args.min_accepted_generated_cards,
                    "provider": os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT", ""),
                    "model": os.environ.get("CCG_LOCAL_STRUCTURED_MODEL", ""),
                    "verdict": PRODUCT_BLOCKED,
                },
                "generator": {"requested": 0, "accepted": 0, "rejected": 0, "cardPackId": "", "verdict": PRODUCT_BLOCKED},
                "ruleset": initial_ruleset_report,
                "catalog": {"published": False, "catalogId": "", "version": "", "verdict": PRODUCT_BLOCKED},
                "decks": {
                    "playerOneDeckId": "",
                    "playerTwoDeckId": "",
                    "playerOneLegal": False,
                    "playerTwoLegal": False,
                    "playerOneDeckSize": 0,
                    "playerTwoDeckSize": 0,
                    "minDeckSizeRequired": args.min_deck_size,
                    "allCardsFromGeneratedPack": False,
                    "fixtureCardsUsed": False,
                    "starterCardsUsed": False,
                    "bothLegal": False,
                    "verdict": PRODUCT_BLOCKED,
                },
                "match": {"matchId": "", "terminal": False, "winner": "", "turnCount": 0, "commandCount": 0, "eventCount": 0, "finalStateHash": "", "verdict": PRODUCT_BLOCKED},
                "clientBehavior": {"publicApiOnly": True, "eventPolling": False, "reconnectTested": False, "hiddenInfoPassed": False, "verdict": PRODUCT_BLOCKED},
                "replay": {"verified": False, "finalStateHash": "", "finalStateHashMatched": False, "verdict": PRODUCT_BLOCKED},
            }
            return product_block(out, args, started, reason, stage_data)

        if not generator_command or "error" in provider_health:
            reason = "No healthy real generator/provider path is available after repository discovery and local provider remediation."
            write_json(out / "generation/validation-report.json", {
                "requestedCards": 0,
                "generatedCards": 0,
                "acceptedCards": 0,
                "rejectedCards": 0,
                "schemaValidation": "not-run",
                "semanticValidation": "not-run",
                "rulesEffectValidation": "not-run",
                "duplicateChecks": "not-run",
                "unsupportedMechanicChecks": "not-run",
                "assetMediaStatus": "not-run",
                "finalGenerationVerdict": PRODUCT_BLOCKED,
                "diagnostics": [reason],
            })
            write_json(out / "generation/catalog-publish-report.json", {
                "published": False,
                "finalPublishVerdict": PRODUCT_BLOCKED,
                "diagnostics": [reason],
            })
            return product_block(out, args, started, reason)

        env = os.environ.copy()
        env.update({
            "CCG_FINAL_E2E_RUN_ID": args.run_id,
            "CCG_FINAL_E2E_SEED": args.seed,
            "CCG_FINAL_E2E_OUTPUT_DIR": str(out),
            "CCG_FINAL_E2E_BACKEND_URL": args.backend_url,
            "CCG_FINAL_E2E_PROVIDER_TIMEOUT_SECONDS": str(args.provider_timeout_seconds),
            "CCG_FINAL_E2E_GENERATED_CARD_COUNT": str(args.generated_card_request_count),
        })
        generation_log = out / "generation/generator-command.log"
        generator_card_count = int(env.get("CCG_FINAL_E2E_GENERATED_CARD_COUNT", "4"))
        generator_timeout_seconds = max(
            args.provider_timeout_seconds,
            (args.provider_timeout_seconds * generator_card_count) + 180)
        with generation_log.open("w", encoding="utf-8") as log:
            completed = subprocess.run(generator_command, shell=True, env=env, text=True, stdout=log, stderr=subprocess.STDOUT, timeout=generator_timeout_seconds)
        if completed.returncode != 0:
            return fail(out, args, started, f"Configured real generator command failed with exit code {completed.returncode}.")

        manifest_path = out / "generation/card-pack-manifest.json"
        validation_path = out / "generation/validation-report.json"
        if not manifest_path.exists() or not validation_path.exists():
            return product_block(out, args, started, "Generator command did not write card-pack-manifest.json and validation-report.json.")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        validation = json.loads(validation_path.read_text(encoding="utf-8"))
        accepted = int(validation.get("acceptedCards") or validation.get("accepted") or 0)
        requested = int(validation.get("requestedCards") or validation.get("requested") or accepted)
        rejected = int(validation.get("rejectedCards") or validation.get("rejected") or 0)
        generation_stage_data = {
            "previousRunAudit": previous_audit,
            "generation": {
                "requested": requested,
                "accepted": accepted,
                "rejected": rejected,
                "minRequestedRequired": args.generated_card_request_count,
                "minAcceptedRequired": args.min_accepted_generated_cards,
                "provider": os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT", ""),
                "model": os.environ.get("CCG_LOCAL_STRUCTURED_MODEL", ""),
                "verdict": FAILED,
            },
            "generator": {
                "requested": requested,
                "accepted": accepted,
                "rejected": rejected,
                "cardPackId": manifest.get("cardPackId", ""),
                "verdict": FAILED,
            },
            "ruleset": initial_ruleset_report,
        }
        if args.full_scale and requested < args.generated_card_request_count:
            return fail(out, args, started, f"Generator requested {requested} cards, below full-scale requirement {args.generated_card_request_count}.", generation_stage_data)
        if args.full_scale and accepted < args.min_accepted_generated_cards:
            return fail(out, args, started, f"Generation accepted {accepted} cards, below full-scale requirement {args.min_accepted_generated_cards}.", generation_stage_data)
        if accepted <= 0:
            return fail(out, args, started, "Generation completed but produced no accepted cards.", generation_stage_data)

        catalog = manifest.get("catalogSnapshot") or manifest.get("catalog") or manifest
        catalog_id = catalog.get("snapshotId") or manifest.get("catalogId") or manifest.get("cardPackId") or ""
        if not catalog_id:
            return fail(out, args, started, "Generated manifest did not include a catalog snapshot id.")
        ruleset_profile = manifest.get("rulesetProfile")
        if ruleset_profile:
            http_json_idempotent_post(args.backend_url, "/rulesets/profiles", ruleset_profile, timeout=20)
            write_json(out / "generation/ruleset.json", ruleset_profile)
        write_json(out / "generation/catalog.json", catalog)
        ruleset_report = discover_ruleset_constraints(out, args, capabilities, public_ruleset)
        ruleset_report["generatedPackId"] = manifest.get("cardPackId", catalog_id)
        write_json(out / "ruleset-discovery-report.json", ruleset_report)
        if args.full_scale and args.require_production_ruleset and ruleset_report["verdict"] != READY:
            return product_block(out, args, started, "Generated catalog uses a ruleset profile without production-scale deck size and copy-limit constraints.", {
                "previousRunAudit": previous_audit,
                "generation": {
                    "requested": requested,
                    "accepted": accepted,
                    "rejected": rejected,
                    "minRequestedRequired": args.generated_card_request_count,
                    "minAcceptedRequired": args.min_accepted_generated_cards,
                    "provider": os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT", ""),
                    "model": os.environ.get("CCG_LOCAL_STRUCTURED_MODEL", ""),
                    "verdict": PRODUCT_BLOCKED,
                },
                "generator": {"requested": requested, "accepted": accepted, "rejected": rejected, "cardPackId": manifest.get("cardPackId", catalog_id), "verdict": PRODUCT_BLOCKED},
                "ruleset": ruleset_report,
            })
        published_catalog = http_json_idempotent_post(args.backend_url, "/catalog/snapshots", catalog, timeout=20)
        compatibility = http_json("GET", args.backend_url, f"/catalog/snapshots/{urllib.parse.quote(catalog_id)}/compatibility", timeout=20)
        catalog_ok = bool(compatibility.get("succeeded", compatibility.get("isCompatible", False)))
        write_json(out / "generation/catalog-publish-report.json", {
            "cardPackId": manifest.get("cardPackId", catalog_id),
            "catalogId": catalog_id,
            "version": manifest.get("version", catalog.get("createdAt", "")),
            "publishedCardCount": len(catalog.get("cards", [])),
            "rejectedCardCount": rejected,
            "validationResult": compatibility,
            "apiEndpointsOrCommandsUsed": ["POST /catalog/snapshots", f"GET /catalog/snapshots/{catalog_id}/compatibility"],
            "finalPublishVerdict": READY if catalog_ok else FAILED,
        })
        if not catalog_ok:
            return fail(out, args, started, "Published catalog failed compatibility validation.")

        deck_one_path = out / "decks/deck-player-one.json"
        deck_two_path = out / "decks/deck-player-two.json"
        existing_deck_one = json.loads(deck_one_path.read_text(encoding="utf-8")) if deck_one_path.exists() and deck_one_path.stat().st_size > 0 else {}
        existing_deck_two = json.loads(deck_two_path.read_text(encoding="utf-8")) if deck_two_path.exists() and deck_two_path.stat().st_size > 0 else {}
        if not existing_deck_one.get("deckId") or not existing_deck_two.get("deckId"):
            cards = catalog.get("cards", [])
            if len(cards) < 2:
                return fail(out, args, started, "Generated catalog has fewer than two cards for distinct decks.")
            ruleset_id = catalog.get("rulesetId") or catalog.get("rulesetProfileId")
            authoritative_deck_size = int(ruleset_report.get("deckSize") or args.min_deck_size)
            if args.full_scale and len(cards) < authoritative_deck_size * 2:
                return fail(out, args, started, f"Generated catalog has {len(cards)} generated cards; {authoritative_deck_size * 2} are required for two full decks.")
            deck_one_cards = cards[0::2][:authoritative_deck_size]
            deck_two_cards = cards[1::2][:authoritative_deck_size]
            if args.full_scale and (len(deck_one_cards) < authoritative_deck_size or len(deck_two_cards) < authoritative_deck_size):
                return fail(out, args, started, "Generated catalog does not contain enough distinct cards to build two full generated-card-only decks.")
            deck_one = {"deckId": f"{args.run_id}-deck-player-one", "ownerPlayerId": "player-one", "playerId": "player-one", "catalogSnapshotId": catalog_id, "rulesetId": ruleset_id, "rulesetProfileId": ruleset_id, "cards": [{"cardId": card["cardId"], "cardVersionId": card.get("cardVersionId") or card.get("CardVersionId"), "count": 1} for card in deck_one_cards]}
            deck_two = {"deckId": f"{args.run_id}-deck-player-two", "ownerPlayerId": "player-two", "playerId": "player-two", "catalogSnapshotId": catalog_id, "rulesetId": ruleset_id, "rulesetProfileId": ruleset_id, "cards": [{"cardId": card["cardId"], "cardVersionId": card.get("cardVersionId") or card.get("CardVersionId"), "count": 1} for card in deck_two_cards]}
            write_json(deck_one_path, deck_one)
            write_json(deck_two_path, deck_two)
        deck_one = json.loads(deck_one_path.read_text(encoding="utf-8"))
        deck_two = json.loads(deck_two_path.read_text(encoding="utf-8"))
        generated_card_ids = {card.get("cardId") for card in catalog.get("cards", [])}
        deck_card_ids = [card.get("cardId") for card in deck_one.get("cards", []) + deck_two.get("cards", [])]
        all_from_generated = all(card_id in generated_card_ids for card_id in deck_card_ids)
        fixture_cards_used = any("fixture" in (card_id or "").lower() for card_id in deck_card_ids)
        starter_cards_used = any("starter" in (card_id or "").lower() for card_id in deck_card_ids)
        if args.full_scale and (not all_from_generated or (args.forbid_fixture_cards and fixture_cards_used) or (args.forbid_starter_pool and starter_cards_used)):
            return fail(out, args, started, "Deck provenance check failed; deck contains non-generated, fixture, or starter cards.")
        stored_one = http_json_idempotent_post(args.backend_url, "/decks", deck_one, timeout=20)
        stored_two = http_json_idempotent_post(args.backend_url, "/decks", deck_two, timeout=20)
        readiness_one = http_json("GET", args.backend_url, f"/matches/readiness/{urllib.parse.quote(deck_one['deckId'])}", timeout=20)
        readiness_two = http_json("GET", args.backend_url, f"/matches/readiness/{urllib.parse.quote(deck_two['deckId'])}", timeout=20)
        deck_report = {
            "playerOneDeckId": deck_one["deckId"],
            "playerTwoDeckId": deck_two["deckId"],
            "playerOneLegal": bool(readiness_one.get("isReady")),
            "playerTwoLegal": bool(readiness_two.get("isReady")),
            "playerOneSize": sum(card.get("count", 0) for card in deck_one.get("cards", [])),
            "playerTwoSize": sum(card.get("count", 0) for card in deck_two.get("cards", [])),
            "playerOneDeckSize": sum(card.get("count", 0) for card in deck_one.get("cards", [])),
            "playerTwoDeckSize": sum(card.get("count", 0) for card in deck_two.get("cards", [])),
            "minDeckSizeRequired": args.min_deck_size,
            "allCardsFromGeneratedPack": all_from_generated,
            "fixtureCardsUsed": fixture_cards_used,
            "starterCardsUsed": starter_cards_used,
            "excludedMatchFaultRiskCards": 0,
            "excludedActionTypes": [],
            "validationEndpoints": [f"/matches/readiness/{deck_one['deckId']}", f"/matches/readiness/{deck_two['deckId']}"],
            "rawResults": {"playerOne": readiness_one, "playerTwo": readiness_two},
            "verdict": READY if readiness_one.get("isReady") and readiness_two.get("isReady") else FAILED,
        }
        deck_report["bothLegal"] = deck_report["playerOneLegal"] and deck_report["playerTwoLegal"]
        if args.full_scale and (deck_report["playerOneDeckSize"] < args.min_deck_size or deck_report["playerTwoDeckSize"] < args.min_deck_size):
            deck_report["verdict"] = FAILED
        write_json(out / "decks/deck-validation-report.json", deck_report)
        card_lookup = {card.get("cardId"): card for card in catalog.get("cards", [])}
        def card_name(card_id):
            card = card_lookup.get(card_id, {})
            definition = card.get("definition", {})
            static = definition.get("staticData") or definition.get("StaticData") or {}
            return static.get("name") or static.get("Name") or card_id
        (out / "decks/deck-composition.md").write_text(
            "# Deck composition\n\n"
            f"- PlayerOne deck size: {deck_report['playerOneDeckSize']}\n"
            f"- PlayerTwo deck size: {deck_report['playerTwoDeckSize']}\n"
            f"- Minimum deck size required: {args.min_deck_size}\n"
            f"- Generated pack provenance: `{manifest.get('cardPackId', catalog_id)}` / `{catalog_id}`\n"
            f"- All cards from generated pack: {all_from_generated}\n"
            f"- Fixture cards used: {fixture_cards_used}\n"
            f"- Starter cards used: {starter_cards_used}\n"
            f"- Validation result: `{deck_report['verdict']}`\n\n"
            "## PlayerOne cards\n\n"
            + "\n".join(f"- `{card['cardId']}` x{card.get('count', 0)} - {card_name(card['cardId'])}" for card in deck_one.get("cards", []))
            + "\n\n## PlayerTwo cards\n\n"
            + "\n".join(f"- `{card['cardId']}` x{card.get('count', 0)} - {card_name(card['cardId'])}" for card in deck_two.get("cards", []))
            + "\n",
            encoding="utf-8")
        if deck_report["verdict"] != READY:
            return fail(out, args, started, "Generated decks failed backend readiness validation.")

        p1 = deck_one.get("playerId") or deck_one.get("ownerPlayerId")
        p2 = deck_two.get("playerId") or deck_two.get("ownerPlayerId")
        sessions = {
            "playerOne": http_json("POST", args.backend_url, "/sessions", {"playerId": p1}, timeout=20),
            "playerTwo": http_json("POST", args.backend_url, "/sessions", {"playerId": p2}, timeout=20),
            "spectator": http_json("POST", args.backend_url, "/sessions", {"playerId": f"{args.run_id}-spectator"}, timeout=20),
        }
        write_json(out / "match/sessions.json", sessions)
        p1_token = sessions["playerOne"]["session"]["sessionToken"]
        p2_token = sessions["playerTwo"]["session"]["sessionToken"]
        spectator_token = sessions["spectator"]["session"]["sessionToken"]
        match_id = f"{args.run_id}-match"
        start_body = {
            "matchId": match_id,
            "gameId": f"{args.run_id}-game",
            "deckIds": [deck_one["deckId"], deck_two["deckId"]],
            "playerSessionTokens": {p1: p1_token, p2: p2_token},
            "startingPlayerId": p1,
            "spectatorSessionTokens": [spectator_token],
        }
        match_start = http_json("POST", args.backend_url, "/matches", start_body, timeout=20)
        write_json(out / "match/match-start.json", match_start)
        if not match_start.get("succeeded"):
            return fail(out, args, started, "Public match start failed.")

        p1_state = http_json("GET", args.backend_url, f"/matches/{match_id}/state", token=p1_token, timeout=20)
        p2_state = http_json("GET", args.backend_url, f"/matches/{match_id}/state", token=p2_token, timeout=20)
        spectator_state = http_json("GET", args.backend_url, f"/matches/{match_id}/state", token=spectator_token, timeout=20)
        for label, state in [("playerOne", p1_state), ("playerTwo", p2_state), ("spectator", spectator_state)]:
            append_jsonl(out / "match/state-snapshots.jsonl", {"viewer": label, "phase": "initial", "state": state})
        hidden_checks, hidden_ok = hidden_info_safe(p1_state, p2_state, spectator_state, p1, p2)
        write_json(out / "match/hidden-info-report.json", {"checks": hidden_checks, "passed": hidden_ok, "verdict": READY if hidden_ok else FAILED})
        if not hidden_ok:
            return fail(out, args, started, "Initial hidden-information checks failed.")

        current_state = p1_state
        event_cursor = 0
        all_events = []
        submitted_commands = []
        reconnected = False
        reconnect_report = {}
        deadline = time.time() + args.match_timeout_seconds
        while time.time() < deadline:
            if current_state.get("isTerminal"):
                break
            active = current_state.get("activePlayerId")
            token = p1_token if active == p1 else p2_token
            legal = http_json("GET", args.backend_url, f"/matches/{match_id}/players/{active}/legal-actions", token=token, timeout=20)
            actions = legal.get("legalActions", [])
            if not actions:
                return fail(out, args, started, f"No legal actions for non-terminal active player {active}.")
            command = choose_action(actions)
            append_jsonl(out / "match/bot-decision-log.jsonl", {"activePlayerId": active, "chosen": command, "legalActionCount": len(actions), "stateHash": current_state.get("stateHash")})
            submission = {
                "sessionToken": token,
                "command": command,
                "commandId": f"{args.run_id}-command-{len(submitted_commands)+1:04d}",
                "expectedPreStateHash": current_state.get("stateHash"),
                "correlationId": args.run_id,
            }
            result = submit_command_with_rate_limit(args.backend_url, match_id, submission, out, timeout=20)
            if not result.get("succeeded"):
                return fail(out, args, started, f"Command submission failed for {active}.")
            submitted_commands.append(submission)
            append_jsonl(out / "match/command-log.jsonl", submission)
            for event in result.get("events", []):
                append_jsonl(out / "match/event-log.jsonl", event)
                all_events.append(event)
            current_state = result.get("state") or current_state
            append_jsonl(out / "match/state-snapshots.jsonl", {"viewer": active, "phase": "after-command", "state": current_state})
            poll = http_json("GET", args.backend_url, f"/matches/{match_id}/events?afterEventSeq={event_cursor}", token=token, timeout=20)
            if not poll.get("succeeded"):
                return fail(out, args, started, "Event polling failed.")
            seqs = [event.get("eventSeq", 0) for event in poll.get("events", [])]
            if seqs != sorted(seqs):
                return fail(out, args, started, "Event polling returned non-monotonic event sequence.")
            if poll.get("nextEventSeq", event_cursor) < event_cursor:
                return fail(out, args, started, "Event cursor regressed.")
            event_cursor = poll.get("nextEventSeq", event_cursor)
            if not reconnected and len(submitted_commands) >= 1:
                reconnect = http_json("POST", args.backend_url, f"/matches/{match_id}/reconnect?afterEventSeq=0", token=token, timeout=20)
                reconnect_report = {
                    "triggerPoint": len(submitted_commands),
                    "preReconnectStateHash": current_state.get("stateHash"),
                    "postReconnectStateHash": (reconnect.get("state") or {}).get("stateHash"),
                    "eventCursorBefore": 0,
                    "eventCursorAfter": reconnect.get("nextEventSeq"),
                    "missedEventsCount": len(reconnect.get("events", [])),
                    "commandSubmissionResumedSuccessfully": False,
                    "succeeded": bool(reconnect.get("succeeded")),
                }
                write_json(out / "match/reconnect-report.json", reconnect_report)
                reconnected = True
            if reconnected:
                reconnect_report["commandSubmissionResumedSuccessfully"] = len(submitted_commands) > reconnect_report["triggerPoint"]
                write_json(out / "match/reconnect-report.json", reconnect_report)
        if not current_state.get("isTerminal"):
            return fail(out, args, started, "Match timeout reached before terminal state.")
        if not reconnected:
            return fail(out, args, started, "Match reached terminal before reconnect flow was tested.")

        replay = http_json("POST", args.backend_url, f"/matches/{match_id}/replay/verify", token=p1_token, timeout=60)
        write_json(out / "replay/replay-verification-report.json", replay)
        replay_verified = bool(replay.get("succeeded") and replay.get("replay", {}).get("isVerified"))
        if not replay_verified:
            return fail(out, args, started, "Replay verification failed.")
        if str(current_state.get("terminalReason", "")).upper() == "ENGINE_FAULT":
            return fail(out, args, started, "Match reached ENGINE_FAULT terminal state, which is not a normal gameplay result for full-scale production acceptance.")

        stage_data = {
            "previousRunAudit": previous_audit,
            "generation": {
                "requested": requested,
                "accepted": accepted,
                "rejected": rejected,
                "minRequestedRequired": args.generated_card_request_count,
                "minAcceptedRequired": args.min_accepted_generated_cards,
                "provider": os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT", ""),
                "model": os.environ.get("CCG_LOCAL_STRUCTURED_MODEL", ""),
                "verdict": READY,
            },
            "ruleset": ruleset_report,
            "generator": {"requested": requested, "accepted": accepted, "rejected": rejected, "cardPackId": manifest.get("cardPackId", catalog_id), "verdict": READY},
            "catalog": {"published": True, "catalogId": catalog_id, "version": manifest.get("version", ""), "verdict": READY},
            "decks": {
                "playerOneDeckId": deck_one["deckId"],
                "playerTwoDeckId": deck_two["deckId"],
                "playerOneLegal": True,
                "playerTwoLegal": True,
                "playerOneDeckSize": deck_report["playerOneDeckSize"],
                "playerTwoDeckSize": deck_report["playerTwoDeckSize"],
                "minDeckSizeRequired": args.min_deck_size,
                "allCardsFromGeneratedPack": all_from_generated,
                "fixtureCardsUsed": fixture_cards_used,
                "starterCardsUsed": starter_cards_used,
                "bothLegal": True,
                "verdict": READY,
            },
            "match": {
                "matchId": match_id,
                "terminal": True,
                "winner": current_state.get("terminalReason", ""),
                "turnCount": current_state.get("turnNumber", 0),
                "commandCount": current_state.get("commandCount", len(submitted_commands)),
                "eventCount": len(all_events),
                "finalStateHash": current_state.get("stateHash", ""),
                "verdict": READY,
            },
            "clientBehavior": {"publicApiOnly": True, "eventPolling": True, "reconnectTested": True, "hiddenInfoPassed": True, "verdict": READY},
            "replay": {"verified": True, "finalStateHash": replay.get("replay", {}).get("finalStateHash", ""), "finalStateHashMatched": replay.get("replay", {}).get("finalStateHash", "") == current_state.get("stateHash", ""), "verdict": READY},
        }
        final_reports(out, args, started, READY, "Complete full-scale generated-card public-client E2E flow reached terminal state and replay verified.", stage_data)
        print(f"Final acceptance verdict: {READY}")
        print(f"Artifacts: {out}")
        return 0
    except subprocess.TimeoutExpired:
        return product_block(out, args, started, "Configured generation command timed out.")
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        return fail(out, args, started, f"HTTP {error.code} from {error.url}: {detail[:500]}")
    except Exception as error:
        return fail(out, args, started, f"Unhandled final E2E runner error: {error}")
    finally:
        if backend_process is not None and not args.keep_backend_running and backend_process.poll() is None:
            backend_process.terminate()
            try:
                backend_process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                backend_process.kill()

if __name__ == "__main__":
    raise SystemExit(main())
PY
