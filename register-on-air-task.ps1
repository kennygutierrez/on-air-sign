param(
    [Parameter(Mandatory)]
    [string]$ScriptPath
)

if (-not (Test-Path $ScriptPath)) {
    Write-Error "Script not found at $ScriptPath"
    exit 1
}

$action  = New-ScheduledTaskAction -Execute "pwsh" -Argument "-WindowStyle Hidden -File `"$ScriptPath`""
$trigger = New-ScheduledTaskTrigger -AtLogOn
Register-ScheduledTask -TaskName "OnAirLight" -Action $action -Trigger $trigger -Force
Get-ScheduledTask -TaskName "OnAirLight" | Select-Object TaskName, State
