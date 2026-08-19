param([string]$Environment='prod',[string]$COMPANY_NAME='mrsoftwareinc')
$rg="rg-$COMPANY_NAME-$Environment"
$app="api-$COMPANY_NAME-$Environment"
$config="appcs-$COMPANY_NAME-$Environment"
$hostName=az webapp show -g $rg -n $app --query defaultHostName -o tsv
if ($LASTEXITCODE -ne 0 -or -not $hostName) { throw 'Could not resolve Azure web app hostname.' }
$base="https://$hostName"
@{
  'Azure:Domain:BaseUrl'=$base
  'Cors:AllowedOrigins:0'=$base
  'Azure:Acs:InviteBaseUrl'="$base/invite"
}.GetEnumerator() | ForEach-Object {
  az appconfig kv set --name $config --key $_.Key --value $_.Value --auth-mode login --yes -o none
  if ($LASTEXITCODE -ne 0) { throw "Failed setting $($_.Key)" }
}
Write-Host "Preview URL set to $base"
