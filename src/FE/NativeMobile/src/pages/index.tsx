import { useHandleSignInCallback } from '@logto/react';
import { useLogto } from '@logto/react';

const Callback = () => {
  const { isLoading } = useHandleSignInCallback(() => {
    // Navigate to root path when finished
  });

  // When it's working in progress
  if (isLoading) {
    return <div>Redirecting...</div>;
  }

  return null;
};


const Home = () => {
  const { signIn, signOut, isAuthenticated } = useLogto();

  return isAuthenticated ? (
    <button onClick={() => signOut('http://localhost:8081/')}>Sign Out</button>
  ) : (
    <button onClick={() => signIn('http://localhost:8081/callback')}>Sign In</button>
  );
};