import { Link } from 'react-router-dom';

export function NoAutorizadoPage() {
  return (
    <div className="card empty">
      <h1 className="empty-title">Su rol no tiene acceso a esta sección</h1>
      <p className="muted">Si necesita entrar, pida a un administrador de su empresa que revise su rol.</p>
      <Link to="/inicio">Ir al inicio</Link>
    </div>
  );
}
