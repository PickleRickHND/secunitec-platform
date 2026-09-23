import { ErrorResponse } from 'oidc-client-ts';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { esEstadoLogin, userManager } from '../../auth/oidc';
import { useSesion } from '../../auth/useSesion';
import { useCountdown } from '../../lib/useCountdown';
import styles from './Publico.module.css';

type Resultado = { tipo: 'procesando' } | { tipo: 'limitado'; hasta: number } | { tipo: 'error'; mensaje: string };

/** Vuelta del login: canjea el código (PKCE) por los tokens en /connect/token a través del gateway. */
export function CallbackPage() {
  const navigate = useNavigate();
  const { iniciarSesion } = useSesion();
  const [resultado, setResultado] = useState<Resultado>({ tipo: 'procesando' });
  const procesado = useRef(false);
  const segundos = useCountdown(resultado.tipo === 'limitado' ? resultado.hasta : null);

  useEffect(() => {
    // El código es de un solo uso: StrictMode monta el efecto dos veces en desarrollo.
    if (procesado.current) {
      return;
    }

    procesado.current = true;
    userManager
      .signinRedirectCallback()
      .then((user) => {
        void navigate(esEstadoLogin(user.state) ? user.state.volverA : '/inicio', { replace: true });
      })
      .catch((error: unknown) => {
        // R02: /connect/token admite 5 canjes por minuto por IP. El código no llegó a Identity, pero su estado ya se
        // consumió: al terminar la espera se inicia otro login (con la sesión de Identity no pide la contraseña).
        if (error instanceof ErrorResponse && error.error === 'rate_limited') {
          setResultado({ tipo: 'limitado', hasta: Date.now() + 60_000 });
          return;
        }

        setResultado({ tipo: 'error', mensaje: error instanceof Error ? error.message : 'No se pudo completar el inicio de sesión.' });
      });
  }, [navigate]);

  if (resultado.tipo === 'procesando') {
    return (
      <p className={styles.mensaje} role="status">
        Completando el inicio de sesión…
      </p>
    );
  }

  return (
    <div className={styles.mensaje}>
      <h1 className="card-title">{resultado.tipo === 'limitado' ? 'Demasiados inicios de sesión seguidos' : 'No se pudo iniciar sesión'}</h1>
      <p className="muted">
        {resultado.tipo === 'limitado'
          ? `El gateway admite pocos canjes de token por minuto para frenar ataques de fuerza bruta. Podrá reintentar en ${segundos} s.`
          : resultado.mensaje}
      </p>
      <button type="button" className="btn btn-primary" disabled={resultado.tipo === 'limitado' && segundos > 0} onClick={() => void iniciarSesion('/inicio')}>
        Reintentar
      </button>
    </div>
  );
}
