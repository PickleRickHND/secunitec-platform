import { describe, expect, it } from 'vitest';
import { parseRetryAfter, remainingSeconds } from './retryAfter';

const now = new Date('2026-09-23T15:00:00Z');

describe('parseRetryAfter', () => {
  it('lee los segundos de la cabecera', () => {
    expect(parseRetryAfter('38', null, now)).toBe(38);
    expect(parseRetryAfter(' 0 ', null, now)).toBe(0);
  });

  it('convierte una fecha HTTP en segundos restantes, redondeando hacia arriba', () => {
    expect(parseRetryAfter('Wed, 23 Sep 2026 15:00:30 GMT', null, now)).toBe(30);
    expect(parseRetryAfter('Wed, 23 Sep 2026 15:00:30 GMT', null, new Date(now.getTime() + 29_200))).toBe(1);
    expect(parseRetryAfter('Wednesday, 23-Sep-26 15:00:10 GMT', null, now)).toBe(10);
    expect(parseRetryAfter('Wed Sep 23 15:00:05 2026', null, now)).toBe(5);
  });

  it('una fecha pasada no da segundos negativos', () => {
    expect(parseRetryAfter('Wed, 23 Sep 2026 14:59:00 GMT', null, now)).toBe(0);
  });

  it('sin cabecera usa retry_after del cuerpo del gateway', () => {
    expect(parseRetryAfter(null, { error: 'rate_limited', retry_after: 42 }, now)).toBe(42);
    expect(parseRetryAfter(undefined, { retry_after: 1.2 }, now)).toBe(2);
  });

  it('la cabecera manda sobre el cuerpo', () => {
    expect(parseRetryAfter('5', { retry_after: 42 }, now)).toBe(5);
  });

  it('valores inválidos devuelven null', () => {
    expect(parseRetryAfter('pronto', null, now)).toBeNull();
    expect(parseRetryAfter('-3', null, now)).toBeNull();
    expect(parseRetryAfter(null, { retry_after: -1 }, now)).toBeNull();
    expect(parseRetryAfter(null, { retry_after: '10' }, now)).toBeNull();
    expect(parseRetryAfter(null, 'texto', now)).toBeNull();
  });
});

describe('remainingSeconds', () => {
  it('cuenta hacia atrás en segundos enteros y se detiene en cero', () => {
    const deadline = now.getTime() + 10_000;
    expect(remainingSeconds(deadline, now.getTime())).toBe(10);
    expect(remainingSeconds(deadline, now.getTime() + 9_001)).toBe(1);
    expect(remainingSeconds(deadline, now.getTime() + 10_000)).toBe(0);
    expect(remainingSeconds(deadline, now.getTime() + 60_000)).toBe(0);
  });
});
