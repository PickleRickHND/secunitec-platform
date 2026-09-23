// ProblemDetails (RFC 9457) de Billing: title, detail, code estable y, en validaciones, errors por campo.

export interface Problem {
  readonly status: number;
  readonly title?: string;
  readonly detail?: string;
  readonly code?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

const mensajesPorEstado: Record<number, string> = {
  400: 'Revise los datos: el servidor los rechazó.',
  401: 'Su sesión venció. Inicie sesión de nuevo.',
  403: 'Su rol no tiene permiso para esta acción.',
  404: 'No encontramos lo que buscaba.',
  409: 'Ya existe un registro con esos datos.',
  429: 'El gateway limitó sus peticiones. Espere unos segundos.',
};

export function toProblem(status: number, body: unknown): Problem {
  if (typeof body !== 'object' || body === null) {
    return { status };
  }

  const record = body as Record<string, unknown>;
  const text = (key: string) => (typeof record[key] === 'string' ? record[key] : undefined);
  const errors = record.errors;
  return {
    status,
    title: text('title'),
    detail: text('detail'),
    code: text('code') ?? (text('error') === 'rate_limited' ? 'rate_limited' : undefined),
    errors:
      typeof errors === 'object' && errors !== null
        ? Object.fromEntries(
            Object.entries(errors as Record<string, unknown>)
              .filter(([, value]) => Array.isArray(value))
              .map(([key, value]) => [key, (value as unknown[]).filter((item): item is string => typeof item === 'string')]),
          )
        : undefined,
  };
}

/** Mensaje para la persona: el detalle del servidor si existe; si no, uno por código de estado. */
export function problemMessage(problem: Problem): string {
  if (problem.errors && Object.keys(problem.errors).length > 0) {
    return 'Corrija los campos marcados.';
  }

  return problem.detail ?? mensajesPorEstado[problem.status] ?? 'El servidor no pudo completar la operación. Intente de nuevo.';
}

/** Primer error de un campo (camelCase, como lo devuelve Billing: "lineas[0].precioUnitario"). */
export function fieldError(problem: Problem | null | undefined, field: string): string | undefined {
  return problem?.errors?.[field]?.[0];
}
