import { Link } from 'react-router-dom';

export function NoEncontradoPage() {
  return (
    <div className="card empty">
      <h1 className="empty-title">Esta página no existe</h1>
      <p className="muted">Revise la dirección o vuelva al inicio.</p>
      <Link to="/inicio">Ir al inicio</Link>
    </div>
  );
}
