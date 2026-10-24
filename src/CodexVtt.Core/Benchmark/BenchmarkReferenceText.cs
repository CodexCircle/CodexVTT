namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Testo parlato nel file di riferimento ufficiale (capitolo 6 di Pinocchio, LibriVox, voce di Simona), compresa l'introduzione e
/// la chiusura della registrazione. Trascritto a mano da Giacomo ascoltando il file, con la punteggiatura corretta; un solo
/// parlante. Serve a misurare l'affidabilità di ogni modello come 100 meno il tasso di errore sulle parole (<c>WordErrorRate</c>).
/// Vale solo per il file ufficiale: con un file scelto dall'utente non c'è un testo con cui confrontare.
/// </summary>
public static class BenchmarkReferenceText
{
    public const string Text = """
Questa è una registrazione LibriVox. Tutte le registrazioni di LibriVox sono di dominio pubblico. Per ulteriori informazioni o per sapere come diventare volontario, visitate il sito librivox.org.
Registrato da Simona Pagliari, Cagliari, Italia. "Le avventure di Pinocchio" di Collodi. Capitolo sesto.
Pinocchio si addormenta coi piedi sul caldano e la mattina dopo si sveglia coi piedi tutti bruciati.
Per l'appunto, era una nottaccia d'inferno. Tuonava forte, lampeggiava come se il cielo pigliasse fuoco e un ventaccio freddo e strapazzone, fischiando rabbiosamente e sollevando un immenso nuvolo di polvere, faceva stridere e cigolare tutti gli alberi della campagna.
Pinocchio aveva una gran paura dei tuoni e dei lampi.
Se non che la fame era più forte della paura, motivo per cui accostò l'uscio di casa e, presa la carriera, in un centinaio di salti arrivò fino al paese, con la lingua fuori e col fiato grosso, come un cane da caccia.
Ma trovò tutto buio e tutto deserto. Le botteghe erano chiuse, le porte di casa chiuse, le finestre chiuse e nella strada nemmeno un cane. Pareva il paese dei morti. Allora Pinocchio, preso dalla disperazione e dalla fame, si attaccò al campanello d'una casa e cominciò a suonare a distesa, dicendo dentro di sé: "Qualcuno si affaccerà".
Difatti, si affacciò un vecchino col berretto da notte in capo, il quale gridò tutto stizzito: "Che cosa volete a quest'ora?" "Che mi fareste il piacere di darmi un po' di pane?" "Aspettami costì che torno subito", rispose il vecchino, credendo di aver da fare con qualcuno di quei ragazzacci rompicolli che si divertono di notte a suonare i campanelli delle case per molestare la gente perbene che se la dorme tranquillamente. Dopo mezzo minuto la finestra si riaprì e la voce del solito vecchino gridò a Pinocchio: "Fatti sotto e para il cappello". Pinocchio si levò subito il suo cappelluccio, ma mentre faceva l'atto di pararlo, sentì pioversi addosso un'enorme catinellata d'acqua che lo annaffiò tutto dalla testa ai piedi, come se fosse un vaso di geranio appassito. Tornò a casa bagnato come un pulcino e rifinito dalla stanchezza e dalla fame. E perché non aveva più forza da reggersi ritto, si pose a sedere, appoggiando i piedi fradici e impillaccherati sopra un caldano pieno di brace accesa. E lì si addormentò. E nel dormire i piedi, che erano di legno, gli presero fuoco e adagio adagio gli si carbonizzarono e diventarono cenere. E Pinocchio seguitava a dormire e a russare, come se i suoi piedi fossero quelli d'un altro. Finalmente, sul far del giorno, si svegliò perché qualcuno aveva bussato alla porta. "Chi è?" domandò sbadigliando e stropicciandosi gli occhi. "Sono io", rispose una voce. Quella voce era di Geppetto.
Fine del sesto capitolo.
""";
}
