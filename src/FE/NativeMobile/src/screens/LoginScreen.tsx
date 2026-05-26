import { View, Text, TouchableOpacity, StyleSheet, ActivityIndicator } from 'react-native';
import { useAuth } from '../auth/AuthContext';

export default function LoginScreen() {
  const { login, isLoading } = useAuth();

  return (
    <View style={styles.container}>
      <Text style={styles.title}>NativeMobile</Text>
      <Text style={styles.subtitle}>Passkey-ready Azure AD auth</Text>

      <TouchableOpacity
        style={styles.button}
        onPress={login}
        disabled={isLoading}
        activeOpacity={0.8}
      >
        {isLoading ? (
          <ActivityIndicator color="#fff" />
        ) : (
          <Text style={styles.buttonText}>Sign in with Microsoft</Text>
        )}
      </TouchableOpacity>

      <Text style={styles.hint}>
        Uses system browser for native passkey support.{'\n'}
        Sign in with a passkey-enabled Azure AD account.
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#fff',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
  },
  title: {
    fontSize: 32,
    fontWeight: '700',
    marginBottom: 8,
  },
  subtitle: {
    fontSize: 16,
    color: '#666',
    marginBottom: 48,
  },
  button: {
    backgroundColor: '#0078D4',
    paddingVertical: 14,
    paddingHorizontal: 32,
    borderRadius: 8,
    minWidth: 260,
    alignItems: 'center',
  },
  buttonText: {
    color: '#fff',
    fontSize: 16,
    fontWeight: '600',
  },
  hint: {
    marginTop: 32,
    fontSize: 13,
    color: '#999',
    textAlign: 'center',
    lineHeight: 20,
  },
});
