import { NavLink, Outlet } from 'react-router-dom';
import { billing } from '../api/billing';
import { nombreRol, type Permiso, puede } from '../auth/roles';
import { useSesion } from '../auth/useSesion';
import { BrandMark } from '../components/BrandMark';
import { RateLimitBanner } from '../components/RateLimitBanner';
import { useApi } from '../lib/useApi';
import styles from './AppShell.module.css';

const secciones: { ruta: string; nombre: string; permiso: Permiso }[] = [
  { ruta: '/inicio', nombre: 'Inicio', permiso: 'leerFacturas' },
  { ruta: '/facturas', nombre: 'Facturas', permiso: 'leerFacturas' },
  { ruta: '/clientes', nombre: 'Clientes', permiso: 'gestionarClientes' },
  { ruta: '/obligado', nombre: 'Obligado tributario', permiso: 'leerFacturas' },
  { ruta: '/auditoria', nombre: 'Auditoría', permiso: 'leerAuditoria' },
  { ruta: '/resiliencia', nombre: 'Resiliencia', permiso: 'leerFacturas' },
];

export function AppShell() {
  const { roles, email, cerrarSesion } = useSesion();
  const obligado = useApi('obligado', (signal) => billing.obligado(signal));

  return (
    <div className={styles.shell}>
      <header className={styles.topbar}>
        <div className={styles.topbarInner}>
          <NavLink to="/inicio" className={styles.brand}>
            <BrandMark className={styles.brandMark} />
            <span>Secunitec</span>
          </NavLink>
          <div className={styles.empresa}>
            {obligado.data ? (
              <>
                <span className={styles.empresaNombre}>{obligado.data.razonSocial}</span>
                <span className={styles.empresaRtn}>RTN {obligado.data.rtn}</span>
              </>
            ) : (
              <span className={styles.empresaRtn}>Facturación electrónica</span>
            )}
          </div>
          <span className="spacer" />
          <div className={styles.usuario}>
            <span className={styles.usuarioCorreo}>{email}</span>
            <span className={styles.usuarioRol}>{roles.map((role) => nombreRol[role]).join(', ') || 'Sin rol asignado'}</span>
          </div>
          <button type="button" className={styles.salir} onClick={() => void cerrarSesion()}>
            Cerrar sesión
          </button>
        </div>
      </header>
      <nav className={styles.tabs} aria-label="Secciones">
        <div className={styles.tabsInner}>
          {secciones
            .filter((seccion) => puede(roles, seccion.permiso))
            .map((seccion) => (
              <NavLink key={seccion.ruta} to={seccion.ruta} className={({ isActive }) => (isActive ? `${styles.tab} ${styles.tabActiva}` : styles.tab)}>
                {seccion.nombre}
              </NavLink>
            ))}
        </div>
      </nav>
      <main className={styles.contenido}>
        <RateLimitBanner />
        <Outlet />
      </main>
    </div>
  );
}
