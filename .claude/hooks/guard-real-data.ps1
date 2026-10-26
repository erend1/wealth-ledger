# PreToolUse guard for shell-like tool calls (see CLAUDE.md "Real household data").
# Reads the hook payload from stdin. Exit 2 blocks the call and returns stderr to Claude.
# This is a textual guardrail against accidents, not a security boundary.
# Private paths (for example the real backup folder) go one per line in the gitignored
# .claude/hooks/protected-paths.local.txt of the main checkout, never in the public repository.

$ErrorActionPreference = 'Stop'

try {
    [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $command = [string]$payload.tool_input.command
    if ([string]::IsNullOrWhiteSpace($command) -and $payload.tool_name -notin @('Bash', 'PowerShell')) {
        # Other tools (Monitor, terminal tools): check every input field as text.
        $command = $payload.tool_input | ConvertTo-Json -Compress -Depth 6
    }
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

    $reasons = [System.Collections.Generic.List[string]]::new()
    function Block([string]$reason) {
        if (-not $reasons.Contains($reason)) { $reasons.Add($reason) }
    }

    # The check runs per statement. Quoted text is not parsed, so a message that mentions a
    # launch still trips rule 1 unless its statement is one of the text-only commands below.
    $statements = @($command -split '\r?\n|;|&&|\|\||\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $messageOnly = '^(git\s+(commit|tag|notes|log|show|diff)\b|gh\s+(pr|issue|release)\s+(view|list|create|edit|comment|review)\b|echo\b|Write-(Output|Host)\b)'
    $searchOnly = '^(rg|grep|findstr|Select-String|git\s+grep)\b'
    $commandHasLabFlags = ($command -match '--Storage:DatabasePath=') -and ($command -match '--Backup:Directory=')

    foreach ($statement in $statements) {
        $isMessage = $statement -match $messageOnly

        # 1. Starting the host or the Operations CLI requires an explicit synthetic lab (/safe-lab).
        if (-not $isMessage -and $statement -notmatch $searchOnly) {
            $startProcess = $statement -match '\bStart-Process\b'
            if ($statement -match '\bdotnet(\.exe)?["'']?\s+watch\b' -or
                ($startProcess -and $statement -match '\bdotnet\b' -and $statement -match '\bwatch\b')) {
                Block 'dotnet watch is not allowed in this repository.'
            }
            $isRun = ($statement -match '\bdotnet(\.exe)?["'']?\s+run\b') -or
                ($startProcess -and $statement -match '\bdotnet(\.exe)?\b' -and $statement -match '\brun\b')
            $launchesBinary = ($statement -match '^(&\s*)?["'']?([^\s"'']*[\\/])?WealthLedger\.(Api|Operations)(\.exe|\.dll)?["'']?(\s|$)') -or
                ($statement -match '\bdotnet(\.exe)?["'']?\s+(exec\s+)?["'']?[^\s"'']*WealthLedger\.(Api|Operations)\.dll\b') -or
                ($startProcess -and $statement -match 'WealthLedger\.(Api|Operations)\b')
            if ($isRun -and ($statement -split '\s--(\s|$)', 2)[0] -notmatch '--no-launch-profile\b') {
                Block 'dotnet run needs --no-launch-profile before the "--" separator (launchSettings.json targets real data).'
            }
            if ($isRun -or $launchesBinary) {
                $statementHasLabFlags = ($statement -match '--Storage:DatabasePath=') -and ($statement -match '--Backup:Directory=')
                $usesVariables = $statement -match '(@\w+|\$\{?\w+)'
                if (-not ($statementHasLabFlags -or ($usesVariables -and $commandHasLabFlags))) {
                    Block 'starting the host or Operations CLI needs explicit synthetic --Storage:DatabasePath= and --Backup:Directory= in the same call; use /safe-lab.'
                }
            }
        }

        # 2. Never touch the real default data folder or the launch profile.
        if (-not $isMessage) {
            if ($statement -match '(LOCALAPPDATA|LocalApplicationData|AppData[\\/]+Local)\W{1,12}WealthLedger(?![\w-])') {
                Block 'the command refers to the real %LOCALAPPDATA%\WealthLedger data folder.'
            }
            elseif ($statement -match '(LOCALAPPDATA|LocalApplicationData|AppData[\\/]+Local)\b.*(wealth|\*\.db\b|\.wlbackup)' -and
                $statement -notmatch 'WealthLedger(Labs|-Learning)') {
                Block 'searching app data for WealthLedger files is off limits; work only inside a WealthLedgerLabs lab.'
            }
            if ($statement -match '(USERPROFILE|\$\{?HOME\b|~[\\/]|[A-Za-z]:[\\/]+Users\b|/[a-z]/Users\b)' -and
                $statement -match '(-Recurse\b|\bfind\b|\s-r\b|--recursive\b)' -and
                $statement -match '(\.db\b|\.wlbackup\b|\*\.db)' -and
                $statement -notmatch 'WealthLedgerLabs') {
                Block 'recursive searches of the user profile for databases or backups are off limits; work only inside a WealthLedgerLabs lab.'
            }
            if ($statement -match 'launch(Settings\b|\*|\?)') {
                Block 'launchSettings.json points at real data and is off limits.'
            }
        }

        # 3. No force-push and no push to main.
        $push = [regex]::Match($statement, '\bgit\b.*?\bpush\b(?<args>.*)$')
        if (-not $isMessage -and $push.Success) {
            $pushArgs = $push.Groups['args'].Value
            if ($pushArgs -match '(\s--force(-with-lease|-if-includes)?\b|\s-[a-zA-Z]*f[a-zA-Z]*\b|\s\+\S|\s--(mirror|all)\b)') {
                Block 'force-push, --mirror and --all are not allowed.'
            }
            if ($pushArgs -match '(?<=[\s:''"])(refs/heads/)?main(?=[\s:''"]|$)') {
                Block 'never push to main; open a PR instead.'
            }
        }
    }

    # 4. Private paths from the gitignored list. Worktrees without their own copy use the
    # main checkout's list; with no list anywhere the command is blocked, not waved through.
    $listName = '.claude/hooks/protected-paths.local.txt'
    $protectedList = Join-Path $PSScriptRoot 'protected-paths.local.txt'
    if (-not (Test-Path -LiteralPath $protectedList)) {
        $cwd = if ($payload.cwd) { [string]$payload.cwd } else { (Get-Location).Path }
        $commonDir = & git -C $cwd rev-parse --path-format=absolute --git-common-dir 2>$null
        if ($LASTEXITCODE -eq 0 -and $commonDir) {
            $protectedList = Join-Path (Split-Path -Parent $commonDir) $listName
        }
    }
    if (-not (Test-Path -LiteralPath $protectedList)) {
        Block "the private-path list is missing; create $listName in the main checkout (comment lines only is fine)."
    }
    else {
        $normalized = $command.ToLowerInvariant().Replace('\', '/')
        $homeForm = ([string]$env:USERPROFILE).ToLowerInvariant().Replace('\', '/').TrimEnd('/')
        foreach ($line in Get-Content -LiteralPath $protectedList) {
            $entry = $line.Trim()
            if ($entry -eq '' -or $entry.StartsWith('#')) { continue }
            $windowsForm = $entry.ToLowerInvariant().Replace('\', '/').TrimEnd('/')
            $forms = @($windowsForm)
            if ($windowsForm -match '^([a-z]):/(.*)$') { $forms += "/$($Matches[1])/$($Matches[2])" }
            if ($homeForm -and $windowsForm.StartsWith("$homeForm/")) {
                # Home-relative spellings: ~/x, $HOME/x, $env:USERPROFILE\x, %USERPROFILE%\x.
                $rest = $windowsForm.Substring($homeForm.Length)
                $forms += @("~$rest", "`$home$rest", "`${home}$rest", "`$env:userprofile$rest", "%userprofile%$rest", "`$userprofile$rest")
            }
            # The last path segment as a whole token also catches Join-Path and variable forms.
            $name = ($windowsForm -split '/')[-1]
            $nameHit = $name -and ($normalized -match ('(?<![\w-])' + [regex]::Escape($name) + '(?![\w-])'))
            if ($nameHit -or ($forms | Where-Object { $normalized.Contains($_) })) {
                Block 'the command refers to a protected private path.'
                break
            }
        }
    }

    if ($reasons.Count -gt 0) {
        [Console]::Error.WriteLine('WealthLedger guard blocked this command: ' + ($reasons -join ' '))
        exit 2
    }
    exit 0
}
catch {
    [Console]::Error.WriteLine("WealthLedger guard could not check this command, so it is blocked to be safe: $_")
    exit 2
}
