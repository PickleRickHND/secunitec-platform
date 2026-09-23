import { type KeyboardEvent, type PointerEvent, useEffect, useMemo, useRef, useState } from 'react';
import { formatEntero } from '../../lib/format';
import { CLASES, type Clase, etiquetaClase, type Punto } from './burst';
import { claseSerie } from './series';
import styles from './Resiliencia.module.css';

const ALTO = 300;
const MARGEN = { arriba: 16, derecha: 152, abajo: 36, izquierda: 48 };

function ticksLimpios(maximo: number, cantidad = 4): number[] {
  if (maximo <= 0) {
    return [0];
  }

  const bruto = maximo / cantidad;
  const magnitud = 10 ** Math.floor(Math.log10(bruto));
  const paso = [1, 2, 5, 10].map((m) => m * magnitud).find((p) => p >= bruto) ?? magnitud * 10;
  return Array.from({ length: Math.floor(maximo / paso) + 1 }, (_, i) => i * paso);
}

/**
 * Respuestas acumuladas por segundo, una línea por clase (solo las que aparecen) y la línea del límite por usuario.
 * Con el límite de ventana fija, "Aceptadas" se aplana en el límite mientras "Bloqueadas" sigue subiendo.
 */
export function TrafficChart({ serie, limite, segundos }: { readonly serie: readonly Punto[]; readonly limite: number; readonly segundos: number }) {
  const contenedor = useRef<HTMLDivElement>(null);
  const [ancho, setAncho] = useState(720);
  const [foco, setFoco] = useState<number | null>(null);

  useEffect(() => {
    const elemento = contenedor.current;
    if (!elemento) {
      return undefined;
    }

    const observer = new ResizeObserver(([entrada]) => {
      if (entrada) {
        setAncho(Math.max(320, Math.floor(entrada.contentRect.width)));
      }
    });
    observer.observe(elemento);
    return () => {
      observer.disconnect();
    };
  }, []);

  const clases = useMemo(() => CLASES.filter((clase) => serie.some((punto) => punto.acumulado[clase] > 0)), [serie]);
  const ultimo = serie.at(-1);
  const maxX = Math.max(segundos, serie.length - 1, 1);
  const maxDato = Math.max(0, ...serie.map((punto) => Math.max(...CLASES.map((clase) => punto.acumulado[clase]))));
  const maxY = Math.max(maxDato, limite) * 1.1;
  const ticksY = ticksLimpios(maxY);
  const ticksX = ticksLimpios(maxX, Math.min(8, Math.max(2, Math.floor(ancho / 110))));
  const angosto = ancho < 560;
  const derecha = angosto ? 16 : MARGEN.derecha;
  const plotAncho = ancho - MARGEN.izquierda - derecha;
  const plotAlto = ALTO - MARGEN.arriba - MARGEN.abajo;
  const x = (segundo: number) => MARGEN.izquierda + (segundo / maxX) * plotAncho;
  const y = (valor: number) => MARGEN.arriba + plotAlto - (valor / maxY) * plotAlto;
  const camino = (clase: Clase) => serie.map((punto, i) => `${i === 0 ? 'M' : 'L'}${x(punto.segundo).toFixed(1)},${y(punto.acumulado[clase]).toFixed(1)}`).join(' ');

  const puntoFoco = foco !== null ? serie[Math.min(foco, serie.length - 1)] : undefined;

  const mover = (event: PointerEvent<SVGRectElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const relativo = (event.clientX - rect.left) / rect.width;
    setFoco(Math.max(0, Math.min(serie.length - 1, Math.round(relativo * maxX))));
  };

  const teclado = (event: KeyboardEvent<SVGSVGElement>) => {
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
      event.preventDefault();
      setFoco((actual) => Math.max(0, Math.min(serie.length - 1, (actual ?? serie.length - 1) + (event.key === 'ArrowRight' ? 1 : -1))));
    } else if (event.key === 'Escape') {
      setFoco(null);
    }
  };

  // Etiquetas directas al final de cada línea, separadas si se enciman (con una línea guía hacia el punto).
  const etiquetas = useMemo(() => {
    if (!ultimo || angosto) {
      return [];
    }

    const ordenadas = clases.map((clase) => ({ clase, yReal: y(ultimo.acumulado[clase]) })).sort((a, b) => a.yReal - b.yReal);
    let anterior = -Infinity;
    return ordenadas.map((etiqueta) => {
      const yTexto = Math.max(etiqueta.yReal, anterior + 18);
      anterior = yTexto;
      return { ...etiqueta, yTexto };
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- y depende de maxY/plotAlto, ya cubiertos por ultimo y ancho.
  }, [ultimo, clases, angosto, maxY, ancho]);

  return (
    <div ref={contenedor} className={styles.grafica}>
      <svg
        width={ancho}
        height={ALTO}
        role="img"
        aria-label={`Respuestas acumuladas por segundo. ${clases.map((clase) => `${etiquetaClase[clase].nombre}: ${formatEntero(ultimo?.acumulado[clase] ?? 0)}`).join('. ')}.`}
        tabIndex={0}
        onKeyDown={teclado}
        onBlur={() => { setFoco(null); }}
      >
        {ticksY.map((tick) => (
          <g key={`y${tick}`}>
            <line className={styles.grid} x1={MARGEN.izquierda} x2={MARGEN.izquierda + plotAncho} y1={y(tick)} y2={y(tick)} />
            <text className={styles.eje} x={MARGEN.izquierda - 8} y={y(tick)} dy="0.32em" textAnchor="end">
              {formatEntero(tick)}
            </text>
          </g>
        ))}
        {ticksX.map((tick) => (
          <text key={`x${tick}`} className={styles.eje} x={x(tick)} y={ALTO - MARGEN.abajo + 20} textAnchor="middle">
            {tick} s
          </text>
        ))}
        <line className={styles.limite} x1={MARGEN.izquierda} x2={MARGEN.izquierda + plotAncho} y1={y(limite)} y2={y(limite)} />
        <text className={styles.limiteTexto} x={MARGEN.izquierda + 6} y={y(limite) - 6}>
          Límite del gateway: {formatEntero(limite)} por minuto
        </text>
        {clases.map((clase) => (
          <path key={clase} className={`${styles.linea} ${claseSerie[clase]}`} d={camino(clase)} />
        ))}
        {ultimo
          ? clases.map((clase) => (
              <circle key={clase} className={`${styles.punto} ${claseSerie[clase]}`} cx={x(ultimo.segundo)} cy={y(ultimo.acumulado[clase])} r={4} />
            ))
          : null}
        {etiquetas.map((etiqueta) => (
          <g key={etiqueta.clase}>
            {Math.abs(etiqueta.yTexto - etiqueta.yReal) > 2 ? (
              <line className={styles.guia} x1={x(ultimo?.segundo ?? 0) + 6} x2={MARGEN.izquierda + plotAncho + 10} y1={etiqueta.yReal} y2={etiqueta.yTexto} />
            ) : null}
            <text className={styles.etiqueta} x={MARGEN.izquierda + plotAncho + 12} y={etiqueta.yTexto} dy="0.32em">
              <tspan className={styles.etiquetaValor}>{formatEntero(ultimo?.acumulado[etiqueta.clase] ?? 0)}</tspan> {etiquetaClase[etiqueta.clase].codigo}
            </text>
          </g>
        ))}
        {puntoFoco ? <line className={styles.crosshair} x1={x(puntoFoco.segundo)} x2={x(puntoFoco.segundo)} y1={MARGEN.arriba} y2={MARGEN.arriba + plotAlto} /> : null}
        <rect
          className={styles.hit}
          x={MARGEN.izquierda}
          y={MARGEN.arriba}
          width={plotAncho}
          height={plotAlto}
          onPointerMove={mover}
          onPointerLeave={() => { setFoco(null); }}
        />
      </svg>
      {puntoFoco ? (
        <div
          className={styles.tooltip}
          ref={(nodo) => {
            // Posición por CSSOM (permitido por la CSP), nunca con un atributo style en el HTML.
            if (nodo) {
              const izquierda = x(puntoFoco.segundo);
              nodo.style.left = `${izquierda > ancho / 2 ? izquierda - 12 : izquierda + 12}px`;
              nodo.style.transform = izquierda > ancho / 2 ? 'translateX(-100%)' : 'none';
            }
          }}
        >
          <p className={styles.tooltipTitulo}>Segundo {puntoFoco.segundo}</p>
          {clases.map((clase) => (
            <p key={clase} className={styles.tooltipFila}>
              <span className={`${styles.clave} ${claseSerie[clase]}`} aria-hidden="true" />
              <strong>{formatEntero(puntoFoco.acumulado[clase])}</strong>
              <span className="muted">{etiquetaClase[clase].nombre}</span>
            </p>
          ))}
        </div>
      ) : null}
    </div>
  );
}
