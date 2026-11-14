# Bot ATAS X : BigTrades Piégés (Losanges ◆), Reversal, Options X-Ray & HVN / LVN — (.NET 10)

Nouveau Bot de Trading & Indicateur Order Flow **déjà compilé** ([`BigTradesReversalOptionsBot.dll`](./BigTradesReversalOptionsBot.dll) & [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll)) pour **ATAS X** (Windows & macOS, `.NET 10.0`) reproduisant la configuration complète **BigTrades Piégés (losanges ◆) + Reversal + Indicateur Options X-Ray + HVN / LVN** :

1. **Détection des BigTrades Piégés (Losanges Concentriques Rouges/Verts `◆`) & Reversal Automatique (`Groupe 13`)** :
   - **Capture Temps Réel & Footprint (`OnCumulativeTrade`, `OnNewTrade` & `ClusterRow`)** : Détecte tous les gros ordres agressifs au marché dépassant le seuil calibré (`BigTradeMinLots = 40 lots` par défaut sur `NQ`, auto-adapté sur `MNQ`, `ES`, `MES`, `GC`, `CL`, `BTC`) et les affiche sous forme de **doubles losanges concentriques** (Rouge = `BigTrade Sell ◆`, Vert = `BigTrade Buy ◆`).
   - **Identification du Piégeage (`TrappedBigTradeSide` & Contour Or `◆ PIÉGÉ`)** :
     - **Reversal ACHAT (`▲`)** : Lorsqu'un gros `BigTrade Sell ◆` (losange rouge) frappe le bas de la bougie ou une **HVN Zone / PUT WALL** mais est absorbé et que le prix clôture en retournement haussier au-dessus du losange (`hasBigTradeBullReversal`).
     - **Reversal VENTE (`▼`)** : Lorsqu'un gros `BigTrade Buy ◆` (losange vert) frappe le haut de la bougie ou une **HVN Zone / CALL WALL** mais est rejeté et que le prix clôture en retournement baissier sous le losange (`hasBigTradeBearReversal`).
   - **Toast d'Alerte BigTrade en bas à gauche (`RenderBigTradeAlertToast`)** : Affiche la notification temps réel style ATAS : `Alert — NQ: New BigTrade at 27899.25 (sell) is more than 40 lots`.
2. **Indicateur Options X-Ray Intégré (`CALL WALL`, `MAX PAIN`, `PUT WALL`, `0 DTE Expected Move` & Profil GEX par Strike — `Groupe 14`)** :
   - **Niveaux Institutionnels Options X-Ray (`ComputeOptionsXRayLevelsForBar` & `RenderOptionsXRayOverlay`)** :
     - **`CALL WALL`** (ligne verte continue + badge `CALL WALL 28050 · +15M` / `+11.9M`) : Strike supérieur à plus forte concentration Call Gamma Exposure.
     - **`MAX PAIN`** (ligne rouge pointillée + badge `MAX PAIN 27900`) : Strike d'équilibre gravitationnel de la session.
     - **`PUT WALL`** (ligne violette continue + badge `PUT WALL 27850 · -69.5M` / `-82.4M`) : Strike inférieur à plus forte concentration Put Gamma Exposure.
     - **Bandeau `Options X-Ray: SPX options data`** en haut à droite affichant le mouvement attendu `0 DTE = 1226pts · MP 27900`.
   - **Histogramme GEX Horizontal par Strike (`ShowOptionsStrikeGexProfile`)** : Barres horizontales sur le bord droit du graphique (jaunes au-dessus du Max Pain pour le Call GEX, gris-bleu/violet en dessous pour le Put GEX).
   - **Étiquettes d'Ordres Actifs Style DOM ATAS (`RenderDomStyleActiveOrderTags`)** : Affiche directement sur la charte les étiquettes `X │ 2 │ TP │ $ +3755.00 │ R`, `X │ 2 │ BUY │ $+5905.00` et `X │ 2 │ SL │ $ -2355.00 │ R`.
3. **Conservation Intégrale du Code HVN (*High Volume Nodes*) & LVN (*Low Volume Nodes*)** :
   - **HVN Zone & POC** : Nœuds de fort volume intra-bougie et bandes horizontales `HVN Zone` du profil de volume composite (session ou glissant) projetées sur la charte.
   - **LVN Pocket** : Creux de liquidité / *single prints* intra-bougie et vallées `LVN` du profil composite servant de zones de rejet ou d'accélération vers le prochain HVN.
