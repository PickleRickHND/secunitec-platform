import type { Problem } from '../../api/problem';
import { fieldError } from '../../api/problem';
import styles from './Facturas.module.css';

export interface LineaBorrador {
  clave: number;
  descripcion: string;
  cantidad: string;
  precioUnitario: string;
  exento: boolean;
}

/** Campos de una línea. Los errores llegan del servidor con el nombre del campo (lineas[0].precioUnitario). */
export function LineaCampos({
  linea,
  prefijo,
  problema,
  onCambiar,
  onQuitar,
}: {
  readonly linea: LineaBorrador;
  readonly prefijo: string;
  readonly problema: Problem | null;
  readonly onCambiar: (linea: LineaBorrador) => void;
  readonly onQuitar?: () => void;
}) {
  const error = (campo: string) => fieldError(problema, prefijo ? `${prefijo}.${campo}` : campo);
  const id = (campo: string) => `linea-${linea.clave}-${campo}`;
  return (
    <div className={styles.linea}>
      <div className="field">
        <label className="field-label" htmlFor={id('descripcion')}>
          Descripción
        </label>
        <input
          id={id('descripcion')}
          className="input"
          value={linea.descripcion}
          maxLength={250}
          required
          aria-invalid={Boolean(error('descripcion'))}
          onChange={(event) => { onCambiar({ ...linea, descripcion: event.target.value }); }}
        />
        {error('descripcion') ? <span className="field-error">{error('descripcion')}</span> : null}
      </div>
      <div className="field">
        <label className="field-label" htmlFor={id('cantidad')}>
          Cantidad
        </label>
        <input
          id={id('cantidad')}
          className="input num"
          inputMode="decimal"
          value={linea.cantidad}
          required
          aria-invalid={Boolean(error('cantidad'))}
          onChange={(event) => { onCambiar({ ...linea, cantidad: event.target.value }); }}
        />
        {error('cantidad') ? <span className="field-error">{error('cantidad')}</span> : null}
      </div>
      <div className="field">
        <label className="field-label" htmlFor={id('precio')}>
          Precio unitario (L)
        </label>
        <input
          id={id('precio')}
          className="input num"
          inputMode="decimal"
          value={linea.precioUnitario}
          required
          aria-invalid={Boolean(error('precioUnitario'))}
          onChange={(event) => { onCambiar({ ...linea, precioUnitario: event.target.value }); }}
        />
        {error('precioUnitario') ? <span className="field-error">{error('precioUnitario')}</span> : null}
      </div>
      <label className={`checkbox ${styles.lineaExento}`}>
        <input type="checkbox" checked={linea.exento} onChange={(event) => { onCambiar({ ...linea, exento: event.target.checked }); }} />
        Exento de ISV
      </label>
      {onQuitar ? (
        <button type="button" className={`btn btn-link ${styles.lineaExento}`} onClick={onQuitar}>
          Quitar
        </button>
      ) : (
        <span />
      )}
    </div>
  );
}
