import type { ReactNode } from 'react';
import styles from './KpiCard.module.css';

/**
 * Tarjeta de indicador: etiqueta, valor y nota. La marca de color (serie de la gráfica o estado) va al lado de la
 * etiqueta; el texto siempre en tinta.
 */
export function KpiCard({
  etiqueta,
  valor,
  nota,
  marca,
}: {
  readonly etiqueta: string;
  readonly valor: ReactNode;
  readonly nota?: ReactNode;
  readonly marca?: string;
}) {
  return (
    <div className={`card ${styles.kpi}`}>
      <p className={styles.etiqueta}>
        {marca ? <span className={`${styles.marca} ${marca}`} aria-hidden="true" /> : null}
        {etiqueta}
      </p>
      <p className={styles.valor}>{valor}</p>
      {nota ? <p className={styles.nota}>{nota}</p> : null}
    </div>
  );
}

export function KpiRow({ children, compacta = false }: { readonly children: ReactNode; readonly compacta?: boolean }) {
  return <div className={compacta ? `${styles.fila} ${styles.compacta}` : styles.fila}>{children}</div>;
}
