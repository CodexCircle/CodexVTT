import { useEffect, useRef, type ReactNode } from 'react';
import { useI18n } from '../i18n/i18n';

/** Dialog modale con un piccolo modulo: Invio conferma, Esc annulla, focus sul primo campo. */
export function FormDialog({
  title,
  submitLabel,
  onSubmit,
  onCancel,
  children,
}: {
  title: string;
  submitLabel: string;
  onSubmit: () => void;
  onCancel: () => void;
  children: ReactNode;
}) {
  const { t } = useI18n();
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => {
    form.current?.querySelector<HTMLElement>('input, select, button')?.focus();
  }, []);

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="form-dialog-title"
      className="dialog"
      onKeyDown={(e) => {
        if (e.key === 'Escape') onCancel();
      }}
    >
      <form
        ref={form}
        className="dialog__box"
        onSubmit={(e) => {
          e.preventDefault();
          onSubmit();
        }}
      >
        <h3 id="form-dialog-title">{title}</h3>
        {children}
        <div className="toolbar">
          <button type="submit" className="primary">
            {submitLabel}
          </button>
          <button type="button" className="secondary" onClick={onCancel}>
            {t('cancel')}
          </button>
        </div>
      </form>
    </div>
  );
}
