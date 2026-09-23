import { type ReactNode, useCallback, useMemo, useState } from 'react';
import { ToastContext } from './toastContext';
import styles from './Toaster.module.css';

interface Aviso {
  readonly id: number;
  readonly texto: string;
}

let siguiente = 0;

/** Confirmaciones breves ("Factura emitida") en una región aria-live. */
export function Toaster({ children }: { readonly children: ReactNode }) {
  const [avisos, setAvisos] = useState<Aviso[]>([]);

  const avisar = useCallback((texto: string) => {
    const id = ++siguiente;
    setAvisos((actuales) => [...actuales, { id, texto }]);
    window.setTimeout(() => {
      setAvisos((actuales) => actuales.filter((aviso) => aviso.id !== id));
    }, 4000);
  }, []);

  const valor = useMemo(() => avisar, [avisar]);
  return (
    <ToastContext value={valor}>
      {children}
      <div className={styles.region} role="status" aria-live="polite">
        {avisos.map((aviso) => (
          <p key={aviso.id} className={styles.aviso}>
            {aviso.texto}
          </p>
        ))}
      </div>
    </ToastContext>
  );
}
