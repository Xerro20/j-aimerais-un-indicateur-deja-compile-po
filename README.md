# Bot ATAS X : BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN / LVN — (.NET 10)

Nouveau Bot de Trading & Indicateur **déjà compilé** ([`BigTradesReversalOptionsBot.dll`](./BigTradesReversalOptionsBot.dll) & [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll)) pour **ATAS X** (Windows & macOS, `.NET 10.0`).

> **Important :** Toute l'ancienne stratégie qui ne fonctionnait pas (*Heavy Absorption*, anciens ratios *V.PIÉGÉS / A.PIÉGÉS*, anciens filtres 7 piliers EMA/VWAP/Z-Score/CVD) a été **intégralement retirée**. Le bot **n'ouvre des trades QUE sur la nouvelle stratégie demandée** : **BigTrades Piégés (Losanges `◆`) + Reversal**, accompagnée de l'indicateur **Options X-Ray**, du code **HVN / LVN** et des **outils pratiques** discutés.

---

## 🎯 Architecture Exclusive du Nouveau Bot

1. **Ouverture des Trades UNIQUEMENT sur BigTrades Piégés (Losanges `◆`) + Reversal (`Groupe 13`)** :
   - **Capture Temps Réel & Footprint (`OnCumulativeTrade`, `OnNewTrade` & `ClusterRow`)** : Détecte les gros ordres agressifs au marché dépassant le seuil calibré (`BigTradeMinLots = 40 lots` par défaut sur `NQ`, auto-adapté sur `MNQ`, `ES`, `MES`, `GC`, `CL`, `BTC`) et les affiche sous forme de **doubles losanges concentriques** (Rouge = `BigTrade Sell ◆`, Vert = `BigTrade Buy ◆`).
   - **Signal d'Entrée ACHAT (`▲ REVERSAL LONG`)** : Uniquement lorsqu'un **Losange Rouge (`BigTrade Sell ◆`)** apparaît dans le bas du mouvement (ou sur une **HVN Zone / PUT WALL / MAX PAIN**), se retrouve **piégé / absorbé** (contour Or lumineux `◆ PIÉGÉ`) et que la bougie effectue un **Reversal haussier** en clôturant au-dessus du losange rouge piégé.
   - **Signal d'Entrée VENTE (`▼ REVERSAL SHORT`)** : Uniquement lorsqu'un **Losange Vert (`BigTrade Buy ◆`)** apparaît dans le haut du mouvement (ou sous une **HVN Zone / CALL WALL / MAX PAIN**), se retrouve **piégé / rejeté** (contour Or lumineux `◆ PIÉGÉ`) et que la bougie effectue un **Reversal baissier** en clôturant sous le losange vert piégé.
   - **Notification BigTrade en bas à gauche (`RenderBigTradeAlertToast`)** : Affiche le toast temps réel style ATAS : `Alert — NQ: New BigTrade at 27899.25 (sell) is more than 40 lots`.

