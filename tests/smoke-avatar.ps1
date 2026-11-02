param(
    [string]$ApiUrl = 'http://127.0.0.1:58080',
    [Parameter(Mandatory)][string]$Username,
    [Parameter(Mandatory)][string]$Password,
    [ValidateSet('Create','Verify')][string]$Mode = 'Create'
)
$ErrorActionPreference = 'Stop'

if ($Mode -eq 'Create') {
    $auth = Invoke-RestMethod "$ApiUrl/api/v1/auth/register" -Method Post -ContentType 'application/json' -Body (@{
        username=$Username; email="$Username@example.test"; password=$Password
    } | ConvertTo-Json)
} else {
    $auth = Invoke-RestMethod "$ApiUrl/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body (@{
        login=$Username; password=$Password
    } | ConvertTo-Json)
}

$headers = @{ Authorization="Bearer $($auth.accessToken)" }
if ($Mode -eq 'Create') {
    $saved = Invoke-RestMethod "$ApiUrl/api/v1/users/me/avatar" -Method Put -Headers $headers -ContentType 'application/json' -Body (@{
        displayName='Persistent Resident'; baseModel='human_b'; skinColor='#7A4E35'; hairColor='#D6B36A'; hairStyle='mohawk';
        cameraSensitivity=0.0045; invertCameraY=$true;
        equippedItems=@(
            @{slot='top'; itemKey='jacket_basic'; tintColor='#AA3366'},
            @{slot='bottom'; itemKey='shorts_basic'; tintColor='#224488'},
            @{slot='shoes'; itemKey='boots_basic'; tintColor='#332211'},
            @{slot='accessory'; itemKey='glasses_round'; tintColor='#E9C46A'}
        )
    } | ConvertTo-Json -Depth 5)
}

$loaded = Invoke-RestMethod "$ApiUrl/api/v1/users/me/avatar" -Headers $headers
if ($loaded.displayName -ne 'Persistent Resident' -or $loaded.baseModel -ne 'human_b' -or $loaded.hairStyle -ne 'mohawk' -or -not $loaded.invertCameraY) {
    throw 'Avatar persistence mismatch.'
}
if (($loaded.equippedItems | Where-Object slot -eq 'accessory').itemKey -ne 'glasses_round') { throw 'Equipment persistence mismatch.' }
Write-Output "Avatar persistence $Mode passed for $Username."
