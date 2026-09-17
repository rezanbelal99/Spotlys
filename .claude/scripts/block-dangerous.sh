#!/usr/bin/env bash
# PreToolUse hook for Bash (docs/DEVOPS.md §2). CLAUDE.md suggests; this enforces.
# Reads the tool-call JSON on stdin, blocks (exit 2, message on stderr) on:
#   - git push --force (any spelling)
#   - git reset --hard targeting main
#   - DROP TABLE (any case)
#   - kubectl delete against the production namespace
#   - curl to a host not on the allowlist
set -euo pipefail

command="$(python3 -c "import json,sys; print(json.load(sys.stdin).get('tool_input',{}).get('command',''))" 2>/dev/null || true)"

[ -z "$command" ] && exit 0

block() {
  echo "block-dangerous: $1" >&2
  exit 2
}

lc="$(echo "$command" | tr '[:upper:]' '[:lower:]')"

case "$lc" in
  *"git push"*"--force"*|*"git push"*" -f"*)
    block "git push --force is blocked. Ask the user to run it themselves if truly needed." ;;
esac

case "$lc" in
  *"git reset"*"--hard"*)
    if echo "$lc" | grep -qE '(^|[^a-z])main([^a-z]|$)|--hard\s*$'; then
      block "git reset --hard on/near main is blocked."
    fi
    ;;
esac

case "$lc" in
  *"drop table"*)
    block "DROP TABLE is blocked. Use an EF migration with an explicit rollback path (docs/DEVOPS.md §6)." ;;
esac

case "$lc" in
  *"kubectl delete"*"production"*|*"kubectl "*"-n production"*"delete"*)
    block "kubectl delete against the production namespace is blocked." ;;
esac

if echo "$lc" | grep -qE '\bcurl\b'; then
  allowlist_regex='hvakosterstrommen\.no|api\.met\.no|frost\.met\.no|nve\.no|api\.entsoe\.eu|nuget\.org|npmjs\.org|registry\.npmjs\.org|github\.com|raw\.githubusercontent\.com|localhost|127\.0\.0\.1'
  host="$(echo "$command" | grep -oE 'https?://[^ /"'"'"']+' | head -n1 | sed -E 's#https?://##')"
  if [ -n "$host" ] && ! echo "$host" | grep -qE "$allowlist_regex"; then
    block "curl to '$host' is not on the allowlist. Add it here if it's a real upstream (docs/DATA.md)."
  fi
fi

exit 0
