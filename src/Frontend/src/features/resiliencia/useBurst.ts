import { useCallback, useEffect, useRef, useState } from 'react';
import { nuevoCorrelationId } from '../../api/http';
import { parseRetryAfter } from '../../api/retryAfter';
import type { Role } from '../../auth/roles';
import { config } from '../../config';
import { alterarToken, calendario, clasificar, type Escenario, peticionPara, type Resultado } from './burst';

/** Tope de peticiones en vuelo: la ráfaga respeta el calendario, pero no abre cientos de conexiones a la vez. */
const EN_VUELO_MAX = 24;

export interface Rafaga {
  readonly escenario: Escenario;
  readonly peticiones: number;
  readonly segundos: number;
}

interface Estado {
  readonly corriendo: boolean;
  readonly rafaga: Rafaga | null;
  readonly resultados: readonly Resultado[];
  readonly enviadas: number;
  /** Instante (ms, reloj del sistema) hasta el que el gateway pidió esperar; null sin bloqueo. */
  readonly bloqueadoHasta: number | null;
  /** Segundos transcurridos desde el inicio (para el eje de la gráfica). */
  readonly transcurrido: number;
}

const inicial: Estado = { corriendo: false, rafaga: null, resultados: [], enviadas: 0, bloqueadoHasta: null, transcurrido: 0 };

/**
 * R10: lanza N peticiones reales al gateway en M segundos y publica cada respuesta. No usa el cliente HTTP común: el
 * panel quiere ver los 401 y 429 tal cual, sin redirigir al login ni mostrar la barra de aviso.
 */
export function useBurst(accessToken: () => Promise<string | null>, roles: readonly Role[]) {
  const [estado, setEstado] = useState<Estado>(inicial);
  const controller = useRef<AbortController | null>(null);
  const timers = useRef<number[]>([]);
  const buffer = useRef<Resultado[]>([]);
  const inicio = useRef(0);

  const detener = useCallback(() => {
    controller.current?.abort();
    timers.current.forEach((id) => { window.clearTimeout(id); });
    timers.current = [];
    setEstado((actual) => ({ ...actual, corriendo: false }));
  }, []);

  useEffect(() => detener, [detener]);

  // Las respuestas llegan por cientos: se publican a la interfaz diez veces por segundo, no una por una.
  useEffect(() => {
    if (!estado.corriendo) {
      return undefined;
    }

    const id = window.setInterval(() => {
      const nuevas = buffer.current.splice(0);
      const transcurrido = Math.floor((performance.now() - inicio.current) / 1000);
      setEstado((actual) => {
        const resultados = nuevas.length ? [...actual.resultados, ...nuevas] : actual.resultados;
        const bloqueos = nuevas.filter((r) => r.retryAfter !== null).map((r) => Date.now() + (r.retryAfter ?? 0) * 1000);
        const bloqueadoHasta = bloqueos.length ? Math.max(actual.bloqueadoHasta ?? 0, ...bloqueos) : actual.bloqueadoHasta;
        const terminado = actual.rafaga !== null && resultados.length >= actual.rafaga.peticiones;
        return { ...actual, resultados, bloqueadoHasta, transcurrido, corriendo: !terminado };
      });
    }, 100);
    return () => {
      window.clearInterval(id);
    };
  }, [estado.corriendo]);

  const lanzar = useCallback(
    async (rafaga: Rafaga) => {
      detener();
      const token = await accessToken();
      const abort = new AbortController();
      controller.current = abort;
      buffer.current = [];
      inicio.current = performance.now();
      setEstado({ ...inicial, corriendo: true, rafaga });

      const cola: number[] = [];
      let enVuelo = 0;
      const enviar = (indice: number) => {
        enVuelo++;
        setEstado((actual) => ({ ...actual, enviadas: actual.enviadas + 1 }));
        const peticion = peticionPara(rafaga.escenario, indice, roles);
        const correlationId = nuevoCorrelationId(`rafaga-${indice}`);
        const headers: Record<string, string> = { Accept: 'application/json', 'X-Correlation-Id': correlationId };
        if (peticion.autenticacion !== 'ninguna' && token) {
          headers.Authorization = `Bearer ${peticion.autenticacion === 'alterada' ? alterarToken(token) : token}`;
        }

        const enviadaEn = performance.now();
        void fetch(config.gatewayUrl + peticion.path, { headers, cache: 'no-store', signal: abort.signal })
          .then(async (response) => {
            const body: unknown = response.status === 429 ? await response.json().catch(() => null) : await response.text().catch(() => null);
            return {
              status: response.status,
              retryAfter: response.status === 429 ? parseRetryAfter(response.headers.get('Retry-After'), body) : null,
            };
          })
          .catch(() => ({ status: null, retryAfter: null }))
          .then(({ status, retryAfter }) => {
            if (abort.signal.aborted) {
              return;
            }

            const ahora = performance.now();
            buffer.current.push({
              indice,
              clase: clasificar(status),
              status,
              respuestaMs: ahora - inicio.current,
              latenciaMs: Math.round(ahora - enviadaEn),
              correlationId,
              retryAfter,
            });
          })
          .finally(() => {
            enVuelo--;
            const siguiente = cola.shift();
            if (siguiente !== undefined && !abort.signal.aborted) {
              enviar(siguiente);
            }
          });
      };

      timers.current = calendario(rafaga.peticiones, rafaga.segundos).map((desfase, indice) =>
        window.setTimeout(() => {
          if (enVuelo < EN_VUELO_MAX) {
            enviar(indice);
          } else {
            cola.push(indice);
          }
        }, desfase),
      );
    },
    [accessToken, detener, roles],
  );

  return { ...estado, lanzar, detener };
}
