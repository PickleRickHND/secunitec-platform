import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { userManager } from '../../auth/oidc';
import styles from './Publico.module.css';

export function LogoutCallbackPage() {
  const navigate = useNavigate();

  useEffect(() => {
    void userManager
      .signoutRedirectCallback()
      .catch(() => userManager.removeUser())
      .finally(() => void navigate('/', { replace: true }));
  }, [navigate]);

  return (
    <p className={styles.mensaje} role="status">
      Cerrando la sesión…
    </p>
  );
}
