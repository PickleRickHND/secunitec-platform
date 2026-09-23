// Cliente HTTP del SPA hacia el gateway (R09): Bearer del usuario, X-Correlation-Id por petición y errores como
// ProblemDetails. La autorización la decide el servidor: aquí solo se interpreta la respuesta.
import { config } from '../config';
import { rateLimitStore } from '../lib/rateLimitStore';
import { parseRetryAfter } from './retryAfter';
import { type Problem, problemMessage, toProblem } from './problem';

export class ApiError extends Error {
  constructor(
    readonly problem: Problem,
    readonly retryAfter: number | null,
    readonly correlationId: string,
  ) {
    super(problemMessage(problem));
    this.name = 'ApiError';
  }

  get status(): number {
    return this.problem.status;
  }
}

type TokenProvider = () => Promise<string | null>;

let tokenProvider: TokenProvider = () => Promise.resolve(null);
let onUnauthorized: () => void = () => undefined;

export function configureHttp(options: { tokenProvider: TokenProvider; onUnauthorized: () => void }): void {
  tokenProvider = options.tokenProvider;
  onUnauthorized = options.onUnauthorized;
}

export function nuevoCorrelationId(prefijo = 'spa'): string {
  return `${prefijo}-${crypto.randomUUID()}`;
}

interface Opciones {
  readonly method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  readonly body?: unknown;
  readonly signal?: AbortSignal;
}

function parsear(text: string): unknown {
  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

export async function api<T>(path: string, { method = 'GET', body, signal }: Opciones = {}): Promise<T> {
  const correlationId = nuevoCorrelationId();
  const headers: Record<string, string> = { Accept: 'application/json', 'X-Correlation-Id': correlationId };
  const token = await tokenProvider();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  let response: Response;
  try {
    // cache no-store y URLs estables: sin parámetros anti-caché, así el preflight de CORS se reutiliza.
    response = await fetch(config.gatewayUrl + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: 'no-store',
      signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }

    throw new ApiError(
      { status: 0, detail: 'No se pudo conectar con el gateway. Revise que el sistema esté levantado y que confíe en su certificado.' },
      null,
      correlationId,
    );
  }

  const parsed = parsear(await response.text());

  if (response.ok) {
    return parsed as T;
  }

  const retryAfter = response.status === 429 ? parseRetryAfter(response.headers.get('Retry-After'), parsed) : null;
  if (response.status === 429) {
    rateLimitStore.bloquear(retryAfter ?? 60);
  }

  if (response.status === 401) {
    onUnauthorized();
  }

  throw new ApiError(toProblem(response.status, parsed), retryAfter, response.headers.get('X-Correlation-Id') ?? correlationId);
}
