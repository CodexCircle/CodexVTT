import { useId, useState } from 'react';
import type { ModelInfo } from '../bridge/protocol';
import { useI18n } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';

/**
 * «i» in alto a destra nella scheda del modello: core, RAM, scheda video e memoria video del PC, requisiti del modello
 * e affidabilità. Il suggerimento si apre al passaggio del mouse e al focus da tastiera (Esc lo chiude) ed è legato al
 * pulsante con aria-describedby.
 */
export function ModelInfoTip({ app, model }: { app: CodexVttState; model: ModelInfo }) {
  const { t, formatBytes, formatMemory } = useI18n();
  const [open, setOpen] = useState(false);
  const id = useId();
  const hw = app.system?.hardware;
  const usesGpu = Boolean(hw?.gpuAcceleration) && model.engine === 'whisper';
  const gpus = hw?.gpus ?? [];
  const accuracy = model.accuracy;

  return (
    <span className="infotip">
      <button
        type="button"
        className="infotip__button"
        aria-label={t('modelInfo', { model: model.displayName })}
        aria-describedby={id}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}
      >
        <span aria-hidden="true">i</span>
      </button>
      <span role="tooltip" id={id} className="infotip__bubble" data-open={open}>
        {hw && (
          <>
            <strong>{t('infoThisPc')}</strong>
            <span>{t('infoCores', { cores: hw.logicalCores })}</span>
            <span>{t('infoRam', { total: formatMemory(hw.totalMemoryBytes) })}</span>
            <span>{gpus.length > 0 ? t('infoGpu', { gpu: gpus.join(', ') }) : t('infoGpuNone')}</span>
            {gpus.length > 0 && (
              <>
                <span>{usesGpu ? t('infoGpuUsed') : t('infoGpuNotUsed')}</span>
                <span>{hw.gpuVramBytes ? t('infoVram', { vram: formatBytes(hw.gpuVramBytes) }) : t('infoVramUnknown')}</span>
              </>
            )}
          </>
        )}
        <strong>{t('infoRequirements')}</strong>
        <span>{t('infoNeedRam', { ram: formatBytes(model.ramRequiredBytes) })}</span>
        <span>{t('infoNeedDisk', { size: formatBytes(model.sizeBytes) })}</span>
        <strong>{t('infoReliability')}</strong>
        <span>
          {accuracy
            ? `${t('accuracyBadge', { percent: new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(accuracy.percent) })} · ${t('accuracyTip', {
                metric: accuracy.metric,
                source: accuracy.source,
                measured: t(accuracy.measuredByProject ? 'accuracyMeasured' : 'accuracyPublished'),
              })}`
            : t('accuracyUnknown')}
        </span>
      </span>
    </span>
  );
}
