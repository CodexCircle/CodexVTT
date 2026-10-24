import { memo, useEffect, useRef, type RefObject } from 'react';
import { Icon } from '../components/Icon';
import { useI18n } from '../i18n/i18n';
import { formatRuler, formatStamp, type EditSegment, type EditSpeaker } from './model';
import { SpeakerDot, speakerLabel } from './speakers';

const speeds = [1, 1.25, 1.5, 2, 0.75];
const rulerSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];

export interface Transport {
  playing: boolean;
  speed: number;
  pps: number;
  toggle: () => void;
  cycleSpeed: () => void;
  setPps: (pps: number) => void;
}

interface Props {
  segments: EditSegment[];
  speakers: EditSpeaker[];
  duration: number;
  current: number;
  transport: Transport;
  headRef: RefObject<HTMLDivElement | null>;
  scrollRef: RefObject<HTMLDivElement | null>;
  onSeek: (seconds: number) => void;
  onBlock: (index: number) => void;
  onAddSpeaker: () => void;
  onAddSegment: () => void;
}

export const minZoom = 10;
export const maxZoom = 120;

const Ruler = memo(function Ruler({ duration, pps }: { duration: number; pps: number }) {
  // Un'etichetta ogni ~70 px; le tacche intermedie solo se non sono troppe.
  const labelStep = rulerSteps.find((s) => s * pps >= 70) ?? 3600;
  const minor = labelStep >= 5 ? labelStep / 5 : labelStep;
  const ticks: number[] = [];
  const showMinor = duration / minor <= 3000;
  const step = showMinor ? minor : labelStep;
  for (let t = 0; t <= duration + 1; t += step) ticks.push(t);
  return (
    <div className="ruler" aria-hidden="true">
      {ticks.map((t) => {
        const isLabel = Math.abs(t / labelStep - Math.round(t / labelStep)) < 1e-6;
        return (
          <span key={t}>
            <u style={{ left: t * pps }} />
            {isLabel && <b style={{ left: t * pps }}>{formatRuler(t)}</b>}
          </span>
        );
      })}
    </div>
  );
});

const Lanes = memo(function Lanes({
  segments,
  speakers,
  pps,
  current,
  onBlock,
}: Pick<Props, 'segments' | 'speakers' | 'current' | 'onBlock'> & { pps: number }) {
  const { t } = useI18n();
  return (
    <div className="lanes">
      {speakers.map((s) => (
        <div className="lane" key={s.id}>
          {segments.map((g, i) =>
            g.speaker !== s.id ? null : (
              <button
                key={g.id}
                type="button"
                tabIndex={-1}
                className={`blk${i === current ? ' sel' : ''}`}
                style={{ left: g.start * pps, width: Math.max(8, (g.end - g.start) * pps) }}
                aria-label={t('seekTo', { time: formatStamp(g.start) })}
                onClick={() => onBlock(i)}
              />
            ),
          )}
        </div>
      ))}
    </div>
  );
});

/** Righello, una corsia per parlante, cursore e comandi del lettore. */
export function Timeline({ segments, speakers, duration, current, transport, headRef, scrollRef, onSeek, onBlock, onAddSpeaker, onAddSegment }: Props) {
  const { t } = useI18n();
  const { pps } = transport;
  const headColumn = useRef<HTMLDivElement>(null);

  // La colonna dei nomi segue lo scorrimento verticale delle corsie.
  const syncHead = () => {
    if (headColumn.current && scrollRef.current) headColumn.current.style.transform = `translateY(${-scrollRef.current.scrollTop}px)`;
  };
  useEffect(syncHead);

  return (
    <div className="tl">
      <div className="tlmain">
        <div className="tlhead">
          <div className="top">
            <span>{t('speakers')}</span>
            <button type="button" className="btn icon sm ghost" title={t('addSpeaker')} aria-label={t('addSpeaker')} onClick={onAddSpeaker}>
              <Icon name="plus" />
            </button>
          </div>
          <div className="tlhead__rows">
            <div ref={headColumn}>
              {speakers.map((s, i) => (
                <div className="row" key={s.id}>
                  <SpeakerDot speaker={s} />
                  <span className="grow">{speakerLabel(s, i, t)}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
        <div
          className="tlscroll"
          ref={scrollRef}
          onScroll={syncHead}
          onClick={(e) => {
            if ((e.target as HTMLElement).closest('.blk')) return;
            const box = e.currentTarget.firstElementChild!.getBoundingClientRect();
            onSeek(Math.max(0, (e.clientX - box.left) / pps));
          }}
        >
          <div className="tlin" style={{ width: Math.ceil(duration * pps) + 80 }}>
            <Ruler duration={duration} pps={pps} />
            <Lanes segments={segments} speakers={speakers} pps={pps} current={current} onBlock={onBlock} />
            <div className="head" ref={headRef} />
          </div>
        </div>
      </div>

      <div className="ctrl">
        <button type="button" className="iconbtn" title={t('zoomOut')} aria-label={t('zoomOut')} onClick={() => transport.setPps(Math.max(minZoom, pps - 10))}>
          <Icon name="zoomOut" />
        </button>
        <input
          type="range"
          min={minZoom}
          max={maxZoom}
          value={pps}
          aria-label={t('zoom')}
          onChange={(e) => transport.setPps(Number(e.target.value))}
        />
        <button type="button" className="iconbtn" title={t('zoomIn')} aria-label={t('zoomIn')} onClick={() => transport.setPps(Math.min(maxZoom, pps + 10))}>
          <Icon name="zoomIn" />
        </button>
        <div className="mid">
          <button type="button" className="speed" aria-label={t('playbackSpeed')} onClick={transport.cycleSpeed}>
            {transport.speed.toFixed(1)}x
          </button>
          <button type="button" className="play" aria-label={transport.playing ? t('pause') : t('play')} onClick={transport.toggle}>
            <Icon name={transport.playing ? 'pause' : 'play'} />
          </button>
        </div>
        <button type="button" className="btn sm" onClick={onAddSegment}>
          {t('addSegment')}
        </button>
      </div>
    </div>
  );
}

export { speeds };
