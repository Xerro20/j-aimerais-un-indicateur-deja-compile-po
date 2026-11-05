# Footprint HVN / LVN, Heavy Absorption, Traders Piégés & Alertes Flux d'Options — Indicateur pour ATAS X (.NET 10)

Indicateur Order Flow **déjà compilé** (`FootprintHvnLvnAbsorption.dll`) pour **ATAS X** (Windows & macOS, `.NET 10.0`) permettant d'identifier et de visualiser directement sur le **Footprint** :
1. **HVN (*High Volume Nodes*) & POC** — Nœuds de fort volume intra-bougie et zones HVN du profil de volume composite (session ou glissant).
2. **LVN (*Low Volume Nodes*)** — Creux de liquidité / *single prints* intra-bougie et vallées LVN du profil composite.
3. **Heavy Absorption (*Absorption Massive Bid / Ask*)** — Détection multi-critères des absorptions passives sur les extrêmes de bougie ou murs intra-bar, avec surbrillance néon du cluster, étiquette `ABS ▲ / ▼`, marqueur de bougie et rayon Support/Résistance projeté jusqu'à mitigation.
4. **Acheteurs & Vendeurs Piégés (*Trapped Buyers & Trapped Sellers*)** — Détection des ordres agressifs au marché piégés en perte latente dans les mèches de rejet (`V.PIÉGÉS ▲` en bas = signal haussier, `A.PIÉGÉS ▼` en haut = signal baissier, ou `ABS+PIÉGÉ ▲/▼` en cas de confluence), avec rayon de zone de liquidation.
5. **Alerte Visuelle ACHAT ▲ / VENTE ▼ prenant en compte le Flux d'Options ATAS X** — Intègre le flux d'options fourni par ATAS X (`IOptionsDataFeed`, `Security.OptionType` Call/Put, `StrikePrice`, variations d'`OpenInterest` $\Delta\text{OI}$, `OnNewTrade` / `OnCumulativeTrade` Sweeps & Block Trades) pour déclencher une **Alerte Visuelle Achat / Vente** sur la bougie (`★ ACHAT ▲` / `★ VENTE ▼`), une **Bannière Visuelle d'Alerte** en haut du graphique et les niveaux d'**Entrée / Stop-Loss**.

---

## 📥 Fichier `.dll` déjà compilé

Le binaire compilé prêt à l'emploi pour ATAS X est disponible à trois emplacements dans ce dépôt :
- [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll) *(racine du projet)*
- [`dist/FootprintHvnLvnAbsorption.dll`](./dist/FootprintHvnLvnAbsorption.dll)
- [`bin/Release/net10.0/FootprintHvnLvnAbsorption.dll`](./bin/Release/net10.0/FootprintHvnLvnAbsorption.dll)

| Propriété | Valeur |
| :--- | :--- |
| **Nom dans ATAS X** | `Footprint HVN / LVN, Absorption, Trapped & Options Flow Alerts` |
| **Catégorie ATAS X** | `Order Flow - Custom` |
| **Framework cible** | `.NET 10.0` (`ATAS X` Windows & macOS) |
| **Taille du binaire** | `71 680 octets` (70,0 Ko) |
| **SHA-256** | `9f22e085909ab299ff1ec32c19106f1d8a2adbef751260368ce02d4a19de3cec` |

---

## 🚀 Installation dans ATAS X

### Sur Windows
1. Téléchargez le fichier [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll).
2. *(Optionnel)* Si Windows bloque le fichier téléchargé depuis Internet : Clic droit sur `FootprintHvnLvnAbsorption.dll` → **Propriétés** → cochez **Débloquer** (*Unblock*) en bas → **Appliquer**.
3. Copiez `FootprintHvnLvnAbsorption.dll` dans le dossier des indicateurs d'ATAS :
   ```text
   %USERPROFILE%\Documents\ATAS\Indicators
   ```
   *(Exemple : `C:\Users\VotreNom\Documents\ATAS\Indicators\`)*
4. Sur votre graphique Footprint dans **ATAS X**, ouvrez le gestionnaire d'indicateurs (**Ctrl+I** ou icône Indicateurs), cherchez **`Footprint HVN / LVN, Absorption, Trapped & Options Flow Alerts`** (dans la catégorie **`Order Flow - Custom`**) et cliquez sur **Add**.

### Sur macOS (ATAS X)
1. Copiez `FootprintHvnLvnAbsorption.dll` dans :
   ```bash
   ~/Documents/ATAS/Indicators/
   ```
2. Ajoutez **`Footprint HVN / LVN, Absorption, Trapped & Options Flow Alerts`** depuis la fenêtre des indicateurs d'ATAS X.

---

## ⚡ Alerte Visuelle Achat / Vente & Flux d'Options ATAS X (`Groupe 9`)

L'indicateur exploite toutes les sources de flux d'options et de dérivés exposées par l'API **ATAS X (`ATAS.DataFeedsCore` & `ATAS.Indicators`)** :
1. **Flux Natif `IOptionsDataFeed` & `Security` (`OptionTypes.Call` / `OptionTypes.Put`)** :
   - Interroge automatiquement le service `IOptionsDataFeed` d'ATAS X (`GetOptionSeriesAsync` / `GetOptionsAsync`) ainsi que les propriétés `OptionType`, `StrikePrice` et `OpenInterest` du contrat actif pour mesurer le volume et l'Open Interest sur les **Calls** vs **Puts** et identifier le strike Gamma majeur.
2. **Flux Temps Réel `OpenInterest` ($\Delta\text{OI}$), `OnNewTrade` & `OnCumulativeTrade` (Sweeps / Block Prints)** :
   - Mesure la variation d'Open Interest ($\Delta\text{OI} = \text{OI}_{bar} - \text{OI}_{bar-1}$ et amplitude intra-bar `MaxOI - MinOI`), les **Sweeps** agressifs au marché (`OnNewTrade` / `OnCumulativeTrade`), le positionnement par rapport au `VWAP` de la bougie et l'asymétrie du carnet (`MarketDepthInfo.CumulativeDomBids` vs `CumulativeDomAsks`).
   - Calcule pour chaque bougie :
     - **Calls Flow %** vs **Puts Flow %**
     - **Put/Call Ratio (PCR)**
     - **Score de Confluence (`0 - 100 %`)**
3. **Conditions de déclenchement de l'Alerte Visuelle** :
   - **★ ALERTE VISUELLE ACHAT (`BUY ▲`)** :
     - Détection d'une **Heavy Absorption Bullish (au Bid)** et/ou de **Vendeurs Piégés ▲** dans la mèche inférieure,
     - **ET** confirmation par le **Flux d'Options ATAS X** (`Calls Flow % >= Dominance Min`, `PCR < 1.0`, $\Delta\text{OI}$ ou Sweep Call haussier).
   - **★ ALERTE VISUELLE VENTE (`SELL ▼`)** :
     - Détection d'une **Heavy Absorption Bearish (à l'Ask)** et/ou d'**Acheteurs Piégés ▼** dans la mèche supérieure,
     - **ET** confirmation par le **Flux d'Options ATAS X** (`Puts Flow % >= Dominance Min`, `PCR > 1.0`, $\Delta\text{OI}$ ou Sweep Put baissier).
4. **Triple Affichage Visuel + Alertes Sonores** :
   - **Cartouche sur la bougie** : Encadré néon `★ ACHAT ▲ [Score%]` ou `★ VENTE ▼ [Score%]` avec le ratio Calls/Puts et le PCR.
   - **Bannière Visuelle d'Alerte** en haut du graphique : Affiche le signal actif, le prix d'entrée suggéré, le Stop-Loss, le PCR et le $\Delta\text{OI}$.
   - **Lignes Prix d'Entrée (`ACHAT/VENTE @ ...`) & Stop-Loss (`SL @ ...`)** tracées directement à droite de la bougie de signal.
   - **Séries ATAS X** : `VisualBuyAlert` et `VisualSellAlert` disponibles pour les stratégies et alertes conditionnelles ATAS X.
