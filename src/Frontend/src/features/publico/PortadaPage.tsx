import { Navigate } from 'react-router-dom';
import { useSesion } from '../../auth/useSesion';
import { BrandMark } from '../../components/BrandMark';
import styles from './Publico.module.css';

export function PortadaPage() {
  const { user, cargando, iniciarSesion } = useSesion();
  if (!cargando && user) {
    return <Navigate to="/inicio" replace />;
  }

  return (
    <div className={styles.pagina}>
      <header className={styles.topbar}>
        <span className={styles.brand}>
          <BrandMark className={styles.brandMark} />
          Secunitec
        </span>
        <span className={styles.contexto}>Facturación electrónica</span>
      </header>
      <main className={styles.portada}>
        <h1 className={styles.titulo}>Facturas con CAI, auditoría de cada acceso y un gateway que resiste ráfagas</h1>
        <p className={styles.lead}>
          Emita y anule facturas dentro del rango autorizado por el SAR, consulte quién hizo qué en su empresa y vea en
          vivo cómo el gateway bloquea el exceso de peticiones con 429 en vez de caerse.
        </p>
        <div className="row">
          <button type="button" className="btn btn-primary" disabled={cargando} onClick={() => void iniciarSesion('/inicio')}>
            Iniciar sesión
          </button>
          <p className="muted small">Si su cuenta es nueva, un administrador debe asignarle un rol antes de entrar.</p>
        </div>
      </main>
    </div>
  );
}
