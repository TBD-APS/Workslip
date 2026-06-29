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

  //urls
  'Azure:Domain:BaseUrl': 'https://workslip-v2-0.vercel.app'

  //OAuth
  'Azure:AdOAuth:TenantId': az.tenant().tenantId
  'Azure:AdOAuth:Instance': az.environment().authentication.loginEndpoint
  
  //LocalJwt
  'Jwt:Issuer': 'WorkslipApi'
  'Jwt:Audience': 'WorkslipClient'
  'Jwt:ExpiryMinutes': '60'
  // Authorization policies
  'Authorization:Policies:RequireSuperadmin': 'Superadmin'
  'Authorization:Policies:RequireAdmin': 'Admin'
  'Authorization:Policies:RequireAuditor': 'Auditor'
  'Authorization:Policies:RequireReadAccess': 'User|Auditor'
  'Authorization:Policies:RequireUser': 'User'

  //Storage account
  'Azure:DocumentFileStorage:ContainerName': 'report-attachments' //fix
  'Azure:DocumentFileStorage:LocalRootPath': 'UploadedFiles' //fix
  
  // Role hierarchy
  'Authorization:RoleHierarchy:Superadmin:0': 'Admin'
  'Authorization:RoleHierarchy:Admin:0': 'User'
  
  //Email
  'Azure:Acs:InviteBaseUrl': 'https://workslip-v2-0.vercel.app/invite'
  'Azure:Acs:PLainHeaderText': 'Du er blevet inviteret til Workslip'
  
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
}
