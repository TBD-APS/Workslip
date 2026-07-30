import { Buffer } from 'node:buffer';

interface MailosaurMessageSummary {
  id?: string;
}

interface MailosaurSearchResponse {
  items?: MailosaurMessageSummary[];
}

interface MailosaurMessage {
  subject?: string;
  text?: { body?: string };
  html?: { body?: string };
}

interface WaitForOneTimeCodeOptions {
  apiKey: string;
  serverId: string;
  email: string;
  receivedAfter: Date;
  timeoutMs?: number;
}

const MAILOSAUR_API = 'https://mailosaur.com/api';
const CODE_PATTERN = /(?:^|\D)(\d{6})(?:\D|$)/;

function authorizationHeader(apiKey: string): string {
  return `Basic ${Buffer.from(`api:${apiKey}`, 'utf8').toString('base64')}`;
}

async function fetchJson<T>(url: string, apiKey: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...init,
    headers: {
      Authorization: authorizationHeader(apiKey),
      Accept: 'application/json',
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw new Error(`Mailosaur request failed with status ${response.status}`);
  }

  return response.json() as Promise<T>;
}

function extractCode(message: MailosaurMessage): string | null {
  const searchableContent = [
    message.subject,
    message.text?.body,
    message.html?.body,
  ]
    .filter((value): value is string => Boolean(value))
    .join('\n');

  return searchableContent.match(CODE_PATTERN)?.[1] ?? null;
}

export async function waitForOneTimeCode({
  apiKey,
  serverId,
  email,
  receivedAfter,
  timeoutMs = 60_000,
}: WaitForOneTimeCodeOptions): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  const searchUrl = new URL(`${MAILOSAUR_API}/messages/search`);
  searchUrl.searchParams.set('server', serverId);
  searchUrl.searchParams.set('receivedAfter', receivedAfter.toISOString());
  searchUrl.searchParams.set('itemsPerPage', '10');

  while (Date.now() < deadline) {
    const searchResult = await fetchJson<MailosaurSearchResponse>(
      searchUrl.toString(),
      apiKey,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sentTo: email }),
      },
    );

    const messageId = searchResult.items?.[0]?.id;
    if (messageId) {
      const message = await fetchJson<MailosaurMessage>(
        `${MAILOSAUR_API}/messages/${encodeURIComponent(messageId)}`,
        apiKey,
      );
      const code = extractCode(message);
      if (code) return code;
    }

    await new Promise((resolve) => setTimeout(resolve, 2_000));
  }

  throw new Error('No matching Workslip one-time code arrived before the timeout');
}
