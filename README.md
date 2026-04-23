# ClaudePredictor

A PowerShell `ICommandPredictor` plugin that provides tab-completion and inline suggestions for the Claude Code CLI.

## Features

- Subcommand completion (`claude auth`, `claude mcp`, `claude plugin`, etc.)
- Option value suggestions (`--model=sonnet`, `--permission-mode=auto`, etc.)
- File and directory path completion for relevant options (`--settings`, `--mcp-config`, `--add-dir`, etc.)
- Supports both space-separated and `=`-style option syntax

## Requirements

- PowerShell 7.2+
- PSReadLine 2.2.2+
- .NET SDK 10.0

## Quick Start

### 1. Build

```powershell
dotnet build -c Release
```

The compiled DLL is at `ClaudePredictor/bin/Release/net10.0/ClaudePredictor.dll`.

### 2. Install

Add the following to your PowerShell profile (open it with `notepad $PROFILE`):

```powershell
$predictorPath = Join-Path $PSScriptRoot "path\to\ClaudePredictor.dll"
if (Test-Path $predictorPath) {
    Import-Module $predictorPath -ErrorAction SilentlyContinue
    Set-PSReadLineOption -PredictionSource HistoryAndPlugin
    Set-PSReadLineOption -PredictionViewStyle ListView
    Set-PSReadLineKeyHandler -Chord Ctrl+Spacebar -Function MenuComplete
}
```

Replace the path with the actual DLL location from step 1.

### 3. Notes for psmux users

If you run PowerShell inside psmux, make sure prediction is enabled in `~/.psmux.conf`:

```
set -g allow-predictions on
```

Reload the configuration after updating:

```
tmux source-file ~/.psmux.conf
```

### 4. Verify

```powershell
Get-PSSubsystem -Kind CommandPredictor
```

You should see `ClaudePredictor` listed. Type `claude ` and press `Ctrl+Spacebar` to see suggestions.
