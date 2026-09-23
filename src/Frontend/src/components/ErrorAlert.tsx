import type { ApiError } from '../api/http';

/** Error del servidor en el tono de la interfaz: qué pasó y, si hay, el correlation id para soporte. */
export function ErrorAlert({ error, onReintentar }: { readonly error: ApiError | null; readonly onReintentar?: () => void }) {
  if (!error) {
    return null;
  }

  return (
    <div className="alert alert-danger" role="alert">
      <div className="alert-text">
        <p>{error.message}</p>
        {error.correlationId ? <p className="muted small">Referencia: {error.correlationId}</p> : null}
      </div>
      {onReintentar && error.status !== 403 ? (
        <button type="button" className="btn btn-secondary btn-small" onClick={onReintentar}>
          Reintentar
        </button>
      ) : null}
    </div>
  );
}
