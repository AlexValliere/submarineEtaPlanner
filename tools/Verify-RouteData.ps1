param(
    [string]$UpstreamFile
)

$expectedHash = "24996254FAB3FFC4A74F1AFA2C9212732888A0C6387DAB026B75EA566B6D67FF"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localFile = Join-Path $repositoryRoot "src/SubmarineEtaPlanner/CalculatedData.msgpack"

if (-not (Test-Path -LiteralPath $localFile -PathType Leaf)) {
    throw "Bundled route data was not found at $localFile"
}

$localHash = (Get-FileHash -LiteralPath $localFile -Algorithm SHA256).Hash
if ($localHash -ne $expectedHash) {
    throw "Bundled route-data hash mismatch. Expected $expectedHash, found $localHash."
}

Write-Host "Bundled route data verified: $localHash"

if (-not [string]::IsNullOrWhiteSpace($UpstreamFile)) {
    $resolvedUpstream = (Resolve-Path -LiteralPath $UpstreamFile).Path
    $upstreamHash = (Get-FileHash -LiteralPath $resolvedUpstream -Algorithm SHA256).Hash
    if ($upstreamHash -ne $localHash) {
        throw "Upstream route data does not match the bundled file. Upstream: $upstreamHash; bundled: $localHash."
    }

    Write-Host "Upstream file matches byte-for-byte: $resolvedUpstream"
}