4. **Heavy Absorption & Acheteurs/Vendeurs Piégés (*Order Flow Footprint*)** — Détection multi-critères des absorptions passives Bid/Ask (`ABS ▲ / ▼`) et des ordres agressifs piégés dans les mèches (`V.PIÉGÉS ▲` / `A.PIÉGÉS ▼`).
5. **Flux d'Options ATAS X (`IOptionsDataFeed`, Calls/Puts, PCR, $\Delta\text{OI}$ & Sweeps)** — Intègre le flux d'options fourni par ATAS X pour confirmer les retournements BigTrades.
6. **Raffinement Quant Senior (`Groupe 11` : Confluence Multi-Piliers $\ge 3$, Protection Anti-Écarts de Prix, Stop-Loss Structurel Hors Bougie & Prise de Profit Dynamique alignée sur les Murs d'Options)** — Validation institutionnelle combinant **BigTrades Piégés (`◆`)**, **HVN/LVN**, **Options X-Ray (`PUT WALL` / `CALL WALL` / `MAX PAIN`)**, **Heavy Absorption**, **Delta Tail / Divergence CVD**, **Structure Multi-Bougies & EMA 8/21/50 + VWAP** et **Z-Score**, avec **Stop-Loss structurel strictement hors de la bougie** et alignement intelligent de **`TP1` / `TP2`** devant les murs d'options (`PUT WALL` / `CALL WALL`).
7. **Bot de Trading Automatique Intégré, Synchronisation 100% Ordre Visuel ↔ Ordre Réel & Coupe-Circuit `Max Drawdown` / `Max PnL` (`Groupe 12`)** :
   - **Synchronisation 100% Exacte Ordre Visuel ↔ Ordre Réel (`OnlyShowExecutedBotSignals`, `_lockedExecutedSignalsByBar`, `GetLiveExecutableChartPrice`)** : Ancrage direct de `EntryPrice` sur le prix d'exécution réel (`BestAskPrice` / `BestBidPrice` puis `pos.AveragePrice`).
   - **Risk Guard — Drawdown Max (`BotMaxDrawdownUsd`)** & **Profit Lock — PnL Max (`BotMaxProfitTargetUsd`)**.
   - **Synchronisation Temps Réel du PnL Réel avec le Compte Actif ATAS (`SyncPnlWithActiveAccount`, `AccountPnlScope`)**.
   - **Boutons Interactifs sur la Charte** : Activation du Bot (`[ ● BOT QUANT : ON/OFF ]`), Mode (`SIM/PAPER` ↔ `LIVE ATAS`), Horaires (`DÉBUT → FIN`), Compte Actif et Profil Marché (`NQ/MNQ`, `ES/MES`, `BTC`).

---

## 📥 Fichiers `.dll` déjà compilés

Deux binaires compilés prêts à l'emploi pour ATAS X sont disponibles à la racine du projet et dans `dist/` :
- [`BigTradesReversalOptionsBot.dll`](./BigTradesReversalOptionsBot.dll) *(bot dédié avec BigTrades ◆ + Reversal + Options X-Ray + HVN/LVN activés par défaut)*
- [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll) *(inclut les deux entrées dans la liste des indicateurs ATAS X)*
- [`dist/BigTradesReversalOptionsBot.dll`](./dist/BigTradesReversalOptionsBot.dll) & [`dist/FootprintHvnLvnAbsorption.dll`](./dist/FootprintHvnLvnAbsorption.dll)

| Propriété | Valeur |
| :--- | :--- |
| **Nom dans ATAS X (Nouveau Bot)** | `ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN` |
| **Nom dans ATAS X (Suite Complète)** | `Footprint Quant OrderFlow, Options Flow & Auto-Bot (Max DD / Max PnL)` |
| **Catégorie ATAS X** | `Order Flow - Custom` |
| **Framework cible** | `.NET 10.0` (`ATAS X` Windows & macOS) |
| **Taille du binaire** | `185 344 octets` (181,0 Ko) |
| **SHA-256** | `f85d31bb92871aec2a7d949bd8c8894c95bba1fe2d5153096b0f89122369c77c` |

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
