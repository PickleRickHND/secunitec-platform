import { createContext, use } from 'react';

export const ToastContext = createContext<(texto: string) => void>(() => undefined);

/** Muestra una confirmación breve ("Factura emitida"). */
export function useAvisar(): (texto: string) => void {
  return use(ToastContext);
}
