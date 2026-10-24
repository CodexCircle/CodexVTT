import { useId, useState } from "react";
import type { ModelInfo } from "../bridge/protocol";
import { useI18n } from "../i18n/i18n";

/**
 * «i» in alto a destra nella scheda del modello: informazioni sul modello (formato, licenza, provenienza), requisiti
 * e affidabilità. Le caratteristiche del PC stanno già nel riquadro «Il tuo PC». Il suggerimento si apre al passaggio del mouse e al focus da tastiera (Esc lo chiude) ed è legato al
 * pulsante con aria-describedby.
 */
export function ModelInfoTip({
  model,
  measuredAccuracy,
}: {
  model: ModelInfo;
  measuredAccuracy?: number;
}) {
  const { t, formatBytes } = useI18n();
  const [open, setOpen] = useState(false);
  const id = useId();
  const accuracy = model.accuracy;

  return (
    <span className="infotip">
      <button
        type="button"
        className="infotip__button"
        aria-label={t("modelInfo", { model: model.displayName })}
        aria-describedby={id}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => e.key === "Escape" && setOpen(false)}
      >
        <span aria-hidden="true">i</span>
      </button>
      <span role="tooltip" id={id} className="infotip__bubble" data-open={open}>
        <strong>{t("infoAbout")}</strong>
        <span>
          {t("infoQuantization", { quantization: model.quantization })}
        </span>
        <span>{t("infoLicense", { license: model.license })}</span>
        {model.attribution && <span>{model.attribution}</span>}
        {model.requiresLanguage && <span>{t("requiresLanguageNote")}</span>}
        <strong>{t("infoRequirements")}</strong>
        <span>
          {t("infoNeedRam", { ram: formatBytes(model.ramRequiredBytes) })}
        </span>
        <strong>{t("infoReliability")}</strong>
        <span>
          {measuredAccuracy !== undefined
            ? t("accuracyBenchmark", {
                percent: new Intl.NumberFormat(undefined, {
                  maximumFractionDigits: 1,
                }).format(measuredAccuracy),
              })
            : accuracy
              ? `${t("accuracyBadge", { percent: new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(accuracy.percent) })} · ${t(
                  "accuracyTip",
                  {
                    metric: accuracy.metric,
                    source: accuracy.source,
                    measured: t(
                      accuracy.measuredByProject
                        ? "accuracyMeasured"
                        : "accuracyPublished",
                    ),
                  },
                )}`
              : t("accuracyUnknown")}
        </span>
      </span>
    </span>
  );
}
