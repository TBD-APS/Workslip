@description('Azure region for the self-hosted PostHog VM.')
param location string = resourceGroup().location

@description('VM name.')
param vmName string

@description('Linux administrator username.')
param adminUsername string

@secure()
@description('SSH public key used for VM administration.')
param sshPublicKey string

@description('Single trusted CIDR allowed to reach SSH, normally the deploying operator public IP as /32.')
param adminSourceCidr string

@description('Public DNS label. Azure exposes <label>.<region>.cloudapp.azure.com.')
param dnsLabel string

@description('VM SKU. PostHog self-hosting currently recommends roughly 4 vCPU and 16 GiB RAM.')
param vmSize string = 'Standard_D4as_v5'

@description('Persistent Docker data disk size in GiB.')
param dataDiskSizeGb int = 128

var vnetName = '${vmName}-vnet'
var subnetName = 'posthog'
var nsgName = '${vmName}-nsg'
var publicIpName = '${vmName}-pip'
var nicName = '${vmName}-nic'
var dataDiskName = '${vmName}-data'

var cloudInit = '''
#cloud-config
package_update: true
package_upgrade: true
packages:
  - ca-certificates
  - curl
  - jq
  - openssl
write_files:
  - path: /usr/local/sbin/prepare-posthog-data.sh
    permissions: '0755'
    content: |
      #!/usr/bin/env bash
      set -euo pipefail
      disk=/dev/disk/azure/scsi1/lun0
      for attempt in $(seq 1 60); do
        if [ -b "$disk" ]; then
          break
        fi
        sleep 2
      done
      if [ ! -b "$disk" ]; then
        echo "PostHog data disk was not attached at $disk" >&2
        exit 1
      fi
      if ! blkid "$disk" >/dev/null 2>&1; then
        mkfs.ext4 -F "$disk"
      fi
      mkdir -p /var/lib/docker
      uuid=$(blkid -s UUID -o value "$disk")
      if ! grep -q "UUID=$uuid /var/lib/docker" /etc/fstab; then
        echo "UUID=$uuid /var/lib/docker ext4 defaults,nofail 0 2" >> /etc/fstab
      fi
      if ! mountpoint -q /var/lib/docker; then
        mount /var/lib/docker
      fi
runcmd:
  - [ bash, -lc, '/usr/local/sbin/prepare-posthog-data.sh' ]
'''

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: nsgName
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowHttps'
        properties: {
          priority: 100
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowHttpForLetsEncrypt'
        properties: {
          priority: 110
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '80'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowSshFromOperator'
        properties: {
          priority: 120
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '22'
          sourceAddressPrefix: adminSourceCidr
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource publicIp 'Microsoft.Network/publicIPAddresses@2023-11-01' = {
  name: publicIpName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: {
      domainNameLabel: dnsLabel
    }
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.90.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.90.1.0/24'
          networkSecurityGroup: {
            id: nsg.id
          }
        }
      }
    ]
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2023-11-01' = {
  name: nicName
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'primary'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: {
            id: publicIp.id
          }
          subnet: {
            id: resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, subnetName)
          }
        }
      }
    ]
  }
}

resource dataDisk 'Microsoft.Compute/disks@2023-10-02' = {
  name: dataDiskName
  location: location
  sku: {
    name: 'StandardSSD_LRS'
  }
  properties: {
    creationData: {
      createOption: 'Empty'
    }
    diskSizeGB: dataDiskSizeGb
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: vmName
  location: location
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      customData: base64(cloudInit)
      linuxConfiguration: {
        disablePasswordAuthentication: true
        provisionVMAgent: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: sshPublicKey
            }
          ]
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        diskSizeGB: 64
        managedDisk: {
          storageAccountType: 'StandardSSD_LRS'
        }
      }
      dataDisks: [
        {
          lun: 0
          name: dataDisk.name
          createOption: 'Attach'
          caching: 'ReadWrite'
          managedDisk: {
            id: dataDisk.id
          }
        }
      ]
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
          properties: {
            primary: true
          }
        }
      ]
    }
  }
}

output vmName string = vm.name
output fqdn string = publicIp.properties.dnsSettings.fqdn
output publicIpAddress string = publicIp.properties.ipAddress
output dataDiskName string = dataDisk.name
