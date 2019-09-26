# PDSProject - LAN File Sharing

PDSProject (scegliere altro nome plz) is a LAN file sharing application developed for the PDS(programmazione di sistema) course at Politecnico di Torino. With it you can:
* Send files and directory
* Customize your user profile

It uses UDP to discover other users on the network and TCP to send the actual files. The application is also able to detect network changes and discovers the new users inside a new LAN.

### Authors
Rossella Scarcia https://www.linkedin.com/in/rossellascarcia/

Maria Giulia Canu https://www.linkedin.com/in/mariagiuliacanu/

Cose da fare:
### "Protocollo" di comunicazione:

Il "protocollo" è composto da un header composto da:
* 5 bytes di identificazione del tipo di pacchetto. Questo può essere: FSEND(invio file), CIMAGE(invio immagine profilo), RFILE(richiesta file), YFILE(accettazione file) e NFILE(annullamento file).
* Nel caso di un pacchetto di tipo FSEND o CIMAGE, l'header è seguito dal nome del file di dimensione massima di 256 bytes e dalla dimensione del file di dimensione di 15 bytes (per sicurezza) di tipo unsigned long

### Host: 
* Aggiungere informazioni di configurazione per l'utente locale.
### MyTCPListener:
* Controllare se un file con lo stesso nome esiste/ meccanismo di richiesta di ricezione file (dipende configurazione).
* Aggiungere eccezioni in caso di: congestione di rete, file non completamente inviato, fine già esistente.
* Pensare ad un sistema che crea nuovi thread in base al numero di thread fisici supportati dalla CPU e in caso quanti siano attivi in quel momento dal processo.
* Aggiungere meccanismo per sapere la percentuale di completamento del file ricevuto.
* Cambiare la codifica del testo a UTF-16.
### MyTCPSender:
* Aggiungere la posibilità di mandare più file a uno o più host.
* Creare un meccanismo per ottimizzare il numero di thread usati a seconda del numero di thread fisici che la CPU supporta e rispetto a quanti sono in esecuzione in questo momento.
* Cambiare il carattere di separazione tra nome del file e dimensione.
* Specificare una lungheza massima del nome del file (pesato sulla massima dimensione del path di Windows), forse 4024 byte sono sufficienti.
* Cambiare la codifica del testo a UTF-16.
### MyUDPListener:
* Gestire eccezioni in caso di: pacchetti UDP non in formato, problemi di serializzazione, congestione di rete.
* Riorganizzare un po' il codice, troppo confusionario 
### MyUDPSender:
* Gestire eccezioni in caso di rete non disponibile.
### SharedInfo:
* Aggiungere tutti i dati riguardanti quali thread sono in esecuzione in questo momento, con relativo stato.
* Aggiungere lock object per accesso concorrente all'istnaza singleton.
* Cambiare il codice che controll IP e IP broadcast in modo tale che controlli quale sia la rete LAN che riceve i dati, per adesso considera solo la LAN del wifi.
### Generale:
* Gestire in generale le eccezioni (di nuovo :V).
* Gestire correttamente la concorrenza, attualmente niente viene protetto.
* Pulire le strutture dati in caso non servano più i dati al loro interno.
* Create un sistema di filesystem per gestire profili host, profilo user locale e i file ricevuti.
* Altro da aggiungere quando viene in mente
