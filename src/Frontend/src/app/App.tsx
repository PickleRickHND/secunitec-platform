import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthProvider';
import { RequireAuth } from '../auth/RequireAuth';
import { Toaster } from '../components/Toaster';
import { AuditoriaPage } from '../features/auditoria/AuditoriaPage';
import { ClientesPage } from '../features/clientes/ClientesPage';
import { FacturaDetallePage } from '../features/facturas/FacturaDetallePage';
import { FacturaNuevaPage } from '../features/facturas/FacturaNuevaPage';
import { FacturasPage } from '../features/facturas/FacturasPage';
import { InicioPage } from '../features/inicio/InicioPage';
import { ObligadoPage } from '../features/obligado/ObligadoPage';
import { CallbackPage } from '../features/publico/CallbackPage';
import { LogoutCallbackPage } from '../features/publico/LogoutCallbackPage';
import { NoEncontradoPage } from '../features/publico/NoEncontradoPage';
import { PortadaPage } from '../features/publico/PortadaPage';
import { ResilienciaPage } from '../features/resiliencia/ResilienciaPage';
import { AppShell } from './AppShell';

export function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Toaster>
          <Routes>
            <Route path="/" element={<PortadaPage />} />
            <Route path="/auth/callback" element={<CallbackPage />} />
            <Route path="/auth/logout-callback" element={<LogoutCallbackPage />} />
            <Route
              element={
                <RequireAuth>
                  <AppShell />
                </RequireAuth>
              }
            >
              <Route path="/inicio" element={<InicioPage />} />
              <Route path="/facturas" element={<FacturasPage />} />
              <Route
                path="/facturas/nueva"
                element={
                  <RequireAuth permiso="gestionarFacturas">
                    <FacturaNuevaPage />
                  </RequireAuth>
                }
              />
              <Route path="/facturas/:id" element={<FacturaDetallePage />} />
              <Route
                path="/clientes"
                element={
                  <RequireAuth permiso="gestionarClientes">
                    <ClientesPage />
                  </RequireAuth>
                }
              />
              <Route path="/obligado" element={<ObligadoPage />} />
              <Route
                path="/auditoria"
                element={
                  <RequireAuth permiso="leerAuditoria">
                    <AuditoriaPage />
                  </RequireAuth>
                }
              />
              <Route path="/resiliencia" element={<ResilienciaPage />} />
              <Route path="/inicio/*" element={<Navigate to="/inicio" replace />} />
              <Route path="*" element={<NoEncontradoPage />} />
            </Route>
          </Routes>
        </Toaster>
      </AuthProvider>
    </BrowserRouter>
  );
}
