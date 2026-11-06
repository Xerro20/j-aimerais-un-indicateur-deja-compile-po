# Footprint Quant Order Flow, Flux d'Options & Bot de Trading Automatique (Max Drawdown / Max PnL) — ATAS X (.NET 10)

Indicateur & Bot Quant Senior Order Flow **déjà compilé** (`FootprintHvnLvnAbsorption.dll`) pour **ATAS X** (Windows & macOS, `.NET 10.0`) permettant d'identifier, de filtrer statistiquement et de trader automatiquement sur le **Footprint** :
1. **HVN (*High Volume Nodes*) & POC** — Nœuds de fort volume intra-bougie et zones HVN du profil de volume composite (session ou glissant).
2. **LVN (*Low Volume Nodes*)** — Creux de liquidité / *single prints* intra-bougie et vallées LVN du profil composite.
3. **Heavy Absorption (*Absorption Massive Bid / Ask*)** — Détection multi-critères des absorptions passives sur les extrêmes de bougie ou murs intra-bar, avec surbrillance néon du cluster, étiquette `ABS ▲ / ▼` et rayon Support/Résistance projeté jusqu'à mitigation.
4. **Acheteurs & Vendeurs Piégés (*Trapped Buyers & Trapped Sellers*)** — Détection des ordres agressifs au marché piégés en perte latente dans les mèches de rejet (`V.PIÉGÉS ▲` en bas = signal haussier, `A.PIÉGÉS ▼` en haut = signal baissier, ou `ABS+PIÉGÉ ▲/▼` en cas de confluence).
5. **Flux d'Options ATAS X (`IOptionsDataFeed`, Calls/Puts, PCR, $\Delta\text{OI}$ & Sweeps)** — Intègre le flux d'options fourni par ATAS X (`OptionTypes.Call` / `OptionTypes.Put`, `StrikePrice`, `OpenInterest` $\Delta\text{OI}$, `OnNewTrade` / `OnCumulativeTrade`) pour confirmer les signaux Achat / Vente.
6. **Raffinement Quant Senior (`Groupe 11` : Z-Score $\sigma$, ATR(14), Divergence CVD & Régime VWAP)** — Normalisation statistique des clusters par Z-Score de volume et de delta ($Z \ge 1.15\sigma$), calcul de volatilité dynamique ATR(14) pour le Stop-Loss et les objectifs `TP1` (`1.6R`) / `TP2` (`3.0R`), et score de confluence multi-facteurs (`0 - 99%`).
7. **Bot de Trading Quant Intégré & Coupe-Circuit `Max Drawdown` / `Max PnL` (`Groupe 12`)** :
   - **Exécution & Simulation Temps Réel** : Ouvre/inverse les positions sur les signaux Quant validés, gère la sortie partielle 50 % + passage automatique à **Breakeven** sur `TP1`, et la clôture finale sur `TP2` ou `SL`.
   - **Risk Guard — Drawdown Max (`BotMaxDrawdownUsd`)** : Suit le *High-Water Mark* (pic d'équité) en temps réel. Si le drawdown courant atteint la limite fixée en `$`, le Bot liquide immédiatement la position et coupe toute nouvelle prise de position (`COUPE-CIRCUIT : DRAWDOWN MAX ATTEINT`).
   - **Profit Lock — PnL Max (`BotMaxProfitTargetUsd`)** : Dès que le PnL cumulé atteint l'objectif de gain maximum en `$`, le Bot sécurise les gains et verrouille la session (`OBJECTIF PNL MAX VERROUILLÉ`).
   - **Bouton d'Activation Interactif directement sur la Charte (`ShowChartBotToggleButton`)** : Barre de contrôle cliquable à la souris directement sur le graphique ATAS X (`ProcessMouseClick` / `ProcessMouseMove`) avec voyant LED permettant d'activer/désactiver le Bot en un clic (`[ ● BOT QUANT : ON (ACTIF) ]` / `[ ○ BOT QUANT : OFF ]`), de basculer le mode (`[ MODE: SIM/PAPER ]` ↔ `[ MODE: LIVE ATAS ]`), de réinitialiser le coupe-circuit (`[ RESET ]`), ainsi qu'un bouton rapide `[ ● BOT : ON/OFF ]` intégré dans l'en-tête du panneau HUD.
   - **Mode Simulation/Backtest ou Routage Live ATAS X (`ITradingManager`)** : Fonctionne par défaut en mode sécurisé `BacktestAndPaperOnly` sur le graphique ou en mode `LiveAutoTradingManager` (`OpenOrderAsync` / `ClosePositionAsync`).

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
| **Taille du binaire** | `96 768 octets` (94,5 Ko) |
| **SHA-256** | `baad65111a7d1b1a8e7839702cc26838cc8f48a3c23c7c14334a3901d14a46f7` |

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
