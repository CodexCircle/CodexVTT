import type { MessageKey } from '../i18n/i18n';
import type { EditSpeaker } from './model';

export function speakerLabel(speaker: EditSpeaker, index: number, t: (key: MessageKey, params?: Record<string, string | number>) => string) {
  return speaker.name ?? t('speakerDefault', { n: index });
}

/** Pallino colorato del parlante (decorativo: accanto c'è sempre il nome). */
export function SpeakerDot({ speaker }: { speaker: EditSpeaker }) {
  return <span className="av" aria-hidden="true" style={{ background: `radial-gradient(circle at 30% 30%, ${speaker.color[0]}, ${speaker.color[1]})` }} />;
}
