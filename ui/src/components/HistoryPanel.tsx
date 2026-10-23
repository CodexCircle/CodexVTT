import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { collectionColors, type Collection, type CollectionColor, type HistoryEntry } from '../bridge/protocol';
import { useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { ConfirmDialog } from './ConfirmDialog';
import { FormDialog } from './FormDialog';

type Filter = 'all' | 'none' | string;

const titleOf = (e: HistoryEntry) => e.title || e.audioFileName;

export function HistoryPanel({ app, onOpened }: { app: CodexVttState; onOpened: () => void }) {
  const { t, formatDate, formatShortDate, formatDuration } = useI18n();
  const history = app.history;
  const entries = useMemo(() => history?.entries ?? [], [history]);
  const collections = useMemo(() => history?.collections ?? [], [history]);
  const [filter, setFilter] = useState<Filter>('all');
  const [menuFor, setMenuFor] = useState<string>();
  const [renaming, setRenaming] = useState<{ entry: HistoryEntry; title: string }>();
  const [picking, setPicking] = useState<{ entry: HistoryEntry; collectionId: string; newName: string }>();
  const [editing, setEditing] = useState<{ id?: string; name: string; color: CollectionColor }>();
  const [removingCollection, setRemovingCollection] = useState<Collection>();
  const [deleting, setDeleting] = useState<HistoryEntry>();
  const [dragging, setDragging] = useState<string>();

  const current = collections.find((c) => c.id === filter);
  // Se la raccolta mostrata viene eliminata, si torna a tutte.
  useEffect(() => {
    if (filter !== 'all' && filter !== 'none' && !current) setFilter('all');
  }, [filter, current]);

  const byId = useMemo(() => new Map(entries.map((e) => [e.id, e])), [entries]);
  const collectionOf = (e: HistoryEntry) => collections.find((c) => c.id === e.collectionId);
  const visible = current
    ? current.entryIds.map((id) => byId.get(id)).filter((e): e is HistoryEntry => Boolean(e))
    : filter === 'none'
      ? entries.filter((e) => !e.collectionId)
      : entries;

  const reorder = (ids: string[]) => current && void app.reorderCollection(current.id, ids);
  const move = (id: string, delta: number) => {
    if (!current) return;
    const ids = [...current.entryIds];
    const from = ids.indexOf(id);
    const to = from + delta;
    if (from < 0 || to < 0 || to >= ids.length) return;
    [ids[from], ids[to]] = [ids[to], ids[from]];
    reorder(ids);
  };
  const dropOn = (targetId: string) => {
    if (!current || !dragging || dragging === targetId) return;
    const ids = current.entryIds.filter((id) => id !== dragging);
    ids.splice(
      ids.indexOf(targetId) + (current.entryIds.indexOf(dragging) < current.entryIds.indexOf(targetId) ? 1 : 0),
      0,
      dragging,
    );
    reorder(ids);
  };

  const open = async (entry: HistoryEntry) => {
    setMenuFor(undefined);
    if (await app.openFromHistory(entry.id)) onOpened();
  };

  return (
    <section aria-labelledby="history-title" className="panel">
      <div className="page-head">
        <div>
          <h2 id="history-title" className="section-title">
            {t('tabHistory')}
          </h2>
          <div className="page-sub">
            <select
              className="filter"
              aria-label={t('collectionFilter')}
              value={current ? current.id : filter === 'none' ? 'none' : 'all'}
              onChange={(e) => setFilter(e.target.value)}
            >
              <option value="all">{t('allCollections')}</option>
              {collections.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
              <option value="none">{t('noCollection')}</option>
            </select>
            <span className="muted">{t('historyCount', { shown: visible.length, total: entries.length })}</span>
          </div>
        </div>
        <button type="button" onClick={() => setEditing({ name: '', color: 'bordeaux' })}>
          {t('newCollection')}
        </button>
      </div>

      {current && (
        <div className="collection-bar" style={{ ['--tag' as string]: `var(--tag-${current.color})` }}>
          <span className="collection-bar__name">{current.name}</span>
          <span className="muted">{current.entryIds.length > 1 ? t('reorderHint') : ''}</span>
          <span className="collection-bar__actions">
            <button type="button" className="link" onClick={() => setEditing({ ...current })}>
              {t('editCollectionShort')}
            </button>
            <button type="button" className="link" onClick={() => setRemovingCollection(current)}>
              {t('deleteCollection')}
            </button>
          </span>
        </div>
      )}

      {history && entries.length === 0 && <p className="muted empty">{t('historyEmpty')}</p>}
      {current && visible.length === 0 && <p className="muted empty">{t('collectionEmpty')}</p>}

      <ul className="cards" aria-label={t('historyList')}>
        {visible.map((entry, index) => {
          const collection = collectionOf(entry);
          const title = titleOf(entry);
          return (
            <li
              key={entry.id}
              className={`hcard ${dragging === entry.id ? 'hcard--dragging' : ''}`}
              aria-label={title}
              style={{ ['--tag' as string]: collection ? `var(--tag-${collection.color})` : 'var(--tag-none)' }}
              draggable={Boolean(current)}
              onDragStart={(e) => {
                setDragging(entry.id);
                e.dataTransfer.effectAllowed = 'move';
              }}
              onDragEnd={() => setDragging(undefined)}
              onDragOver={(e) => {
                if (current && dragging) e.preventDefault();
              }}
              onDrop={(e) => {
                e.preventDefault();
                dropOn(entry.id);
                setDragging(undefined);
              }}
            >
              <div className="hcard__top">
                <span className="hcard__tag">{collection?.name ?? t('noCollection')}</span>
                <button
                  type="button"
                  className="hcard__more"
                  aria-label={t('moreActions', { name: title })}
                  aria-haspopup="menu"
                  aria-expanded={menuFor === entry.id}
                  onClick={() => setMenuFor(menuFor === entry.id ? undefined : entry.id)}
                >
                  <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
                    <circle cx="5" cy="12" r="1.6" />
                    <circle cx="12" cy="12" r="1.6" />
                    <circle cx="19" cy="12" r="1.6" />
                  </svg>
                </button>
              </div>
              <p className="hcard__preview">{entry.preview}</p>
              <button
                type="button"
                className="hcard__open"
                aria-label={t('openEntry', { name: title })}
                onClick={() => void open(entry)}
              >
                <span className="hcard__meta" title={formatDate(entry.completedAt)}>
                  {formatShortDate(entry.completedAt)} · {formatDuration(entry.audioDuration)}
                </span>
                <strong className="hcard__title">{title}</strong>
                {entry.title && <span className="hcard__file">{entry.audioFileName}</span>}
              </button>
              {menuFor === entry.id && (
                <Menu onClose={() => setMenuFor(undefined)}>
                  <MenuItem onSelect={() => void open(entry)}>{t('open')}</MenuItem>
                  <MenuItem
                    onSelect={() => {
                      setMenuFor(undefined);
                      setRenaming({ entry, title: entry.title ?? '' });
                    }}
                  >
                    {t('rename')}
                  </MenuItem>
                  <MenuItem
                    onSelect={() => {
                      setMenuFor(undefined);
                      setPicking({ entry, collectionId: entry.collectionId ?? '', newName: '' });
                    }}
                  >
                    {t('setCollection')}
                  </MenuItem>
                  {current && (
                    <>
                      <MenuItem disabled={index === 0} onSelect={() => move(entry.id, -1)}>
                        {t('moveEarlier')}
                      </MenuItem>
                      <MenuItem disabled={index === visible.length - 1} onSelect={() => move(entry.id, 1)}>
                        {t('moveLater')}
                      </MenuItem>
                    </>
                  )}
                  <MenuItem
                    danger
                    onSelect={() => {
                      setMenuFor(undefined);
                      setDeleting(entry);
                    }}
                  >
                    {t('delete')}
                  </MenuItem>
                </Menu>
              )}
            </li>
          );
        })}
      </ul>

      <p className="folder-line muted">
        {t('historyFolder')}: <code>{history?.directory}</code>
        {app.desktop && (
          <>
            <button type="button" className="link" onClick={() => void app.openHistoryFolder()}>
              {t('openFolder')}
            </button>
            <button type="button" className="link" onClick={() => void app.pickHistoryFolder()}>
              {t('changeFolder')}
            </button>
          </>
        )}
      </p>

      {renaming && (
        <FormDialog
          title={t('renameTitle')}
          submitLabel={t('save')}
          onCancel={() => setRenaming(undefined)}
          onSubmit={() => {
            void app.renameEntry(renaming.entry.id, renaming.title);
            setRenaming(undefined);
          }}
        >
          <label>
            {t('titleLabel')}
            <input
              type="text"
              value={renaming.title}
              placeholder={renaming.entry.audioFileName}
              onChange={(e) => setRenaming({ ...renaming, title: e.target.value })}
            />
          </label>
          <p className="hint">{t('titleHint')}</p>
        </FormDialog>
      )}

      {picking && (
        <FormDialog
          title={t('chooseCollectionTitle')}
          submitLabel={t('save')}
          onCancel={() => setPicking(undefined)}
          onSubmit={async () => {
            const { entry, newName } = picking;
            setPicking(undefined);
            let target = picking.collectionId || null;
            if (target === 'new') {
              const created = newName.trim() ? await app.saveCollection({ name: newName, color: 'bordeaux' }) : undefined;
              if (!created) return;
              target = created.id;
            }
            void app.moveToCollection(entry.id, target);
          }}
        >
          <fieldset className="choices">
            <legend className="sr-only">{t('setCollection')}</legend>
            {[
              ...collections.map((c) => ({ id: c.id, name: c.name, color: c.color as string })),
              { id: '', name: t('noCollection'), color: 'none' },
            ].map((option) => (
              <label key={option.id} className="choice" style={{ ['--tag' as string]: `var(--tag-${option.color})` }}>
                <input
                  type="radio"
                  name="collection"
                  checked={picking.collectionId === option.id}
                  onChange={() => setPicking({ ...picking, collectionId: option.id })}
                />
                <span className="choice__dot" aria-hidden="true" />
                {option.name}
              </label>
            ))}
            <label className="choice choice--new">
              <input
                type="radio"
                name="collection"
                checked={picking.collectionId === 'new'}
                onChange={() => setPicking({ ...picking, collectionId: 'new' })}
              />
              <input
                type="text"
                aria-label={t('collectionName')}
                placeholder={t('newCollection')}
                value={picking.newName}
                onFocus={() => setPicking({ ...picking, collectionId: 'new' })}
                onChange={(e) => setPicking({ ...picking, collectionId: 'new', newName: e.target.value })}
              />
            </label>
          </fieldset>
        </FormDialog>
      )}

      {editing && (
        <FormDialog
          title={editing.id ? t('editCollection') : t('newCollection')}
          submitLabel={t('save')}
          onCancel={() => setEditing(undefined)}
          onSubmit={async () => {
            if (!editing.name.trim()) return;
            const saved = await app.saveCollection(editing);
            setEditing(undefined);
            if (saved && !editing.id) setFilter(saved.id);
          }}
        >
          <label>
            {t('collectionName')}
            <input type="text" required value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
          </label>
          <fieldset className="swatches">
            <legend>{t('collectionColor')}</legend>
            {collectionColors.map((color) => (
              <label
                key={color}
                className="swatch"
                style={{ ['--tag' as string]: `var(--tag-${color})` }}
                title={t(`color_${color}` as MessageKey)}
              >
                <input
                  type="radio"
                  name="color"
                  checked={editing.color === color}
                  onChange={() => setEditing({ ...editing, color })}
                />
                <span className="sr-only">{t(`color_${color}` as MessageKey)}</span>
              </label>
            ))}
          </fieldset>
        </FormDialog>
      )}

      {removingCollection && (
        <ConfirmDialog
          title={t('deleteCollectionTitle', { name: removingCollection.name })}
          confirmLabel={t('deleteCollection')}
          onConfirm={() => {
            void app.deleteCollection(removingCollection.id);
            setRemovingCollection(undefined);
          }}
          onCancel={() => setRemovingCollection(undefined)}
        >
          <p>{t('deleteCollectionText')}</p>
        </ConfirmDialog>
      )}

      {deleting && (
        <ConfirmDialog
          title={t('deleteConfirmTitle')}
          confirmLabel={t('delete')}
          onConfirm={() => {
            void app.deleteFromHistory(deleting.id);
            setDeleting(undefined);
          }}
          onCancel={() => setDeleting(undefined)}
        >
          <p>{t('deleteConfirmText', { file: deleting.textFileName })}</p>
        </ConfirmDialog>
      )}
    </section>
  );
}

/** Menu a comparsa della scheda: si chiude con Esc o con un clic fuori. */
function Menu({ onClose, children }: { onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('[role="menuitem"]:not(:disabled)')?.focus();
    const away = (e: MouseEvent) => {
      if (!ref.current?.parentElement?.contains(e.target as Node)) onClose();
    };
    document.addEventListener('mousedown', away);
    return () => document.removeEventListener('mousedown', away);
  }, [onClose]);
  return (
    <div
      ref={ref}
      role="menu"
      className="menu"
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
          e.preventDefault();
          const items = [...(ref.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not(:disabled)') ?? [])];
          const i = items.indexOf(document.activeElement as HTMLElement);
          items[(i + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus();
        }
      }}
    >
      {children}
    </div>
  );
}

function MenuItem({
  onSelect,
  disabled,
  danger,
  children,
}: {
  onSelect: () => void;
  disabled?: boolean;
  danger?: boolean;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      role="menuitem"
      className={`menu__item ${danger ? 'menu__item--danger' : ''}`}
      disabled={disabled}
      onClick={onSelect}
    >
      {children}
    </button>
  );
}
