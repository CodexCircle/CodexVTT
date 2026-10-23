# Architecture Decision Records

Registro delle decisioni architetturali di CodexVTT. Ogni ADR descrive contesto, opzioni, decisione e conseguenze. Uno stato `Accettato` si cambia solo con un nuovo ADR che lo sostituisce.

| ADR | Titolo | Stato |
|---|---|---|
| [0001](0001-architettura-e-stack.md) | Architettura, stack e contratto core ↔ UI | Proposto |
| [0002](0002-runtime-whisper-locale.md) | Runtime Whisper locale | Proposto (spike Windows da completare) |
| [0003](0003-modelli-catalogo-e-distribuzione.md) | Catalogo, distribuzione e verifica dei modelli | Proposto |
| [0004](0004-decodifica-audio.md) | Decodifica e normalizzazione audio | Proposto |
| [0005](0005-packaging-e-distribuzione.md) | Packaging, installazione e aggiornamenti | Proposto |
| [0006](0006-privacy-e-rete.md) | Privacy, uso della rete e non-esfiltrazione | Proposto |
| [0007](0007-versione-server.md) | Versione server: UI nel browser, core come servizio | Proposto |

Documenti di supporto:

- [Revisione tecnica per aree](../architecture/revisione-tecnica.md): i pareri raccolti per l'issue #1.
- [Perimetro V1 e non-obiettivi](../architecture/perimetro-v1.md)
- [Matrice licenze](../architecture/licenze.md)
- [Registro dei rischi](../architecture/rischi.md)
- [Spike runtime](../../spike/runtime-probe/README.md)

Gli ADR passano da `Proposto` ad `Accettato` con l'approvazione della PR che chiude l'issue #1.

## Modello

```markdown
# ADR-NNNN — Titolo

- Stato: Proposto | Accettato | Sostituito da ADR-XXXX
- Data: AAAA-MM-GG
- Issue: #N

## Contesto
## Opzioni considerate
## Decisione
## Conseguenze
## Punti aperti
```
