import { LogtoProvider, type LogtoConfig } from '@logto/rn';
import { useLogto } from '@logto/rn';
import { Button } from 'react-native';

const config: LogtoConfig = {
  endpoint: 'https://x2b5in.logto.app/',
  appId: 'kfajj4qa53aqzxn0fu8bc',
};

const App = () => (
  <LogtoProvider config={config}>
    <Content />
  </LogtoProvider>
);

const Content = () => {
  const { signIn, signOut, isAuthenticated } = useLogto();

  return (
    <div>
      {isAuthenticated ? (
        <Button title="Sign out" onPress={async () => signOut()} />
      ) : (
        <Button title="Sign in" onPress={async () => signIn('nativepasskeydemo://callback')} />
      )}
    </div>
  );
};

const Content = () => {
  const { getIdTokenClaims, isAuthenticated } = useLogto();
  const [user, setUser] = useState(null);

  useEffect(() => {
    if (isAuthenticated) {
      getIdTokenClaims().then((claims) => {
        setUser(claims); // { sub: '...', ... }
      });
    }
  }, [isAuthenticated, getIdTokenClaims]);

 // ...
};