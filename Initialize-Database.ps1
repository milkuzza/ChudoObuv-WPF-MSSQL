param(
    [string]$Server = '(LocalDB)\MSSQLLocalDB',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,100}$')][string]$Database = 'ChudoObuv'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
if ($env:CHUDOOBUV_CONNECTION_STRING) {
    $builder.ConnectionString = $env:CHUDOOBUV_CONNECTION_STRING
    if ($builder['Initial Catalog']) { $Database = $builder['Initial Catalog'] }
} else {
    $builder['Data Source'] = $Server
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 30
}
if ($Database -notmatch '^[A-Za-z][A-Za-z0-9_]{0,100}$') { throw 'Use a database name containing only letters, digits and underscore.' }
$builder['Initial Catalog'] = 'master'
$connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 60
    $command.CommandText = 'SELECT DB_ID(@database)'
    [void]$command.Parameters.AddWithValue('@database', $Database)
    $exists = $command.ExecuteScalar()
    $command.Parameters.Clear()
    if ($exists -is [DBNull]) {
        $command.CommandText = 'CREATE DATABASE [' + $Database + '] COLLATE Cyrillic_General_100_CI_AS'
        [void]$command.ExecuteNonQuery()
    }
    $connection.ChangeDatabase($Database)
    $command.CommandText = "SELECT CONVERT(nvarchar(30),value) FROM sys.extended_properties WHERE class=0 AND name=N'ChudoObuv.SchemaVersion'"
    $version = $command.ExecuteScalar()
    if ($version -eq '1') {
        Write-Host ('Database ready: ' + $builder['Data Source'] + ' / ' + $Database)
        return
    }
    $command.CommandText = 'SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0'
    if ([int]$command.ExecuteScalar() -gt 0) { throw 'This database already contains tables. Use a new database name; existing data will not be overwritten.' }
    foreach ($file in @('01_schema.sql','02_seed.sql')) {
        $sql = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot ('database\' + $file)), [System.Text.Encoding]::UTF8)
        foreach ($batch in [regex]::Split($sql, '(?im)^\s*GO\s*\r?$')) {
            if (-not [string]::IsNullOrWhiteSpace($batch)) {
                $command.CommandText = $batch
                [void]$command.ExecuteNonQuery()
            }
        }
    }
    Write-Host ('Database created and imported: ' + $Database)
} catch {
    Write-Error ('Database initialization failed. Check SQL Server/LocalDB and START.md. ' + $_.Exception.Message)
    exit 1
} finally {
    if ($connection) { $connection.Dispose() }
}
