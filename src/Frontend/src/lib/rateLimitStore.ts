// Estado global del último 429 recibido fuera del panel: la barra de aviso muestra la cuenta regresiva.
import { useSyncExternalStore } from 'react';

type Listener = () => void;

let hasta = 0;
const listeners = new Set<Listener>();

const subscribe = (listener: Listener): (() => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

const getSnapshot = (): number => hasta;

export const rateLimitStore = {
  /** Registra un bloqueo del gateway durante los segundos indicados (se queda con el más largo). */
  bloquear: (segundos: number, ahora: number = Date.now()): void => {
    const nuevo = ahora + segundos * 1000;
    if (nuevo > hasta) {
      hasta = nuevo;
      listeners.forEach((listener) => {
        listener();
      });
    }
  },
  subscribe,
  getSnapshot,
};

/** Instante (ms) hasta el que el gateway pidió esperar; 0 si nunca bloqueó. */
export function useRateLimitDeadline(): number {
  return useSyncExternalStore(subscribe, getSnapshot);
}
