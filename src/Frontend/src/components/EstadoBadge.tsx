import type { EstadoFactura, ResultadoAuditoria } from '../api/types';

const estados: Record<EstadoFactura, string> = { Borrador: 'badge-draft', Emitida: 'badge-ok', Anulada: 'badge-danger' };

export function EstadoFacturaBadge({ estado }: { readonly estado: EstadoFactura }) {
  return <span className={`badge ${estados[estado]}`}>{estado}</span>;
}

const resultados: Record<ResultadoAuditoria, { clase: string; texto: string }> = {
  Exito: { clase: 'badge-ok', texto: 'Éxito' },
  Denegado: { clase: 'badge-danger', texto: 'Denegado' },
  Fallido: { clase: 'badge-warn', texto: 'Fallido' },
};

export function ResultadoBadge({ resultado }: { readonly resultado: ResultadoAuditoria }) {
  const { clase, texto } = resultados[resultado];
  return <span className={`badge ${clase}`}>{texto}</span>;
}
