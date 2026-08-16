import assert from 'node:assert/strict';
import test from 'node:test';
import { issueLocalDevelopmentToken, requireLoopbackOrigin } from './playwright-ephemeral-auth.mjs';

test('accepts only loopback origins', () => {
  assert.equal(requireLoopbackOrigin('http://127.0.0.1:5270', 'app'), 'http://127.0.0.1:5270');
  assert.equal(requireLoopbackOrigin('http://localhost:5262', 'api'), 'http://localhost:5262');
  assert.equal(requireLoopbackOrigin('http://[::1]:5262', 'api'), 'http://[::1]:5262');
});

test('rejects remote and disguised loopback targets', () => {
  for (const value of [
    'https://app.mrsoftware.dk',
    'https://workslip-v2-0.vercel.app',
    'http://127.0.0.1.evil.example:5262',
    'http://localhost.evil.example:5262',
    'file:///tmp/workslip',
  ]) {
    assert.throws(() => requireLoopbackOrigin(value, 'target'), /loopback|HTTP/);
  }
});

test('rejects credentials and non-origin values', () => {
  assert.throws(() => requireLoopbackOrigin('http://user:pass@127.0.0.1:5262', 'api'), /credentials/);
  assert.throws(() => requireLoopbackOrigin('http://127.0.0.1:5262/api', 'api'), /origin/);
  assert.throws(() => requireLoopbackOrigin('http://127.0.0.1:5262/?token=x', 'api'), /query/);
});

test('remote target fails before any token request', async () => {
  let calls = 0;
  await assert.rejects(
    () => issueLocalDevelopmentToken({
      apiUrl: 'https://app.mrsoftware.dk',
      email: 'admin@example.invalid',
      fetchImpl: async () => {
        calls += 1;
        throw new Error('must not be called');
      },
    }),
    /loopback/,
  );
  assert.equal(calls, 0);
});

test('development token response must contain token and user', async () => {
  const fetchImpl = async (url, options) => {
    assert.equal(url, 'http://127.0.0.1:5262/api/dev/token');
    assert.equal(options.method, 'POST');
    return {
      ok: true,
      status: 200,
      json: async () => ({ token: 'synthetic-token', user: { email: 'admin@example.invalid', role: 'Admin' } }),
    };
  };

  const result = await issueLocalDevelopmentToken({
    apiUrl: 'http://127.0.0.1:5262',
    email: 'ADMIN@example.invalid',
    fetchImpl,
  });

  assert.equal(result.token, 'synthetic-token');
  assert.equal(result.user.role, 'Admin');
});
