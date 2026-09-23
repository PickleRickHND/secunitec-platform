/** Escudo de Secunitec (mismo trazo que las páginas de cuenta de Identity). */
export function BrandMark({ className }: { readonly className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path fill="currentColor" d="M12 2 4 5v6.2c0 4.9 3.4 9.4 8 10.8 4.6-1.4 8-5.9 8-10.8V5l-8-3Zm0 2.1 6 2.3v4.8c0 3.9-2.6 7.5-6 8.7-3.4-1.2-6-4.8-6-8.7V6.4l6-2.3Z" />
      <path fill="currentColor" d="m10.6 14.6-2.8-2.8 1.4-1.4 1.4 1.4 3.9-3.9 1.4 1.4-5.3 5.3Z" />
    </svg>
  );
}
