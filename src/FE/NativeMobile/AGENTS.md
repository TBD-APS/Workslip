# Expo HAS CHANGED

Read the exact versioned docs at https://docs.expo.dev/versions/v56.0.0/ before writing any code.

# Cross-Platform Azure AD + Passkey Auth

## Azure AD App Registration

1. Go to **Azure Portal > App Registrations > New Registration**
2. Name: `NativeMobile`, supported account types: your choice
3. Add **two redirect URIs** under `Mobile and desktop applications`:
   - `nativepasskeydemo://auth`
4. Under **Authentication > Advanced settings**: set `Allow public client flows` to **Yes**
5. No client secret needed (public client with PKCE)
6. Note the **Application (client) ID** and **Directory (tenant) ID**

The same redirect URI `nativepasskeydemo://auth` works for both iOS and Android because the `expo-auth-session` library generates the correct platform-specific deep link.

## Environment Variables

Create `.env` (gitignored) in project root:

```
EXPO_PUBLIC_AZURE_TENANT_ID=your-tenant-id
EXPO_PUBLIC_AZURE_CLIENT_ID=your-client-id
```

## Passkey Support

Works identically on both platforms via the system browser:

| Platform | Browser Component | Passkey Provider |
|----------|------------------|------------------|
| iOS      | ASWebAuthenticationSession | iCloud Keychain |
| Android  | Chrome Custom Tabs | Google Password Manager |

The Microsoft login page handles the WebAuthn ceremony natively. When the user's Azure AD account has passkeys registered, they're prompted automatically during sign-in.

## Building & Running

```bash
# Install dependencies
npm install

# Development builds (requires dev client, not Expo Go)
npx expo run:ios
npx expo run:android

# Or use EAS Build for production
eas build --platform all --profile development
```

## iOS-Specific Notes

- Xcode required (macOS only)
- `usesNonExemptEncryption: false` is set in app.json to avoid App Store export compliance prompts
- `NSFaceIDUsageDescription` configured for biometric auth in SecureStore
- Passkeys require iCloud Keychain enabled on device and Apple Developer account

## Android-Specific Notes

- Android Studio required for dev builds
- `compileSdkVersion` defaults to 34+ (Expo SDK 56)
- Passkeys require Google Play Services and Chrome installed
- Android Auto Backup configured to exclude SecureStore data
