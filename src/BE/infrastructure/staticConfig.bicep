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
  'Azure:AdOAuth:TenantId': tenant().tenantId
  'Azure:AdOAuth:Instance': 'https://login.microsoftonline.com/'

  // Authorization policies
  'Authorization:Policies:RequireSuperadmin': 'SuperAdmin'
  'Authorization:Policies:RequireAdmin': 'Admin'
  'Authorization:Policies:RequireUser': 'User'

  //Storage account
  'Azure:DocumentFileStorage:ContainerName': 'report-attachments' //fix
  'Azure:DocumentFileStorage:LocalRootPath': 'UploadedFiles' //fix

  // Role hierarchy
  'Authorization:RoleHierarchy:Superadmin:0': 'Admin'
  'Authorization:RoleHierarchy:Admin:0': 'User'
}
