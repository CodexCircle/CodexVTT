import { useI18n } from '../i18n/i18n';
import type { CodexVttState, QueueItem } from '../state/useCodexVtt';
import { Editor } from '../editor/Editor';
import type { Selection } from './FileList';
import { RunStatus } from './RunStatus';

interface Props {
  app: CodexVttState;
  selection: Selection;
  onDirtyChange: (dirty: boolean) => void;
  onCopied: () => void;
}

/** Cosa mostrare per il file scelto nell'elenco: il testo da correggere, o lo stato se non è ancora pronto. */
export function Workbench({ app, selection, onDirtyChange, onCopied }: Props) {
  const { t, formatDuration, describeError } = useI18n();
  const open = app.open;

  if (selection?.kind === 'queue') {
    const item: QueueItem | undefined = app.queue.find((i) => i.key === selection.key);
    if (item && item.status !== 'done') {
      const error = item.error ? describeError(item.error.code) : undefined;
      return (
        <div className="page" aria-label={item.fileName}>
          <h2 className="section-title">{item.fileName}</h2>
          <div className="card">
            <div className="kv">
              <b>{t(`queueStatus_${item.status}` as 'queueStatus_queued')}</b>
              {item.info && <span className="muted">{formatDuration(item.info.duration)}</span>}
            </div>
            {item.status === 'working' && <RunStatus app={app} />}
            {error && (
              <p className="warning">
                {error.title} {error.action}
              </p>
            )}
          </div>
        </div>
      );
    }
  }

  // Il risultato aperto deve essere quello scelto: finché il core non l'ha caricato la pagina resta vuota.
  const chosen =
    selection?.kind === 'recent'
      ? open?.historyId === selection.id
      : selection?.kind === 'queue'
        ? (() => {
            const item = app.queue.find((i) => i.key === selection.key);
            return item?.historyId ? open?.historyId === item.historyId : open?.jobId !== undefined && open.jobId === item?.jobId;
          })()
        : true;
  if (!open || !chosen) return <div className="page" aria-busy="true" />;

  const entry = open.historyId ? app.history?.entries.find((e) => e.id === open.historyId) : undefined;
  const title = entry?.title || open.result.audioFileName;
  const historyId = open.historyId;

  return (
    <div className="workbench">
    <Editor
      key={`${historyId ?? open.jobId}:${open.result.completedAt}`}
      result={open.result}
      title={title}
      onRename={historyId ? (name) => void app.renameEntry(historyId, name) : undefined}
      onExport={(format) => void app.exportAs(format)}
      onDirtyChange={onDirtyChange}
      onCopied={onCopied}
    />
    </div>
  );
}
