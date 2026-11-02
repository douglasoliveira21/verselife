param([string]$ApiUrl = 'http://localhost:8080')
$ErrorActionPreference = 'Stop'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$username = "smoke_$suffix"
$email = "$username@example.test"
$password = 'SmokePassword123!'

function Invoke-ExpectedFailure([scriptblock]$Action, [int]$StatusCode) {
    try { & $Action; throw "Expected HTTP $StatusCode but request succeeded." }
    catch { if ($_.Exception.Response.StatusCode.value__ -ne $StatusCode) { throw } }
}

$health = Invoke-WebRequest "$ApiUrl/health" -UseBasicParsing
if ($health.StatusCode -ne 200) { throw 'Health check failed.' }

$registered = Invoke-RestMethod "$ApiUrl/api/v1/auth/register" -Method Post -ContentType 'application/json' -Body (@{ username=$username; email=$email; password=$password } | ConvertTo-Json)
if (-not $registered.accessToken -or -not $registered.refreshToken) { throw 'Registration did not return tokens.' }

Invoke-ExpectedFailure { Invoke-RestMethod "$ApiUrl/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body (@{ login=$username; password='WrongPassword123!' } | ConvertTo-Json) } 401

$login = Invoke-RestMethod "$ApiUrl/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body (@{ login=$username; password=$password } | ConvertTo-Json)
$profile = Invoke-RestMethod "$ApiUrl/api/v1/users/me" -Headers @{ Authorization="Bearer $($login.accessToken)" }
if ($profile.username -ne $username) { throw 'Profile mismatch.' }

$rotated = Invoke-RestMethod "$ApiUrl/api/v1/auth/refresh" -Method Post -ContentType 'application/json' -Body (@{ refreshToken=$login.refreshToken } | ConvertTo-Json)
Invoke-ExpectedFailure { Invoke-RestMethod "$ApiUrl/api/v1/auth/refresh" -Method Post -ContentType 'application/json' -Body (@{ refreshToken=$login.refreshToken } | ConvertTo-Json) } 401

Invoke-RestMethod "$ApiUrl/api/v1/auth/logout" -Method Post -ContentType 'application/json' -Body (@{ refreshToken=$rotated.refreshToken } | ConvertTo-Json)
Invoke-ExpectedFailure { Invoke-RestMethod "$ApiUrl/api/v1/auth/refresh" -Method Post -ContentType 'application/json' -Body (@{ refreshToken=$rotated.refreshToken } | ConvertTo-Json) } 401

Write-Output 'Smoke auth tests passed: health, register, invalid password, login, profile, rotation/reuse, and logout.'
