param([Parameter(Mandatory=$true)][string]$Path)
$a = [Reflection.Assembly]::LoadFile($Path)
@($a.GetManifestResourceNames() | Where-Object { $_ -like '__m.*' }).Count