2. **Indicateur Options X-Ray Intégré (`CALL WALL`, `MAX PAIN`, `PUT WALL`, `0 DTE` & Profil GEX — `Groupe 14`)** :
   - **`CALL WALL`** (ligne verte continue + badge `CALL WALL 28050 · +15M` / `+11.9M`) : Strike supérieur de résistance Call Gamma Exposure (bloque les achats immédiats sous le mur et sert de cible/résistance).
   - **`MAX PAIN`** (ligne rouge pointillée + badge `MAX PAIN 27900`) : Strike d'équilibre gravitationnel de la session.
   - **`PUT WALL`** (ligne violette continue + badge `PUT WALL 27850 · -69.5M` / `-82.4M`) : Strike inférieur Put Gamma Exposure (sert de support ou d'objectif de prise de profit `TP2`).
   - **En-tête `Options X-Ray: SPX options data`** (`0 DTE = 1226pts · MP 27900`) et **Histogramme GEX Horizontal par Strike** sur le bord droit du graphique (barres jaunes Call GEX au-dessus du Max Pain, barres gris-bleu/violet Put GEX en dessous).

3. **Code HVN (*High Volume Nodes*) & LVN (*Low Volume Nodes*) Conservé (`Groupes 1 à 4`)** :
   - **HVN Zone & POC** : Nœuds de fort volume intra-bougie et bandes horizontales `HVN Zone` du profil de volume composite (session ou glissant) projetées sur la charte.
   - **LVN Pocket** : Creux de liquidité intra-bougie et vallées `LVN Pocket` du profil composite servant de zones de rejet et d'ancrage structurel du Stop-Loss.

4. **Outils Pratiques Conservés sur la Charte (`Groupe 12`)** :
   - **Bouton d'Activation Interactif sur la Charte** : `[ ● BOT BIGTRADES : ON (ACTIF) / OFF ]`, `[ MODE: LIVE ATAS / SIM ]`, `[ RESET ]`, `[ PROFIL ]`, `[ CROISÉ: NQ➔MNQ / ES➔MES ]`.
   - **Encadrement Horaire Interactif (Heures de Démarrage & de Fin)** : `[ HORAIRES: ON/OFF ] | DÉBUT [-] [08:00] [+] → FIN [-] [22:00] [+]` avec clôture automatique optionnelle en fin de plage.
   - **Synchronisation Temps Réel du PnL Réel avec le Compte Actif ATAS** : Lecture directe du PnL Réalisé + Latent, du Solde et de la Position ouverte depuis `TradingManager.Portfolio` / `Position` / `MyTrades`.
   - **Coupe-Circuit `Drawdown Max ($)` & Verrouillage `PnL Max ($)`** : Arrêt immédiat et fermeture Market de sécurité dès que la limite est atteinte.
   - **Étiquettes d'Ordres Actifs Style DOM ATAS** : `X │ 2 │ TP │ $ +3755.00 │ R`, `X │ 2 │ BUY │ $+5905.00` et `X │ 2 │ SL │ $ -2355.00 │ R` + alignement 100% entre le signal visuel et l'ordre réel exécuté.

---

## 📥 Fichiers `.dll` déjà compilés

Deux binaires compilés prêts à l'emploi pour ATAS X sont disponibles à la racine du projet et dans `dist/` :
- [`BigTradesReversalOptionsBot.dll`](./BigTradesReversalOptionsBot.dll)
- [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll)
- [`dist/BigTradesReversalOptionsBot.dll`](./dist/BigTradesReversalOptionsBot.dll) & [`dist/FootprintHvnLvnAbsorption.dll`](./dist/FootprintHvnLvnAbsorption.dll)

| Propriété | Valeur |
| :--- | :--- |
| **Nom dans ATAS X** | `ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN` |
| **Catégorie ATAS X** | `Order Flow - Custom` |
| **Framework cible** | `.NET 10.0` (`ATAS X` Windows & macOS) |
| **Taille du binaire** | `162 304 octets` (158,5 Ko) |
| **SHA-256** | `6d04958d0dbe9351a6da71cbcd2aa314c7279933830c3ed5b7ef88c74b3420c5` |

---

## 🚀 Installation dans ATAS X

### Sur Windows
1. Téléchargez le fichier [`BigTradesReversalOptionsBot.dll`](./BigTradesReversalOptionsBot.dll) (ou [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll)).
2. *(Optionnel)* Si Windows bloque le fichier téléchargé depuis Internet : Clic droit sur le fichier `.dll` → **Propriétés** → cochez **Débloquer** (*Unblock*) en bas → **Appliquer**.
3. Copiez le fichier `.dll` dans le dossier des indicateurs d'ATAS :
   ```text
   %USERPROFILE%\Documents\ATAS\Indicators
   ```
4. Sur votre graphique dans **ATAS X**, ouvrez le gestionnaire d'indicateurs (**Ctrl+I**), cherchez **`ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN`** (dans la catégorie **`Order Flow - Custom`**) et cliquez sur **Add**.

### Sur macOS (ATAS X)
1. Copiez [`BigTradesReversalOptionsBot.dll`](./BigTradesReversalOptionsBot.dll) dans :
   ```bash
   ~/Documents/ATAS/Indicators/
   ```
2. Ajoutez **`ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN`** depuis la fenêtre des indicateurs d'ATAS X.
