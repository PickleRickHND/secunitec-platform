import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import { grafana } from '../support/entorno';

// Evidencia de 5.2 y 5.3 (no es parte de los E2E del SPA): captura los tres dashboards en un rango de tiempo.
// Solo corre con GRAFANA_CAPTURAS=<carpeta>. GRAFANA_DESDE y GRAFANA_HASTA (epoch en ms) fijan el rango de una prueba de
// estrés (los escribe scripts/stress/run-jmeter.sh); sin ellos, los últimos 15 minutos.
// Autenticación básica por cabecera con la contraseña de .env: no pasa por el formulario de login ni la imprime.
const dashboards = [
  { uid: 'secunitec-resiliencia', archivo: 'grafana-resiliencia-gateway.png' },
  { uid: 'secunitec-use', archivo: 'grafana-use-overview.png' },
  { uid: 'secunitec-trazas', archivo: 'grafana-trazas.png' },
] as const;

const carpeta = process.env.GRAFANA_CAPTURAS;
const autorizacion = `Basic ${Buffer.from(`${grafana.usuario}:${grafana.password ?? ''}`).toString('base64')}`;

// Viewport alto: Grafana solo dibuja los paneles visibles, así la captura los incluye a todos sin recortar.
test.use({ baseURL: grafana.url, extraHTTPHeaders: { Authorization: autorizacion }, viewport: { width: 1600, height: 2000 } });

test('capturas de los dashboards de Grafana', async ({ page }) => {
  test.skip(!carpeta, 'Solo con GRAFANA_CAPTURAS=<carpeta>.');
  test.skip(!grafana.password, 'Defina GRAFANA_ADMIN_PASSWORD en .env.');
  test.setTimeout(180_000);

  const destino = resolve(carpeta!);
  mkdirSync(destino, { recursive: true });
  const desde = process.env.GRAFANA_DESDE ?? 'now-15m';
  const hasta = process.env.GRAFANA_HASTA ?? 'now';

  for (const dashboard of dashboards) {
    // kiosk: sin menú lateral. Tempo mantiene la búsqueda abierta en streaming, así que no hay "networkidle":
    // se espera un tiempo fijo a que terminen las consultas y se dibujen los paneles.
    await page.goto(`/d/${dashboard.uid}?orgId=1&from=${desde}&to=${hasta}&kiosk`);
    await expect(page.getByRole('heading', { level: 3 }).first()).toBeVisible();
    await page.waitForTimeout(8000);
    await page.screenshot({ path: resolve(destino, dashboard.archivo), fullPage: true });
  }
});
