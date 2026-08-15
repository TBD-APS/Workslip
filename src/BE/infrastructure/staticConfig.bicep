param appConfigurationName string

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2023-03-01' existing = {
  name: appConfigurationName
}

resource staticConfigValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = [
  for item in items(appConfigValues): {
    parent: appConfiguration
    name: item.key
    properties: {
      value: string(item.value)
    }
  }
]

var appConfigValues = {
  'Azure:Domain:BaseUrl': 'https://app.mrsoftware.dk'
  'Cors:AllowedOrigins:0': 'https://app.mrsoftware.dk'
  'Cors:AllowedOrigins:1': 'https://workslip-v2-0.vercel.app'

  'Azure:AdOAuth:TenantId': az.tenant().tenantId
  'Azure:AdOAuth:Instance': az.environment().authentication.loginEndpoint

  'Jwt:Issuer': 'WorkslipApi'
  'Jwt:Audience': 'WorkslipClient'
  'Jwt:ExpiryMinutes': '60'

  'Authorization:Policies:RequireSuperadmin': 'Superadmin'
  'Authorization:Policies:RequireAdmin': 'Admin'
  'Authorization:Policies:RequireAuditor': 'Auditor'
  'Authorization:Policies:RequireReadAccess': 'User|Auditor'
  'Authorization:Policies:RequireUser': 'User'

  'Azure:DocumentFileStorage:ContainerName': 'uploads'
  'Azure:DocumentFileStorage:LocalRootPath': 'UploadedFiles'
  'PowerBiExport:HistoryMonths': '24'
  'PowerBiExport:RefreshIntervalMinutes': '60'

  'Authorization:RoleHierarchy:Superadmin:0': 'Admin'
  'Authorization:RoleHierarchy:Admin:0': 'User'

  'Azure:Acs:InviteBaseUrl': 'https://app.mrsoftware.dk/invite'
  'Azure:Acs:PLainHeaderText': 'Du er blevet inviteret til Workslip af MR Software'
  'Azure:Acs:HtmlInviteText': '''
  <html>
  <body style="font-family: Arial, sans-serif; padding: 24px;">
    <h2>Velkommen til Workslip</h2>
    <p>Du er blevet inviteret til at deltage i Workslip.</p>
    <p>
      <a href="{inviteLink}"
         style="display: inline-block; padding: 12px 24px; background-color: #0057b7; color: #fff; text-decoration: none; border-radius: 6px;">
        Accepter invitation
      </a>
    </p>
    <p>Linket udløber om 7 dage.</p>
    <hr/>
    <p style="color: #666; font-size: 12px;">Workslip – automatisk invitation</p>
  </body>
  </html>'''
  'Azure:Acs:PlainInviteText': '''
    Du er blevet inviteret til Workslip.
    Klik på følgende link for at acceptere invitationen:
    {inviteLink}
    Linket udløber om 7 dage.
  '''
  'Azure:Acs:OtcHeaderText': 'Din midlertidige adgangskode til Workslip'
  'Azure:Acs:OtcHtmlText': '''
  <html>
  <body style="font-family: Arial, sans-serif; padding: 24px;">
    <h2>Midlertidig adgangskode</h2>
    <p>Du har anmodet om en midlertidig adgangskode til Workslip. Din adgangskode er:</p>
    <p style="font-size: 32px; font-weight: bold; letter-spacing: 8px; text-align: center; padding: 16px; background-color: #f5f5f5; border-radius: 8px;">
      {otcCode}
    </p>
    <p>Koden udløber om 10 minutter og kan kun bruges én gang.</p>
    <p>Hvis du ikke har bedt om denne kode, kan du ignorere denne email.</p>
    <hr/>
    <p style="color: #666; font-size: 12px;">Workslip af MR Software</p>
  </body>
  </html>'''
  'Azure:Acs:OtcPlainText': '''
    Du har anmodet om en midlertidig adgangskode til Workslip.

    Din adgangskode er: {otcCode}

    Koden udløber om 10 minutter og kan kun bruges én gang.
    Hvis du ikke har bedt om denne kode, kan du ignorere denne email.

    Workslip af MR Software
  '''
}
