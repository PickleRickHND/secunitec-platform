import type { Clase } from './burst';
import styles from './Resiliencia.module.css';

/** Clase CSS de cada serie: el color sale de la paleta categórica validada, en orden fijo por clase de respuesta. */
export const claseSerie: Record<Clase, string> = {
  aceptadas: styles.serie1 ?? '',
  bloqueadas: styles.serie2 ?? '',
  sinAutenticar: styles.serie3 ?? '',
  sinPermiso: styles.serie4 ?? '',
  otros: styles.serie5 ?? '',
};
