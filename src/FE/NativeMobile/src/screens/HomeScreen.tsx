import { View, Text, TouchableOpacity, StyleSheet, Alert } from 'react-native';
import { useAuth } from '../auth/AuthContext';

export default function HomeScreen() {
  const { user, logout } = useAuth();

  const handleLogout = () => {
    Alert.alert('Sign out', 'Are you sure?', [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Sign out', style: 'destructive', onPress: logout },
    ]);
  };

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Signed In</Text>

      <View style={styles.card}>
        <Text style={styles.label}>Name</Text>
        <Text style={styles.value}>{user?.name}</Text>

        <Text style={styles.label}>Email</Text>
        <Text style={styles.value}>{user?.email ?? user?.preferred_username}</Text>

        <Text style={styles.label}>User ID</Text>
        <Text style={styles.mono}>{user?.sub}</Text>
      </View>

      <TouchableOpacity
        style={styles.button}
        onPress={handleLogout}
        activeOpacity={0.8}
      >
        <Text style={styles.buttonText}>Sign Out</Text>
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#fff',
    padding: 24,
    justifyContent: 'center',
  },
  title: {
    fontSize: 28,
    fontWeight: '700',
    marginBottom: 24,
    textAlign: 'center',
  },
  card: {
    backgroundColor: '#f5f5f5',
    borderRadius: 12,
    padding: 20,
    marginBottom: 32,
  },
  label: {
    fontSize: 12,
    fontWeight: '600',
    color: '#999',
    textTransform: 'uppercase',
    marginTop: 12,
    marginBottom: 4,
  },
  value: {
    fontSize: 16,
    color: '#333',
  },
  mono: {
    fontSize: 13,
    color: '#666',
    fontFamily: 'monospace',
  },
  button: {
    backgroundColor: '#e0e0e0',
    paddingVertical: 14,
    borderRadius: 8,
    alignItems: 'center',
  },
  buttonText: {
    color: '#333',
    fontSize: 16,
    fontWeight: '600',
  },
});
