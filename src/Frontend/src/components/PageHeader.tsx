import type { ReactNode } from 'react';
import styles from './PageHeader.module.css';

export function PageHeader({ titulo, descripcion, acciones }: { readonly titulo: string; readonly descripcion?: ReactNode; readonly acciones?: ReactNode }) {
  return (
    <div className={styles.header}>
      <div className={styles.texto}>
        <h1 className={styles.titulo}>{titulo}</h1>
        {descripcion ? <p className={styles.descripcion}>{descripcion}</p> : null}
      </div>
      {acciones ? <div className={styles.acciones}>{acciones}</div> : null}
    </div>
  );
}
