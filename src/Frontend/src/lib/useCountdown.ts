import { useEffect, useState } from 'react';
import { remainingSeconds } from '../api/retryAfter';

/** Segundos que faltan hasta el instante dado; se actualiza cuatro veces por segundo mientras queda tiempo. */
export function useCountdown(deadlineMs: number | null): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!deadlineMs) {
      return undefined;
    }

    const tick = () => {
      setNow(Date.now());
    };
    tick();
    const id = window.setInterval(tick, 250);
    return () => {
      window.clearInterval(id);
    };
  }, [deadlineMs]);

  return deadlineMs ? remainingSeconds(deadlineMs, now) : 0;
}
