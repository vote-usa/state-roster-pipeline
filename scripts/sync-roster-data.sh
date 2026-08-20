#!/usr/bin/env bash
# Sync roster outputs into a local checkout of vote-usa/state-roster-data,
# commit there, and update data/input/snapshot.json in this repo.
#
# Does NOT push unless you pass --push.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATA_REPO_URL="${DATA_REPO_URL:-https://github.com/vote-usa/state-roster-data.git}"
DATA_REPO_DIR="${DATA_REPO_DIR:-$REPO_ROOT/../state-roster-data}"
YEAR="${YEAR:-$(date -u +%Y)}"
STATES="${STATES:-CA WA}"
DO_COLLECT=0
DO_PUSH=0
COMMIT_MSG=""

usage() {
  cat <<'EOF'
Usage: scripts/sync-roster-data.sh [options]

Options:
  --data-repo-dir <path>  Checkout of state-roster-data (default: ../state-roster-data)
  --year <yyyy>           Election year when collecting (default: current UTC year)
  --states "CA WA"        States to sync / collect (default: CA WA)
  --collect               Re-run collectors into the data repo (needs network)
  --from-local            Copy from this repo's data/output/ (default)
  --push                  Push the data-repo commit (default: no push)
  --message <text>        Commit message for the data repo
  -h, --help              Show this help

Environment:
  DATA_REPO_URL   Remote URL (default: https://github.com/vote-usa/state-roster-data.git)
  DATA_REPO_DIR   Same as --data-repo-dir
  YEAR / STATES   Same as --year / --states
EOF
}

FROM_LOCAL=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --data-repo-dir) DATA_REPO_DIR="$2"; shift 2 ;;
    --year) YEAR="$2"; shift 2 ;;
    --states) STATES="$2"; shift 2 ;;
    --collect) DO_COLLECT=1; FROM_LOCAL=0; shift ;;
    --from-local) FROM_LOCAL=1; DO_COLLECT=0; shift ;;
    --push) DO_PUSH=1; shift ;;
    --message) COMMIT_MSG="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

ensure_data_repo() {
  if [[ -d "$DATA_REPO_DIR/.git" ]]; then
    echo "Using existing data repo at $DATA_REPO_DIR"
    git -C "$DATA_REPO_DIR" fetch origin 2>/dev/null || true
    # Empty remotes / first clone may have no branches yet.
    if git -C "$DATA_REPO_DIR" rev-parse --verify HEAD >/dev/null 2>&1; then
      local branch
      branch="$(git -C "$DATA_REPO_DIR" rev-parse --abbrev-ref HEAD)"
      if git -C "$DATA_REPO_DIR" rev-parse --verify "origin/$branch" >/dev/null 2>&1; then
        git -C "$DATA_REPO_DIR" pull --ff-only origin "$branch" || true
      fi
    fi
  else
    echo "Cloning $DATA_REPO_URL -> $DATA_REPO_DIR"
    mkdir -p "$(dirname "$DATA_REPO_DIR")"
    git clone "$DATA_REPO_URL" "$DATA_REPO_DIR"
  fi

  if [[ ! -f "$DATA_REPO_DIR/README.md" ]]; then
    cat > "$DATA_REPO_DIR/README.md" <<'EOF'
# state-roster-data

Published U.S. state ballot roster snapshots produced by
[vote-usa/state-roster-pipeline](https://github.com/vote-usa/state-roster-pipeline).

Layout:

```
<state>/          # two-letter lowercase code, e.g. ca/, wa/
  elections.json|csv
  candidates.json|csv
  measures.json|csv
  county_directory.json
  county_ballots.json|csv
```

Inputs (`state_catalog.json`, `county_fips.json`, `sources.json`) stay in the
pipeline repo under `data/input/`. The pipeline records the published commit in
`data/input/snapshot.json`.
EOF
  fi
}

sync_from_local() {
  local state lower src dest
  for state in $STATES; do
    lower="$(printf '%s' "$state" | tr '[:upper:]' '[:lower:]')"
    src="$REPO_ROOT/data/output/$lower"
    dest="$DATA_REPO_DIR/$lower"
    if [[ ! -d "$src" ]]; then
      echo "Missing local outputs at $src — run the collector or pass --collect" >&2
      exit 1
    fi
    echo "Copying $src -> $dest"
    mkdir -p "$dest"
    rsync -a --delete \
      --exclude '.git' \
      "$src/" "$dest/"
  done
}

collect_into_data_repo() {
  local state
  for state in $STATES; do
    echo "Collecting $state ($YEAR) -> $DATA_REPO_DIR"
    dotnet run --project "$REPO_ROOT/src/StateBallot.Cli" -- \
      --state "$state" \
      --year "$YEAR" \
      --input-root "$REPO_ROOT/data" \
      --output-root "$DATA_REPO_DIR"
  done
}

commit_data_repo() {
  git -C "$DATA_REPO_DIR" add -A
  if git -C "$DATA_REPO_DIR" diff --cached --quiet; then
    echo "No changes in data repo."
    return 0
  fi
  local msg="${COMMIT_MSG:-Refresh roster snapshots ($(date -u +%Y-%m-%d))}"
  git -C "$DATA_REPO_DIR" -c user.email="${GIT_AUTHOR_EMAIL:-roster-bot@vote-usa.org}" \
    -c user.name="${GIT_AUTHOR_NAME:-roster-bot}" \
    commit -m "$msg"
  echo "Committed in data repo: $(git -C "$DATA_REPO_DIR" rev-parse HEAD)"
}

write_snapshot() {
  local commit generated push_note
  if git -C "$DATA_REPO_DIR" rev-parse --verify HEAD >/dev/null 2>&1; then
    commit="$(git -C "$DATA_REPO_DIR" rev-parse HEAD)"
  else
    commit=""
  fi
  generated="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  if [[ "$DO_PUSH" -eq 0 ]]; then
    push_note="Local sync only; remote push not performed."
  else
    push_note=""
  fi
  mkdir -p "$REPO_ROOT/data/input"
  YEAR="$YEAR" STATES="$STATES" DATA_REPO_URL="$DATA_REPO_URL" \
  COMMIT="$commit" GENERATED="$generated" PUSH_NOTE="$push_note" \
  SNAPSHOT_PATH="$REPO_ROOT/data/input/snapshot.json" \
  python3 - <<'PY'
import json, os
from pathlib import Path
doc = {
    "repository": os.environ["DATA_REPO_URL"],
    "commit": os.environ["COMMIT"] or None,
    "generated_at": os.environ["GENERATED"],
    "year": int(os.environ["YEAR"]),
    "states": os.environ["STATES"].split(),
}
note = os.environ.get("PUSH_NOTE") or None
if note:
    doc["note"] = note
path = Path(os.environ["SNAPSHOT_PATH"])
path.write_text(json.dumps(doc, indent=2) + "\n")
print(f"Wrote {path}")
PY
}

ensure_data_repo

if [[ "$DO_COLLECT" -eq 1 ]]; then
  collect_into_data_repo
else
  sync_from_local
fi

commit_data_repo
write_snapshot

if [[ "$DO_PUSH" -eq 1 ]]; then
  echo "Pushing data repo..."
  git -C "$DATA_REPO_DIR" push -u origin HEAD
else
  echo "Skipping push (pass --push when ready)."
  echo "Data repo: $DATA_REPO_DIR"
  echo "HEAD: $(git -C "$DATA_REPO_DIR" rev-parse HEAD 2>/dev/null || echo '(no commits)')"
fi
