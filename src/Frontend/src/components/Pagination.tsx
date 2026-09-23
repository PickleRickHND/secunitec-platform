import { formatEntero } from '../lib/format';

export function Pagination({
  pagina,
  tamano,
  total,
  onCambiar,
}: {
  readonly pagina: number;
  readonly tamano: number;
  readonly total: number;
  readonly onCambiar: (pagina: number) => void;
}) {
  const paginas = Math.max(1, Math.ceil(total / tamano));
  if (total === 0) {
    return null;
  }

  const desde = (pagina - 1) * tamano + 1;
  const hasta = Math.min(total, pagina * tamano);
  return (
    <nav className="row card-body" aria-label="Paginación">
      <p className="muted small">
        {formatEntero(desde)} a {formatEntero(hasta)} de {formatEntero(total)}
      </p>
      <span className="spacer" />
      <button type="button" className="btn btn-secondary btn-small" disabled={pagina <= 1} onClick={() => { onCambiar(pagina - 1); }}>
        Anterior
      </button>
      <button type="button" className="btn btn-secondary btn-small" disabled={pagina >= paginas} onClick={() => { onCambiar(pagina + 1); }}>
        Siguiente
      </button>
    </nav>
  );
}
