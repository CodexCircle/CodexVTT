namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Testo parlato nei brani ufficiali oltre a Pinocchio (<see cref="BenchmarkReferenceText"/>), compresa l'introduzione e la
/// chiusura LibriVox di ogni registrazione. Trascritti a mano da Giacomo ascoltando i file, con la punteggiatura corretta;
/// un solo parlante per brano. Servono a misurare l'affidabilità di ogni modello come 100 meno il tasso di errore sulle parole.
/// </summary>
public static class BenchmarkClipTexts
{
    /// <summary>«Il giornalino di Gian Burrasca» di Vamba, 7 novembre, letto da Riccardo Fasol (LibriVox).</summary>
    public const string GianBurrasca7Novembre = """
7 novembre de "Il giornalino di Gian Burrasca".
Questa è una registrazione LibriVox. Tutte le registrazioni di LibriVox sono di pubblico dominio. Per ulteriori informazioni o per sapere come diventare volontario, visitate il sito LibriVox.org.
Registrato da Riccardo Fasol. "Il giornalino di Gian Burrasca", di Vamba.
7 novembre.
Stamani a scuola, alla lezione di latino, ne è successa una che merita davvero d'esser raccontata. Renzo, che sta di posto accanto a me, aveva portato un po' di pece presa nel negozio di suo zio, che fa il calzolaio. E io, colto il momento che un compagno che davanti a noi si era alzato per andare a dir la lezione, ho steso ben bene questa pallottolina di pece nel posto dove sta a sedere questo ragazzo, che è Mario Betti, ma noi si chiama il Milordo, perché va vestito tutto per l'appunto e all'inglese, mentre invece ha sempre il collo e gli orecchi così sudici che pare proprio uno spazzaturaio travestito da signore. Naturalmente quando è ritornato al suo posto non si è accorto di niente. Ma dopo un po' di tempo, la pece sulla quale stava a sedere gli s'era riscaldata sotto e ha fatto presa sui calzoni in modo che egli, nel muoversi e nel sentirsi tirare per di dietro, ha cominciato a borbottare e a smaniare. Il professore se n'è accorto e allora tra Muscolo e il Milordo è avvenuta una scena da crepar dal ridere. "Che c'è lì? Che ha il Betti?" "Ecco, io..." "Zitto!" "Ma..." "Fermo!" "Ma io non posso..." "Zitto e fermo! Guai se lo vedo muovere un muscolo!" "Ma scusi, io non posso..." "Non può, non può star zitto né fermo. Allora esca dal suo posto!" "Ma io non posso!" "Vada fuori di scuola!" "Non posso!" "Aaah!" E con un ruggito Muscolo si è scagliato sul povero Milordo e afferratolo per un braccio lo ha tirato fuori del banco con l'intenzione di buttarlo fuori di scuola, ma l'ha lasciato subito perché ha sentito un gran crack e si è accorto che un pezzo dei calzoni di quel povero ragazzo era rimasto attaccato sul sedile.
Muscolo è rimasto male, ma è rimasto peggio il Milordo e bisognava vederli tutte e due impappinate a guardarsi in faccia senza che nessun dei due si potesse spiegare l'accaduto. Una risata clamorosa è rimbombata nella classe e il professore, rovesciando su tutti la sua rabbia, ha urlato: "Tutti fermi, tutti zitti, guai se..." Ma non ha avuto il coraggio di finire il suo solito ritornello. Eh sì, altro che Muscolo, tutta la scolaresca era a bocca spalancata ed era impossibile, anche volendo, che qualcuno si potesse frenare. Basta. Dopo, per questo fatto è venuto il preside e per l'affare della pece siamo stati interrogati in sette o otto di noi altri, che stiamo nel banco dietro a quello del Milordo, ma non ci sono state spiate fortunatamente e la cosa è rimasta lì. Però il preside guardandomi fisso ha detto: "Stia attento chi è stato, che può esser che la paghi quando meno se l'aspetta".
Oggi il dottore ha sfasciato l'occhio all'avvocato Maralli e ha detto che domani potrà incominciare a tenere l'imposta della finestra un po' aperta, in modo che passi nella camera appena un filo di luce.
Fine del 7 novembre.
""";

    /// <summary>«Unbeaten Tracks in Japan» di Isabella L. Bird, Letter XXXIV (sezione 41), letta da Availle (LibriVox).</summary>
    public const string UnbeatenTracksLetter34 = """
Section 41 of Unbeaten Tracks in Japan by Isabella L. Bird. This LibriVox recording is in the public domain. Recording by Availle in September 2012.
Letter 34. Hakodate, Yezo.
I am enjoying Hakodate so much that, though my tour is all planned and my arrangements are made, I linger on from day to day. There has been an unpleasant esclarecissement about Ito. You will remember that I engaged him without a character, and that he told both Lady Parkes and me that, after I had done so, his former master, Mr. Maryes, asked him to go back to him, to which he had replied that he had a contract with a lady.
Mr. Maryes is here, and I now find that he had a contract with Ito, by which Ito bound himself to serve him as long as he required him for seven dollars a month, but that, hearing that I offered twelve dollars, he ran away from him and entered my service with a lie.
Mr. Maryes had been put to the greatest inconvenience by his defection and has been hindered greatly in completing his botanical collection, for Ito is very clever, and he had not only trained him to dry plants successfully, but he could trust him to go away for two or three days and collect seeds.
I am very sorry about it. He says that Ito was a bad boy when he came to him, but he thinks that he cured him of some of his faults and that he has served me faithfully.
I have seen Mr. Maryes at the consul's and have arranged that, after my Yezo tour is over, Ito shall be returned to his rightful master, who will take him to China and Formosa for a year and a half, and who, I think, will look after his well-being in every way.
Doctor and Mrs. Hepburn, who are here, heard a bad account of the boy after I began my travels and were uneasy about me, but except for this original lie, I have no fault to find with him, and his Shinto creed has not taught him any better.
When I paid him his wages this morning, he asked me if I had any fault to find, and I told him of my objection to his manners, which he took in very good part and promised to amend them. "But," he added, "mine are just missionary manners."
Yesterday, I dined at the consulate to meet Count Diesbach of the French Legation, Mr. von Siebold of the Austrian Legation, and Lieutenant Kreitner of the Austrian Army, who start tomorrow on an exploring expedition in the interior, intending to cross the sources of the rivers which fall into the sea on the southern coast and measure the heights of some of the mountains. They are well found in food and claret, but take such a number of pack ponies with them that I predict that they will fail, and that I, who have reduced my luggage to forty-five pounds, will succeed.
I hope to start on my long-projected tour tomorrow. I have planned it for myself with the confidence of an experienced traveler and look forward to it with great pleasure, as a visit to the aborigines is sure to be full of novel and interesting experiences.
Goodbye for a long time.
I.L.B.
End of Section 41.
""";
}
