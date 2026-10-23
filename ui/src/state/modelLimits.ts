import type { ModelInfo, SelectionReason } from '../bridge/protocol';

/** Limiti di questo PC ricavati dalle motivazioni della raccomandazione, per spiegare perché un modello è escluso. */
export interface ModelLimits {
  /** RAM massima che un modello può richiedere su questo PC. */
  ramLimitBytes?: number;
  /** Il modello più pesante che rientra nel budget del processore. */
  heaviestFit?: ModelInfo;
}

export function modelLimits(models: ModelInfo[], reasons: SelectionReason[] = []): ModelLimits {
  const arg = (code: string, name: string) => Number(reasons.find((r) => r.code === code)?.args?.[name]);
  const ramLimit = arg('ram_limit', 'limitBytes');
  const maxCost = arg('cpu_budget', 'maxCost');
  const heaviestFit = Number.isFinite(maxCost)
    ? models.filter((m) => m.relativeCost <= maxCost).sort((a, b) => b.relativeCost - a.relativeCost)[0]
    : undefined;
  return { ramLimitBytes: Number.isFinite(ramLimit) ? ramLimit : undefined, heaviestFit };
}
