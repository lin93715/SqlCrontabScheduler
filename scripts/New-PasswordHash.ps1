#Requires -Version 7.4
[CmdletBinding()]
param()

$secret = Read-Host '輸入排程平台密碼（至少 12 字元）' -AsSecureString
if ($secret.Length -lt 12) {
    throw '密碼至少需要 12 字元。'
}
try {
    $password = [System.Net.NetworkCredential]::new('', $secret).Password
    $salt = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $hash = [System.Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
        $password, $salt, 600000, [System.Security.Cryptography.HashAlgorithmName]::SHA256, 32
    )
    '{0}.{1}' -f [Convert]::ToBase64String($salt), [Convert]::ToBase64String($hash)
}
finally {
    $password = $null
    $secret.Dispose()
}
