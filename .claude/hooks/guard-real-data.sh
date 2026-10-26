#!/usr/bin/env bash
# PreToolUse guard for shell-like tool calls (see CLAUDE.md "Real household data").
# Cheap textual pre-filter: most commands cannot touch real data and pass in milliseconds.
# Anything that might is checked precisely by guard-real-data.ps1. Exit 2 blocks the call.
# A missing script, a missing pwsh, a crash or a timeout also blocks: settings.json adds
# "|| exit 2" and onFailure "block".

# ASCII-only case mapping: under tr-TR, lowercasing "I" would yield a dotless "ı".
export LC_ALL=C

payload=$(cat)
lower=${payload,,}
here=$(dirname "${BASH_SOURCE[0]}")
list="$here/protected-paths.local.txt"

suspect=
terms='dotnet|launch|appdata|localapplicationdata|backup|\.db([^a-z0-9_]|$)|git.*push|wealthledger\.(api|operations)'
if [[ $lower =~ $terms ]]; then
  suspect=1
elif [[ ! -f $list ]]; then
  # Worktree without its own list: the PowerShell check finds the main checkout's copy.
  suspect=1
else
  # Pre-filter on the last segment of each private path; the PowerShell check matches the full path.
  while IFS= read -r entry || [[ -n $entry ]]; do
    entry=${entry%$'\r'}
    entry=${entry%[\\/]}
    name=${entry##*[\\/]}
    name=${name,,}
    [[ -z $name || $name == '#'* ]] && continue
    if [[ $lower == *"$name"* ]]; then
      suspect=1
      break
    fi
  done < "$list"
fi

[[ -n $suspect ]] || exit 0

printf '%s' "$payload" | pwsh -NoProfile -NonInteractive -File "$here/guard-real-data.ps1"
