$Script:app = ""
$Script:core = ""
try {
    $now = Get-Date
    $Script:app = Get-Process -Name "StreamFlow.App" -ErrorAction Stop
    $Script:app = "StreamFlow.App:   PID={0} CPU_s={1:N1} WorkingSetMB={2:N0} UptimeSec={3:N0}" -f $Script:app.Id, $Script:app.CPU, ($Script:app.WorkingSet64/1MB), ($now - $Script:app.StartTime).TotalSeconds
}
catch {
    $Script:app = "App not running"
}
try {
    $now = Get-Date
    $Script:core = Get-Process -Name "streamflow-core" -ErrorAction Stop
    $Script:core = "streamflow-core:  PID={0} CPU_s={1:N1} WorkingSetMB={2:N0} UptimeSec={3:N0}" -f $Script:core.Id, $Script:core.CPU, ($Script:core.WorkingSet64/1MB), ($now - $Script:core.StartTime).TotalSeconds
}
catch {
    $Script:core = "Core not running"
}

Write-Output $Script:app
Write-Output $Script:core
