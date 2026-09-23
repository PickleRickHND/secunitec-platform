import { type SubmitEvent, type ReactNode, useEffect, useRef } from 'react';

/** Diálogo modal nativo (foco atrapado y Escape los maneja el navegador). */
export function Dialog({
  abierto,
  titulo,
  onCerrar,
  onConfirmar,
  confirmar,
  tono = 'primary',
  ocupado = false,
  children,
}: {
  readonly abierto: boolean;
  readonly titulo: string;
  readonly onCerrar: () => void;
  readonly onConfirmar: () => void;
  readonly confirmar: string;
  readonly tono?: 'primary' | 'danger';
  readonly ocupado?: boolean;
  readonly children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (abierto && dialog && !dialog.open) {
      dialog.showModal();
    } else if (!abierto && dialog?.open) {
      dialog.close();
    }
  }, [abierto]);

  const enviar = (event: SubmitEvent) => {
    event.preventDefault();
    onConfirmar();
  };

  return (
    <dialog ref={ref} className="dialog" aria-labelledby="dialog-titulo" onClose={onCerrar}>
      <form onSubmit={enviar}>
        <div className="dialog-body">
          <h2 id="dialog-titulo" className="card-title">
            {titulo}
          </h2>
          {children}
        </div>
        <div className="dialog-actions">
          <button type="button" className="btn btn-secondary" onClick={onCerrar} disabled={ocupado}>
            Cancelar
          </button>
          <button type="submit" className={`btn ${tono === 'danger' ? 'btn-danger' : 'btn-primary'}`} disabled={ocupado}>
            {confirmar}
          </button>
        </div>
      </form>
    </dialog>
  );
}
