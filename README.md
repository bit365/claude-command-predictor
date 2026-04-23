# ClaudePredictor

`ClaudePredictor` is a PowerShell `ICommandPredictor` plugin for `claude` CLI completion.

## Requirements

- PowerShell `7.2+`
- `PSReadLine 2.2.2+`
- .NET SDK `10.0` (project target: `net10.0`)

## Build

From repo root:

```powershell
dotnet build .\ClaudePredictor\ClaudePredictor.csproj -c Release
```

Output:

`.\ClaudePredictor\bin\Release\net10.0\ClaudePredictor.dll`

## Use in current `pwsh` session

```powershell
Import-Module "C:\Users\stack\source\repos\ClaudePredictor\ClaudePredictor\bin\Release\net10.0\ClaudePredictor.dll"
Set-PSReadLineOption -PredictionSource HistoryAndPlugin
Set-PSReadLineOption -PredictionViewStyle ListView
Set-PSReadLineKeyHandler -Chord Ctrl+Spacebar -Function MenuComplete
```

Verify:

```powershell
Get-PSSubsystem -Kind CommandPredictor
```

## Auto-load on PowerShell startup

Open your profile:

```powershell
if (!(Test-Path $PROFILE)) { New-Item -Path $PROFILE -ItemType File -Force | Out-Null }
notepad $PROFILE
```

Add:

```powershell
$claudePredictorDll = "C:\Users\stack\source\repos\ClaudePredictor\ClaudePredictor\bin\Release\net10.0\ClaudePredictor.dll"
if (Test-Path $claudePredictorDll) {
    Import-Module $claudePredictorDll -ErrorAction SilentlyContinue
    Set-PSReadLineOption -PredictionSource HistoryAndPlugin
    Set-PSReadLineOption -PredictionViewStyle ListView
    Set-PSReadLineKeyHandler -Chord Ctrl+Spacebar -Function MenuComplete
}
