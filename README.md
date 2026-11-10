# Footprint Quant Order Flow, Flux d'Options & Bot de Trading Automatique (Max Drawdown / Max PnL) — ATAS X (.NET 10)

Indicateur & Bot Quant Senior Order Flow **déjà compilé** (`FootprintHvnLvnAbsorption.dll`) pour **ATAS X** (Windows & macOS, `.NET 10.0`) permettant d'identifier, de filtrer statistiquement et de trader automatiquement sur le **Footprint** :
1. **HVN (*High Volume Nodes*) & POC** — Nœuds de fort volume intra-bougie et zones HVN du profil de volume composite (session ou glissant).
2. **LVN (*Low Volume Nodes*)** — Creux de liquidité / *single prints* intra-bougie et vallées LVN du profil composite.
3. **Heavy Absorption (*Absorption Massive Bid / Ask*)** — Détection multi-critères des absorptions passives sur les extrêmes de bougie ou murs intra-bar, avec surbrillance néon du cluster, étiquette `ABS ▲ / ▼` et rayon Support/Résistance projeté jusqu'à mitigation.
4. **Acheteurs & Vendeurs Piégés (*Trapped Buyers & Trapped Sellers*)** — Détection des ordres agressifs au marché piégés en perte latente dans les mèches de rejet (`V.PIÉGÉS ▲` en bas = signal haussier, `A.PIÉGÉS ▼` en haut = signal baissier, ou `ABS+PIÉGÉ ▲/▼` en cas de confluence).
5. **Flux d'Options ATAS X (`IOptionsDataFeed`, Calls/Puts, PCR, $\Delta\text{OI}$ & Sweeps)** — Intègre le flux d'options fourni par ATAS X (`OptionTypes.Call` / `OptionTypes.Put`, `StrikePrice`, `OpenInterest` $\Delta\text{OI}$, `OnNewTrade` / `OnCumulativeTrade`) pour confirmer les signaux Achat / Vente.
6. **Raffinement Quant Senior (`Groupe 11` : Confluence 5 Piliers HVN/LVN + Absorption + Piégés + Flux d'Options, Z-Score $\sigma$, CVD & Prise de Profit Dynamique)** — Validation institutionnelle de l'enchère par bougie combinant les **5 piliers de la stratégie** (**1. HVN / POC**, **2. Rejet LVN**, **3. Heavy Absorption**, **4. Acheteurs/Vendeurs Piégés**, **5. Flux d'Options ATAS X + Z-Score $\ge 0,85\sigma$ / Divergence CVD**), Stop-Loss structurel resserré sous/sur le cluster absorbé (`0,85 × ATR`), **`TP1` haute probabilité (`0,75R`, plafonné à `0,55 × ATR`)** avec prise de profit intégrale (`1 contrat`) ou partielle 50 % + **Profit-Lock (`+45 % de TP1`)**, **Early Profit-Lock dès `+0,45R`**, et **`TP2` (`1,50R`)** avec **Trailing Stop dynamique tick-par-tick**.
7. **Bot de Trading Quant Intégré, Alignement 100% Signaux ↔ Ouvertures de Trades & Coupe-Circuit `Max Drawdown` / `Max PnL` (`Groupe 12`)** :
   - **Correspondance 100% Exacte Signaux Affichés ↔ Ouvertures de Trades (`OnlyShowExecutedBotSignals`)** : Chaque cartouche `★ QUANT ACHAT ▲ / VENTE ▼`, flèche et ligne `ACHAT @ / VENTE @` affiché sur le graphique correspond exactement à l'ouverture d'un trade du Bot au même prix (`EntryPrice = TriggerPrice`) et sur la même bougie (avec verrouillage du signal exécuté par bougie `_lockedExecutedSignalsByBar`). En mode `LiveAutoTradingManager` (actif par défaut), le Bot ouvre, inverse atomiquement et exécute en temps réel tick-par-tick (`OnNewTrade`, `OnPositionChanged`, `OnCalculate`, `OnRender`) les prises de profit **`TP1`**, **`Early Profit-Lock`**, **`Trailing Stop`** et **`TP2`** via `ATAS ITradingManager`.
   - **Risk Guard — Drawdown Max (`BotMaxDrawdownUsd`)** : Suit le *High-Water Mark* (pic d'équité) en temps réel. Si le drawdown courant atteint la limite fixée en `$`, le Bot liquide immédiatement la position et coupe toute nouvelle prise de position (`COUPE-CIRCUIT : DRAWDOWN MAX ATTEINT`).
   - **Profit Lock — PnL Max (`BotMaxProfitTargetUsd`)** : Dès que le PnL cumulé atteint l'objectif de gain maximum en `$`, le Bot sécurise les gains et verrouille la session (`OBJECTIF PNL MAX VERROUILLÉ`).
   - **Synchronisation Temps Réel du PnL Réel avec le Compte Actif ATAS (`SyncPnlWithActiveAccount`, `AccountPnlScope`)** :
     - Se connecte directement à `TradingManager.Portfolio`, `TradingManager.Position`, `TradingStatisticsProvider.FilteredStatistics.HistoryMyTrades` et `TradingManager.MyTrades`, et écoute en temps réel les événements `OnPortfolioChanged`, `OnPositionChanged`, `OnNewMyTrade`, `OnOrderChanged` et `TradingManager.PortfolioSelected`.
     - Synchronise automatiquement le **Compte Actif sélectionné dans Chart Trader** (`AccountID`), le **Solde (`Balance`)**, la **Position Réelle Ouverte (`Volume` & `AveragePrice`)**, le **PnL Réalisé (`ClosedPnL`)**, le **PnL Latent Tick-par-Tick (`UnrealizedPnL` avec repli Mark-to-Market temps réel)** et le **PnL Réel Total (`Réalisé + Latent`)**.
     - Applique le coupe-circuit **Max Drawdown (`$`)** et **Max PnL (`$`)** directement sur le **PnL Réel du Compte Actif**, avec un 3ᵉ encadrement interactif cliquable sur la charte (`[ ● SYNC COMPTE: ... ] | [ PORTÉE: COMPTE GLOBAL / INSTRUMENT ] | PNL RÉEL: ...$`).
   - **Bouton d'Activation Interactif directement sur la Charte (`ShowChartBotToggleButton`)** : Barre de contrôle cliquable à la souris directement sur le graphique ATAS X (`ProcessMouseClick` / `ProcessMouseMove`) avec voyant LED permettant d'activer/désactiver le Bot en un clic (`[ ● BOT QUANT : ON (ACTIF) ]` / `[ ○ BOT QUANT : OFF ]`), de basculer le mode (`[ MODE: SIM/PAPER ]` ↔ `[ MODE: LIVE ATAS ]`), de réinitialiser le coupe-circuit (`[ RESET ]`), ainsi qu'un bouton rapide `[ ● BOT : ON/OFF ]` intégré dans l'en-tête du panneau HUD.
   - **Encadrement Interactif des Horaires sur la Charte (`EnableBotSchedule`, `BotStartTimeText`, `BotEndTimeText`, `BotFlattenAtEndTime`)** : Encadrement dessiné directement sur la charte (`[ 🕒 HORAIRES: ON/OFF ] | DÉBUT: [-] [08:00] [+] → FIN: [-] [22:00] [+]`) permettant d'activer/désactiver le filtre horaire et de régler l'**Heure de Démarrage** et l'**Heure de Fin** directement à la souris sur le graphique (par pas de 15 min ou 1 h) ou en saisissant `HH:mm` dans les paramètres, avec clôture automatique optionnelle à l'heure de fin.
   - **Mode Simulation/Backtest ou Routage Live ATAS X (`ITradingManager`)** : Fonctionne en mode `LiveAutoTradingManager` (`OpenOrderAsync` / `ClosePositionAsync`) ou `BacktestAndPaperOnly`.
8. **Adaptation Native Multi-Marchés (BTC Binance Futures + Futurs CME Clés `ES`, `NQ`, `GC`, `CL`, `YM`, `RTY`) & Trading Croisé ATAS X (`ICrossTradingIndicatorContext`)** :
   - **100 % Intact sur BTC Binance Futures (`MarketProfilePreset.CryptoBinanceFutures`)** : Conserve intégralement tous les seuils et comportements d'origine lorsque le graphique est sur un contrat Crypto (`BTCUSDT`, `ETHUSDT`, etc.).
   - **Calibrage Automatique CME (`MarketProfilePreset.AutoDetect` / `CmeSp500_ES_MES` / `CmeNasdaq_NQ_MNQ` / `CmeKeyFutures_GC_CL_YM_RTY`)** :
     - Adapte automatiquement les seuils d'Absorption, de Traders Piégés, le Z-Score, le regroupement de micro-clusters (`EnableCmeMicroClusterPooling` pour les carnets fins `NQ` / `MNQ` / `GC` / `CL`), l'estimation synthétique d'`OpenInterest` lorsque `Candle.OI == 0` en intraday CME et les valeurs de point exactes (`ES = 50 $/pt`, `MES = 5 $/pt`, `NQ = 20 $/pt`, `MNQ = 2 $/pt`, `GC = 100 $/pt`, `MGC = 10 $/pt`, `CL = 1000 $/pt`, `MCL = 100 $/pt`, `YM = 5 $/pt`, `MYM = 0,50 $/pt`, `RTY = 50 $/pt`, `M2K = 5 $/pt`).
   - **Support Natif du Trading Croisé ATAS X (`CrossTradingExecutionMode` + `ICrossTradingIndicatorContext`)** :
     - Permet d'afficher l'indicateur sur la **charte du contrat normal (`ES`, `NQ`, `GC`, `CL`, `YM`, `RTY`)** pour bénéficier de la profondeur Order Flow institutionnelle, tout en exécutant les ordres (`TradingManager.Security`), en gérant **`TP1` / `Early Profit-Lock` / `Trailing Stop` / `TP2`** tick-par-tick et en synchronisant le **PnL Réel** sur le **contrat Micro (`MES`, `MNQ`, `MGC`, `MCL`, `MYM`, `M2K`)** sélectionné dans le Trading Croisé d'ATAS X.
     - Deux nouveaux boutons interactifs cliquables directement sur la charte : **`[ PROFIL: AUTO (ES/S&P | NQ/NAS | BTC) ]`** et **`[ CROISÉ: ES➔MES / NQ➔MNQ ]`**.

---

## 📥 Fichier `.dll` déjà compilé

Le binaire compilé prêt à l'emploi pour ATAS X est disponible à trois emplacements dans ce dépôt :
- [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll) *(racine du projet)*
- [`dist/FootprintHvnLvnAbsorption.dll`](./dist/FootprintHvnLvnAbsorption.dll)
- [`bin/Release/net10.0/FootprintHvnLvnAbsorption.dll`](./bin/Release/net10.0/FootprintHvnLvnAbsorption.dll)

| Propriété | Valeur |
| :--- | :--- |
| **Nom dans ATAS X** | `Footprint Quant OrderFlow, Options Flow & Auto-Bot (Max DD / Max PnL)` |
| **Catégorie ATAS X** | `Order Flow - Custom` |
| **Framework cible** | `.NET 10.0` (`ATAS X` Windows & macOS) |
| **Taille du binaire** | `154 624 octets` (151,0 Ko) |
| **SHA-256** | `17f53b6f3a93d51bf0385a09dc6ae11595a285a6a8594f9eb6592cba6c2e88d2` |

---

## 🚀 Installation dans ATAS X

### Sur Windows
1. Téléchargez le fichier [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll).
2. *(Optionnel)* Si Windows bloque le fichier téléchargé depuis Internet : Clic droit sur `FootprintHvnLvnAbsorption.dll` → **Propriétés** → cochez **Débloquer** (*Unblock*) en bas → **Appliquer**.
3. Copiez `FootprintHvnLvnAbsorption.dll` dans le dossier des indicateurs d'ATAS :
   ```text
   %USERPROFILE%\Documents\ATAS\Indicators
   ```
4. Sur votre graphique Footprint dans **ATAS X**, ouvrez le gestionnaire d'indicateurs (**Ctrl+I**), cherchez **`Footprint Quant OrderFlow, Options Flow & Auto-Bot (Max DD / Max PnL)`** (dans la catégorie **`Order Flow - Custom`**) et cliquez sur **Add**.

### Sur macOS (ATAS X)
1. Copiez `FootprintHvnLvnAbsorption.dll` dans :
   ```bash
   ~/Documents/ATAS/Indicators/
   ```
2. Ajoutez **`Footprint Quant OrderFlow, Options Flow & Auto-Bot (Max DD / Max PnL)`** depuis la fenêtre des indicateurs d'ATAS X.
