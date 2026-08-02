import type {
  ApplicationInsights as ApplicationInsightsClient,
  IDependencyTelemetry,
  IEventTelemetry,
  IExceptionTelemetry,
  ITelemetryItem,
} from '@microsoft/applicationinsights-web';

const connectionString = import.meta.env.VITE_APPLICATIONINSIGHTS_CONNECTION_STRING;
const release = import.meta.env.VITE_APP_RELEASE?.trim() || __BUILD_TIME__;

let client: ApplicationInsightsClient | null = null;
let initializationPromise: Promise<void> | null = null;
let globalHandlersInstalled = false;
let pendingInteraction: { correlationId: string; action: string; createdAt: number } | null = null;
const recentlyReportedErrors = new Map<string, number>();
const duplicateWindowMs = 60_000;
const interactionLifetimeMs = 5_000;

const sensitiveValuePattern = /((?:["']?(?:authorization|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|secret|api[_-]?key)["']?)\s*[:=]\s*["']?)[^"',}\s]+(["']?)/gi;
const sensitiveKeyPattern = /^(authorization|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|secret|api[_-]?key|request|requestdata|response|payload|body|headers)$/i;
const credentialPattern = /(authorization\s*[:=]\s*["']?(?:bearer|basic)\s+)[^"',}\s]+/gi;
const bearerPattern = /bearer\s+[a-z0-9._~+/=-]+/gi;
const emailPattern = /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi;
const phonePattern = /(?:\+?\d[\d\s().-]{7,}\d)/g;
const quotedSensitiveValuePattern = /((?:["']?(?:authorization|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|secret|api[_-]?key)["']?)\s*[:=]\s*)(["'])(.*?)\2/gi;

function sanitizeText(value: string): string {
  return value
    .replace(quotedSensitiveValuePattern, '$1$2[REDACTED]$2')
    .replace(sensitiveValuePattern, '$1[REDACTED]$2')
    .replace(credentialPattern, '$1[REDACTED]')
    .replace(bearerPattern, 'Bearer [REDACTED]')
    .replace(/([?&](?:access[_-]?token|refresh[_-]?token|id[_-]?token|authorization|api[_-]?key)=)[^&#\s]+/gi, '$1[REDACTED]')
    .replace(emailPattern, '[REDACTED_EMAIL]')
    .replace(phonePattern, '[REDACTED_PHONE]')
    .slice(0, 4000);
}

function sanitizeEndpoint(value: string): string {
  return sanitizeText(value.split('?')[0])
    .replace(/\/[0-9a-f]{8}-[0-9a-f-]{27}/gi, '/:id')
    .replace(/\/\d+(?=\/|$)/g, '/:id');
}

function actionEventName(action: string): string {
  const slug = action
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80);
  return `ui.action.${slug || 'unknown'}`;
}

export function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
}

export function trackUserInteraction(action: string, correlationId: string): void {
  if (!client) return;
  const telemetry: IEventTelemetry = {
    name: actionEventName(action),
    properties: {
      action: sanitizeText(action),
      correlationId,
      route: currentRoute(),
      release,
    },
  };
  client.trackEvent(telemetry);
}

export function beginUserInteraction(action: string): string {
  const correlationId = createCorrelationId();
  pendingInteraction = { correlationId, action, createdAt: Date.now() };
  return correlationId;
}

export function consumePendingInteraction(): { correlationId: string; action: string } | null {
  if (!pendingInteraction) return null;
  const interaction = pendingInteraction;
  pendingInteraction = null;
  return Date.now() - interaction.createdAt <= interactionLifetimeMs
    ? { correlationId: interaction.correlationId, action: interaction.action }
    : null;
}

export function trackApiDependency(details: {
  correlationId: string;
  action?: string;
  method: string;
  url: string;
  durationMs: number;
  responseCode?: number;
  success: boolean;
}): void {
  if (!client) return;
  const endpoint = sanitizeEndpoint(details.url);
  const dependency: IDependencyTelemetry = {
    id: details.correlationId,
    target: endpoint,
    name: `${details.method.toUpperCase()} ${endpoint}`,
    data: endpoint,
    duration: details.durationMs,
    success: details.success,
    responseCode: details.responseCode ?? 0,
    type: 'HTTP',
    properties: {
      correlationId: details.correlationId,
      ...(details.action ? { action: sanitizeText(details.action) } : {}),
      route: currentRoute(),
      release,
    },
  };
  client.trackDependencyData(dependency);
}

function sanitizeValue(value: unknown, seen = new WeakSet<object>(), depth = 0): unknown {
  if (typeof value === 'string') return sanitizeText(value);
  if (Array.isArray(value)) return value.slice(0, 100).map((entry) => sanitizeValue(entry, seen, depth + 1));
  if (!value || typeof value !== 'object') return value;
  if (seen.has(value)) return '[REDACTED_CIRCULAR]';
  if (depth >= 8) return '[REDACTED_DEPTH]';

  seen.add(value);

  return Object.fromEntries(
    Object.entries(value).slice(0, 100).map(([key, entryValue]) => [
      sanitizeText(key),
      sensitiveKeyPattern.test(key) ? '[REDACTED]' : sanitizeValue(entryValue, seen, depth + 1),
    ]),
  );
}

function sanitizeError(error: unknown): Error {
  const source = toError(error);
  const safeName = /^[A-Za-z][A-Za-z0-9_$.-]{0,63}$/.test(source.name) ? source.name : 'Error';
  const sanitized = new Error(`${safeName} [${fingerprint(source.message)}]`);
  sanitized.name = safeName;
  if (source.stack) {
    const frames = source.stack.split('\n').slice(1).map(sanitizeText);
    sanitized.stack = [sanitized.toString(), ...frames].join('\n');
  }
  return sanitized;
}

function fingerprint(value: string): string {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return (hash >>> 0).toString(16).padStart(8, '0');
}

function toError(error: unknown): Error {
  if (error instanceof Error) return error;
  if (error && typeof error === 'object') {
    const record = error as Record<string, unknown>;
    const normalized = new Error(typeof record.message === 'string' ? record.message : 'Unhandled promise rejection');
    if (typeof record.name === 'string') normalized.name = record.name;
    if (typeof record.stack === 'string') normalized.stack = record.stack;
    return normalized;
  }
  return new Error(String(error));
}

function currentRoute(): string {
  return sanitizeEndpoint(window.location.pathname || '/');
}

function sanitizeTelemetryItem(item: ITelemetryItem): boolean {
  if (item.data) item.data = sanitizeValue(item.data) as Record<string, unknown>;
  if (item.baseData) item.baseData = sanitizeValue(item.baseData) as Record<string, unknown>;
  if (item.tags) item.tags = sanitizeValue(item.tags) as Record<string, string>;
  if (item.ext) item.ext = sanitizeValue(item.ext) as typeof item.ext;
  return true;
}

export function initializeApplicationInsights(): Promise<void> {
  const localOptIn = import.meta.env.VITE_APPLICATIONINSIGHTS_ENABLE_LOCAL === 'true';
  if ((!import.meta.env.PROD && !localOptIn) || !connectionString || client) {
    return Promise.resolve();
  }
  if (initializationPromise) return initializationPromise;

  initializationPromise = import('@microsoft/applicationinsights-web')
    .then(({ ApplicationInsights }) => {
      if (client) return;

      const nextClient = new ApplicationInsights({
        config: {
          connectionString,
          disableAjaxTracking: true,
          disableCookiesUsage: true,
          disableExceptionTracking: true,
          disableFetchTracking: true,
          enableAutoRouteTracking: false,
          samplingPercentage: 100,
        },
      });

      nextClient.addTelemetryInitializer((item) => {
        item.tags = {
          ...(item.tags ?? {}),
          'ai.application.ver': release,
        };
        return sanitizeTelemetryItem(item);
      });
      nextClient.loadAppInsights();
      client = nextClient;
      nextClient.trackEvent({
        name: 'telemetry.heartbeat',
        properties: {
          route: currentRoute(),
          release,
        },
      });
    })
    .catch(() => {
      client = null;
      initializationPromise = null;
    });

  return initializationPromise;
}

export function reportFrontendError(error: unknown, source: string, details: Record<string, string> = {}): void {
  if (!client) return;

  const sanitizedError = sanitizeError(error);
  const route = currentRoute();
  const now = Date.now();
  for (const [key, timestamp] of recentlyReportedErrors) {
    if (now - timestamp > duplicateWindowMs) recentlyReportedErrors.delete(key);
  }
  const dedupeKey = `${source}|${route}|${sanitizedError.name}|${sanitizedError.message}`;
  if (recentlyReportedErrors.has(dedupeKey)) return;
  recentlyReportedErrors.set(dedupeKey, now);

  const telemetry: IExceptionTelemetry = {
    exception: sanitizedError,
    properties: {
      ...Object.fromEntries(Object.entries(details).map(([key, value]) => [key, sanitizeText(value)])),
      route,
      source,
      release,
    },
  };

  client.trackException(telemetry);
}

export function installGlobalApplicationInsightsHandlers(): void {
  if (!client || globalHandlersInstalled) return;
  globalHandlersInstalled = true;

  window.addEventListener('error', (event) => {
    const location = event.filename ? ` (${event.filename}:${event.lineno}:${event.colno})` : '';
    reportFrontendError(event.error ?? new Error(`${event.message || 'Resource load error'}${location}`), 'window.error');
  }, true);

  window.addEventListener('unhandledrejection', (event) => {
    reportFrontendError(event.reason, 'window.unhandledrejection');
  });

  document.addEventListener('click', (event) => {
    const target = event.target instanceof Element
      ? event.target.closest('button,[role="button"]')
      : null;
    if (!target) return;
    const action = target.getAttribute('data-telemetry-action')
      ?? target.getAttribute('aria-label')
      ?? target.textContent?.trim();
    if (action) beginUserInteraction(action.slice(0, 120));
  }, true);
}
