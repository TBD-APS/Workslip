param([string]$Environment='prod',[string]$COMPANY_NAME='mrsoftwareinc')
$rg="rg-$COMPANY_NAME-$Environment"
$app="api-$COMPANY_NAME-$Environment"
$config="appcs-$COMPANY_NAME-$Environment"
$vault="kv-$COMPANY_NAME-$Environment"
$fail=@()
$tenant=az account show --query tenantId -o tsv
$endpoint=az webapp config appsettings list -g $rg -n $app --query "[?name=='Azure__AppConfiguration__Endpoint'].value | [0]" -o tsv
if ($endpoint -ne "https://$config.azconfig.io") { $fail += 'App Service -> App Config endpoint mismatch' }
$appTenant=az appconfig kv show --name $config --key 'Azure:AdOAuth:TenantId' --auth-mode login --query value -o tsv 2>$null
if ($appTenant -ne $tenant) { $fail += 'App Config tenant mismatch' }
foreach ($pair in @(
  @('Jwt:SigningKey','Jwt--SigningKey'),
  @('Azure:Sql:ConnectionString','Azure--Sql--ConnectionString'),
  @('Azure:Acs:ConnectionString','Azure--Acs--ConnectionString')
)) {
  $value=az appconfig kv show --name $config --key $pair[0] --auth-mode login --query value -o tsv 2>$null
  if ($value -notmatch [regex]::Escape("https://$vault.vault.azure.net/secrets/$($pair[1])")) { $fail += "$($pair[0]) points to wrong Key Vault secret" }
  $enabled=az keyvault secret show --vault-name $vault --name $pair[1] --query attributes.enabled -o tsv 2>$null
  if ($enabled -ne 'true') { $fail += "$($pair[1]) missing/disabled" }
}
if ($fail.Count) { $fail | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }; exit 1 }
Write-Host 'POST-DEPLOY CONFIG AUDIT: PASSED' -ForegroundColor Green
