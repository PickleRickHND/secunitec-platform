import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../api/http';

interface Estado<T> {
  readonly data: T | null;
  readonly error: ApiError | null;
  /** Clave (con su versión) de la última respuesta recibida. */
  readonly resuelta: string | null;
}

/**
 * Carga datos del gateway y los vuelve a pedir cuando cambia la clave. Mientras recarga conserva los datos anteriores
 * (sin parpadeo). Cancela la petición en curso al desmontar o al cambiar la clave.
 */
export function useApi<T>(clave: string | null, cargar: (signal: AbortSignal) => Promise<T>) {
  const [version, setVersion] = useState(0);
  const [estado, setEstado] = useState<Estado<T>>({ data: null, error: null, resuelta: null });
  const cargarRef = useRef(cargar);
  const actual = clave === null ? null : `${clave}#${String(version)}`;

  useEffect(() => {
    cargarRef.current = cargar;
  });

  useEffect(() => {
    if (actual === null) {
      return undefined;
    }

    const controller = new AbortController();
    cargarRef
      .current(controller.signal)
      .then((data) => {
        setEstado({ data, error: null, resuelta: actual });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setEstado((anterior) => ({
          data: anterior.data,
          error: error instanceof ApiError ? error : new ApiError({ status: 0 }, null, ''),
          resuelta: actual,
        }));
      });
    return () => {
      controller.abort();
    };
  }, [actual]);

  const recargar = useCallback(() => {
    setVersion((valor) => valor + 1);
  }, []);

  const reemplazar = useCallback(
    (data: T) => {
      setEstado({ data, error: null, resuelta: actual });
    },
    [actual],
  );

  return { data: estado.data, error: estado.error, cargando: actual !== null && estado.resuelta !== actual, recargar, reemplazar };
}
