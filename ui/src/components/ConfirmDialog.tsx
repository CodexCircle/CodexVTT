import type { ReactNode } from 'react';
import { useI18n } from '../i18n/i18n';

/** Dialog modale di conferma: focus sul pulsante principale, Esc per annullare. */
export function ConfirmDialog({
  title,
  confirmLabel,
  onConfirm,
  onCancel,
  children,
}: {
  title: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  children: ReactNode;
}) {
  const { t } = useI18n();
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-title"
      className="dialog"
      onKeyDown={(e) => {
        if (e.key === 'Escape') onCancel();
      }}
    >
      <div className="dialog__box">
        <h3 id="confirm-title">{title}</h3>
        {children}
        <div className="toolbar">
          <button type="button" className="primary" autoFocus onClick={onConfirm}>
            {confirmLabel}
          </button>
          <button type="button" className="secondary" onClick={onCancel}>
            {t('cancel')}
          </button>
        </div>
      </div>
    </div>
  );
}
